using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PerunNetworkManager.Core.Models;

namespace PerunNetworkManager.Core.Services
{
    /// <summary>
    /// Service responsible for network scanning operations including device discovery,
    /// port scanning, and Wake-on-LAN functionality.
    /// </summary>
    public class NetworkScannerService
    {
        private readonly ILogger<NetworkScannerService> _logger;
        private readonly MacVendorService _macVendorService;
        private readonly ConcurrentDictionary<string, ScannedDevice> _discoveredDevices;
        private readonly SemaphoreSlim _scanSemaphore;
        private readonly object _progressLock = new object();
        
        // Events for progress reporting
        public event EventHandler<DeviceDiscoveredEventArgs> DeviceDiscovered;
        public event EventHandler<ScanProgressEventArgs> ScanProgress;
        public event EventHandler<ScanCompletedEventArgs> ScanCompleted;

        // Native methods for ARP table access
        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern int SendARP(uint destIP, uint srcIP, byte[] macAddr, ref int physAddrLen);

        [DllImport("ws2_32.dll")]
        private static extern uint inet_addr(string ipAddress);

        public NetworkScannerService(ILogger<NetworkScannerService> logger, MacVendorService macVendorService)
        {
            _logger = logger;
            _macVendorService = macVendorService;
            _discoveredDevices = new ConcurrentDictionary<string, ScannedDevice>();
            _scanSemaphore = new SemaphoreSlim(Environment.ProcessorCount * 4); // Configurable thread pool
        }

        /// <summary>
        /// Discovers all available subnets on the local machine.
        /// </summary>
        public async Task<List<SubnetInfo>> DiscoverSubnetsAsync()
        {
            var subnets = new List<SubnetInfo>();

            try
            {
                _logger.LogInformation("Starting subnet discovery");

                await Task.Run(() =>
                {
                    var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                        .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);

                    foreach (var ni in networkInterfaces)
                    {
                        var ipProperties = ni.GetIPProperties();
                        
                        foreach (var unicast in ipProperties.UnicastAddresses)
                        {
                            if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                var subnet = new SubnetInfo
                                {
                                    NetworkAddress = GetNetworkAddress(unicast.Address, unicast.IPv4Mask),
                                    SubnetMask = unicast.IPv4Mask,
                                    CIDR = GetCIDRNotation(unicast.IPv4Mask),
                                    InterfaceName = ni.Name,
                                    InterfaceDescription = ni.Description
                                };

                                subnets.Add(subnet);
                                _logger.LogDebug($"Discovered subnet: {subnet.NetworkAddress}/{subnet.CIDR} on {subnet.InterfaceName}");
                            }
                        }
                    }
                });

                _logger.LogInformation($"Discovered {subnets.Count} subnets");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error discovering subnets");
                throw;
            }

            return subnets;
        }

        /// <summary>
        /// Scans a subnet for active devices using multi-threaded approach.
        /// </summary>
        public async Task<List<ScannedDevice>> ScanSubnetAsync(SubnetInfo subnet, ScanOptions options, CancellationToken cancellationToken = default)
        {
            _discoveredDevices.Clear();
            var scanStartTime = DateTime.UtcNow;
            
            try
            {
                _logger.LogInformation($"Starting subnet scan: {subnet.NetworkAddress}/{subnet.CIDR}");
                
                // Calculate IP range
                var ipRange = CalculateIPRange(subnet);
                var totalHosts = ipRange.Count;
                var scannedHosts = 0;

                // Create parallel tasks for scanning
                var scanTasks = new List<Task>();
                
                foreach (var ipAddress in ipRange)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    await _scanSemaphore.WaitAsync(cancellationToken);
                    
                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            await ScanHostAsync(ipAddress, options, cancellationToken);
                            
                            // Report progress
                            lock (_progressLock)
                            {
                                scannedHosts++;
                                var progress = (double)scannedHosts / totalHosts * 100;
                                
                                ScanProgress?.Invoke(this, new ScanProgressEventArgs
                                {
                                    CurrentHost = ipAddress,
                                    TotalHosts = totalHosts,
                                    ScannedHosts = scannedHosts,
                                    ProgressPercentage = progress
                                });
                            }
                        }
                        finally
                        {
                            _scanSemaphore.Release();
                        }
                    }, cancellationToken);

                    scanTasks.Add(task);
                    
                    // Limit concurrent tasks
                    if (scanTasks.Count >= options.MaxConcurrentScans)
                    {
                        await Task.WhenAny(scanTasks);
                        scanTasks.RemoveAll(t => t.IsCompleted);
                    }
                }

                // Wait for all remaining tasks
                await Task.WhenAll(scanTasks);

                var devices = _discoveredDevices.Values.ToList();
                var scanDuration = DateTime.UtcNow - scanStartTime;

                // Fire completion event
                ScanCompleted?.Invoke(this, new ScanCompletedEventArgs
                {
                    DevicesFound = devices.Count,
                    ScanDuration = scanDuration,
                    Subnet = subnet
                });

                _logger.LogInformation($"Subnet scan completed. Found {devices.Count} devices in {scanDuration.TotalSeconds:F2} seconds");
                
                return devices;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Subnet scan was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during subnet scan");
                throw;
            }
        }

        /// <summary>
        /// Scans a single host for device information.
        /// </summary>
        private async Task ScanHostAsync(string ipAddress, ScanOptions options, CancellationToken cancellationToken)
        {
            try
            {
                // Step 1: Ping the host
                var isAlive = await IsHostAliveAsync(ipAddress, options.PingTimeout, cancellationToken);
                
                if (!isAlive && !options.ScanOfflineHosts)
                    return;

                var device = new ScannedDevice
                {
                    IPAddress = ipAddress,
                    IsOnline = isAlive,
                    FirstSeen = DateTime.UtcNow,
                    LastSeen = DateTime.UtcNow
                };

                // Step 2: Get MAC address via ARP
                if (isAlive)
                {
                    device.MACAddress = await GetMacAddressAsync(ipAddress);
                    
                    if (!string.IsNullOrEmpty(device.MACAddress))
                    {
                        device.Manufacturer = await _macVendorService.GetVendorNameAsync(device.MACAddress);
                    }
                }

                // Step 3: Resolve hostname
                if (options.ResolveHostnames)
                {
                    device.Hostname = await ResolveHostnameAsync(ipAddress, cancellationToken);
                    
                    // Try NetBIOS if DNS failed
                    if (string.IsNullOrEmpty(device.Hostname) && options.UseNetBIOS)
                    {
                        device.Hostname = await GetNetBIOSNameAsync(ipAddress, cancellationToken);
                    }
                }

                // Step 4: Port scanning
                if (options.PerformPortScan && isAlive)
                {
                    device.OpenPorts = await ScanPortsAsync(ipAddress, options.PortsToScan, options.PortScanTimeout, cancellationToken);
                    
                    // Device categorization based on open ports
                    device.DeviceType = CategorizeDevice(device);
                    
                    // Banner grabbing for service identification
                    if (options.PerformBannerGrabbing && device.OpenPorts.Any())
                    {
                        device.Services = await GrabBannersAsync(ipAddress, device.OpenPorts, cancellationToken);
                    }
                }

                // Step 5: Get additional system info via WMI if possible
                if (options.UseWMI && isAlive)
                {
                    await EnrichDeviceWithWMIDataAsync(device, cancellationToken);
                }

                // Add to discovered devices
                _discoveredDevices.TryAdd(ipAddress, device);
                
                // Fire device discovered event
                DeviceDiscovered?.Invoke(this, new DeviceDiscoveredEventArgs { Device = device });
                
                _logger.LogDebug($"Discovered device: {ipAddress} - {device.Hostname ?? "Unknown"} ({device.DeviceType})");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error scanning host {ipAddress}");
            }
        }

        /// <summary>
        /// Checks if a host is alive using ICMP ping.
        /// </summary>
        private async Task<bool> IsHostAliveAsync(string ipAddress, int timeout, CancellationToken cancellationToken)
        {
            try
            {
                using (var ping = new Ping())
                {
                    var reply = await ping.SendPingAsync(ipAddress, timeout);
                    return reply.Status == IPStatus.Success;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets MAC address for an IP using ARP.
        /// </summary>
        private async Task<string> GetMacAddressAsync(string ipAddress)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var destIP = inet_addr(ipAddress);
                    var macAddr = new byte[6];
                    var macAddrLen = macAddr.Length;

                    if (SendARP(destIP, 0, macAddr, ref macAddrLen) == 0)
                    {
                        return BitConverter.ToString(macAddr, 0, macAddrLen).Replace("-", ":");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, $"Failed to get MAC address for {ipAddress}");
                }

                return null;
            });
        }

        /// <summary>
        /// Resolves hostname using DNS.
        /// </summary>
        private async Task<string> ResolveHostnameAsync(string ipAddress, CancellationToken cancellationToken)
        {
            try
            {
                var hostEntry = await Dns.GetHostEntryAsync(ipAddress);
                return hostEntry.HostName;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets NetBIOS name for a host.
        /// </summary>
        private async Task<string> GetNetBIOSNameAsync(string ipAddress, CancellationToken cancellationToken)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    using (var client = new UdpClient())
                    {
                        client.Client.ReceiveTimeout = 1000;
                        
                        // NetBIOS Name Service port
                        var endpoint = new IPEndPoint(IPAddress.Parse(ipAddress), 137);
                        
                        // NetBIOS name query packet
                        byte[] packet = new byte[] 
                        {
                            0x00, 0x00, 0x00, 0x10, 0x00, 0x01, 0x00, 0x00,
                            0x00, 0x00, 0x00, 0x00, 0x20, 0x43, 0x4b, 0x41,
                            0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41,
                            0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41,
                            0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41,
                            0x41, 0x41, 0x41, 0x41, 0x41, 0x00, 0x00, 0x21,
                            0x00, 0x01
                        };

                        await client.SendAsync(packet, packet.Length, endpoint);
                        
                        var result = await client.ReceiveAsync();
                        
                        if (result.Buffer.Length > 56)
                        {
                            var name = Encoding.ASCII.GetString(result.Buffer, 57, 15).Trim();
                            return name;
                        }
                    }
                }
                catch
                {
                    // NetBIOS query failed
                }

                return null;
            }, cancellationToken);
        }

        /// <summary>
        /// Scans specified ports on a host.
        /// </summary>
        private async Task<List<int>> ScanPortsAsync(string ipAddress, int[] ports, int timeout, CancellationToken cancellationToken)
        {
            var openPorts = new ConcurrentBag<int>();
            var scanTasks = new List<Task>();

            foreach (var port in ports)
            {
                var task = Task.Run(async () =>
                {
                    if (await IsPortOpenAsync(ipAddress, port, timeout, cancellationToken))
                    {
                        openPorts.Add(port);
                    }
                }, cancellationToken);

                scanTasks.Add(task);
            }

            await Task.WhenAll(scanTasks);
            
            return openPorts.OrderBy(p => p).ToList();
        }

        /// <summary>
        /// Checks if a specific port is open.
        /// </summary>
        private async Task<bool> IsPortOpenAsync(string ipAddress, int port, int timeout, CancellationToken cancellationToken)
        {
            try
            {
                using (var tcpClient = new TcpClient())
                {
                    var connectTask = tcpClient.ConnectAsync(ipAddress, port);
                    var timeoutTask = Task.Delay(timeout, cancellationToken);

                    var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                    
                    if (completedTask == connectTask && tcpClient.Connected)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Port is closed or filtered
            }

            return false;
        }

        /// <summary>
        /// Performs banner grabbing on open ports.
        /// </summary>
        private async Task<Dictionary<int, string>> GrabBannersAsync(string ipAddress, List<int> openPorts, CancellationToken cancellationToken)
        {
            var services = new ConcurrentDictionary<int, string>();
            var tasks = new List<Task>();

            foreach (var port in openPorts)
            {
                var task = Task.Run(async () =>
                {
                    var banner = await GrabBannerAsync(ipAddress, port, cancellationToken);
                    if (!string.IsNullOrEmpty(banner))
                    {
                        services.TryAdd(port, banner);
                    }
                }, cancellationToken);

                tasks.Add(task);
            }

            await Task.WhenAll(tasks);
            
            return services.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        /// <summary>
        /// Grabs banner from a specific port.
        /// </summary>
        private async Task<string> GrabBannerAsync(string ipAddress, int port, CancellationToken cancellationToken)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    await client.ConnectAsync(ipAddress, port);
                    
                    using (var stream = client.GetStream())
                    {
                        stream.ReadTimeout = 2000;
                        stream.WriteTimeout = 2000;

                        // Send a generic probe for some services
                        if (port == 80 || port == 8080)
                        {
                            var request = Encoding.ASCII.GetBytes("HEAD / HTTP/1.0\r\n\r\n");
                            await stream.WriteAsync(request, 0, request.Length, cancellationToken);
                        }

                        // Read response
                        var buffer = new byte[1024];
                        var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                        
                        if (bytesRead > 0)
                        {
                            var banner = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                            
                            // Extract service name from banner
                            return ExtractServiceName(banner, port);
                        }
                    }
                }
            }
            catch
            {
                // Banner grab failed
            }

            // Return known service for common ports
            return GetWellKnownService(port);
        }

        /// <summary>
        /// Extracts service name from banner.
        /// </summary>
        private string ExtractServiceName(string banner, int port)
        {
            // HTTP Server
            if (banner.Contains("HTTP/"))
            {
                var match = Regex.Match(banner, @"Server:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
                if (match.Success)
                    return match.Groups[1].Value.Trim();
                return "HTTP";
            }

            // SSH
            if (banner.StartsWith("SSH-"))
            {
                return banner.Split('\r', '\n')[0];
            }

            // FTP
            if (banner.Contains("220") && port == 21)
            {
                var lines = banner.Split('\n');
                if (lines.Length > 0)
                    return lines[0].Trim();
            }

            // SMTP
            if (banner.Contains("220") && port == 25)
            {
                var match = Regex.Match(banner, @"220\s+([^\s]+)");
                if (match.Success)
                    return $"SMTP - {match.Groups[1].Value}";
            }

            return GetWellKnownService(port);
        }

        /// <summary>
        /// Gets well-known service name for a port.
        /// </summary>
        private string GetWellKnownService(int port)
        {
            return port switch
            {
                21 => "FTP",
                22 => "SSH",
                23 => "Telnet",
                25 => "SMTP",
                53 => "DNS",
                80 => "HTTP",
                110 => "POP3",
                135 => "RPC",
                139 => "NetBIOS",
                143 => "IMAP",
                443 => "HTTPS",
                445 => "SMB",
                1433 => "MSSQL",
                3306 => "MySQL",
                3389 => "RDP",
                5432 => "PostgreSQL",
                8080 => "HTTP-Proxy",
                _ => $"Port {port}"
            };
        }

        /// <summary>
        /// Categorizes device based on open ports and other characteristics.
        /// </summary>
        private DeviceType CategorizeDevice(ScannedDevice device)
        {
            if (device.OpenPorts == null || !device.OpenPorts.Any())
                return DeviceType.Unknown;

            // Router/Gateway detection
            if (device.OpenPorts.Contains(80) || device.OpenPorts.Contains(443))
            {
                if (device.OpenPorts.Contains(53) || device.OpenPorts.Contains(67))
                    return DeviceType.Router;
            }

            // Printer detection
            if (device.OpenPorts.Contains(9100) || device.OpenPorts.Contains(515) || device.OpenPorts.Contains(631))
                return DeviceType.Printer;

            // Server detection
            if (device.OpenPorts.Any(p => new[] { 21, 22, 25, 80, 443, 3306, 1433, 5432 }.Contains(p)))
                return DeviceType.Server;

            // Computer detection
            if (device.OpenPorts.Contains(445) || device.OpenPorts.Contains(139) || device.OpenPorts.Contains(3389))
                return DeviceType.Computer;

            // NAS detection
            if (device.OpenPorts.Contains(445) && (device.OpenPorts.Contains(80) || device.OpenPorts.Contains(443)))
            {
                if (device.Manufacturer?.ToLower().Contains("synology") == true ||
                    device.Manufacturer?.ToLower().Contains("qnap") == true)
                    return DeviceType.NAS;
            }

            // Mobile device detection (harder without additional info)
            if (device.Manufacturer != null)
            {
                var mobileBrands = new[] { "apple", "samsung", "xiaomi", "huawei", "google" };
                if (mobileBrands.Any(brand => device.Manufacturer.ToLower().Contains(brand)))
                    return DeviceType.Mobile;
            }

            // IoT device detection
            if (device.OpenPorts.Contains(8883) || device.OpenPorts.Contains(1883)) // MQTT ports
                return DeviceType.IoT;

            return DeviceType.Unknown;
        }

        /// <summary>
        /// Enriches device information using WMI (Windows hosts only).
        /// </summary>
        private async Task EnrichDeviceWithWMIDataAsync(ScannedDevice device, CancellationToken cancellationToken)
        {
            if (!device.OpenPorts?.Contains(135) == true) // RPC port required for WMI
                return;

            await Task.Run(() =>
            {
                try
                {
                    var scope = new ManagementScope($@"\\{device.IPAddress}\root\cimv2");
                    scope.Connect();

                    // Get OS information
                    using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM Win32_OperatingSystem")))
                    {
                        foreach (ManagementObject os in searcher.Get())
                        {
                            device.OperatingSystem = os["Caption"]?.ToString();
                            device.OSVersion = os["Version"]?.ToString();
                            break;
                        }
                    }

                    // Get computer system info
                    using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM Win32_ComputerSystem")))
                    {
                        foreach (ManagementObject cs in searcher.Get())
                        {
                            device.Manufacturer = cs["Manufacturer"]?.ToString();
                            device.Model = cs["Model"]?.ToString();
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"WMI query failed for {device.IPAddress}: {ex.Message}");
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Sends Wake-on-LAN magic packet to wake up a device.
        /// </summary>
        public async Task<bool> WakeOnLanAsync(ScannedDevice device)
        {
            if (string.IsNullOrEmpty(device.MACAddress))
            {
                _logger.LogWarning($"Cannot send WOL packet: MAC address not available for {device.IPAddress}");
                return false;
            }

            try
            {
                _logger.LogInformation($"Sending Wake-on-LAN packet to {device.MACAddress}");
                
                // Parse MAC address
                var macBytes = device.MACAddress.Split(':', '-')
                    .Select(x => Convert.ToByte(x, 16))
                    .ToArray();

                // Create magic packet
                var magicPacket = new byte[102];
                
                // First 6 bytes: 0xFF
                for (int i = 0; i < 6; i++)
                    magicPacket[i] = 0xFF;

                // Repeat MAC address 16 times
                for (int i = 1; i <= 16; i++)
                {
                    Array.Copy(macBytes, 0, magicPacket, i * 6, 6);
                }

                // Send packet to broadcast address
                using (var client = new UdpClient())
                {
                    client.EnableBroadcast = true;
                    
                    // Send to multiple broadcast addresses for better compatibility
                    var endpoints = new[]
                    {
                        new IPEndPoint(IPAddress.Broadcast, 9),
                        new IPEndPoint(IPAddress.Broadcast, 7),
                        new IPEndPoint(IPAddress.Parse("255.255.255.255"), 9)
                    };

                    foreach (var endpoint in endpoints)
                    {
                        await client.SendAsync(magicPacket, magicPacket.Length, endpoint);
                    }
                }

                _logger.LogInformation($"Wake-on-LAN packet sent successfully to {device.MACAddress}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send Wake-on-LAN packet to {device.MACAddress}");
                return false;
            }
        }

        /// <summary>
        /// Calculates IP range for a subnet.
        /// </summary>
        private List<string> CalculateIPRange(SubnetInfo subnet)
        {
            var ipRange = new List<string>();
            
            var networkBytes = subnet.NetworkAddress.GetAddressBytes();
            var maskBytes = subnet.SubnetMask.GetAddressBytes();
            
            // Calculate number of hosts
            var maskBits = subnet.CIDR;
            var hostBits = 32 - maskBits;
            var numberOfHosts = (int)Math.Pow(2, hostBits) - 2; // Exclude network and broadcast
            
            // Generate IP addresses
            var startIP = BitConverter.ToUInt32(networkBytes.Reverse().ToArray(), 0) + 1;
            
            for (uint i = 0; i < numberOfHosts && i < 65534; i++) // Limit to prevent memory issues
            {
                var ipBytes = BitConverter.GetBytes(startIP + i).Reverse().ToArray();
                ipRange.Add(new IPAddress(ipBytes).ToString());
            }

            return ipRange;
        }

        /// <summary>
        /// Gets network address from IP and subnet mask.
        /// </summary>
        private IPAddress GetNetworkAddress(IPAddress ip, IPAddress subnetMask)
        {
            var ipBytes = ip.GetAddressBytes();
            var maskBytes = subnetMask.GetAddressBytes();
            var networkBytes = new byte[4];

            for (int i = 0; i < 4; i++)
            {
                networkBytes[i] = (byte)(ipBytes[i] & maskBytes[i]);
            }

            return new IPAddress(networkBytes);
        }

        /// <summary>
        /// Converts subnet mask to CIDR notation.
        /// </summary>
        private int GetCIDRNotation(IPAddress subnetMask)
        {
            var maskBytes = subnetMask.GetAddressBytes();
            var bits = 0;

            foreach (var b in maskBytes)
            {
                bits += Convert.ToString(b, 2).Count(c => c == '1');
            }

            return bits;
        }
    }

    // Supporting classes for events
    public class DeviceDiscoveredEventArgs : EventArgs
    {
        public ScannedDevice Device { get; set; }
    }

    public class ScanProgressEventArgs : EventArgs
    {
        public string CurrentHost { get; set; }
        public int TotalHosts { get; set; }
        public int ScannedHosts { get; set; }
        public double ProgressPercentage { get; set; }
    }

    public class ScanCompletedEventArgs : EventArgs
    {
        public int DevicesFound { get; set; }
        public TimeSpan ScanDuration { get; set; }
        public SubnetInfo Subnet { get; set; }
    }

    // Supporting models
    public class SubnetInfo
    {
        public IPAddress NetworkAddress { get; set; }
        public IPAddress SubnetMask { get; set; }
        public int CIDR { get; set; }
        public string InterfaceName { get; set; }
        public string InterfaceDescription { get; set; }

        public override string ToString()
        {
            return $"{NetworkAddress}/{CIDR} ({InterfaceName})";
        }
    }

    public class ScanOptions
    {
        public int PingTimeout { get; set; } = 1000;
        public int PortScanTimeout { get; set; } = 500;
        public bool ResolveHostnames { get; set; } = true;
        public bool UseNetBIOS { get; set; } = true;
        public bool PerformPortScan { get; set; } = true;
        public bool PerformBannerGrabbing { get; set; } = true;
        public bool UseWMI { get; set; } = true;
        public bool ScanOfflineHosts { get; set; } = false;
        public int MaxConcurrentScans { get; set; } = 100;
        
        public int[] PortsToScan { get; set; } = new[]
        {
            21, 22, 23, 25, 53, 67, 80, 110, 135, 139, 143, 443, 445,
            515, 631, 1433, 1883, 3306, 3389, 5432, 8080, 8883, 9100
        };
    }

    public class ScannedDevice
    {
        public string IPAddress { get; set; }
        public string MACAddress { get; set; }
        public string Hostname { get; set; }
        public string Manufacturer { get; set; }
        public string Model { get; set; }
        public DeviceType DeviceType { get; set; }
        public bool IsOnline { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public List<int> OpenPorts { get; set; }
        public Dictionary<int, string> Services { get; set; }
        public string OperatingSystem { get; set; }
        public string OSVersion { get; set; }
        public string Description { get; set; }
        
        public ScannedDevice()
        {
            OpenPorts = new List<int>();
            Services = new Dictionary<int, string>();
            DeviceType = DeviceType.Unknown;
        }
    }

    public enum DeviceType
    {
        Unknown,
        Computer,
        Server,
        Router,
        Printer,
        Mobile,
        IoT,
        NAS,
        Switch,
        AccessPoint,
        Camera,
        Other
    }
}

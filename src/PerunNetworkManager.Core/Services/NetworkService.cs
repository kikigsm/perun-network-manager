using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.ServiceProcess;
using Microsoft.Win32;
using PerunNetworkManager.Core.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;

namespace PerunNetworkManager.Core.Services
{
    public class NetworkService
    {
        private readonly ILogger<NetworkService> _logger;
        private readonly Dictionary<string, NetworkConfiguration> _backupConfigurations = new();

        public NetworkService(ILogger<NetworkService> logger)
        {
            _logger = logger;
        }

        public async Task<List<NetworkAdapter>> GetNetworkAdaptersAsync()
        {
            var adapters = new List<NetworkAdapter>();

            try
            {
                var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
                
                foreach (var ni in networkInterfaces)
                {
                    var adapter = NetworkAdapter.FromNetworkInterface(ni);
                    await EnrichAdapterInformationAsync(adapter);
                    adapters.Add(adapter);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving network adapters");
            }

            return adapters;
        }

        private async Task EnrichAdapterInformationAsync(NetworkAdapter adapter)
        {
            try
            {
                // Get additional adapter information from WMI
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_NetworkAdapter WHERE GUID = '{adapter.Id}'");

                foreach (ManagementObject networkAdapter in searcher.Get())
                {
                    adapter.Manufacturer = networkAdapter["Manufacturer"]?.ToString() ?? "";
                    
                    var driverDate = networkAdapter["DriverDate"]?.ToString();
                    if (DateTime.TryParse(driverDate, out var parsedDate))
                    {
                        adapter.DriverDate = parsedDate;
                    }
                    
                    adapter.DriverVersion = networkAdapter["DriverVersion"]?.ToString() ?? "";
                    break;
                }

                await Task.Delay(1); // Make it async
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Error enriching adapter information for {adapter.Name}: {ex.Message}");
            }
        }

        public async Task<bool> ApplyNetworkProfileAsync(NetworkProfile profile, string adapterName)
        {
            try
            {
                await Task.Delay(2000); // Wait for network stack to stabilize
                _logger.LogInformation($"Successfully restored backup configuration for adapter '{adapterName}'");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error restoring backup configuration for adapter '{adapterName}'");
                return false;
            }
        }

        private async Task ApplyComputerNameAsync(NetworkProfile profile)
        {
            if (string.IsNullOrEmpty(profile.ComputerName))
                return;

            try
            {
                var currentName = Environment.MachineName;
                if (string.Equals(currentName, profile.ComputerName, StringComparison.OrdinalIgnoreCase))
                    return;

                using var computer = new ManagementObject($"Win32_ComputerSystem.Name='{currentName}'");
                var parameters = computer.GetMethodParameters("Rename");
                parameters["Name"] = profile.ComputerName;
                
                var result = (uint)computer.InvokeMethod("Rename", parameters, null);
                
                if (result == 0)
                {
                    _logger.LogInformation($"Computer name changed to '{profile.ComputerName}' (reboot required)");
                }
                else
                {
                    _logger.LogWarning($"Failed to change computer name. Return code: {result}");
                }

                await Task.Delay(1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying computer name");
            }
        }

        private async Task ApplyWorkgroupDomainAsync(NetworkProfile profile)
        {
            if (string.IsNullOrEmpty(profile.WorkgroupDomain))
                return;

            try
            {
                if (profile.IsDomain)
                {
                    // Join domain
                    using var computer = new ManagementObject("Win32_ComputerSystem.Name='" + Environment.MachineName + "'");
                    var parameters = computer.GetMethodParameters("JoinDomainOrWorkGroup");
                    parameters["Name"] = profile.WorkgroupDomain;
                    parameters["UserName"] = profile.DomainUsername;
                    parameters["Password"] = profile.DomainPassword;
                    parameters["FJoinOptions"] = 3; // NETSETUP_JOIN_DOMAIN | NETSETUP_ACCT_CREATE

                    var result = (uint)computer.InvokeMethod("JoinDomainOrWorkGroup", parameters, null);
                    
                    if (result == 0)
                    {
                        _logger.LogInformation($"Successfully joined domain '{profile.WorkgroupDomain}'");
                    }
                    else
                    {
                        _logger.LogWarning($"Failed to join domain. Return code: {result}");
                    }
                }
                else
                {
                    // Join workgroup
                    using var computer = new ManagementObject("Win32_ComputerSystem.Name='" + Environment.MachineName + "'");
                    var parameters = computer.GetMethodParameters("JoinDomainOrWorkGroup");
                    parameters["Name"] = profile.WorkgroupDomain;
                    parameters["FJoinOptions"] = 0; // NETSETUP_JOIN_WORKGROUP

                    var result = (uint)computer.InvokeMethod("JoinDomainOrWorkGroup", parameters, null);
                    
                    if (result == 0)
                    {
                        _logger.LogInformation($"Successfully joined workgroup '{profile.WorkgroupDomain}'");
                    }
                    else
                    {
                        _logger.LogWarning($"Failed to join workgroup. Return code: {result}");
                    }
                }

                await Task.Delay(1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying workgroup/domain settings");
            }
        }

        private async Task ApplyProxySettingsAsync(NetworkProfile profile)
        {
            if (!profile.Proxy.UseProxy)
            {
                // Disable proxy
                await SetProxyAsync(false, "", 0, "", "", "");
                return;
            }

            try
            {
                await SetProxyAsync(
                    true,
                    profile.Proxy.Server,
                    profile.Proxy.Port,
                    profile.Proxy.Username,
                    profile.Proxy.Password,
                    profile.Proxy.BypassList);

                _logger.LogInformation($"Applied proxy settings: {profile.Proxy.Server}:{profile.Proxy.Port}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying proxy settings");
            }
        }

        private async Task SetProxyAsync(bool enable, string server, int port, string username, string password, string bypassList)
        {
            try
            {
                const string registryPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
                
                using var key = Registry.CurrentUser.OpenSubKey(registryPath, true);
                if (key != null)
                {
                    if (enable)
                    {
                        key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                        key.SetValue("ProxyServer", $"{server}:{port}", RegistryValueKind.String);
                        
                        if (!string.IsNullOrEmpty(bypassList))
                        {
                            key.SetValue("ProxyOverride", bypassList, RegistryValueKind.String);
                        }
                    }
                    else
                    {
                        key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                        key.DeleteValue("ProxyServer", false);
                        key.DeleteValue("ProxyOverride", false);
                    }
                }

                // Refresh Internet Explorer settings
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);

                await Task.Delay(1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting proxy configuration");
            }
        }

        private async Task ApplyPrintersAsync(NetworkProfile profile)
        {
            foreach (var printer in profile.Printers)
            {
                try
                {
                    await InstallPrinterAsync(printer);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error installing printer '{printer.Name}'");
                }
            }
        }

        private async Task InstallPrinterAsync(PrinterConfiguration printer)
        {
            try
            {
                // Use WMI to install network printer
                using var printerClass = new ManagementClass("Win32_Printer");
                var parameters = printerClass.GetMethodParameters("AddPrinterConnection");
                parameters["Name"] = printer.Path;

                var result = (uint)printerClass.InvokeMethod("AddPrinterConnection", parameters, null);
                
                if (result == 0)
                {
                    _logger.LogInformation($"Successfully installed printer '{printer.Name}'");

                    // Set as default if specified
                    if (printer.IsDefault)
                    {
                        await SetDefaultPrinterAsync(printer.Path);
                    }
                }
                else
                {
                    _logger.LogWarning($"Failed to install printer '{printer.Name}'. Return code: {result}");
                }

                await Task.Delay(1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error installing printer '{printer.Name}'");
            }
        }

        private async Task SetDefaultPrinterAsync(string printerPath)
        {
            try
            {
                using var printers = new ManagementObjectSearcher("SELECT * FROM Win32_Printer");
                foreach (ManagementObject printer in printers.Get())
                {
                    if (printer["Name"]?.ToString() == printerPath)
                    {
                        printer.InvokeMethod("SetDefaultPrinter", null);
                        _logger.LogInformation($"Set '{printerPath}' as default printer");
                        break;
                    }
                }

                await Task.Delay(1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error setting default printer '{printerPath}'");
            }
        }

        private async Task ExecuteScriptsAsync(List<CustomScript> scripts, ScriptTrigger trigger)
        {
            var scriptsToExecute = scripts.Where(s => s.Enabled && s.Trigger == trigger).ToList();

            foreach (var script in scriptsToExecute)
            {
                try
                {
                    await ExecuteScriptAsync(script);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error executing script '{script.Name}'");
                }
            }
        }

        private async Task ExecuteScriptAsync(CustomScript script)
        {
            try
            {
                _logger.LogInformation($"Executing script '{script.Name}'");

                var processInfo = new ProcessStartInfo();
                var tempFile = "";

                switch (script.Type)
                {
                    case ScriptType.PowerShell:
                        tempFile = Path.GetTempFileName() + ".ps1";
                        await File.WriteAllTextAsync(tempFile, script.Content);
                        processInfo.FileName = "powershell.exe";
                        processInfo.Arguments = $"-ExecutionPolicy Bypass -File \"{tempFile}\"";
                        break;

                    case ScriptType.Batch:
                        tempFile = Path.GetTempFileName() + ".bat";
                        await File.WriteAllTextAsync(tempFile, script.Content);
                        processInfo.FileName = "cmd.exe";
                        processInfo.Arguments = $"/c \"{tempFile}\"";
                        break;

                    case ScriptType.Executable:
                        processInfo.FileName = script.Content;
                        break;
                }

                processInfo.UseShellExecute = false;
                processInfo.RedirectStandardOutput = true;
                processInfo.RedirectStandardError = true;
                processInfo.CreateNoWindow = true;

                using var process = new Process { StartInfo = processInfo };
                
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (s, e) => {
                    if (e.Data != null) outputBuilder.AppendLine(e.Data);
                };
                
                process.ErrorDataReceived += (s, e) => {
                    if (e.Data != null) errorBuilder.AppendLine(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(script.TimeoutSeconds));
                var processTask = process.WaitForExitAsync();

                var completedTask = await Task.WhenAny(processTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    process.Kill();
                    _logger.LogWarning($"Script '{script.Name}' timed out after {script.TimeoutSeconds} seconds");
                }
                else
                {
                    var output = outputBuilder.ToString();
                    var error = errorBuilder.ToString();

                    if (process.ExitCode == 0)
                    {
                        _logger.LogInformation($"Script '{script.Name}' executed successfully");
                        if (!string.IsNullOrEmpty(output))
                        {
                            _logger.LogDebug($"Script output: {output}");
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"Script '{script.Name}' exited with code {process.ExitCode}");
                        if (!string.IsNullOrEmpty(error))
                        {
                            _logger.LogWarning($"Script error: {error}");
                        }
                    }
                }

                // Clean up temp file
                if (!string.IsNullOrEmpty(tempFile) && File.Exists(tempFile))
                {
                    try
                    {
                        File.Delete(tempFile);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error executing script '{script.Name}'");
            }
        }

        public async Task<bool> EnableAdapterAsync(string adapterName)
        {
            return await SetAdapterStateAsync(adapterName, true);
        }

        public async Task<bool> DisableAdapterAsync(string adapterName)
        {
            return await SetAdapterStateAsync(adapterName, false);
        }

        private async Task<bool> SetAdapterStateAsync(string adapterName, bool enable)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_NetworkAdapter WHERE Name = '{adapterName}'");

                foreach (ManagementObject adapter in searcher.Get())
                {
                    var method = enable ? "Enable" : "Disable";
                    var result = (uint)adapter.InvokeMethod(method, null);
                    
                    if (result == 0)
                    {
                        _logger.LogInformation($"Successfully {(enable ? "enabled" : "disabled")} adapter '{adapterName}'");
                        return true;
                    }
                    else
                    {
                        _logger.LogError($"Failed to {(enable ? "enable" : "disable")} adapter '{adapterName}'. Return code: {result}");
                        return false;
                    }
                }

                await Task.Delay(1);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error {(enable ? "enabling" : "disabling")} adapter '{adapterName}'");
                return false;
            }
        }

        public async Task<NetworkDiagnosticResult> PerformNetworkDiagnosticsAsync()
        {
            var result = new NetworkDiagnosticResult();

            try
            {
                // Test internet connectivity
                result.InternetConnectivity = await TestInternetConnectivityAsync();

                // Test DNS resolution
                result.DnsResolution = await TestDnsResolutionAsync();

                // Get network adapter status
                result.AdapterStatuses = await GetAdapterStatusesAsync();

                // Test gateway connectivity
                result.GatewayConnectivity = await TestGatewayConnectivityAsync();

                _logger.LogInformation("Network diagnostics completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing network diagnostics");
            }

            return result;
        }

        private async Task<bool> TestInternetConnectivityAsync()
        {
            try
            {
                var testHosts = new[] { "8.8.8.8", "1.1.1.1", "208.67.222.222" };
                
                foreach (var host in testHosts)
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(host, 5000);
                    
                    if (reply.Status == IPStatus.Success)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> TestDnsResolutionAsync()
        {
            try
            {
                var testDomains = new[] { "google.com", "microsoft.com", "cloudflare.com" };
                
                foreach (var domain in testDomains)
                {
                    try
                    {
                        var addresses = await Dns.GetHostAddressesAsync(domain);
                        if (addresses.Any())
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task<List<AdapterStatus>> GetAdapterStatusesAsync()
        {
            var statuses = new List<AdapterStatus>();

            try
            {
                var adapters = await GetNetworkAdaptersAsync();
                
                foreach (var adapter in adapters)
                {
                    statuses.Add(new AdapterStatus
                    {
                        Name = adapter.Name,
                        IsEnabled = adapter.IsEnabled,
                        IsConnected = adapter.Status == OperationalStatus.Up,
                        HasIPAddress = adapter.IPAddresses.Any()
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting adapter statuses");
            }

            return statuses;
        }

        private async Task<bool> TestGatewayConnectivityAsync()
        {
            try
            {
                var gateways = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
                    .SelectMany(ni => ni.GetIPProperties().GatewayAddresses)
                    .Where(ga => ga.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(ga => ga.Address)
                    .Distinct();

                foreach (var gateway in gateways)
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(gateway, 3000);
                    
                    if (reply.Status == IPStatus.Success)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        // P/Invoke for Internet Explorer proxy settings
        [System.Runtime.InteropServices.DllImport("wininet.dll")]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        private const int INTERNET_OPTION_REFRESH = 37;
    }

    public class NetworkConfiguration
    {
        public string AdapterName { get; set; } = string.Empty;
        public DateTime BackupDate { get; set; }
        public List<IPConfiguration> IPAddresses { get; set; } = new();
        public List<string> Gateways { get; set; } = new();
        public List<string> DnsServers { get; set; } = new();
    }

    public class IPConfiguration
    {
        public string Address { get; set; } = string.Empty;
        public string SubnetMask { get; set; } = string.Empty;
    }

    public class NetworkDiagnosticResult
    {
        public bool InternetConnectivity { get; set; }
        public bool DnsResolution { get; set; }
        public bool GatewayConnectivity { get; set; }
        public List<AdapterStatus> AdapterStatuses { get; set; } = new();
        public DateTime TestDateTime { get; set; } = DateTime.Now;
    }

    public class AdapterStatus
    {
        public string Name { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public bool IsConnected { get; set; }
        public bool HasIPAddress { get; set; }
    }
}Applying network profile '{profile.Name}' to adapter '{adapterName}'");

                // Backup current configuration
                await BackupCurrentConfigurationAsync(adapterName);

                // Execute pre-apply scripts
                await ExecuteScriptsAsync(profile.Scripts, ScriptTrigger.BeforeApply);

                // Apply network configuration
                var success = await ApplyNetworkConfigurationAsync(profile, adapterName);

                if (success)
                {
                    // Apply additional configurations
                    await ApplyComputerNameAsync(profile);
                    await ApplyWorkgroupDomainAsync(profile);
                    await ApplyProxySettingsAsync(profile);
                    await ApplyPrintersAsync(profile);

                    // Execute post-apply scripts
                    await ExecuteScriptsAsync(profile.Scripts, ScriptTrigger.AfterApply);

                    _logger.LogInformation($"Successfully applied network profile '{profile.Name}'");
                    return true;
                }
                else
                {
                    _logger.LogWarning($"Failed to apply network profile '{profile.Name}'");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error applying network profile '{profile.Name}'");
                return false;
            }
        }

        private async Task<bool> ApplyNetworkConfigurationAsync(NetworkProfile profile, string adapterName)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_NetworkAdapterConfiguration WHERE Description LIKE '%{adapterName}%'");

                foreach (ManagementObject networkAdapter in searcher.Get())
                {
                    if (profile.UseDHCP)
                    {
                        // Enable DHCP
                        var dhcpResult = (uint)networkAdapter.InvokeMethod("EnableDHCP", null);
                        if (dhcpResult != 0)
                        {
                            _logger.LogError($"Failed to enable DHCP. Return code: {dhcpResult}");
                            return false;
                        }

                        // Set DNS servers if specified
                        if (!string.IsNullOrEmpty(profile.PrimaryDNS) || !string.IsNullOrEmpty(profile.SecondaryDNS))
                        {
                            var dnsServers = new List<string>();
                            if (!string.IsNullOrEmpty(profile.PrimaryDNS))
                                dnsServers.Add(profile.PrimaryDNS);
                            if (!string.IsNullOrEmpty(profile.SecondaryDNS))
                                dnsServers.Add(profile.SecondaryDNS);

                            var dnsParams = networkAdapter.GetMethodParameters("SetDNSServerSearchOrder");
                            dnsParams["DNSServerSearchOrder"] = dnsServers.ToArray();
                            var dnsResult = (uint)networkAdapter.InvokeMethod("SetDNSServerSearchOrder", dnsParams, null);
                            
                            if (dnsResult != 0)
                            {
                                _logger.LogWarning($"Failed to set DNS servers. Return code: {dnsResult}");
                            }
                        }
                    }
                    else
                    {
                        // Set static IP configuration
                        var ipParams = networkAdapter.GetMethodParameters("EnableStatic");
                        ipParams["IPAddress"] = new[] { profile.IPAddress };
                        ipParams["SubnetMask"] = new[] { profile.SubnetMask };

                        var ipResult = (uint)networkAdapter.InvokeMethod("EnableStatic", ipParams, null);
                        if (ipResult != 0 && ipResult != 1) // 1 = reboot required
                        {
                            _logger.LogError($"Failed to set static IP. Return code: {ipResult}");
                            return false;
                        }

                        // Set gateway
                        if (!string.IsNullOrEmpty(profile.DefaultGateway))
                        {
                            var gwParams = networkAdapter.GetMethodParameters("SetGateways");
                            gwParams["DefaultIPGateway"] = new[] { profile.DefaultGateway };
                            gwParams["GatewayCostMetric"] = new[] { 1 };

                            var gwResult = (uint)networkAdapter.InvokeMethod("SetGateways", gwParams, null);
                            if (gwResult != 0 && gwResult != 1)
                            {
                                _logger.LogWarning($"Failed to set gateway. Return code: {gwResult}");
                            }
                        }

                        // Set DNS servers
                        var dnsServers = new List<string>();
                        if (!string.IsNullOrEmpty(profile.PrimaryDNS))
                            dnsServers.Add(profile.PrimaryDNS);
                        if (!string.IsNullOrEmpty(profile.SecondaryDNS))
                            dnsServers.Add(profile.SecondaryDNS);

                        if (dnsServers.Any())
                        {
                            var dnsParams = networkAdapter.GetMethodParameters("SetDNSServerSearchOrder");
                            dnsParams["DNSServerSearchOrder"] = dnsServers.ToArray();
                            var dnsResult = (uint)networkAdapter.InvokeMethod("SetDNSServerSearchOrder", dnsParams, null);
                            
                            if (dnsResult != 0)
                            {
                                _logger.LogWarning($"Failed to set DNS servers. Return code: {dnsResult}");
                            }
                        }
                    }

                    // Set WINS servers
                    if (!string.IsNullOrEmpty(profile.PrimaryWINS) || !string.IsNullOrEmpty(profile.SecondaryWINS))
                    {
                        var winsServers = new List<string>();
                        if (!string.IsNullOrEmpty(profile.PrimaryWINS))
                            winsServers.Add(profile.PrimaryWINS);
                        if (!string.IsNullOrEmpty(profile.SecondaryWINS))
                            winsServers.Add(profile.SecondaryWINS);

                        var winsParams = networkAdapter.GetMethodParameters("SetWINSServer");
                        winsParams["WINSPrimaryServer"] = winsServers.FirstOrDefault();
                        winsParams["WINSSecondaryServer"] = winsServers.Skip(1).FirstOrDefault();

                        var winsResult = (uint)networkAdapter.InvokeMethod("SetWINSServer", winsParams, null);
                        if (winsResult != 0)
                        {
                            _logger.LogWarning($"Failed to set WINS servers. Return code: {winsResult}");
                        }
                    }

                    break;
                }

                // Wait for network stack to stabilize
                await Task.Delay(2000);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying network configuration");
                return false;
            }
        }

        private async Task BackupCurrentConfigurationAsync(string adapterName)
        {
            try
            {
                var adapter = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(ni => ni.Name == adapterName);

                if (adapter != null)
                {
                    var config = new NetworkConfiguration
                    {
                        AdapterName = adapterName,
                        BackupDate = DateTime.Now
                    };

                    var ipProps = adapter.GetIPProperties();
                    
                    // Backup IP addresses
                    config.IPAddresses = ipProps.UnicastAddresses
                        .Where(ua => ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        .Select(ua => new IPConfiguration
                        {
                            Address = ua.Address.ToString(),
                            SubnetMask = ua.IPv4Mask?.ToString() ?? ""
                        })
                        .ToList();

                    // Backup gateways
                    config.Gateways = ipProps.GatewayAddresses
                        .Where(ga => ga.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        .Select(ga => ga.Address.ToString())
                        .ToList();

                    // Backup DNS servers
                    config.DnsServers = ipProps.DnsAddresses
                        .Where(dns => dns.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        .Select(dns => dns.ToString())
                        .ToList();

                    _backupConfigurations[adapterName] = config;
                    _logger.LogInformation($"Backed up configuration for adapter '{adapterName}'");
                }

                await Task.Delay(1); // Make it async
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error backing up configuration for adapter '{adapterName}'");
            }
        }

        public async Task<bool> RestoreBackupConfigurationAsync(string adapterName)
        {
            try
            {
                if (!_backupConfigurations.TryGetValue(adapterName, out var backup))
                {
                    _logger.LogWarning($"No backup configuration found for adapter '{adapterName}'");
                    return false;
                }

                _logger.LogInformation($"Restoring backup configuration for adapter '{adapterName}'");

                using var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_NetworkAdapterConfiguration WHERE Description LIKE '%{adapterName}%'");

                foreach (ManagementObject networkAdapter in searcher.Get())
                {
                    if (backup.IPAddresses.Any())
                    {
                        // Restore static IP configuration
                        var ipParams = networkAdapter.GetMethodParameters("EnableStatic");
                        ipParams["IPAddress"] = backup.IPAddresses.Select(ip => ip.Address).ToArray();
                        ipParams["SubnetMask"] = backup.IPAddresses.Select(ip => ip.SubnetMask).ToArray();

                        var ipResult = (uint)networkAdapter.InvokeMethod("EnableStatic", ipParams, null);
                        if (ipResult != 0 && ipResult != 1)
                        {
                            _logger.LogError($"Failed to restore static IP. Return code: {ipResult}");
                            return false;
                        }

                        // Restore gateways
                        if (backup.Gateways.Any())
                        {
                            var gwParams = networkAdapter.GetMethodParameters("SetGateways");
                            gwParams["DefaultIPGateway"] = backup.Gateways.ToArray();
                            gwParams["GatewayCostMetric"] = Enumerable.Repeat(1, backup.Gateways.Count).ToArray();

                            var gwResult = (uint)networkAdapter.InvokeMethod("SetGateways", gwParams, null);
                            if (gwResult != 0 && gwResult != 1)
                            {
                                _logger.LogWarning($"Failed to restore gateways. Return code: {gwResult}");
                            }
                        }
                    }
                    else
                    {
                        // Enable DHCP if no static IPs were backed up
                        var dhcpResult = (uint)networkAdapter.InvokeMethod("EnableDHCP", null);
                        if (dhcpResult != 0)
                        {
                            _logger.LogError($"Failed to enable DHCP during restore. Return code: {dhcpResult}");
                        }
                    }

                    // Restore DNS servers
                    if (backup.DnsServers.Any())
                    {
                        var dnsParams = networkAdapter.GetMethodParameters("SetDNSServerSearchOrder");
                        dnsParams["DNSServerSearchOrder"] = backup.DnsServers.ToArray();
                        var dnsResult = (uint)networkAdapter.InvokeMethod("SetDNSServerSearchOrder", dnsParams, null);
                        
                        if (dnsResult != 0)
                        {
                            _logger.LogWarning($"Failed to restore DNS servers. Return code: {dnsResult}");
                        }
                    }

                    break;
                }

                await Task.Delay(2000); // Wait for network stack to stabilize
                _logger.LogInformation($"

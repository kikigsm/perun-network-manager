using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PerunNetworkManager.Core.Models;
using PerunNetworkManager.Core.Services;

namespace PerunNetworkManager.ViewModels
{
    public partial class NetworkScannerViewModel : ObservableObject
    {
        private readonly NetworkScannerService _scannerService;
        private readonly ILogger _logger;
        private CancellationTokenSource? _cancellationTokenSource;

        [ObservableProperty]
        private ObservableCollection<SubnetInfo> _availableSubnets = new();

        [ObservableProperty]
        private SubnetInfo? _selectedSubnet;

        [ObservableProperty]
        private ObservableCollection<ScannedDevice> _discoveredDevices = new();

        [ObservableProperty]
        private ObservableCollection<ScannedDevice> _filteredDevices = new();

        [ObservableProperty]
        private ScannedDevice? _selectedDevice;

        [ObservableProperty]
        private string _customSubnetRange = string.Empty;

        [ObservableProperty]
        private bool _isScanning;

        [ObservableProperty]
        private double _scanProgress;

        [ObservableProperty]
        private int _scannedHosts;

        [ObservableProperty]
        private int _totalHosts;

        [ObservableProperty]
        private int _discoveredCount;

        [ObservableProperty]
        private string _scanStatusText = "Ready to scan";

        [ObservableProperty]
        private string _deviceFilterText = string.Empty;

        [ObservableProperty]
        private DeviceType _selectedDeviceTypeFilter = DeviceType.Unknown;

        [ObservableProperty]
        private bool _showOnlineOnly = true;

        [ObservableProperty]
        private ScanOptions _scanOptions = new();

        [ObservableProperty]
        private TimeSpan _scanDuration;

        [ObservableProperty]
        private DateTime? _lastScanTime;

        [ObservableProperty]
        private bool _autoRefreshEnabled;

        [ObservableProperty]
        private int _autoRefreshInterval = 60; // seconds

        public NetworkScannerViewModel(NetworkScannerService scannerService, ILogger logger)
        {
            _scannerService = scannerService;
            _logger = logger;

            InitializeCommands();
            InitializeEventHandlers();
            _ = InitializeAsync();
        }

        private void InitializeCommands()
        {
            DiscoverSubnetsCommand = new AsyncRelayCommand(DiscoverSubnetsAsync);
            StartScanCommand = new AsyncRelayCommand(StartScanAsync, CanStartScan);
            StopScanCommand = new RelayCommand(StopScan, CanStopScan);
            RescanDeviceCommand = new AsyncRelayCommand<ScannedDevice>(RescanDeviceAsync);
            WakeOnLanCommand = new AsyncRelayCommand<ScannedDevice>(WakeOnLanAsync, CanWakeOnLan);
            ExportResultsCommand = new AsyncRelayCommand(ExportResultsAsync, CanExportResults);
            RefreshCommand = new AsyncRelayCommand(RefreshAsync);
            ClearResultsCommand = new RelayCommand(ClearResults, CanClearResults);
            ShowDeviceDetailsCommand = new RelayCommand<ScannedDevice>(ShowDeviceDetails);
            AddToWatchListCommand = new RelayCommand<ScannedDevice>(AddToWatchList);
            RemoveFromWatchListCommand = new RelayCommand<ScannedDevice>(RemoveFromWatchList);
            PingDeviceCommand = new AsyncRelayCommand<ScannedDevice>(PingDeviceAsync);
            TraceRouteCommand = new AsyncRelayCommand<ScannedDevice>(TraceRouteAsync);
            PortScanCommand = new AsyncRelayCommand<ScannedDevice>(PortScanAsync);
        }

        public ICommand DiscoverSubnetsCommand { get; private set; } = null!;
        public ICommand StartScanCommand { get; private set; } = null!;
        public ICommand StopScanCommand { get; private set; } = null!;
        public ICommand RescanDeviceCommand { get; private set; } = null!;
        public ICommand WakeOnLanCommand { get; private set; } = null!;
        public ICommand ExportResultsCommand { get; private set; } = null!;
        public ICommand RefreshCommand { get; private set; } = null!;
        public ICommand ClearResultsCommand { get; private set; } = null!;
        public ICommand ShowDeviceDetailsCommand { get; private set; } = null!;
        public ICommand AddToWatchListCommand { get; private set; } = null!;
        public ICommand RemoveFromWatchListCommand { get; private set; } = null!;
        public ICommand PingDeviceCommand { get; private set; } = null!;
        public ICommand TraceRouteCommand { get; private set; } = null!;
        public ICommand PortScanCommand { get; private set; } = null!;

        private void InitializeEventHandlers()
        {
            _scannerService.DeviceDiscovered += OnDeviceDiscovered;
            _scannerService.ScanProgress += OnScanProgress;
            _scannerService.ScanCompleted += OnScanCompleted;

            PropertyChanged += OnPropertyChanged;
        }

        private async Task InitializeAsync()
        {
            try
            {
                await DiscoverSubnetsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing network scanner");
                ScanStatusText = "Initialization failed";
            }
        }

        private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(DeviceFilterText):
                case nameof(SelectedDeviceTypeFilter):
                case nameof(ShowOnlineOnly):
                    FilterDevices();
                    break;
                case nameof(SelectedSubnet):
                    OnSelectedSubnetChanged();
                    break;
            }
        }

        private async Task DiscoverSubnetsAsync()
        {
            try
            {
                ScanStatusText = "Discovering subnets...";
                var subnets = await _scannerService.DiscoverSubnetsAsync();
                
                AvailableSubnets.Clear();
                foreach (var subnet in subnets)
                {
                    AvailableSubnets.Add(subnet);
                }

                if (AvailableSubnets.Any())
                {
                    SelectedSubnet = AvailableSubnets.First();
                }

                ScanStatusText = $"Found {subnets.Count} subnet(s)";
                _logger.LogInformation($"Discovered {subnets.Count} subnets");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error discovering subnets");
                ScanStatusText = "Failed to discover subnets";
            }
        }

        private void OnSelectedSubnetChanged()
        {
            if (SelectedSubnet != null)
            {
                TotalHosts = SelectedSubnet.UsableHosts;
                ScanStatusText = $"Selected {SelectedSubnet.NetworkRange} ({SelectedSubnet.UsableHosts} hosts)";
            }
        }

        private async Task StartScanAsync()
        {
            if (SelectedSubnet == null && string.IsNullOrEmpty(CustomSubnetRange))
                return;

            try
            {
                _cancellationTokenSource = new CancellationTokenSource();
                IsScanning = true;
                ScanProgress = 0;
                ScannedHosts = 0;
                DiscoveredCount = 0;
                DiscoveredDevices.Clear();
                FilteredDevices.Clear();

                var scanStartTime = DateTime.Now;
                ScanStatusText = "Starting network scan...";

                SubnetInfo targetSubnet;
                if (!string.IsNullOrEmpty(CustomSubnetRange))
                {
                    targetSubnet = ParseCustomSubnetRange(CustomSubnetRange);
                }
                else
                {
                    targetSubnet = SelectedSubnet!;
                }

                var devices = await _scannerService.ScanSubnetAsync(
                    targetSubnet, 
                    ScanOptions, 
                    _cancellationTokenSource.Token);

                ScanDuration = DateTime.Now - scanStartTime;
                LastScanTime = DateTime.Now;
                
                ScanStatusText = $"Scan completed. Found {devices.Count} devices in {ScanDuration.TotalSeconds:F1}s";
                
                FilterDevices();
                
                _logger.LogInformation($"Network scan completed. Found {devices.Count} devices in {ScanDuration.TotalSeconds:F1} seconds");
            }
            catch (OperationCanceledException)
            {
                ScanStatusText = "Scan cancelled";
                _logger.LogInformation("Network scan was cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during network scan");
                ScanStatusText = $"Scan failed: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private bool CanStartScan() => !IsScanning && (SelectedSubnet != null || !string.IsNullOrEmpty(CustomSubnetRange));

        private void StopScan()
        {
            _cancellationTokenSource?.Cancel();
            ScanStatusText = "Stopping scan...";
        }

        private bool CanStopScan() => IsScanning;

        private SubnetInfo ParseCustomSubnetRange(string range)
        {
            try
            {
                // Parse CIDR notation (e.g., "192.168.1.0/24")
                var parts = range.Split('/');
                if (parts.Length != 2)
                    throw new ArgumentException("Invalid CIDR format. Use format like 192.168.1.0/24");

                var networkIP = IPAddress.Parse(parts[0]);
                var cidr = int.Parse(parts[1]);
                
                if (cidr < 1 || cidr > 30)
                    throw new ArgumentException("CIDR prefix must be between 1 and 30");

                // Calculate subnet mask from CIDR
                var mask = 0xFFFFFFFF << (32 - cidr);
                var maskBytes = new byte[]
                {
                    (byte)(mask >> 24),
                    (byte)(mask >> 16),
                    (byte)(mask >> 8),
                    (byte)mask
                };
                
                var subnetMask = new IPAddress(maskBytes);
                return SubnetInfo.FromIPAndMask(networkIP, subnetMask);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Invalid subnet range format: {ex.Message}");
            }
        }

        private void OnDeviceDiscovered(object? sender, DeviceDiscoveredEventArgs e)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                DiscoveredDevices.Add(e.Device);
                DiscoveredCount = DiscoveredDevices.Count;
                
                if (ShouldShowDevice(e.Device))
                {
                    FilteredDevices.Add(e.Device);
                }
                
                ScanStatusText = $"Scanning... Found {DiscoveredCount} devices";
            });
        }

        private void OnScanProgress(object? sender, ScanProgressEventArgs e)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                ScanProgress = e.Progress;
                ScannedHosts = e.Completed;
                TotalHosts = e.Total;
                
                ScanStatusText = $"Scanning... {e.Completed}/{e.Total} hosts ({e.Progress:F1}%)";
            });
        }

        private void OnScanCompleted(object? sender, ScanCompletedEventArgs e)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                ScanProgress = 100;
                DiscoveredCount = e.Devices.Count;
                
                ScanStatusText = $"Scan completed. Found {e.Devices.Count} devices";
            });
        }

        private void FilterDevices()
        {
            FilteredDevices.Clear();
            
            var filtered = DiscoveredDevices.Where(ShouldShowDevice);
            
            foreach (var device in filtered)
            {
                FilteredDevices.Add(device);
            }
        }

        private bool ShouldShowDevice(ScannedDevice device)
        {
            // Filter by online status
            if (ShowOnlineOnly && !device.IsReachable)
                return false;

            // Filter by device type
            if (SelectedDeviceTypeFilter != DeviceType.Unknown && device.DeviceType != SelectedDeviceTypeFilter)
                return false;

            // Filter by text
            if (!string.IsNullOrWhiteSpace(DeviceFilterText))
            {
                var searchText = DeviceFilterText.ToLowerInvariant();
                return device.IPAddressString.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                       device.HostName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                       device.NetBiosName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                       device.MacAddress.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                       device.Vendor.Contains(searchText, StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        private async Task RescanDeviceAsync(ScannedDevice? device)
        {
            if (device == null) return;

            try
            {
                // Perform a fresh scan of the single device
                var subnet = new SubnetInfo
                {
                    FirstUsableIP = device.IPAddress,
                    LastUsableIP = device.IPAddress
                };

                var results = await _scannerService.ScanSubnetAsync(subnet, ScanOptions);
                var updatedDevice = results.FirstOrDefault();

                if (updatedDevice != null)
                {
                    // Update the device in our collections
                    var index = DiscoveredDevices.IndexOf(device);
                    if (index >= 0)
                    {
                        DiscoveredDevices[index] = updatedDevice;
                    }

                    FilterDevices();
                    _logger.LogInformation($"Rescanned device {device.IPAddressString}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error rescanning device {device.IPAddressString}");
            }
        }

        private async Task WakeOnLanAsync(ScannedDevice? device)
        {
            if (device == null) return;

            try
            {
                var success = await _scannerService.WakeOnLanAsync(device);
                if (success)
                {
                    ScanStatusText = $"Wake-on-LAN packet sent to {device.MacAddress}";
                    _logger.LogInformation($"Wake-on-LAN packet sent to {device.MacAddress}");
                }
                else
                {
                    ScanStatusText = $"Failed to send Wake-on-LAN packet to {device.MacAddress}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending Wake-on-LAN to {device.MacAddress}");
                ScanStatusText = $"Wake-on-LAN failed: {ex.Message}";
            }
        }

        private bool CanWakeOnLan(ScannedDevice? device) => 
            device != null && !string.IsNullOrEmpty(device.MacAddress);

        private async Task ExportResultsAsync()
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Export Scan Results",
                    Filter = "CSV Files (*.csv)|*.csv|XML Files (*.xml)|*.xml|JSON Files (*.json)|*.json",
                    DefaultExt = ".csv",
                    FileName = $"NetworkScan_{DateTime.Now:yyyyMMdd_HHmmss}"
                };

                if (dialog.ShowDialog() == true)
                {
                    await ExportToFileAsync(dialog.FileName, FilteredDevices.ToList());
                    ScanStatusText = $"Exported {FilteredDevices.Count} devices to {Path.GetFileName(dialog.FileName)}";
                    _logger.LogInformation($"Exported scan results to {dialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting scan results");
                ScanStatusText = $"Export failed: {ex.Message}";
            }
        }

        private bool CanExportResults() => FilteredDevices.Any();

        private async Task ExportToFileAsync(string fileName, List<ScannedDevice> devices)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            
            switch (extension)
            {
                case ".csv":
                    await ExportToCsvAsync(fileName, devices);
                    break;
                case ".xml":
                    await ExportToXmlAsync(fileName, devices);
                    break;
                case ".json":
                    await ExportToJsonAsync(fileName, devices);
                    break;
                default:
                    throw new ArgumentException("Unsupported file format");
            }
        }

        private async Task ExportToCsvAsync(string fileName, List<ScannedDevice> devices)
        {
            var csv = new StringBuilder();
            
            // Header
            csv.AppendLine("IP Address,MAC Address,Host Name,NetBIOS Name,Vendor,Device Type,Response Time,Status,Open Ports,Services,Operating System,First Seen,Last Seen,Notes");
            
            // Data
            foreach (var device in devices)
            {
                var data = device.ToExportData();
                var line = string.Join(",", data.Values.Select(v => $"\"{v}\""));
                csv.AppendLine(line);
            }
            
            await File.WriteAllTextAsync(fileName, csv.ToString());
        }

        private async Task ExportToXmlAsync(string fileName, List<ScannedDevice> devices)
        {
            var xml = new System.Xml.XmlDocument();
            var root = xml.CreateElement("NetworkScanResults");
            xml.AppendChild(root);
            
            var scanInfo = xml.CreateElement("ScanInfo");
            scanInfo.SetAttribute("Date", LastScanTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Unknown");
            scanInfo.SetAttribute("Duration", ScanDuration.ToString(@"hh\:mm\:ss"));
            scanInfo.SetAttribute("DeviceCount", devices.Count.ToString());
            root.AppendChild(scanInfo);
            
            var devicesElement = xml.CreateElement("Devices");
            root.AppendChild(devicesElement);
            
            foreach (var device in devices)
            {
                var deviceElement = xml.CreateElement("Device");
                var data = device.ToExportData();
                
                foreach (var kvp in data)
                {
                    deviceElement.SetAttribute(kvp.Key.Replace(" ", ""), kvp.Value?.ToString() ?? "");
                }
                
                devicesElement.AppendChild(deviceElement);
            }
            
            xml.Save(fileName);
            await Task.CompletedTask;
        }

        private async Task ExportToJsonAsync(string fileName, List<ScannedDevice> devices)
        {
            var exportData = new
            {
                ScanInfo = new
                {
                    Date = LastScanTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Unknown",
                    Duration = ScanDuration.ToString(@"hh\:mm\:ss"),
                    DeviceCount = devices.Count
                },
                Devices = devices.Select(d => d.ToExportData()).ToList()
            };
            
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(exportData, Newtonsoft.Json.Formatting.Indented);
            await File.WriteAllTextAsync(fileName, json);
        }

        private async Task RefreshAsync()
        {
            try
            {
                await DiscoverSubnetsAsync();
                ScanStatusText = "Refreshed subnets";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing");
                ScanStatusText = "Refresh failed";
            }
        }

        private void ClearResults()
        {
            DiscoveredDevices.Clear();
            FilteredDevices.Clear();
            DiscoveredCount = 0;
            ScanProgress = 0;
            ScannedHosts = 0;
            ScanStatusText = "Results cleared";
        }

        private bool CanClearResults() => DiscoveredDevices.Any();

        private void ShowDeviceDetails(ScannedDevice? device)
        {
            if (device == null) return;

            // TODO: Implement device details dialog
            var detailsWindow = new Views.DeviceDetailsWindow(device);
            detailsWindow.Show();
        }

        private void AddToWatchList(ScannedDevice? device)
        {
            if (device == null) return;
            
            device.IsWatched = true;
            ScanStatusText = $"Added {device.IPAddressString} to watch list";
            _logger.LogInformation($"Added device {device.IPAddressString} to watch list");
        }

        private void RemoveFromWatchList(ScannedDevice? device)
        {
            if (device == null) return;
            
            device.IsWatched = false;
            ScanStatusText = $"Removed {device.IPAddressString} from watch list";
            _logger.LogInformation($"Removed device {device.IPAddressString} from watch list");
        }

        private async Task PingDeviceAsync(ScannedDevice? device)
        {
            if (device == null) return;

            try
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                var reply = await ping.SendPingAsync(device.IPAddress, 5000);
                
                device.UpdateFromPing(reply);
                
                ScanStatusText = reply.Status == System.Net.NetworkInformation.IPStatus.Success
                    ? $"Ping to {device.IPAddressString}: {reply.RoundtripTime}ms"
                    : $"Ping to {device.IPAddressString}: {reply.Status}";
                    
                FilterDevices(); // Refresh display
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error pinging device {device.IPAddressString}");
                ScanStatusText = $"Ping failed: {ex.Message}";
            }
        }

        private async Task TraceRouteAsync(ScannedDevice? device)
        {
            if (device == null) return;

            try
            {
                // TODO: Implement traceroute functionality
                var traceWindow = new Views.TraceRouteWindow(device.IPAddress);
                traceWindow.Show();
                
                ScanStatusText = $"Starting traceroute to {device.IPAddressString}";
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error starting traceroute to {device.IPAddressString}");
                ScanStatusText = $"Traceroute failed: {ex.Message}";
            }
        }

        private async Task PortScanAsync(ScannedDevice? device)
        {
            if (device == null) return;

            try
            {
                ScanStatusText = $"Scanning ports on {device.IPAddressString}...";
                
                var subnet = new SubnetInfo
                {
                    FirstUsableIP = device.IPAddress,
                    LastUsableIP = device.IPAddress
                };
                
                var portScanOptions = new ScanOptions
                {
                    EnablePortScan = true,
                    MaxConcurrentPortScans = 20,
                    PortScanTimeout = 1000
                };
                
                var results = await _scannerService.ScanSubnetAsync(subnet, portScanOptions);
                var updatedDevice = results.FirstOrDefault();
                
                if (updatedDevice != null)
                {
                    device.OpenPorts = updatedDevice.OpenPorts;
                    device.Services = updatedDevice.Services;
                    device.DetermineDeviceType();
                    
                    ScanStatusText = $"Found {device.OpenPorts.Count} open ports on {device.IPAddressString}";
                    FilterDevices(); // Refresh display
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error scanning ports on {device.IPAddressString}");
                ScanStatusText = $"Port scan failed: {ex.Message}";
            }
        }

        protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            
            // Update command can execute states
            ((AsyncRelayCommand)StartScanCommand).NotifyCanExecuteChanged();
            ((RelayCommand)StopScanCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)ExportResultsCommand).NotifyCanExecuteChanged();
            ((RelayCommand)ClearResultsCommand).NotifyCanExecuteChanged();
        }

        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            
            _scannerService.DeviceDiscovered -= OnDeviceDiscovered;
            _scannerService.ScanProgress -= OnScanProgress;
            _scannerService.ScanCompleted -= OnScanCompleted;
        }
    }
}

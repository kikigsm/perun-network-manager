using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.Logging;
using PerunNetworkManager.Core.Models;
using PerunNetworkManager.Core.Services;
using PerunNetworkManager.Helpers;
using PerunNetworkManager.Services;

namespace PerunNetworkManager.ViewModels
{
    /// <summary>
    /// ViewModel for managing network adapters.
    /// </summary>
    public class NetworkAdapterViewModel : INotifyPropertyChanged
    {
        private readonly ILogger<NetworkAdapterViewModel> _logger;
        private readonly NetworkService _networkService;
        private readonly DispatcherTimer _refreshTimer;
        private readonly SemaphoreSlim _refreshSemaphore;
        
        private ObservableCollection<NetworkAdapterInfo> _adapters;
        private ObservableCollection<NetworkAdapterInfo> _filteredAdapters;
        private bool _showDisabledAdapters = true;
        private bool _isLoading;
        private NetworkAdapterInfo _selectedAdapter;

        // For IP configuration dialog
        private bool _configureDHCP;
        private string _configureIP;
        private string _configureSubnet;
        private string _configureGateway;
        private string _configureDNS1;
        private string _configureDNS2;
        private bool _configureAutoDNS;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Gets or sets the collection of network adapters.
        /// </summary>
        public ObservableCollection<NetworkAdapterInfo> Adapters
        {
            get => _adapters;
            set
            {
                _adapters = value;
                OnPropertyChanged();
                UpdateFilteredAdapters();
            }
        }

        /// <summary>
        /// Gets the filtered collection of network adapters.
        /// </summary>
        public ObservableCollection<NetworkAdapterInfo> FilteredAdapters
        {
            get => _filteredAdapters;
            private set
            {
                _filteredAdapters = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Gets or sets whether to show disabled adapters.
        /// </summary>
        public bool ShowDisabledAdapters
        {
            get => _showDisabledAdapters;
            set
            {
                _showDisabledAdapters = value;
                OnPropertyChanged();
                UpdateFilteredAdapters();
            }
        }

        /// <summary>
        /// Gets or sets whether data is being loaded.
        /// </summary>
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }

        #region Configuration Properties

        public bool ConfigureDHCP
        {
            get => _configureDHCP;
            set { _configureDHCP = value; OnPropertyChanged(); }
        }

        public string ConfigureIP
        {
            get => _configureIP;
            set { _configureIP = value; OnPropertyChanged(); }
        }

        public string ConfigureSubnet
        {
            get => _configureSubnet;
            set { _configureSubnet = value; OnPropertyChanged(); }
        }

        public string ConfigureGateway
        {
            get => _configureGateway;
            set { _configureGateway = value; OnPropertyChanged(); }
        }

        public string ConfigureDNS1
        {
            get => _configureDNS1;
            set { _configureDNS1 = value; OnPropertyChanged(); }
        }

        public string ConfigureDNS2
        {
            get => _configureDNS2;
            set { _configureDNS2 = value; OnPropertyChanged(); }
        }

        public bool ConfigureAutoDNS
        {
            get => _configureAutoDNS;
            set { _configureAutoDNS = value; OnPropertyChanged(); }
        }

        #endregion

        #region Commands

        public ICommand RefreshCommand { get; }
        public ICommand EnableDisableCommand { get; }
        public ICommand RenameCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand ViewDetailsCommand { get; }
        public ICommand OpenPropertiesCommand { get; }
        public ICommand DiagnoseCommand { get; }
        public ICommand ConfigureIPCommand { get; }
        public ICommand ApplyIPConfigCommand { get; }
        public ICommand OpenNetworkConnectionsCommand { get; }

        #endregion

        public NetworkAdapterViewModel(ILogger<NetworkAdapterViewModel> logger, NetworkService networkService)
        {
            _logger = logger;
            _networkService = networkService;
            _refreshSemaphore = new SemaphoreSlim(1, 1);

            Adapters = new ObservableCollection<NetworkAdapterInfo>();
            FilteredAdapters = new ObservableCollection<NetworkAdapterInfo>();

            // Initialize commands
            RefreshCommand = new RelayCommand(async () => await RefreshAdaptersAsync());
            EnableDisableCommand = new RelayCommand<NetworkAdapterInfo>(async adapter => await EnableDisableAdapterAsync(adapter));
            RenameCommand = new RelayCommand<NetworkAdapterInfo>(async adapter => await RenameAdapterAsync(adapter));
            ResetCommand = new RelayCommand<NetworkAdapterInfo>(async adapter => await ResetAdapterAsync(adapter));
            ViewDetailsCommand = new RelayCommand<NetworkAdapterInfo>(ViewAdapterDetails);
            OpenPropertiesCommand = new RelayCommand<NetworkAdapterInfo>(OpenAdapterProperties);
            DiagnoseCommand = new RelayCommand<NetworkAdapterInfo>(async adapter => await DiagnoseAdapterAsync(adapter));
            ConfigureIPCommand = new RelayCommand<NetworkAdapterInfo>(ConfigureIPAddress);
            ApplyIPConfigCommand = new RelayCommand(async () => await ApplyIPConfigurationAsync());
            OpenNetworkConnectionsCommand = new RelayCommand(OpenNetworkConnections);

            // Setup refresh timer
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _refreshTimer.Tick += async (s, e) => await RefreshAdapterStatisticsAsync();

            // Initial load
            Task.Run(async () => await RefreshAdaptersAsync());
        }

        /// <summary>
        /// Refreshes the list of network adapters.
        /// </summary>
        private async Task RefreshAdaptersAsync()
        {
            await _refreshSemaphore.WaitAsync();
            try
            {
                IsLoading = true;
                _logger.LogInformation("Refreshing network adapters");

                var adapters = await Task.Run(() => GetNetworkAdapters());
                
                App.Current.Dispatcher.Invoke(() =>
                {
                    Adapters.Clear();
                    foreach (var adapter in adapters)
                    {
                        Adapters.Add(adapter);
                    }
                });

                _refreshTimer.Start();
                _logger.LogInformation($"Found {adapters.Count} network adapters");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing network adapters");
                ShowSnackbar("Failed to refresh network adapters");
            }
            finally
            {
                IsLoading = false;
                _refreshSemaphore.Release();
            }
        }

        /// <summary>
        /// Gets all network adapters from the system.
        /// </summary>
        private List<NetworkAdapterInfo> GetNetworkAdapters()
        {
            var adapters = new List<NetworkAdapterInfo>();

            try
            {
                var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
                
                foreach (var ni in networkInterfaces)
                {
                    var adapter = new NetworkAdapterInfo
                    {
                        Id = ni.Id,
                        Name = ni.Name,
                        Description = ni.Description,
                        AdapterType = GetAdapterType(ni.NetworkInterfaceType),
                        Status = ni.OperationalStatus.ToString(),
                        IsEnabled = ni.OperationalStatus != OperationalStatus.Down,
                        Speed = ni.Speed,
                        MACAddress = GetMacAddress(ni),
                        LastUpdated = DateTime.Now
                    };

                    // Get IP configuration
                    var ipProperties = ni.GetIPProperties();
                    var ipv4Properties = ipProperties.GetIPv4Properties();
                    
                    foreach (var unicast in ipProperties.UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            adapter.IPAddress = unicast.Address.ToString();
                            adapter.SubnetMask = unicast.IPv4Mask?.ToString();
                            break;
                        }
                    }

                    // Get gateway
                    var gateway = ipProperties.GatewayAddresses
                        .FirstOrDefault(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                    if (gateway != null)
                    {
                        adapter.Gateway = gateway.Address.ToString();
                    }

                    // Get DNS servers
                    var dnsServers = ipProperties.DnsAddresses
                        .Where(dns => dns.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        .Select(dns => dns.ToString())
                        .ToList();
                    adapter.DnsServers = string.Join(", ", dnsServers);

                    // Get DHCP status
                    if (ipv4Properties != null)
                    {
                        adapter.IsDHCPEnabled = ipv4Properties.IsDhcpEnabled;
                    }

                    // Get statistics
                    var stats = ni.GetIPStatistics();
                    adapter.BytesSent = stats.BytesSent;
                    adapter.BytesReceived = stats.BytesReceived;
                    adapter.PacketsSent = stats.UnicastPacketsSent;
                    adapter.PacketsReceived = stats.UnicastPacketsReceived;

                    // Get manufacturer using WMI
                    try
                    {
                        adapter.Manufacturer = GetAdapterManufacturer(adapter.Description);
                    }
                    catch { }

                    adapters.Add(adapter);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting network adapters");
            }

            return adapters;
        }

        /// <summary>
        /// Gets adapter type from NetworkInterfaceType.
        /// </summary>
        private AdapterType GetAdapterType(NetworkInterfaceType type)
        {
            return type switch
            {
                NetworkInterfaceType.Ethernet => AdapterType.Ethernet,
                NetworkInterfaceType.Wireless80211 => AdapterType.Wireless,
                NetworkInterfaceType.Loopback => AdapterType.Loopback,
                NetworkInterfaceType.Tunnel => AdapterType.Virtual,
                _ => AdapterType.Unknown
            };
        }

        /// <summary>
        /// Gets MAC address from network interface.
        /// </summary>
        private string GetMacAddress(NetworkInterface ni)
        {
            var mac = ni.GetPhysicalAddress();
            if (mac != null && mac.ToString().Length > 0)
            {
                return string.Join(":", mac.GetAddressBytes().Select(b => b.ToString("X2")));
            }
            return null;
        }

        /// <summary>
        /// Gets adapter manufacturer using WMI.
        /// </summary>
        private string GetAdapterManufacturer(string description)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_NetworkAdapter WHERE Description = '" + description.Replace("'", "''") + "'"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        return obj["Manufacturer"]?.ToString();
                    }
                }
            }
            catch { }
            
            return "Unknown";
        }

        /// <summary>
        /// Updates filtered adapters based on current settings.
        /// </summary>
        private void UpdateFilteredAdapters()
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                FilteredAdapters.Clear();
                
                var filtered = ShowDisabledAdapters 
                    ? Adapters 
                    : Adapters.Where(a => a.IsEnabled);

                foreach (var adapter in filtered)
                {
                    FilteredAdapters.Add(adapter);
                }
            });
        }

        /// <summary>
        /// Refreshes adapter statistics only.
        /// </summary>
        private async Task RefreshAdapterStatisticsAsync()
        {
            if (_refreshSemaphore.CurrentCount == 0)
                return; // Skip if already refreshing

            await Task.Run(() =>
            {
                try
                {
                    var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
                    
                    foreach (var adapter in Adapters)
                    {
                        var ni = networkInterfaces.FirstOrDefault(n => n.Id == adapter.Id);
                        if (ni != null)
                        {
                            var stats = ni.GetIPStatistics();
                            
                            App.Current.Dispatcher.Invoke(() =>
                            {
                                adapter.BytesSent = stats.BytesSent;
                                adapter.BytesReceived = stats.BytesReceived;
                                adapter.PacketsSent = stats.UnicastPacketsSent;
                                adapter.PacketsReceived = stats.UnicastPacketsReceived;
                                adapter.Status = ni.OperationalStatus.ToString();
                                adapter.IsEnabled = ni.OperationalStatus != OperationalStatus.Down;
                                adapter.LastUpdated = DateTime.Now;
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error refreshing adapter statistics");
                }
            });
        }

        /// <summary>
        /// Enables or disables a network adapter.
        /// </summary>
        private async Task EnableDisableAdapterAsync(NetworkAdapterInfo adapter)
        {
            try
            {
                _logger.LogInformation($"{(adapter.IsEnabled ? "Disabling" : "Enabling")} adapter: {adapter.Name}");
                
                var action = adapter.IsEnabled ? "disable" : "enable";
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = $"interface set interface \"{adapter.Name}\" {action}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        Verb = "runas" // Requires admin
                    }
                };

                await Task.Run(() =>
                {
                    process.Start();
                    process.WaitForExit();
                });

                if (process.ExitCode == 0)
                {
                    ShowSnackbar($"Adapter {adapter.Name} {action}d successfully");
                    await RefreshAdaptersAsync();
                }
                else
                {
                    ShowSnackbar($"Failed to {action} adapter. Admin rights required.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error toggling adapter: {adapter.Name}");
                ShowSnackbar("Failed to change adapter state");
            }
        }

        /// <summary>
        /// Renames a network adapter.
        /// </summary>
        private async Task RenameAdapterAsync(NetworkAdapterInfo adapter)
        {
            try
            {
                var dialog = new TextInputDialog
                {
                    Title = "Rename Network Adapter",
                    Message = "Enter new name for the adapter:",
                    InputText = adapter.Name
                };

                if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "netsh",
                            Arguments = $"interface set interface name=\"{adapter.Name}\" newname=\"{dialog.InputText}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            Verb = "runas"
                        }
                    };

                    await Task.Run(() =>
                    {
                        process.Start();
                        process.WaitForExit();
                    });

                    if (process.ExitCode == 0)
                    {
                        ShowSnackbar("Adapter renamed successfully");
                        await RefreshAdaptersAsync();
                    }
                    else
                    {
                        ShowSnackbar("Failed to rename adapter. Admin rights required.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error renaming adapter: {adapter.Name}");
                ShowSnackbar("Failed to rename adapter");
            }
        }

        /// <summary>
        /// Resets a network adapter.
        /// </summary>
        private async Task ResetAdapterAsync(NetworkAdapterInfo adapter)
        {
            try
            {
                var result = await DialogHost.Show(
                    "Are you sure you want to reset this adapter? This will disable and re-enable it.",
                    "AdapterDialogHost");

                if ((bool?)result == true)
                {
                    _logger.LogInformation($"Resetting adapter: {adapter.Name}");
                    
                    // Disable adapter
                    var disableProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "netsh",
                            Arguments = $"interface set interface \"{adapter.Name}\" disable",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            Verb = "runas"
                        }
                    };

                    await Task.Run(() =>
                    {
                        disableProcess.Start();
                        disableProcess.WaitForExit();
                        Thread.Sleep(2000); // Wait 2 seconds
                    });

                    // Enable adapter
                    var enableProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "netsh",
                            Arguments = $"interface set interface \"{adapter.Name}\" enable",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            Verb = "runas"
                        }
                    };

                    await Task.Run(() =>
                    {
                        enableProcess.Start();
                        enableProcess.WaitForExit();
                    });

                    ShowSnackbar("Adapter reset successfully");
                    await RefreshAdaptersAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error resetting adapter: {adapter.Name}");
                ShowSnackbar("Failed to reset adapter");
            }
        }

        /// <summary>
        /// Views detailed information about an adapter.
        /// </summary>
        private void ViewAdapterDetails(NetworkAdapterInfo adapter)
        {
            try
            {
                var window = new AdapterDetailsWindow(adapter);
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error showing adapter details");
            }
        }

        /// <summary>
        /// Opens Windows network adapter properties.
        /// </summary>
        private void OpenAdapterProperties(NetworkAdapterInfo adapter)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ncpa.cpl",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error opening network connections");
            }
        }

        /// <summary>
        /// Diagnoses adapter issues.
        /// </summary>
        private async Task DiagnoseAdapterAsync(NetworkAdapterInfo adapter)
        {
            try
            {
                _logger.LogInformation($"Diagnosing adapter: {adapter.Name}");
                
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "msdt.exe",
                        Arguments = "/id NetworkDiagnosticsNetworkAdapter",
                        UseShellExecute = true
                    }
                };

                await Task.Run(() =>
                {
                    process.Start();
                });

                ShowSnackbar("Network diagnostics launched");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error launching network diagnostics");
                ShowSnackbar("Failed to launch diagnostics");
            }
        }

        /// <summary>
        /// Opens IP configuration dialog for an adapter.
        /// </summary>
        private async void ConfigureIPAddress(NetworkAdapterInfo adapter)
        {
            _selectedAdapter = adapter;
            
            // Set current values
            ConfigureDHCP = adapter.IsDHCPEnabled;
            ConfigureIP = adapter.IPAddress;
            ConfigureSubnet = adapter.SubnetMask;
            ConfigureGateway = adapter.Gateway;
            
            var dnsServers = adapter.DnsServers?.Split(',').Select(s => s.Trim()).ToArray();
            ConfigureDNS1 = dnsServers?.Length > 0 ? dnsServers[0] : "";
            ConfigureDNS2 = dnsServers?.Length > 1 ? dnsServers[1] : "";
            ConfigureAutoDNS = string.IsNullOrEmpty(adapter.DnsServers);

            await DialogHost.Show(this, "AdapterDialogHost");
        }

        /// <summary>
        /// Applies IP configuration to the selected adapter.
        /// </summary>
        private async Task ApplyIPConfigurationAsync()
        {
            try
            {
                if (_selectedAdapter == null)
                    return;

                _logger.LogInformation($"Applying IP configuration to adapter: {_selectedAdapter.Name}");

                if (ConfigureDHCP)
                {
                    // Enable DHCP
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "netsh",
                            Arguments = $"interface ip set address \"{_selectedAdapter.Name}\" dhcp",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            Verb = "runas"
                        }
                    };

                    await Task.Run(() =>
                    {
                        process.Start();
                        process.WaitForExit();
                    });

                    if (ConfigureAutoDNS)
                    {
                        // Enable automatic DNS
                        var dnsProcess = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "netsh",
                                Arguments = $"interface ip set dns \"{_selectedAdapter.Name}\" dhcp",
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                Verb = "runas"
                            }
                        };

                        await Task.Run(() =>
                        {
                            dnsProcess.Start();
                            dnsProcess.WaitForExit();
                        });
                    }
                }
                else
                {
                    // Set static IP
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "netsh",
                            Arguments = $"interface ip set address \"{_selectedAdapter.Name}\" static {ConfigureIP} {ConfigureSubnet} {ConfigureGateway}",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            Verb = "runas"
                        }
                    };

                    await Task.Run(() =>
                    {
                        process.Start();
                        process.WaitForExit();
                    });
                }

                // Set DNS servers if not automatic
                if (!ConfigureAutoDNS)
                {
                    if (!string.IsNullOrWhiteSpace(ConfigureDNS1))
                    {
                        var dns1Process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "netsh",
                                Arguments = $"interface ip set dns \"{_selectedAdapter.Name}\" static {ConfigureDNS1} primary",
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                Verb = "runas"
                            }
                        };

                        await Task.Run(() =>
                        {
                            dns1Process.Start();
                            dns1Process.WaitForExit();
                        });
                    }

                    if (!string.IsNullOrWhiteSpace(ConfigureDNS2))
                    {
                        var dns2Process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "netsh",
                                Arguments = $"interface ip add dns \"{_selectedAdapter.Name}\" {ConfigureDNS2} index=2",
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                Verb = "runas"
                            }
                        };

                        await Task.Run(() =>
                        {
                            dns2Process.Start();
                            dns2Process.WaitForExit();
                        });
                    }
                }

                DialogHost.CloseDialogCommand.Execute(true, null);
                ShowSnackbar("IP configuration applied successfully");
                
                // Refresh to show new settings
                await Task.Delay(2000); // Give Windows time to apply settings
                await RefreshAdaptersAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying IP configuration");
                ShowSnackbar("Failed to apply IP configuration. Admin rights required.");
            }
        }

        /// <summary>
        /// Opens Windows Network Connections control panel.
        /// </summary>
        private void OpenNetworkConnections()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ncpa.cpl",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error opening network connections");
                ShowSnackbar("Failed to open network connections");
            }
        }

        /// <summary>
        /// Shows a message in the snackbar.
        /// </summary>
        private void ShowSnackbar(string message)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                var snackbar = App.Current.MainWindow?.FindName("AdapterSnackbar") as Snackbar;
                snackbar?.MessageQueue?.Enqueue(message);
            });
        }

        /// <summary>
        /// Starts the refresh timer.
        /// </summary>
        public void StartRefreshTimer()
        {
            _refreshTimer?.Start();
        }

        /// <summary>
        /// Stops the refresh timer.
        /// </summary>
        public void StopRefreshTimer()
        {
            _refreshTimer?.Stop();
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Represents information about a network adapter.
    /// </summary>
    public class NetworkAdapterInfo : INotifyPropertyChanged
    {
        private string _status;
        private bool _isEnabled;
        private long _bytesSent;
        private long _bytesReceived;
        private long _packetsSent;
        private long _packetsReceived;
        private DateTime _lastUpdated;
        private bool _showStatistics;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public AdapterType AdapterType { get; set; }
        
        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged();
            }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                OnPropertyChanged();
            }
        }

        public string IPAddress { get; set; }
        public string SubnetMask { get; set; }
        public string Gateway { get; set; }
        public string DnsServers { get; set; }
        public bool IsDHCPEnabled { get; set; }
        public string MACAddress { get; set; }
        public long Speed { get; set; }
        public string Manufacturer { get; set; }

        public long BytesSent
        {
            get => _bytesSent;
            set
            {
                _bytesSent = value;
                OnPropertyChanged();
            }
        }

        public long BytesReceived
        {
            get => _bytesReceived;
            set
            {
                _bytesReceived = value;
                OnPropertyChanged();
            }
        }

        public long PacketsSent
        {
            get => _packetsSent;
            set
            {
                _packetsSent = value;
                OnPropertyChanged();
            }
        }

        public long PacketsReceived
        {
            get => _packetsReceived;
            set
            {
                _packetsReceived = value;
                OnPropertyChanged();
            }
        }

        public DateTime LastUpdated
        {
            get => _lastUpdated;
            set
            {
                _lastUpdated = value;
                OnPropertyChanged();
            }
        }

        public bool ShowStatistics
        {
            get => _showStatistics;
            set
            {
                _showStatistics = value;
                OnPropertyChanged();
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Adapter type enumeration.
    /// </summary>
    public enum AdapterType
    {
        Unknown,
        Ethernet,
        Wireless,
        Virtual,
        Loopback
    }

    /// <summary>
    /// Simple text input dialog.
    /// </summary>
    public class TextInputDialog : Window
    {
        public string InputText { get; set; }
        public string Message { get; set; }

        public TextInputDialog()
        {
            // Simple dialog implementation
            Width = 400;
            Height = 200;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
    }

    /// <summary>
    /// Adapter details window.
    /// </summary>
    public class AdapterDetailsWindow : Window
    {
        public AdapterDetailsWindow(NetworkAdapterInfo adapter)
        {
            Title = $"Adapter Details - {adapter.Name}";
            Width = 600;
            Height = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            // Implementation would show detailed adapter information
        }
    }
}

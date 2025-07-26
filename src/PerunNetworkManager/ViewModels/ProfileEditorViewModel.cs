using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PerunNetworkManager.Core.Models;
using PerunNetworkManager.Core.Services;

namespace PerunNetworkManager.ViewModels
{
    public partial class ProfileEditorViewModel : ObservableObject
    {
        private readonly NetworkService _networkService;
        private readonly ILogger<ProfileEditorViewModel> _logger;

        [ObservableProperty]
        private NetworkProfile _profile;

        [ObservableProperty]
        private ObservableCollection<NetworkAdapter> _availableAdapters = new();

        [ObservableProperty]
        private NetworkAdapter? _selectedAdapter;

        [ObservableProperty]
        private bool _isNewProfile;

        [ObservableProperty]
        private bool _isDirty;

        [ObservableProperty]
        private bool _isValid = true;

        [ObservableProperty]
        private string _validationErrors = string.Empty;

        [ObservableProperty]
        private ObservableCollection<string> _dnsServerTemplates = new();

        [ObservableProperty]
        private ObservableCollection<CustomScript> _scripts = new();

        [ObservableProperty]
        private CustomScript? _selectedScript;

        [ObservableProperty]
        private ObservableCollection<PrinterConfiguration> _printers = new();

        [ObservableProperty]
        private PrinterConfiguration? _selectedPrinter;

        public ProfileEditorViewModel(NetworkProfile profile, NetworkService networkService, ILogger<ProfileEditorViewModel> logger)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _networkService = networkService ?? throw new ArgumentNullException(nameof(networkService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _isNewProfile = profile.Id == Guid.Empty || string.IsNullOrEmpty(profile.Name) || profile.Name == "New Profile";

            InitializeCommands();
            InitializeDnsTemplates();
            LoadNetworkAdapters();
            
            // Initialize collections from profile
            Scripts = new ObservableCollection<CustomScript>(profile.Scripts);
            Printers = new ObservableCollection<PrinterConfiguration>(profile.Printers);

            // Subscribe to property changes for validation and dirty tracking
            PropertyChanged += OnPropertyChanged;
            Profile.PropertyChanged += OnProfilePropertyChanged;

            ValidateProfile();
        }

        private void InitializeCommands()
        {
            SaveCommand = new AsyncRelayCommand(SaveAsync, CanSave);
            CancelCommand = new RelayCommand(Cancel);
            TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, CanTestConnection);
            ApplyDnsTemplateCommand = new RelayCommand<string>(ApplyDnsTemplate);
            AddScriptCommand = new RelayCommand(AddScript);
            EditScriptCommand = new RelayCommand<CustomScript>(EditScript);
            DeleteScriptCommand = new RelayCommand<CustomScript>(DeleteScript);
            AddPrinterCommand = new RelayCommand(AddPrinter);
            EditPrinterCommand = new RelayCommand<PrinterConfiguration>(EditPrinter);
            DeletePrinterCommand = new RelayCommand<PrinterConfiguration>(DeletePrinter);
            GenerateRandomMacCommand = new RelayCommand(GenerateRandomMac);
            DetectCurrentSettingsCommand = new AsyncRelayCommand(DetectCurrentSettingsAsync);
        }

        public ICommand SaveCommand { get; private set; } = null!;
        public ICommand CancelCommand { get; private set; } = null!;
        public ICommand TestConnectionCommand { get; private set; } = null!;
        public ICommand ApplyDnsTemplateCommand { get; private set; } = null!;
        public ICommand AddScriptCommand { get; private set; } = null!;
        public ICommand EditScriptCommand { get; private set; } = null!;
        public ICommand DeleteScriptCommand { get; private set; } = null!;
        public ICommand AddPrinterCommand { get; private set; } = null!;
        public ICommand EditPrinterCommand { get; private set; } = null!;
        public ICommand DeletePrinterCommand { get; private set; } = null!;
        public ICommand GenerateRandomMacCommand { get; private set; } = null!;
        public ICommand DetectCurrentSettingsCommand { get; private set; } = null!;

        public event EventHandler<ProfileSavedEventArgs>? ProfileSaved;
        public event EventHandler? EditCancelled;

        private void InitializeDnsTemplates()
        {
            DnsServerTemplates.Add("Google DNS (8.8.8.8, 8.8.4.4)");
            DnsServerTemplates.Add("Cloudflare DNS (1.1.1.1, 1.0.0.1)");
            DnsServerTemplates.Add("OpenDNS (208.67.222.222, 208.67.220.220)");
            DnsServerTemplates.Add("Quad9 DNS (9.9.9.9, 149.112.112.112)");
            DnsServerTemplates.Add("Comodo DNS (8.26.56.26, 8.20.247.20)");
            DnsServerTemplates.Add("AdGuard DNS (94.140.14.14, 94.140.15.15)");
        }

        private async void LoadNetworkAdapters()
        {
            try
            {
                var adapters = await _networkService.GetNetworkAdaptersAsync();
                AvailableAdapters.Clear();
                
                foreach (var adapter in adapters.Where(a => a.InterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback))
                {
                    AvailableAdapters.Add(adapter);
                }

                // Select the adapter that matches the profile's adapter name
                if (!string.IsNullOrEmpty(Profile.AdapterName))
                {
                    SelectedAdapter = AvailableAdapters.FirstOrDefault(a => a.Name == Profile.AdapterName);
                }
                else
                {
                    // Select the first active adapter
                    SelectedAdapter = AvailableAdapters.FirstOrDefault(a => a.Status == System.Net.NetworkInformation.OperationalStatus.Up);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load network adapters");
            }
        }

        private void OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(IsDirty) && e.PropertyName != nameof(IsValid))
            {
                IsDirty = true;
                ValidateProfile();
            }
        }

        private void OnProfilePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            IsDirty = true;
            ValidateProfile();
        }

        private void ValidateProfile()
        {
            var errors = new List<string>();

            // Validate profile name
            if (string.IsNullOrWhiteSpace(Profile.Name))
            {
                errors.Add("Profile name is required");
            }

            // Validate IP configuration for static profiles
            if (!Profile.UseDHCP)
            {
                if (string.IsNullOrWhiteSpace(Profile.IPAddress) || !IsValidIPAddress(Profile.IPAddress))
                {
                    errors.Add("Valid IP address is required for static configuration");
                }

                if (string.IsNullOrWhiteSpace(Profile.SubnetMask) || !IsValidIPAddress(Profile.SubnetMask))
                {
                    errors.Add("Valid subnet mask is required for static configuration");
                }

                if (!string.IsNullOrWhiteSpace(Profile.DefaultGateway) && !IsValidIPAddress(Profile.DefaultGateway))
                {
                    errors.Add("Default gateway must be a valid IP address if specified");
                }
            }

            // Validate DNS servers
            if (!string.IsNullOrWhiteSpace(Profile.PrimaryDNS) && !IsValidIPAddress(Profile.PrimaryDNS))
            {
                errors.Add("Primary DNS must be a valid IP address if specified");
            }

            if (!string.IsNullOrWhiteSpace(Profile.SecondaryDNS) && !IsValidIPAddress(Profile.SecondaryDNS))
            {
                errors.Add("Secondary DNS must be a valid IP address if specified");
            }

            // Validate WINS servers
            if (!string.IsNullOrWhiteSpace(Profile.PrimaryWINS) && !IsValidIPAddress(Profile.PrimaryWINS))
            {
                errors.Add("Primary WINS must be a valid IP address if specified");
            }

            if (!string.IsNullOrWhiteSpace(Profile.SecondaryWINS) && !IsValidIPAddress(Profile.SecondaryWINS))
            {
                errors.Add("Secondary WINS must be a valid IP address if specified");
            }

            // Validate proxy settings
            if (Profile.Proxy.UseProxy)
            {
                if (string.IsNullOrWhiteSpace(Profile.Proxy.Server))
                {
                    errors.Add("Proxy server is required when proxy is enabled");
                }

                if (Profile.Proxy.Port <= 0 || Profile.Proxy.Port > 65535)
                {
                    errors.Add("Proxy port must be between 1 and 65535");
                }
            }

            // Validate MAC address
            if (!string.IsNullOrWhiteSpace(Profile.MacAddress) && !IsValidMacAddress(Profile.MacAddress))
            {
                errors.Add("MAC address format is invalid");
            }

            IsValid = !errors.Any();
            ValidationErrors = string.Join(Environment.NewLine, errors);
        }

        private bool IsValidIPAddress(string? ipAddress)
        {
            return !string.IsNullOrWhiteSpace(ipAddress) && 
                   System.Net.IPAddress.TryParse(ipAddress, out _);
        }

        private bool IsValidMacAddress(string macAddress)
        {
            // Accept various MAC address formats: 00:11:22:33:44:55, 00-11-22-33-44-55, 001122334455
            var cleanMac = macAddress.Replace(":", "").Replace("-", "").Replace(".", "");
            return cleanMac.Length == 12 && cleanMac.All(c => Uri.IsHexDigit(c));
        }

        private bool CanSave() => IsValid && IsDirty;

        private async Task SaveAsync()
        {
            try
            {
                if (!IsValid)
                {
                    _logger.LogWarning("Cannot save invalid profile");
                    return;
                }

                // Update profile with current selections
                if (SelectedAdapter != null)
                {
                    Profile.AdapterName = SelectedAdapter.Name;
                }

                // Update collections
                Profile.Scripts = Scripts.ToList();
                Profile.Printers = Printers.ToList();

                // Set timestamps
                if (IsNewProfile)
                {
                    Profile.CreatedDate = DateTime.Now;
                }
                Profile.LastModified = DateTime.Now;

                ProfileSaved?.Invoke(this, new ProfileSavedEventArgs(Profile, IsNewProfile));
                IsDirty = false;
                IsNewProfile = false;

                _logger.LogInformation("Profile '{ProfileName}' saved successfully", Profile.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save profile '{ProfileName}'", Profile.Name);
                throw;
            }
        }

        private void Cancel()
        {
            EditCancelled?.Invoke(this, EventArgs.Empty);
        }

        private bool CanTestConnection() => IsValid && !Profile.UseDHCP && !string.IsNullOrWhiteSpace(Profile.DefaultGateway);

        private async Task TestConnectionAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Profile.DefaultGateway))
                    return;

                using var ping = new System.Net.NetworkInformation.Ping();
                var reply = await ping.SendPingAsync(Profile.DefaultGateway, 5000);

                var message = reply.Status == System.Net.NetworkInformation.IPStatus.Success
                    ? $"Gateway {Profile.DefaultGateway} is reachable ({reply.RoundtripTime}ms)"
                    : $"Gateway {Profile.DefaultGateway} is not reachable ({reply.Status})";

                // TODO: Show message to user (could use a dialog or status message)
                _logger.LogInformation("Connection test result: {Message}", message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to test connection to gateway {Gateway}", Profile.DefaultGateway);
            }
        }

        private void ApplyDnsTemplate(string? template)
        {
            if (string.IsNullOrEmpty(template)) return;

            switch (template)
            {
                case var t when t.Contains("Google"):
                    Profile.PrimaryDNS = "8.8.8.8";
                    Profile.SecondaryDNS = "8.8.4.4";
                    break;
                case var t when t.Contains("Cloudflare"):
                    Profile.PrimaryDNS = "1.1.1.1";
                    Profile.SecondaryDNS = "1.0.0.1";
                    break;
                case var t when t.Contains("OpenDNS"):
                    Profile.PrimaryDNS = "208.67.222.222";
                    Profile.SecondaryDNS = "208.67.220.220";
                    break;
                case var t when t.Contains("Quad9"):
                    Profile.PrimaryDNS = "9.9.9.9";
                    Profile.SecondaryDNS = "149.112.112.112";
                    break;
                case var t when t.Contains("Comodo"):
                    Profile.PrimaryDNS = "8.26.56.26";
                    Profile.SecondaryDNS = "8.20.247.20";
                    break;
                case var t when t.Contains("AdGuard"):
                    Profile.PrimaryDNS = "94.140.14.14";
                    Profile.SecondaryDNS = "94.140.15.15";
                    break;
            }

            _logger.LogDebug("Applied DNS template: {Template}", template);
        }

        private void AddScript()
        {
            var newScript = new CustomScript
            {
                Name = "New Script",
                Type = ScriptType.PowerShell,
                Trigger = ScriptTrigger.BeforeApply
            };

            Scripts.Add(newScript);
            SelectedScript = newScript;
        }

        private void EditScript(CustomScript? script)
        {
            if (script != null)
            {
                SelectedScript = script;
                // TODO: Open script editor dialog
            }
        }

        private void DeleteScript(CustomScript? script)
        {
            if (script != null)
            {
                Scripts.Remove(script);
                if (SelectedScript == script)
                {
                    SelectedScript = null;
                }
            }
        }

        private void AddPrinter()
        {
            var newPrinter = new PrinterConfiguration
            {
                Name = "New Printer"
            };

            Printers.Add(newPrinter);
            SelectedPrinter = newPrinter;
        }

        private void EditPrinter(PrinterConfiguration? printer)
        {
            if (printer != null)
            {
                SelectedPrinter = printer;
                // TODO: Open printer configuration dialog
            }
        }

        private void DeletePrinter(PrinterConfiguration? printer)
        {
            if (printer != null)
            {
                Printers.Remove(printer);
                if (SelectedPrinter == printer)
                {
                    SelectedPrinter = null;
                }
            }
        }

        private void GenerateRandomMac()
        {
            var random = new Random();
            var macBytes = new byte[6];
            random.NextBytes(macBytes);
            
            // Set the second bit of the first byte to 1 for locally administered address
            macBytes[0] = (byte)(macBytes[0] | 0x02);
            // Clear the first bit to indicate unicast
            macBytes[0] = (byte)(macBytes[0] & 0xFE);

            Profile.MacAddress = string.Join(":", macBytes.Select(b => b.ToString("X2")));
        }

        private async Task DetectCurrentSettingsAsync()
        {
            try
            {
                if (SelectedAdapter == null) return;

                // Get current IP configuration from the selected adapter
                var adapter = SelectedAdapter;

                if (adapter.IPAddresses.Any())
                {
                    Profile.IPAddress = adapter.IPAddresses.First();
                    Profile.UseDHCP = adapter.IsDhcpEnabled;
                }

                if (adapter.SubnetMasks.Any())
                {
                    Profile.SubnetMask = adapter.SubnetMasks.First();
                }

                if (adapter.Gateways.Any())
                {
                    Profile.DefaultGateway = adapter.Gateways.First();
                }

                if (adapter.DnsServers.Any())
                {
                    Profile.PrimaryDNS = adapter.DnsServers.First();
                    if (adapter.DnsServers.Count > 1)
                    {
                        Profile.SecondaryDNS = adapter.DnsServers[1];
                    }
                }

                Profile.MacAddress = adapter.MacAddress;

                _logger.LogInformation("Detected current settings from adapter '{AdapterName}'", adapter.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to detect current settings");
            }
        }
    }

    public class ProfileSavedEventArgs : EventArgs
    {
        public NetworkProfile Profile { get; }
        public bool IsNewProfile { get; }

        public ProfileSavedEventArgs(NetworkProfile profile, bool isNewProfile)
        {
            Profile = profile;
            IsNewProfile = isNewProfile;
        }
    }
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PerunNetworkManager.Core.Models;
using PerunNetworkManager.Core.Services;

namespace PerunNetworkManager.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly ILogger<MainViewModel> _logger;
        private readonly NetworkService _networkService;
        private readonly ProfileService _profileService;
        private readonly NetworkScannerService _scannerService;
        private readonly SystemTrayService _systemTrayService;

        [ObservableProperty]
        private ObservableCollection<NetworkProfile> _profiles = new();

        [ObservableProperty]
        private ObservableCollection<NetworkProfile> _filteredProfiles = new();

        [ObservableProperty]
        private NetworkProfile? _selectedProfile;

        [ObservableProperty]
        private NetworkProfile? _currentProfile;

        [ObservableProperty]
        private object? _currentView;

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _loadingMessage = string.Empty;

        [ObservableProperty]
        private string _statusText = "Ready";

        [ObservableProperty]
        private Brush _statusColor = Brushes.Green;

        [ObservableProperty]
        private string _activeAdapterName = string.Empty;

        [ObservableProperty]
        private DateTime _lastUpdateTime = DateTime.Now;

        [ObservableProperty]
        private bool _isDarkTheme;

        [ObservableProperty]
        private bool _isAlwaysOnTop;

        public double CurrentDpiX { get; set; } = 96.0;
        public double CurrentDpiY { get; set; } = 96.0;

        public MainViewModel(
            ILogger<MainViewModel> logger,
            NetworkService networkService,
            ProfileService profileService,
            NetworkScannerService scannerService,
            SystemTrayService systemTrayService)
        {
            _logger = logger;
            _networkService = networkService;
            _profileService = profileService;
            _scannerService = scannerService;
            _systemTrayService = systemTrayService;

            // Initialize commands
            InitializeCommands();

            // Subscribe to property changes
            PropertyChanged += OnPropertyChanged;
        }

        private void InitializeCommands()
        {
            NewProfileCommand = new AsyncRelayCommand(NewProfileAsync);
            SaveProfileCommand = new AsyncRelayCommand(SaveProfileAsync, CanSaveProfile);
            DeleteProfileCommand = new AsyncRelayCommand<NetworkProfile>(DeleteProfileAsync, CanDeleteProfile);
            EditProfileCommand = new RelayCommand<NetworkProfile>(EditProfile);
            ApplyProfileCommand = new AsyncRelayCommand<NetworkProfile>(ApplyProfileAsync, CanApplyProfile);
            ImportProfilesCommand = new AsyncRelayCommand(ImportProfilesAsync);
            ExportProfilesCommand = new AsyncRelayCommand(ExportProfilesAsync);
            RefreshCommand = new AsyncRelayCommand(RefreshAsync);
            ShowNetworkScannerCommand = new RelayCommand(ShowNetworkScanner);
            ShowDiagnosticsCommand = new RelayCommand(ShowDiagnostics);
            ShowAdapterManagerCommand = new RelayCommand(ShowAdapterManager);
            ShowSettingsCommand = new RelayCommand(ShowSettings);
            ShowHelpCommand = new RelayCommand(ShowHelp);
            ShowShortcutsCommand = new RelayCommand(ShowShortcuts);
            ShowAboutCommand = new RelayCommand(ShowAbout);
            CheckUpdatesCommand = new AsyncRelayCommand(CheckUpdatesAsync);
            PingTestCommand = new RelayCommand(ShowPingTest);
            SpeedTestCommand = new RelayCommand(ShowSpeedTest);
            ExitApplicationCommand = new RelayCommand(ExitApplication);
        }

        public ICommand NewProfileCommand { get; private set; } = null!;
        public ICommand SaveProfileCommand { get; private set; } = null!;
        public ICommand DeleteProfileCommand { get; private set; } = null!;
        public ICommand EditProfileCommand { get; private set; } = null!;
        public ICommand ApplyProfileCommand { get; private set; } = null!;
        public ICommand ImportProfilesCommand { get; private set; } = null!;
        public ICommand ExportProfilesCommand { get; private set; } = null!;
        public ICommand RefreshCommand { get; private set; } = null!;
        public ICommand ShowNetworkScannerCommand { get; private set; } = null!;
        public ICommand ShowDiagnosticsCommand { get; private set; } = null!;
        public ICommand ShowAdapterManagerCommand { get; private set; } = null!;
        public ICommand ShowSettingsCommand { get; private set; } = null!;
        public ICommand ShowHelpCommand { get; private set; } = null!;
        public ICommand ShowShortcutsCommand { get; private set; } = null!;
        public ICommand ShowAboutCommand { get; private set; } = null!;
        public ICommand CheckUpdatesCommand { get; private set; } = null!;
        public ICommand PingTestCommand { get; private set; } = null!;
        public ICommand SpeedTestCommand { get; private set; } = null!;
        public ICommand ExitApplicationCommand { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            try
            {
                IsLoading = true;
                LoadingMessage = "Initializing Perun Network Manager...";

                // Load profiles
                await LoadProfilesAsync();

                // Load current active profile
                await DetectCurrentProfileAsync();

                // Update adapter information
                await UpdateAdapterInfoAsync();

                // Initialize system tray
                _systemTrayService.Initialize();

                StatusText = "Ready";
                StatusColor = Brushes.Green;
                _logger.LogInformation("Application initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing application");
                StatusText = "Initialization failed";
                StatusColor = Brushes.Red;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadProfilesAsync()
        {
            try
            {
                LoadingMessage = "Loading network profiles...";
                var profiles = await _profileService.GetAllProfilesAsync();
                
                Profiles.Clear();
                foreach (var profile in profiles)
                {
                    Profiles.Add(profile);
                }

                FilterProfiles();
                _logger.LogInformation($"Loaded {profiles.Count} network profiles");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading profiles");
            }
        }

        private async Task DetectCurrentProfileAsync()
        {
            try
            {
                LoadingMessage = "Detecting current network configuration...";
                var currentConfig = await _profileService.GetCurrentNetworkConfigurationAsync();
                
                // Try to match with existing profiles
                var matchingProfile = Profiles.FirstOrDefault(p => 
                    _profileService.ProfileMatchesConfiguration(p, currentConfig));
                
                if (matchingProfile != null)
                {
                    CurrentProfile = matchingProfile;
                    matchingProfile.IsActive = true;
                    
                    // Clear active status from other profiles
                    foreach (var profile in Profiles.Where(p => p != matchingProfile))
                    {
                        profile.IsActive = false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error detecting current profile");
            }
        }

        private async Task UpdateAdapterInfoAsync()
        {
            try
            {
                var adapters = await _networkService.GetNetworkAdaptersAsync();
                var activeAdapter = adapters.FirstOrDefault(a => a.Status == System.Net.NetworkInformation.OperationalStatus.Up);
                
                if (activeAdapter != null)
                {
                    ActiveAdapterName = activeAdapter.Name;
                }
                
                LastUpdateTime = DateTime.Now;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating adapter information");
            }
        }

        private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(SearchText):
                    FilterProfiles();
                    break;
                case nameof(SelectedProfile):
                    if (SelectedProfile == profile)
                    {
                        SelectedProfile = null;
                        CurrentView = null;
                    }
                    
                    FilterProfiles();
                    _logger.LogInformation($"Deleted profile '{profile.Name}'");
                    StatusText = $"Profile '{profile.Name}' deleted";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting profile '{profile.Name}'");
                StatusText = "Failed to delete profile";
                StatusColor = Brushes.Red;
            }
        }

        private bool CanDeleteProfile(NetworkProfile? profile) => profile != null && !profile.IsActive;

        private void EditProfile(NetworkProfile? profile)
        {
            if (profile != null)
            {
                SelectedProfile = profile;
                ShowProfileEditor();
            }
        }

        private async Task ApplyProfileAsync(NetworkProfile? profile)
        {
            if (profile == null) return;

            try
            {
                IsLoading = true;
                LoadingMessage = $"Applying profile '{profile.Name}'...";
                StatusText = $"Applying profile '{profile.Name}'...";
                StatusColor = Brushes.Orange;

                // Get available adapters
                var adapters = await _networkService.GetNetworkAdaptersAsync();
                var targetAdapter = adapters.FirstOrDefault(a => 
                    string.IsNullOrEmpty(profile.AdapterName) || a.Name == profile.AdapterName);

                if (targetAdapter == null)
                {
                    // Use the first available adapter
                    targetAdapter = adapters.FirstOrDefault(a => a.IsEnabled);
                }

                if (targetAdapter == null)
                {
                    throw new InvalidOperationException("No suitable network adapter found");
                }

                var success = await _networkService.ApplyNetworkProfileAsync(profile, targetAdapter.Name);

                if (success)
                {
                    // Update profile states
                    foreach (var p in Profiles)
                    {
                        p.IsActive = p.Id == profile.Id;
                    }

                    CurrentProfile = profile;
                    ActiveAdapterName = targetAdapter.Name;
                    
                    StatusText = $"Profile '{profile.Name}' applied successfully";
                    StatusColor = Brushes.Green;
                    
                    _logger.LogInformation($"Successfully applied profile '{profile.Name}'");
                    
                    // Show system tray notification
                    _systemTrayService.ShowNotification(
                        "Profile Applied", 
                        $"Network profile '{profile.Name}' has been applied successfully");
                }
                else
                {
                    StatusText = $"Failed to apply profile '{profile.Name}'";
                    StatusColor = Brushes.Red;
                    
                    System.Windows.MessageBox.Show(
                        $"Failed to apply network profile '{profile.Name}'. Please check the logs for more details.",
                        "Profile Application Failed",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error applying profile '{profile.Name}'");
                StatusText = $"Error applying profile '{profile.Name}'";
                StatusColor = Brushes.Red;
                
                System.Windows.MessageBox.Show(
                    $"An error occurred while applying the profile: {ex.Message}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
                LastUpdateTime = DateTime.Now;
            }
        }

        private bool CanApplyProfile(NetworkProfile? profile) => profile != null && profile.IsValid() && !profile.IsActive;

        private async Task ImportProfilesAsync()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Import Network Profiles",
                    Filter = "Perun Profile Files (*.npx)|*.npx|JSON Files (*.json)|*.json|XML Files (*.xml)|*.xml|All Files (*.*)|*.*",
                    DefaultExt = ".npx",
                    Multiselect = false
                };

                if (dialog.ShowDialog() == true)
                {
                    IsLoading = true;
                    LoadingMessage = "Importing profiles...";

                    var importedProfiles = await _profileService.ImportProfilesAsync(dialog.FileName);
                    
                    foreach (var profile in importedProfiles)
                    {
                        profile.Id = Guid.NewGuid(); // Generate new IDs to avoid conflicts
                        Profiles.Add(profile);
                    }

                    FilterProfiles();
                    
                    StatusText = $"Imported {importedProfiles.Count} profiles";
                    StatusColor = Brushes.Green;
                    
                    _logger.LogInformation($"Imported {importedProfiles.Count} profiles from {dialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing profiles");
                StatusText = "Failed to import profiles";
                StatusColor = Brushes.Red;
                
                System.Windows.MessageBox.Show(
                    $"Failed to import profiles: {ex.Message}",
                    "Import Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ExportProfilesAsync()
        {
            try
            {
                if (!Profiles.Any())
                {
                    System.Windows.MessageBox.Show(
                        "No profiles available to export.",
                        "No Profiles",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                    return;
                }

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Export Network Profiles",
                    Filter = "Perun Profile Files (*.npx)|*.npx|JSON Files (*.json)|*.json|XML Files (*.xml)|*.xml",
                    DefaultExt = ".npx",
                    FileName = $"PerunProfiles_{DateTime.Now:yyyyMMdd_HHmmss}"
                };

                if (dialog.ShowDialog() == true)
                {
                    IsLoading = true;
                    LoadingMessage = "Exporting profiles...";

                    await _profileService.ExportProfilesAsync(Profiles.ToList(), dialog.FileName);
                    
                    StatusText = $"Exported {Profiles.Count} profiles";
                    StatusColor = Brushes.Green;
                    
                    _logger.LogInformation($"Exported {Profiles.Count} profiles to {dialog.FileName}");
                    
                    System.Windows.MessageBox.Show(
                        $"Successfully exported {Profiles.Count} profiles to:\n{dialog.FileName}",
                        "Export Complete",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting profiles");
                StatusText = "Failed to export profiles";
                StatusColor = Brushes.Red;
                
                System.Windows.MessageBox.Show(
                    $"Failed to export profiles: {ex.Message}",
                    "Export Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task RefreshAsync()
        {
            try
            {
                IsLoading = true;
                LoadingMessage = "Refreshing...";

                await LoadProfilesAsync();
                await DetectCurrentProfileAsync();
                await UpdateAdapterInfoAsync();

                StatusText = "Refreshed";
                StatusColor = Brushes.Green;
                LastUpdateTime = DateTime.Now;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing");
                StatusText = "Refresh failed";
                StatusColor = Brushes.Red;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ShowProfileEditor()
        {
            if (SelectedProfile != null)
            {
                CurrentView = new ProfileEditorViewModel(SelectedProfile, _networkService, _logger);
            }
        }

        private void ShowNetworkScanner()
        {
            CurrentView = new NetworkScannerViewModel(_scannerService, _logger);
        }

        private void ShowDiagnostics()
        {
            CurrentView = new DiagnosticViewModel(_networkService, _logger);
        }

        private void ShowAdapterManager()
        {
            CurrentView = new NetworkAdapterViewModel(_networkService, _logger);
        }

        private void ShowSettings()
        {
            // TODO: Implement settings dialog
            var settingsWindow = new Views.SettingsWindow();
            settingsWindow.ShowDialog();
        }

        private void ShowHelp()
        {
            try
            {
                var helpUrl = "https://github.com/perunsoftware/perun-network-manager/wiki";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = helpUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error opening help");
                System.Windows.MessageBox.Show(
                    "Unable to open help documentation. Please visit the GitHub wiki manually.",
                    "Help Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }

        private void ShowShortcuts()
        {
            var shortcutsText = @"Keyboard Shortcuts:

Ctrl+N - New Profile
Ctrl+S - Save Profile
Ctrl+I - Import Profiles
Ctrl+E - Export Profiles
F5 - Refresh / Apply Selected Profile
Delete - Delete Selected Profile
Ctrl+, - Settings
Escape - Minimize to System Tray

Navigation:
Arrow Keys - Navigate profile list
Enter - Apply selected profile
Tab - Move between controls";

            System.Windows.MessageBox.Show(
                shortcutsText,
                "Keyboard Shortcuts",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        private void ShowAbout()
        {
            var aboutText = @"Perun Network Manager v1.0.0

Advanced Network Profile Manager with Subnet Scanner

Features:
• Network profile management
• Subnet scanning and device discovery
• Network diagnostics
• Adapter management
• System tray integration

Copyright © 2025 Perun Software
Licensed under MIT License

Visit: https://github.com/perunsoftware/perun-network-manager";

            System.Windows.MessageBox.Show(
                aboutText,
                "About Perun Network Manager",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        private async Task CheckUpdatesAsync()
        {
            try
            {
                IsLoading = true;
                LoadingMessage = "Checking for updates...";

                // TODO: Implement update checking logic
                await Task.Delay(2000); // Simulate update check

                System.Windows.MessageBox.Show(
                    "You are running the latest version of Perun Network Manager.",
                    "No Updates Available",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);

                StatusText = "No updates available";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for updates");
                StatusText = "Update check failed";
                StatusColor = Brushes.Red;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ShowPingTest()
        {
            // TODO: Implement ping test dialog
            var pingWindow = new Views.PingTestWindow();
            pingWindow.Show();
        }

        private void ShowSpeedTest()
        {
            // TODO: Implement speed test dialog
            var speedTestWindow = new Views.SpeedTestWindow();
            speedTestWindow.Show();
        }

        private void ExitApplication()
        {
            try
            {
                _systemTrayService.Dispose();
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during application shutdown");
            }
        }

        private void ApplyTheme()
        {
            try
            {
                var application = System.Windows.Application.Current;
                var themeName = IsDarkTheme ? "Dark" : "Light";
                
                // Update Material Design theme
                var paletteHelper = new MaterialDesignThemes.Wpf.PaletteHelper();
                var theme = paletteHelper.GetTheme();
                theme.SetBaseTheme(IsDarkTheme ? MaterialDesignThemes.Wpf.Theme.Dark : MaterialDesignThemes.Wpf.Theme.Light);
                paletteHelper.SetTheme(theme);

                _logger.LogInformation($"Applied {themeName} theme");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying theme");
            }
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            
            // Update command can execute states
            ((AsyncRelayCommand)SaveProfileCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand<NetworkProfile>)DeleteProfileCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand<NetworkProfile>)ApplyProfileCommand).NotifyCanExecuteChanged();
        }
    }
} != null)
                    {
                        ShowProfileEditor();
                    }
                    break;
                case nameof(IsDarkTheme):
                    ApplyTheme();
                    break;
            }
        }

        private void FilterProfiles()
        {
            FilteredProfiles.Clear();
            
            var filtered = string.IsNullOrWhiteSpace(SearchText)
                ? Profiles
                : Profiles.Where(p => 
                    p.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    p.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    p.IPAddress?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true);

            foreach (var profile in filtered)
            {
                FilteredProfiles.Add(profile);
            }
        }

        private async Task NewProfileAsync()
        {
            try
            {
                var newProfile = new NetworkProfile
                {
                    Name = "New Profile",
                    Description = "Network profile created on " + DateTime.Now.ToString("yyyy-MM-dd HH:mm")
                };

                Profiles.Add(newProfile);
                SelectedProfile = newProfile;
                FilterProfiles();

                _logger.LogInformation("Created new network profile");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating new profile");
            }

            await Task.CompletedTask;
        }

        private async Task SaveProfileAsync()
        {
            if (SelectedProfile == null) return;

            try
            {
                IsLoading = true;
                LoadingMessage = "Saving profile...";

                await _profileService.SaveProfileAsync(SelectedProfile);
                
                _logger.LogInformation($"Saved profile '{SelectedProfile.Name}'");
                StatusText = $"Profile '{SelectedProfile.Name}' saved";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving profile '{SelectedProfile?.Name}'");
                StatusText = "Failed to save profile";
                StatusColor = Brushes.Red;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private bool CanSaveProfile() => SelectedProfile != null && SelectedProfile.IsValid();

        private async Task DeleteProfileAsync(NetworkProfile? profile)
        {
            if (profile == null) return;

            try
            {
                var result = System.Windows.MessageBox.Show(
                    $"Are you sure you want to delete the profile '{profile.Name}'?",
                    "Confirm Delete",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    await _profileService.DeleteProfileAsync(profile.Id);
                    Profiles.Remove(profile);
                    
                    if (SelectedProfile

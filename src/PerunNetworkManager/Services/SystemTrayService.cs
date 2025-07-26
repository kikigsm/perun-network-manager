using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.Logging;
using PerunNetworkManager.Core.Models;

namespace PerunNetworkManager.Services
{
    public class SystemTrayService : IDisposable
    {
        private readonly ILogger<SystemTrayService> _logger;
        private TaskbarIcon? _taskbarIcon;
        private readonly Dictionary<Guid, string> _profileQuickAccess;
        private bool _disposed;

        public event EventHandler<ProfileSelectedEventArgs>? ProfileSelected;
        public event EventHandler? ShowMainWindow;
        public event EventHandler? ExitApplication;

        public SystemTrayService(ILogger<SystemTrayService> logger)
        {
            _logger = logger;
            _profileQuickAccess = new Dictionary<Guid, string>();
        }

        public void Initialize()
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_taskbarIcon != null)
                    {
                        var tooltipText = activeProfile != null 
                            ? $"Perun Network Manager - Active: {activeProfile.Name}"
                            : "Perun Network Manager";
                        
                        _taskbarIcon.ToolTipText = tooltipText;
                        
                        // Update icon to indicate active state
                        if (activeProfile != null)
                        {
                            // Could change icon color or add overlay to indicate active profile
                            _taskbarIcon.IconSource = new System.Windows.Media.Imaging.BitmapImage(
                                new Uri("pack://application:,,,/Resources/Images/perun_icon_active.ico"));
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set active profile in system tray");
            }
        }

        private void OnTrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            try
            {
                ShowMainWindow?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling tray double-click");
            }
        }

        private void OnTrayRightMouseUp(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateContextMenu();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling tray right-click");
            }
        }

        private void UpdateContextMenu()
        {
            try
            {
                if (_taskbarIcon == null) return;

                var contextMenu = new System.Windows.Controls.ContextMenu();

                // Show main window
                var showItem = new System.Windows.Controls.MenuItem
                {
                    Header = "Show Perun Network Manager",
                    FontWeight = FontWeights.Bold
                };
                showItem.Click += (s, e) => ShowMainWindow?.Invoke(this, EventArgs.Empty);
                contextMenu.Items.Add(showItem);

                contextMenu.Items.Add(new System.Windows.Controls.Separator());

                // Quick profile switching
                if (_profileQuickAccess.Any())
                {
                    var profilesItem = new System.Windows.Controls.MenuItem
                    {
                        Header = "Quick Profile Switch"
                    };

                    foreach (var profile in _profileQuickAccess)
                    {
                        var profileItem = new System.Windows.Controls.MenuItem
                        {
                            Header = profile.Value,
                            Tag = profile.Key
                        };
                        profileItem.Click += OnProfileMenuItemClick;
                        profilesItem.Items.Add(profileItem);
                    }

                    contextMenu.Items.Add(profilesItem);
                    contextMenu.Items.Add(new System.Windows.Controls.Separator());
                }

                // Quick actions
                var scannerItem = new System.Windows.Controls.MenuItem
                {
                    Header = "Network Scanner"
                };
                scannerItem.Click += (s, e) => 
                {
                    ShowMainWindow?.Invoke(this, EventArgs.Empty);
                    // TODO: Navigate to scanner view
                };
                contextMenu.Items.Add(scannerItem);

                var diagnosticsItem = new System.Windows.Controls.MenuItem
                {
                    Header = "Network Diagnostics"
                };
                diagnosticsItem.Click += (s, e) =>
                {
                    ShowMainWindow?.Invoke(this, EventArgs.Empty);
                    // TODO: Navigate to diagnostics view
                };
                contextMenu.Items.Add(diagnosticsItem);

                contextMenu.Items.Add(new System.Windows.Controls.Separator());

                // Settings
                var settingsItem = new System.Windows.Controls.MenuItem
                {
                    Header = "Settings"
                };
                settingsItem.Click += (s, e) =>
                {
                    ShowMainWindow?.Invoke(this, EventArgs.Empty);
                    // TODO: Open settings
                };
                contextMenu.Items.Add(settingsItem);

                contextMenu.Items.Add(new System.Windows.Controls.Separator());

                // Exit
                var exitItem = new System.Windows.Controls.MenuItem
                {
                    Header = "Exit"
                };
                exitItem.Click += (s, e) => ExitApplication?.Invoke(this, EventArgs.Empty);
                contextMenu.Items.Add(exitItem);

                _taskbarIcon.ContextMenu = contextMenu;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating context menu");
            }
        }

        private void OnProfileMenuItemClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.Tag is Guid profileId)
                {
                    ProfileSelected?.Invoke(this, new ProfileSelectedEventArgs(profileId));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling profile menu item click");
            }
        }

        public void UpdateStatus(string statusMessage, TrayIconStatus status = TrayIconStatus.Normal)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_taskbarIcon != null)
                    {
                        _taskbarIcon.ToolTipText = $"Perun Network Manager - {statusMessage}";

                        // Update icon based on status
                        var iconUri = status switch
                        {
                            TrayIconStatus.Normal => "pack://application:,,,/Resources/Images/perun_icon.ico",
                            TrayIconStatus.Active => "pack://application:,,,/Resources/Images/perun_icon_active.ico",
                            TrayIconStatus.Warning => "pack://application:,,,/Resources/Images/perun_icon_warning.ico",
                            TrayIconStatus.Error => "pack://application:,,,/Resources/Images/perun_icon_error.ico",
                            _ => "pack://application:,,,/Resources/Images/perun_icon.ico"
                        };

                        try
                        {
                            _taskbarIcon.IconSource = new System.Windows.Media.Imaging.BitmapImage(new Uri(iconUri));
                        }
                        catch
                        {
                            // Fallback to default icon if specific status icon doesn't exist
                            _taskbarIcon.IconSource = new System.Windows.Media.Imaging.BitmapImage(
                                new Uri("pack://application:,,,/Resources/Images/perun_icon.ico"));
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update tray icon status");
            }
        }

        public void ShowOperationProgress(string operation, int progressPercent)
        {
            try
            {
                var message = progressPercent >= 0 
                    ? $"{operation} - {progressPercent}%"
                    : operation;
                    
                UpdateStatus(message, TrayIconStatus.Active);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to show operation progress");
            }
        }

        public void Hide()
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_taskbarIcon != null)
                    {
                        _taskbarIcon.Visibility = Visibility.Collapsed;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to hide system tray icon");
            }
        }

        public void Show()
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_taskbarIcon != null)
                    {
                        _taskbarIcon.Visibility = Visibility.Visible;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to show system tray icon");
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    try
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            _taskbarIcon?.Dispose();
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error disposing system tray icon");
                    }
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    public class ProfileSelectedEventArgs : EventArgs
    {
        public Guid ProfileId { get; }

        public ProfileSelectedEventArgs(Guid profileId)
        {
            ProfileId = profileId;
        }
    }

    public enum TrayIconStatus
    {
        Normal,
        Active,
        Warning,
        Error
    }

    public enum BalloonIcon
    {
        None = 0,
        Info = 1,
        Warning = 2,
        Error = 3
    }
}Dispatcher.Invoke(() =>
                {
                    _taskbarIcon = new TaskbarIcon
                    {
                        IconSource = new System.Windows.Media.Imaging.BitmapImage(
                            new Uri("pack://application:,,,/Resources/Images/perun_icon.ico")),
                        ToolTipText = "Perun Network Manager"
                    };

                    _taskbarIcon.TrayMouseDoubleClick += OnTrayMouseDoubleClick;
                    _taskbarIcon.TrayRightMouseUp += OnTrayRightMouseUp;

                    UpdateContextMenu();
                });

                _logger.LogInformation("System tray service initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize system tray service");
            }
        }

        public void ShowNotification(string title, string message, BalloonIcon icon = BalloonIcon.Info, int timeoutMs = 3000)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _taskbarIcon?.ShowBalloonTip(title, message, (Hardcodet.Wpf.TaskbarNotification.BalloonIcon)icon);
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to show notification: {Title}", title);
            }
        }

        public void UpdateProfiles(IEnumerable<NetworkProfile> profiles)
        {
            try
            {
                _profileQuickAccess.Clear();
                
                foreach (var profile in profiles.Take(10)) // Limit to 10 profiles for menu
                {
                    _profileQuickAccess[profile.Id] = profile.Name;
                }

                Application.Current.Dispatcher.Invoke(UpdateContextMenu);
                _logger.LogDebug("Updated system tray profiles menu with {Count} profiles", _profileQuickAccess.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update profiles in system tray");
            }
        }

        public void SetActiveProfile(NetworkProfile? activeProfile)
        {
            try
            {
                Application.Current.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using PerunNetworkManager.Core.Models;
using PerunNetworkManager.Services;
using PerunNetworkManager.ViewModels;
using PerunNetworkManager.Views.Dialogs;

namespace PerunNetworkManager.Views
{
    /// <summary>
    /// Interaction logic for NetworkScannerView.xaml
    /// </summary>
    public partial class NetworkScannerView : UserControl
    {
        private NetworkScannerViewModel _viewModel;
        private GridViewColumnHeader _lastHeaderClicked;
        private ListSortDirection _lastDirection = ListSortDirection.Ascending;
        private readonly ContextMenu _deviceContextMenu;

        public NetworkScannerView()
        {
            InitializeComponent();
            
            // Initialize after loaded to ensure ViewModel is set
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;

            // Create context menu for devices
            _deviceContextMenu = CreateDeviceContextMenu();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _viewModel = DataContext as NetworkScannerViewModel;
            
            if (_viewModel != null)
            {
                // Subscribe to ViewModel events
                _viewModel.DeviceDetailsRequested += OnDeviceDetailsRequested;
                _viewModel.ExportRequested += OnExportRequested;
                _viewModel.ScanCompleted += OnScanCompleted;
                
                // Set up keyboard shortcuts
                SetupKeyboardShortcuts();
                
                // Apply initial animations
                ApplyInitialAnimations();
            }

            // Focus search box on load
            SearchBox?.Focus();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
            {
                // Unsubscribe from events to prevent memory leaks
                _viewModel.DeviceDetailsRequested -= OnDeviceDetailsRequested;
                _viewModel.ExportRequested -= OnExportRequested;
                _viewModel.ScanCompleted -= OnScanCompleted;
            }
        }

        /// <summary>
        /// Creates context menu for device items.
        /// </summary>
        private ContextMenu CreateDeviceContextMenu()
        {
            var contextMenu = new ContextMenu();

            // View Details
            var viewDetailsItem = new MenuItem
            {
                Header = "View Details",
                Icon = new PackIcon { Kind = PackIconKind.InformationOutline }
            };
            viewDetailsItem.Click += (s, e) => ViewDeviceDetails();
            contextMenu.Items.Add(viewDetailsItem);

            contextMenu.Items.Add(new Separator());

            // Wake on LAN
            var wakeOnLanItem = new MenuItem
            {
                Header = "Wake on LAN",
                Icon = new PackIcon { Kind = PackIconKind.Power }
            };
            wakeOnLanItem.Click += (s, e) => WakeOnLan();
            contextMenu.Items.Add(wakeOnLanItem);

            // Remote Desktop
            var rdpItem = new MenuItem
            {
                Header = "Remote Desktop",
                Icon = new PackIcon { Kind = PackIconKind.RemoteDesktop }
            };
            rdpItem.Click += (s, e) => LaunchRemoteDesktop();
            contextMenu.Items.Add(rdpItem);

            // Open in Browser
            var browserItem = new MenuItem
            {
                Header = "Open in Browser",
                Icon = new PackIcon { Kind = PackIconKind.Web }
            };
            browserItem.Click += (s, e) => OpenInBrowser();
            contextMenu.Items.Add(browserItem);

            contextMenu.Items.Add(new Separator());

            // Diagnostic Tools submenu
            var diagnosticMenu = new MenuItem
            {
                Header = "Diagnostic Tools",
                Icon = new PackIcon { Kind = PackIconKind.Wrench }
            };

            var pingItem = new MenuItem { Header = "Ping", Icon = new PackIcon { Kind = PackIconKind.Pulse } };
            pingItem.Click += (s, e) => LaunchPing();
            diagnosticMenu.Items.Add(pingItem);

            var tracerouteItem = new MenuItem { Header = "Traceroute", Icon = new PackIcon { Kind = PackIconKind.MapMarkerPath } };
            tracerouteItem.Click += (s, e) => LaunchTraceroute();
            diagnosticMenu.Items.Add(tracerouteItem);

            var portScanItem = new MenuItem { Header = "Port Scan", Icon = new PackIcon { Kind = PackIconKind.NetworkOutline } };
            portScanItem.Click += (s, e) => LaunchPortScan();
            diagnosticMenu.Items.Add(portScanItem);

            contextMenu.Items.Add(diagnosticMenu);

            contextMenu.Items.Add(new Separator());

            // Copy submenu
            var copyMenu = new MenuItem
            {
                Header = "Copy",
                Icon = new PackIcon { Kind = PackIconKind.ContentCopy }
            };

            var copyIpItem = new MenuItem { Header = "IP Address" };
            copyIpItem.Click += (s, e) => CopyToClipboard("IP");
            copyMenu.Items.Add(copyIpItem);

            var copyMacItem = new MenuItem { Header = "MAC Address" };
            copyMacItem.Click += (s, e) => CopyToClipboard("MAC");
            copyMenu.Items.Add(copyMacItem);

            var copyHostnameItem = new MenuItem { Header = "Hostname" };
            copyHostnameItem.Click += (s, e) => CopyToClipboard("Hostname");
            copyMenu.Items.Add(copyHostnameItem);

            var copyAllItem = new MenuItem { Header = "All Info" };
            copyAllItem.Click += (s, e) => CopyToClipboard("All");
            copyMenu.Items.Add(copyAllItem);

            contextMenu.Items.Add(copyMenu);

            return contextMenu;
        }

        /// <summary>
        /// Sets up keyboard shortcuts for the view.
        /// </summary>
        private void SetupKeyboardShortcuts()
        {
            // F5 - Refresh/Scan
            var f5Command = new RoutedCommand();
            f5Command.InputGestures.Add(new KeyGesture(Key.F5));
            CommandBindings.Add(new CommandBinding(f5Command, (s, e) => _viewModel?.ScanCommand.Execute(null)));

            // Ctrl+F - Focus search
            var searchCommand = new RoutedCommand();
            searchCommand.InputGestures.Add(new KeyGesture(Key.F, ModifierKeys.Control));
            CommandBindings.Add(new CommandBinding(searchCommand, (s, e) => SearchBox?.Focus()));

            // Ctrl+E - Export
            var exportCommand = new RoutedCommand();
            exportCommand.InputGestures.Add(new KeyGesture(Key.E, ModifierKeys.Control));
            CommandBindings.Add(new CommandBinding(exportCommand, (s, e) => _viewModel?.ExportCommand.Execute(null)));

            // Delete - Clear selected
            var deleteCommand = new RoutedCommand();
            deleteCommand.InputGestures.Add(new KeyGesture(Key.Delete));
            CommandBindings.Add(new CommandBinding(deleteCommand, (s, e) => ClearSelectedDevices()));

            // Enter - View details
            var enterCommand = new RoutedCommand();
            enterCommand.InputGestures.Add(new KeyGesture(Key.Return));
            CommandBindings.Add(new CommandBinding(enterCommand, (s, e) => ViewDeviceDetails()));
        }

        /// <summary>
        /// Applies initial animations when view loads.
        /// </summary>
        private void ApplyInitialAnimations()
        {
            // Fade in animation
            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(500),
                EasingFunction = new QuadraticEase()
            };

            BeginAnimation(OpacityProperty, fadeIn);

            // Slide in from bottom
            var transform = new TranslateTransform();
            RenderTransform = transform;

            var slideIn = new DoubleAnimation
            {
                From = 50,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(500),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            transform.BeginAnimation(TranslateTransform.YProperty, slideIn);
        }

        /// <summary>
        /// Handles DataGrid row double-click.
        /// </summary>
        private void DeviceGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                var item = (sender as DataGrid)?.SelectedItem as ScannedDevice;
                if (item != null)
                {
                    ViewDeviceDetails();
                }
            }
        }

        /// <summary>
        /// Handles DataGrid context menu opening.
        /// </summary>
        private void DeviceGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var grid = sender as DataGrid;
            if (grid?.SelectedItem == null)
            {
                e.Handled = true;
                return;
            }

            // Update context menu items based on selected device
            var device = grid.SelectedItem as ScannedDevice;
            if (device != null)
            {
                UpdateContextMenuForDevice(device);
                grid.ContextMenu = _deviceContextMenu;
            }
        }

        /// <summary>
        /// Updates context menu items based on device capabilities.
        /// </summary>
        private void UpdateContextMenuForDevice(ScannedDevice device)
        {
            foreach (var item in _deviceContextMenu.Items)
            {
                if (item is MenuItem menuItem)
                {
                    switch (menuItem.Header?.ToString())
                    {
                        case "Wake on LAN":
                            menuItem.IsEnabled = !string.IsNullOrEmpty(device.MACAddress) && !device.IsOnline;
                            break;
                        case "Remote Desktop":
                            menuItem.IsEnabled = device.IsOnline && 
                                (device.OpenPorts?.Contains(3389) ?? false || 
                                 device.DeviceType == DeviceType.Computer ||
                                 device.DeviceType == DeviceType.Server);
                            break;
                        case "Open in Browser":
                            menuItem.IsEnabled = device.IsOnline && 
                                (device.OpenPorts?.Any(p => p == 80 || p == 443 || p == 8080) ?? false);
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Handles column header click for sorting.
        /// </summary>
        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            var headerClicked = e.OriginalSource as GridViewColumnHeader;
            ListSortDirection direction;

            if (headerClicked != null && headerClicked.Column != null)
            {
                if (headerClicked != _lastHeaderClicked)
                {
                    direction = ListSortDirection.Ascending;
                }
                else
                {
                    direction = _lastDirection == ListSortDirection.Ascending
                        ? ListSortDirection.Descending
                        : ListSortDirection.Ascending;
                }

                var columnBinding = headerClicked.Column.DisplayMemberBinding as Binding;
                var sortBy = columnBinding?.Path.Path ?? headerClicked.Column.Header as string;

                if (!string.IsNullOrEmpty(sortBy))
                {
                    Sort(sortBy, direction);
                    
                    // Update sort indicators
                    UpdateSortIndicators(headerClicked, direction);

                    _lastHeaderClicked = headerClicked;
                    _lastDirection = direction;
                }
            }
        }

        /// <summary>
        /// Sorts the device collection.
        /// </summary>
        private void Sort(string sortBy, ListSortDirection direction)
        {
            var dataView = CollectionViewSource.GetDefaultView(DeviceGrid.ItemsSource);
            
            dataView.SortDescriptions.Clear();
            var sd = new SortDescription(sortBy, direction);
            dataView.SortDescriptions.Add(sd);
            dataView.Refresh();
        }

        /// <summary>
        /// Updates visual sort indicators on column headers.
        /// </summary>
        private void UpdateSortIndicators(GridViewColumnHeader clickedHeader, ListSortDirection direction)
        {
            // Remove all existing sort indicators
            foreach (var header in FindVisualChildren<GridViewColumnHeader>(DeviceGrid))
            {
                if (header.Column != null)
                {
                    header.Column.HeaderTemplate = null;
                }
            }

            // Add sort indicator to clicked header
            var sortIndicator = direction == ListSortDirection.Ascending
                ? "▲"
                : "▼";

            clickedHeader.Column.Header = $"{clickedHeader.Column.Header} {sortIndicator}";
        }

        /// <summary>
        /// Helper method to find visual children of a type.
        /// </summary>
        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    var child = VisualTreeHelper.GetChild(depObj, i);
                    if (child != null && child is T)
                    {
                        yield return (T)child;
                    }

                    foreach (T childOfChild in FindVisualChildren<T>(child))
                    {
                        yield return childOfChild;
                    }
                }
            }
        }

        /// <summary>
        /// Views details of selected device.
        /// </summary>
        private async void ViewDeviceDetails()
        {
            var selectedDevice = DeviceGrid.SelectedItem as ScannedDevice;
            if (selectedDevice != null)
            {
                await ShowDeviceDetailsDialog(selectedDevice);
            }
        }

        /// <summary>
        /// Shows device details dialog.
        /// </summary>
        private async Task ShowDeviceDetailsDialog(ScannedDevice device)
        {
            var dialog = new DeviceDetailsWindow(device)
            {
                Owner = Window.GetWindow(this)
            };

            dialog.ShowDialog();
        }

        /// <summary>
        /// Wakes selected device using Wake-on-LAN.
        /// </summary>
        private async void WakeOnLan()
        {
            var selectedDevice = DeviceGrid.SelectedItem as ScannedDevice;
            if (selectedDevice != null && !string.IsNullOrEmpty(selectedDevice.MACAddress))
            {
                _viewModel?.WakeOnLanCommand.Execute(selectedDevice);
                
                // Show notification
                var snackbar = FindName("NotificationSnackbar") as Snackbar;
                if (snackbar != null)
                {
                    snackbar.MessageQueue?.Enqueue($"Wake-on-LAN packet sent to {selectedDevice.MACAddress}");
                }
            }
        }

        /// <summary>
        /// Launches Remote Desktop for selected device.
        /// </summary>
        private void LaunchRemoteDesktop()
        {
            var selectedDevice = DeviceGrid.SelectedItem as ScannedDevice;
            if (selectedDevice != null && selectedDevice.IsOnline)
            {
                try
                {
                    Process.Start("mstsc.exe", $"/v:{selectedDevice.IPAddress}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to launch Remote Desktop: {ex.Message}", 
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Opens device in web browser.
        /// </summary>
        private void OpenInBrowser()
        {
            var selectedDevice = DeviceGrid.SelectedItem as ScannedDevice;
            if (selectedDevice != null && selectedDevice.IsOnline)
            {
                var protocol = selectedDevice.OpenPorts?.Contains(443) == true ? "https" : "http";
                var port = selectedDevice.OpenPorts?.FirstOrDefault(p => p == 80 || p == 443 || p == 8080);
                
                if (port.HasValue)
                {
                    var url = port.Value == 80 || port.Value == 443
                        ? $"{protocol}://{selectedDevice.IPAddress}"
                        : $"{protocol}://{selectedDevice.IPAddress}:{port}";

                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to open browser: {ex.Message}", 
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        /// <summary>
        /// Launches ping tool for selected device.
        /// </summary>
        private void LaunchPing()
        {
            var selectedDevice = DeviceGrid.SelectedItem as ScannedDevice;
            if (selectedDevice != null)
            {
                _viewModel?.LaunchDiagnosticTool("ping", selectedDevice.IPAddress);
            }
        }

        /// <summary>
        /// Launches traceroute tool for selected device.
        /// </summary>
        private void LaunchTraceroute()
        {
            var selectedDevice = DeviceGrid.SelectedItem as ScannedDevice;
            if (selectedDevice != null)
            {
                _viewModel?.LaunchDiagnosticTool("traceroute", selectedDevice.IPAddress);
            }
        }

        /// <summary>
        /// Launches port scan tool for selected device.
        /// </summary>
        private void LaunchPortScan()
        {
            var selectedDevice = DeviceGrid.SelectedItem as ScannedDevice;
            if (selectedDevice != null)
            {
                _viewModel?.LaunchDiagnosticTool("portscan", selectedDevice.IPAddress);
            }
        }

        /// <summary>
        /// Copies device information to clipboard.
        /// </summary>
        private void CopyToClipboard(string what)
        {
            var selectedDevice = DeviceGrid.SelectedItem as ScannedDevice;
            if (selectedDevice != null)
            {
                string textToCopy = what switch
                {
                    "IP" => selectedDevice.IPAddress,
                    "MAC" => selectedDevice.MACAddress,
                    "Hostname" => selectedDevice.Hostname,
                    "All" => $"IP: {selectedDevice.IPAddress}\n" +
                            $"MAC: {selectedDevice.MACAddress}\n" +
                            $"Hostname: {selectedDevice.Hostname}\n" +
                            $"Manufacturer: {selectedDevice.Manufacturer}\n" +
                            $"Type: {selectedDevice.DeviceType}\n" +
                            $"Status: {(selectedDevice.IsOnline ? "Online" : "Offline")}",
                    _ => selectedDevice.ToString()
                };

                if (!string.IsNullOrEmpty(textToCopy))
                {
                    Clipboard.SetText(textToCopy);
                    
                    // Show notification
                    var snackbar = FindName("NotificationSnackbar") as Snackbar;
                    snackbar?.MessageQueue?.Enqueue($"{what} copied to clipboard");
                }
            }
        }

        /// <summary>
        /// Clears selected devices from the list.
        /// </summary>
        private void ClearSelectedDevices()
        {
            var selectedItems = DeviceGrid.SelectedItems.Cast<ScannedDevice>().ToList();
            if (selectedItems.Any())
            {
                var result = MessageBox.Show(
                    $"Remove {selectedItems.Count} selected device(s) from the list?",
                    "Confirm Remove",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    foreach (var device in selectedItems)
                    {
                        _viewModel?.RemoveDevice(device);
                    }
                }
            }
        }

        /// <summary>
        /// Handles device details requested event from ViewModel.
        /// </summary>
        private async void OnDeviceDetailsRequested(object sender, ScannedDevice device)
        {
            await ShowDeviceDetailsDialog(device);
        }

        /// <summary>
        /// Handles export requested event from ViewModel.
        /// </summary>
        private void OnExportRequested(object sender, EventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export Network Scan Results",
                Filter = "CSV files (*.csv)|*.csv|JSON files (*.json)|*.json|XML files (*.xml)|*.xml|All files (*.*)|*.*",
                DefaultExt = ".csv",
                FileName = $"NetworkScan_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (dialog.ShowDialog() == true)
            {
                _viewModel?.ExportToFile(dialog.FileName);
                
                // Show success notification
                var snackbar = FindName("NotificationSnackbar") as Snackbar;
                snackbar?.MessageQueue?.Enqueue($"Exported to {System.IO.Path.GetFileName(dialog.FileName)}");
            }
        }

        /// <summary>
        /// Handles scan completed event from ViewModel.
        /// </summary>
        private void OnScanCompleted(object sender, ScanCompletedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Animate progress bar completion
                var progressBar = FindName("ScanProgressBar") as ProgressBar;
                if (progressBar != null)
                {
                    var animation = new DoubleAnimation
                    {
                        To = 100,
                        Duration = TimeSpan.FromMilliseconds(300),
                        EasingFunction = new QuadraticEase()
                    };
                    
                    animation.Completed += (s, args) =>
                    {
                        // Hide progress bar after a delay
                        Task.Delay(1000).ContinueWith(_ =>
                        {
                            Dispatcher.Invoke(() => progressBar.Visibility = Visibility.Collapsed);
                        });
                    };

                    progressBar.BeginAnimation(ProgressBar.ValueProperty, animation);
                }

                // Show completion notification
                var snackbar = FindName("NotificationSnackbar") as Snackbar;
                snackbar?.MessageQueue?.Enqueue(
                    $"Scan completed: {e.DevicesFound} devices found in {e.ScanDuration.TotalSeconds:F1} seconds");

                // Flash the results if devices were found
                if (e.DevicesFound > 0)
                {
                    AnimateNewDevices();
                }
            });
        }

        /// <summary>
        /// Animates newly discovered devices.
        /// </summary>
        private void AnimateNewDevices()
        {
            var storyboard = new Storyboard();

            // Create a brief highlight animation
            var colorAnimation = new ColorAnimation
            {
                From = Colors.LightGreen,
                To = Colors.Transparent,
                Duration = TimeSpan.FromSeconds(2),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            Storyboard.SetTarget(colorAnimation, DeviceGrid);
            Storyboard.SetTargetProperty(colorAnimation, 
                new PropertyPath("(DataGrid.RowStyle).(Style.Setters).(Setter.Value).(SolidColorBrush.Color)"));

            storyboard.Children.Add(colorAnimation);
            storyboard.Begin();
        }

        /// <summary>
        /// Handles search box text changes for filtering.
        /// </summary>
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var searchText = (sender as TextBox)?.Text;
            if (_viewModel != null && !string.IsNullOrWhiteSpace(searchText))
            {
                // Debounce search to avoid excessive filtering
                _searchDebounceTimer?.Stop();
                _searchDebounceTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(300)
                };
                _searchDebounceTimer.Tick += (s, args) =>
                {
                    _searchDebounceTimer.Stop();
                    _viewModel.FilterDevices(searchText);
                };
                _searchDebounceTimer.Start();
            }
            else
            {
                _viewModel?.ClearFilter();
            }
        }

        private System.Windows.Threading.DispatcherTimer _searchDebounceTimer;

        /// <summary>
        /// Handles subnet selection changes.
        /// </summary>
        private void SubnetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && _viewModel != null)
            {
                var subnet = e.AddedItems[0] as SubnetInfo;
                if (subnet != null)
                {
                    _viewModel.SelectedSubnet = subnet;
                }
            }
        }

        /// <summary>
        /// Handles advanced settings button click.
        /// </summary>
        private void AdvancedSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ScanSettingsDialog(_viewModel?.ScanOptions)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                _viewModel?.UpdateScanOptions(dialog.ScanOptions);
            }
        }

        /// <summary>
        /// Handles device selection changes for updating UI state.
        /// </summary>
        private void DeviceGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Update selection count in status bar
            var selectionLabel = FindName("SelectionCountLabel") as TextBlock;
            if (selectionLabel != null)
            {
                var count = DeviceGrid.SelectedItems.Count;
                selectionLabel.Text = count > 0 
                    ? $"{count} device(s) selected" 
                    : string.Empty;
            }

            // Enable/disable relevant commands
            _viewModel?.UpdateCommandStates();
        }

        /// <summary>
        /// Handles drag selection in DataGrid.
        /// </summary>
        private void DeviceGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void DeviceGrid_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _dragStartPoint.HasValue)
            {
                var currentPosition = e.GetPosition(null);
                var diff = _dragStartPoint.Value - currentPosition;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    // Could implement drag selection here if needed
                    _dragStartPoint = null;
                }
            }
        }

        private Point? _dragStartPoint;
    }

    // Placeholder classes - these should be actual dialog implementations
    public class DeviceDetailsWindow : Window
    {
        public DeviceDetailsWindow(ScannedDevice device)
        {
            Title = $"Device Details - {device.IPAddress}";
            Width = 600;
            Height = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            // Implementation would show detailed device information
        }
    }

    public class ScanSettingsDialog : Window
    {
        public ScanOptions ScanOptions { get; set; }

        public ScanSettingsDialog(ScanOptions options)
        {
            Title = "Advanced Scan Settings";
            Width = 500;
            Height = 600;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ScanOptions = options ?? new ScanOptions();
            // Implementation would allow editing scan options
        }
    }
}

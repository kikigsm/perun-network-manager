using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using PerunNetworkManager.ViewModels;

namespace PerunNetworkManager.Views
{
    public partial class MainWindow : Window
    {
        private MainViewModel ViewModel => (MainViewModel)DataContext;

        public MainWindow()
        {
            InitializeComponent();
            
            // Set up keyboard shortcuts
            SetupKeyboardShortcuts();
            
            // Handle window state changes for system tray
            StateChanged += MainWindow_StateChanged;
        }

        private void SetupKeyboardShortcuts()
        {
            // Ctrl+N - New Profile
            var newProfileBinding = new KeyBinding(ViewModel.NewProfileCommand, Key.N, ModifierKeys.Control);
            InputBindings.Add(newProfileBinding);

            // Ctrl+S - Save Profile
            var saveProfileBinding = new KeyBinding(ViewModel.SaveProfileCommand, Key.S, ModifierKeys.Control);
            InputBindings.Add(saveProfileBinding);

            // Ctrl+I - Import Profiles
            var importBinding = new KeyBinding(ViewModel.ImportProfilesCommand, Key.I, ModifierKeys.Control);
            InputBindings.Add(importBinding);

            // Ctrl+E - Export Profiles
            var exportBinding = new KeyBinding(ViewModel.ExportProfilesCommand, Key.E, ModifierKeys.Control);
            InputBindings.Add(exportBinding);

            // F5 - Refresh / Apply Profile
            var refreshBinding = new KeyBinding(ViewModel.RefreshCommand, Key.F5, ModifierKeys.None);
            InputBindings.Add(refreshBinding);

            // Delete - Delete Profile
            var deleteBinding = new KeyBinding(ViewModel.DeleteProfileCommand, Key.Delete, ModifierKeys.None);
            InputBindings.Add(deleteBinding);

            // Ctrl+, - Settings
            var settingsBinding = new KeyBinding(ViewModel.ShowSettingsCommand, Key.OemComma, ModifierKeys.Control);
            InputBindings.Add(settingsBinding);

            // Escape - Close dialogs or minimize to tray
            var escapeBinding = new KeyBinding(new RelayCommand(HandleEscapeKey), Key.Escape, ModifierKeys.None);
            InputBindings.Add(escapeBinding);
        }

        private void HandleEscapeKey()
        {
            // If any modal dialogs are open, close them
            // Otherwise, minimize to system tray
            WindowState = WindowState.Minimized;
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            switch (WindowState)
            {
                case WindowState.Minimized:
                    Hide();
                    SystemTrayIcon.ShowBalloonTip("Perun Network Manager", 
                                                "Application minimized to system tray", 
                                                Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
                    break;
            }
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            MainWindow_StateChanged(sender, e);
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            // Instead of closing, minimize to system tray
            e.Cancel = true;
            WindowState = WindowState.Minimized;
        }

        private void SystemTrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            ShowWindow();
        }

        private void ShowWindow_Click(object sender, RoutedEventArgs e)
        {
            ShowWindow();
        }

        private void ShowWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            Focus();
        }

        private void ShowNetworkScanner_Click(object sender, RoutedEventArgs e)
        {
            ShowWindow();
            ViewModel.ShowNetworkScannerCommand.Execute(null);
        }

        private void ShowDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            ShowWindow();
            ViewModel.ShowDiagnosticsCommand.Execute(null);
        }

        private void ExitApplication_Click(object sender, RoutedEventArgs e)
        {
            // Actually exit the application
            ViewModel.ExitApplicationCommand.Execute(null);
            Application.Current.Shutdown();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            // Set up high DPI awareness
            SetupHighDPISupport();
        }

        private void SetupHighDPISupport()
        {
            // Enable per-monitor DPI awareness for better display on multiple monitors
            try
            {
                var source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget != null)
                {
                    var dpiX = 96.0 * source.CompositionTarget.TransformToDevice.M11;
                    var dpiY = 96.0 * source.CompositionTarget.TransformToDevice.M22;
                    
                    // Store DPI information for later use
                    ViewModel.CurrentDpiX = dpiX;
                    ViewModel.CurrentDpiY = dpiY;
                }
            }
            catch (Exception ex)
            {
                // Log DPI detection error but don't crash
                System.Diagnostics.Debug.WriteLine($"DPI detection error: {ex.Message}");
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize the application after window is loaded
            ViewModel.InitializeAsync();
        }

        // Handle global exception for better user experience
        private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"An unexpected error occurred: {e.Exception.Message}", 
                          "Perun Network Manager Error", 
                          MessageBoxButton.OK, 
                          MessageBoxImage.Error);
            
            e.Handled = true;
        }
    }

    // Simple RelayCommand implementation for keyboard shortcuts
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object? parameter)
        {
            return _canExecute?.Invoke() ?? true;
        }

        public void Execute(object? parameter)
        {
            _execute();
        }
    }
}

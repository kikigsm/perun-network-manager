using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using PerunNetworkManager.Core.Services;
using PerunNetworkManager.Services;
using PerunNetworkManager.ViewModels;
using PerunNetworkManager.Views;
using System.IO;
using System.Security.Principal;
using System.Diagnostics;
using Hardcodet.Wpf.TaskbarNotification;

namespace PerunNetworkManager
{
    public partial class App : Application
    {
        private ServiceProvider? _serviceProvider;
        private ILogger<App>? _logger;
        private TaskbarIcon? _taskbarIcon;
        private Mutex? _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Check for single instance
            if (!CheckSingleInstance())
            {
                MessageBox.Show("Perun Network Manager is already running.", 
                              "Application Already Running", 
                              MessageBoxButton.OK, 
                              MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // Check administrator privileges
            if (!IsRunningAsAdministrator())
            {
                var result = MessageBox.Show(
                    "Perun Network Manager requires administrator privileges to manage network settings.\n\n" +
                    "Would you like to restart as administrator?",
                    "Administrator Privileges Required",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    RestartAsAdministrator();
                }
                
                Shutdown();
                return;
            }

            try
            {
                // Setup services
                ConfigureServices();
                
                // Initialize logging
                _logger = _serviceProvider!.GetRequiredService<ILogger<App>>();
                _logger.LogInformation("Perun Network Manager starting...");

                // Setup global exception handling
                SetupExceptionHandling();

                // Initialize localization
                InitializeLocalization();

                // Create and show main window
                var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
                MainWindow = mainWindow;
                
                // Handle command line arguments
                HandleCommandLineArguments(e.Args);

                // Show main window unless started minimized
                if (!e.Args.Contains("-minimized"))
                {
                    mainWindow.Show();
                }

                base.OnStartup(e);
                _logger.LogInformation("Application started successfully");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start application: {ex.Message}", 
                              "Startup Error", 
                              MessageBoxButton.OK, 
                              MessageBoxImage.Error);
                Shutdown();
            }
        }

        private bool CheckSingleInstance()
        {
            bool createdNew;
            _mutex = new Mutex(true, "PerunNetworkManager_SingleInstance", out createdNew);
            return createdNew;
        }

        private bool IsRunningAsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private void RestartAsAdministrator()
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                Process.Start(processInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to restart as administrator: {ex.Message}",
                              "Restart Error",
                              MessageBoxButton.OK,
                              MessageBoxImage.Error);
            }
        }

        private void ConfigureServices()
        {
            var services = new ServiceCollection();

            // Configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
                .Build();

            services.AddSingleton<IConfiguration>(configuration);

            // Logging
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.AddDebug();
                builder.AddFile("Logs/PerunNetworkManager-{Date}.txt");
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // Core Services
            services.AddSingleton<NetworkService>();
            services.AddSingleton<ProfileService>();
            services.AddSingleton<NetworkScannerService>();
            services.AddSingleton<MacVendorService>();
            services.AddSingleton<SystemTrayService>();
            services.AddSingleton<LocalizationService>();

            // ViewModels
            services.AddTransient<MainViewModel>();
            services.AddTransient<ProfileEditorViewModel>();
            services.AddTransient<NetworkScannerViewModel>();
            services.AddTransient<DiagnosticViewModel>();
            services.AddTransient<NetworkAdapterViewModel>();

            // Views
            services.AddTransient<MainWindow>();
            services.AddTransient<ProfileEditorView>();
            services.AddTransient<NetworkScannerView>();
            services.AddTransient<DiagnosticToolsView>();
            services.AddTransient<NetworkAdapterView>();

            _serviceProvider = services.BuildServiceProvider();
        }

        private void SetupExceptionHandling()
        {
            // Handle unhandled exceptions
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            _logger?.LogError(e.Exception, "Unhandled dispatcher exception");
            
            var result = MessageBox.Show(
                $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nWould you like to continue?",
                "Unexpected Error",
                MessageBoxButton.YesNo,
                MessageBoxImage.Error);

            if (result == MessageBoxResult.Yes)
            {
                e.Handled = true;
            }
            else
            {
                Shutdown();
            }
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            _logger?.LogCritical(e.ExceptionObject as Exception, "Unhandled domain exception");
            
            MessageBox.Show(
                $"A critical error occurred and the application must close:\n\n{(e.ExceptionObject as Exception)?.Message}",
                "Critical Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            _logger?.LogError(e.Exception, "Unobserved task exception");
            e.SetObserved(); // Prevent the application from crashing
        }

        private void InitializeLocalization()
        {
            try
            {
                var localizationService = _serviceProvider?.GetService<LocalizationService>();
                localizationService?.Initialize();
                
                // Set culture based on system settings or user preferences
                var savedCulture = Properties.Settings.Default.Language;
                if (!string.IsNullOrEmpty(savedCulture))
                {
                    localizationService?.SetCulture(savedCulture);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to initialize localization");
            }
        }

        private void HandleCommandLineArguments(string[] args)
        {
            foreach (var arg in args)
            {
                switch (arg.ToLowerInvariant())
                {
                    case "-minimized":
                    case "/minimized":
                        MainWindow.WindowState = WindowState.Minimized;
                        MainWindow.Hide();
                        break;
                        
                    case "-autostart":
                    case "/autostart":
                        // Application started automatically with Windows
                        Properties.Settings.Default.AutoStarted = true;
                        break;
                        
                    default:
                        // Check if it's a profile file
                        if (File.Exists(arg) && Path.GetExtension(arg).Equals(".npx", StringComparison.OrdinalIgnoreCase))
                        {
                            // Import profile file
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var profileService = _serviceProvider?.GetService<ProfileService>();
                                    if (profileService != null)
                                    {
                                        await profileService.ImportProfilesAsync(arg);
                                        _logger?.LogInformation($"Imported profile from command line: {arg}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger?.LogError(ex, $"Failed to import profile from command line: {arg}");
                                }
                            });
                        }
                        break;
                }
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _logger?.LogInformation("Application shutting down...");
                
                // Save settings
                Properties.Settings.Default.Save();
                
                // Dispose services
                _serviceProvider?.GetService<SystemTrayService>()?.Dispose();
                _taskbarIcon?.Dispose();
                _serviceProvider?.Dispose();
                _mutex?.ReleaseMutex();
                _mutex?.Dispose();

                _logger?.LogInformation("Application shutdown complete");
            }
            catch (Exception ex)
            {
                // Log but don't prevent shutdown
                _logger?.LogError(ex, "Error during application shutdown");
            }

            base.OnExit(e);
        }
    }

    // Extension method for file logging
    public static class LoggingExtensions
    {
        public static ILoggingBuilder AddFile(this ILoggingBuilder builder, string path)
        {
            return builder.AddProvider(new FileLoggerProvider(path));
        }
    }

    public class FileLoggerProvider : ILoggerProvider
    {
        private readonly string _path;

        public FileLoggerProvider(string path)
        {
            _path = path;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new FileLogger(_path);
        }

        public void Dispose() { }
    }

    public class FileLogger : ILogger
    {
        private readonly string _path;
        private readonly object _lock = new object();

        public FileLogger(string path)
        {
            _path = path.Replace("{Date}", DateTime.Now.ToString("yyyy-MM-dd"));
            
            // Ensure log directory exists
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        public IDisposable BeginScope<TState>(TState state) => null!;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            lock (_lock)
            {
                var message = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{logLevel}] {formatter(state, exception)}";
                if (exception != null)
                {
                    message += Environment.NewLine + exception.ToString();
                }
                
                File.AppendAllText(_path, message + Environment.NewLine);
            }
        }
    }
}

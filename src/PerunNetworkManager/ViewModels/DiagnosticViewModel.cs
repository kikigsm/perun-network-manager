using System.Collections.ObjectModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PerunNetworkManager.Core.Models;
using PerunNetworkManager.Core.Services;

namespace PerunNetworkManager.ViewModels
{
    public partial class DiagnosticViewModel : ObservableObject
    {
        private readonly NetworkService _networkService;
        private readonly ILogger<DiagnosticViewModel> _logger;
        private CancellationTokenSource? _cancellationTokenSource;

        [ObservableProperty]
        private string _pingTarget = "8.8.8.8";

        [ObservableProperty]
        private int _pingCount = 4;

        [ObservableProperty]
        private int _pingTimeout = 5000;

        [ObservableProperty]
        private bool _isPinging;

        [ObservableProperty]
        private ObservableCollection<PingResult> _pingResults = new();

        [ObservableProperty]
        private string _tracerouteTarget = "8.8.8.8";

        [ObservableProperty]
        private int _tracerouteMaxHops = 30;

        [ObservableProperty]
        private bool _isTracingRoute;

        [ObservableProperty]
        private ObservableCollection<TracerouteHop> _tracerouteResults = new();

        [ObservableProperty]
        private string _dnsTarget = "google.com";

        [ObservableProperty]
        private string _dnsServer = "8.8.8.8";

        [ObservableProperty]
        private bool _isResolvingDns;

        [ObservableProperty]
        private ObservableCollection<DnsResult> _dnsResults = new();

        [ObservableProperty]
        private string _portScanTarget = "192.168.1.1";

        [ObservableProperty]
        private string _portScanRange = "1-1000";

        [ObservableProperty]
        private bool _isPortScanning;

        [ObservableProperty]
        private ObservableCollection<PortScanResult> _portScanResults = new();

        [ObservableProperty]
        private ObservableCollection<NetworkAdapter> _networkAdapters = new();

        [ObservableProperty]
        private NetworkAdapter? _selectedAdapter;

        [ObservableProperty]
        private bool _isRunningDiagnostics;

        [ObservableProperty]
        private string _diagnosticStatus = "Ready";

        [ObservableProperty]
        private NetworkDiagnosticResult? _lastDiagnosticResult;

        public DiagnosticViewModel(NetworkService networkService, ILogger<DiagnosticViewModel> logger)
        {
            _networkService = networkService;
            _logger = logger;

            InitializeCommands();
            LoadNetworkAdapters();
        }

        private void InitializeCommands()
        {
            StartPingCommand = new AsyncRelayCommand(StartPingAsync, CanStartPing);
            StopPingCommand = new RelayCommand(StopPing, CanStopPing);
            StartTracerouteCommand = new AsyncRelayCommand(StartTracerouteAsync, CanStartTraceroute);
            StopTracerouteCommand = new RelayCommand(StopTraceroute, CanStopTraceroute);
            ResolveDnsCommand = new AsyncRelayCommand(ResolveDnsAsync, CanResolveDns);
            StartPortScanCommand = new AsyncRelayCommand(StartPortScanAsync, CanStartPortScan);
            StopPortScanCommand = new RelayCommand(StopPortScan, CanStopPortScan);
            RunFullDiagnosticsCommand = new AsyncRelayCommand(RunFullDiagnosticsAsync, CanRunDiagnostics);
            ClearResultsCommand = new RelayCommand(ClearResults);
            ExportResultsCommand = new RelayCommand(ExportResults, CanExportResults);
            RefreshAdaptersCommand = new AsyncRelayCommand(LoadNetworkAdapters);
        }

        public ICommand StartPingCommand { get; private set; } = null!;
        public ICommand StopPingCommand { get; private set; } = null!;
        public ICommand StartTracerouteCommand { get; private set; } = null!;
        public ICommand StopTracerouteCommand { get; private set; } = null!;
        public ICommand ResolveDnsCommand { get; private set; } = null!;
        public ICommand StartPortScanCommand { get; private set; } = null!;
        public ICommand StopPortScanCommand { get; private set; } = null!;
        public ICommand RunFullDiagnosticsCommand { get; private set; } = null!;
        public ICommand ClearResultsCommand { get; private set; } = null!;
        public ICommand ExportResultsCommand { get; private set; } = null!;
        public ICommand RefreshAdaptersCommand { get; private set; } = null!;

        private async Task LoadNetworkAdapters()
        {
            try
            {
                var adapters = await _networkService.GetNetworkAdaptersAsync();
                NetworkAdapters.Clear();
                
                foreach (var adapter in adapters)
                {
                    NetworkAdapters.Add(adapter);
                }

                SelectedAdapter = NetworkAdapters.FirstOrDefault(a => a.Status == OperationalStatus.Up);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load network adapters");
            }
        }

        private bool CanStartPing() => !IsPinging && !string.IsNullOrWhiteSpace(PingTarget);

        private async Task StartPingAsync()
        {
            try
            {
                IsPinging = true;
                _cancellationTokenSource = new CancellationTokenSource();
                PingResults.Clear();

                DiagnosticStatus = $"Pinging {PingTarget}...";

                using var ping = new Ping();
                
                for (int i = 0; i < PingCount && !_cancellationTokenSource.Token.IsCancellationRequested; i++)
                {
                    try
                    {
                        var reply = await ping.SendPingAsync(PingTarget, PingTimeout);
                        
                        var result = new PingResult
                        {
                            SequenceNumber = i + 1,
                            Target = PingTarget,
                            Status = reply.Status,
                            RoundtripTime = reply.RoundtripTime,
                            Timestamp = DateTime.Now,
                            BufferSize = reply.Buffer?.Length ?? 0
                        };

                        PingResults.Add(result);
                        
                        if (i < PingCount - 1)
                        {
                            await Task.Delay(1000, _cancellationTokenSource.Token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        var errorResult = new PingResult
                        {
                            SequenceNumber = i + 1,
                            Target = PingTarget,
                            Status = IPStatus.Unknown,
                            ErrorMessage = ex.Message,
                            Timestamp = DateTime.Now
                        };
                        PingResults.Add(errorResult);
                    }
                }

                var successCount = PingResults.Count(r => r.Status == IPStatus.Success);
                var lossPercentage = ((double)(PingCount - successCount) / PingCount) * 100;
                
                DiagnosticStatus = $"Ping completed: {successCount}/{PingCount} success ({lossPercentage:F0}% loss)";
                
                _logger.LogInformation("Ping test completed for {Target}: {Success}/{Total} success", 
                    PingTarget, successCount, PingCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during ping test");
                DiagnosticStatus = $"Ping failed: {ex.Message}";
            }
            finally
            {
                IsPinging = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private bool CanStopPing() => IsPinging;

        private void StopPing()
        {
            _cancellationTokenSource?.Cancel();
            DiagnosticStatus = "Ping cancelled";
        }

        private bool CanStartTraceroute() => !IsTracingRoute && !string.IsNullOrWhiteSpace(TracerouteTarget);

        private async Task StartTracerouteAsync()
        {
            try
            {
                IsTracingRoute = true;
                _cancellationTokenSource = new CancellationTokenSource();
                TracerouteResults.Clear();

                DiagnosticStatus = $"Tracing route to {TracerouteTarget}...";

                await PerformTracerouteAsync(TracerouteTarget, TracerouteMaxHops, _cancellationTokenSource.Token);

                DiagnosticStatus = $"Traceroute completed: {TracerouteResults.Count} hops";
            }
            catch (OperationCanceledException)
            {
                DiagnosticStatus = "Traceroute cancelled";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during traceroute");
                DiagnosticStatus = $"Traceroute failed: {ex.Message}";
            }
            finally
            {
                IsTracingRoute = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private async Task PerformTracerouteAsync(string target, int maxHops, CancellationToken cancellationToken)
        {
            using var ping = new Ping();
            var options = new PingOptions(1, true);

            for (int hop = 1; hop <= maxHops && !cancellationToken.IsCancellationRequested; hop++)
            {
                options.Ttl = hop;
                
                try
                {
                    var reply = await ping.SendPingAsync(target, 5000, new byte[32], options);
                    
                    var hopResult = new TracerouteHop
                    {
                        HopNumber = hop,
                        IPAddress = reply.Address?.ToString() ?? "N/A",
                        RoundtripTime = reply.RoundtripTime,
                        Status = reply.Status,
                        Timestamp = DateTime.Now
                    };

                    // Try to resolve hostname
                    if (reply.Address != null)
                    {
                        try
                        {
                            var hostEntry = await Dns.GetHostEntryAsync(reply.Address);
                            hopResult.Hostname = hostEntry.HostName;
                        }
                        catch
                        {
                            hopResult.Hostname = "N/A";
                        }
                    }

                    TracerouteResults.Add(hopResult);

                    if (reply.Status == IPStatus.Success)
                    {
                        break; // Reached destination
                    }
                }
                catch (Exception ex)
                {
                    var errorHop = new TracerouteHop
                    {
                        HopNumber = hop,
                        IPAddress = "N/A",
                        Hostname = "Error",
                        Status = IPStatus.Unknown,
                        ErrorMessage = ex.Message,
                        Timestamp = DateTime.Now
                    };
                    TracerouteResults.Add(errorHop);
                }

                await Task.Delay(100, cancellationToken);
            }
        }

        private bool CanStopTraceroute() => IsTracingRoute;

        private void StopTraceroute()
        {
            _cancellationTokenSource?.Cancel();
            DiagnosticStatus = "Traceroute cancelled";
        }

        private bool CanResolveDns() => !IsResolvingDns && !string.IsNullOrWhiteSpace(DnsTarget);

        private async Task ResolveDnsAsync()
        {
            try
            {
                IsResolvingDns = true;
                DnsResults.Clear();

                DiagnosticStatus = $"Resolving {DnsTarget}...";

                // Resolve A records
                try
                {
                    var addresses = await Dns.GetHostAddressesAsync(DnsTarget);
                    foreach (var address in addresses)
                    {
                        DnsResults.

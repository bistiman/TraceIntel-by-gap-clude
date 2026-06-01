using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using TraceIntel.Core.Models;
using TraceIntel.Core.Services;
using TraceIntel.UI.ViewModels;
using DnsClient;

namespace TraceIntel.UI
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private ParallelTraceEngine _traceEngine;

        private string _domainsInput;
        private int _maxHops = 30;
        private string _status;
        private double _progress;
        private bool _isRunning;
        private CancellationTokenSource _cancellationTokenSource;
        private int _hopColumnCount;
        private string _globalSearchText = string.Empty;
        private string _selectedExportFormat = "CSV";
        private int _successfulTracesCount;
        private string _hopMode = "Complete";
        private int _customMaxHops = 30;

        public ObservableCollection<DomainTrace> Results { get; set; }
        public ObservableCollection<HopSection> HopSections { get; } = new();

        public ObservableCollection<RoutingRow> RoutingRows { get; } = new();
        public ICollectionView RoutingRowsView { get; }
        public ObservableCollection<HopColumnFilter> HopFilters { get; } = new();
        public ObservableCollection<string> ExportFormats { get; } = new() { "CSV", "JSON", "TXT" };

        // DNS Reconnaissance Properties
        private DnsReconService _dnsReconService = new();
        private CancellationTokenSource? _dnsReconCts;
        private string _dnsReconTarget = string.Empty;
        private string _dnsReconStatus = "Ready";
        private bool _isDnsReconRunning;
        private string _dnsReconSearchText = string.Empty;
        private DnsRecord? _selectedDnsRecord;
        private int _selectedTabIndex;

        public string DnsReconTarget
        {
            get => _dnsReconTarget;
            set { _dnsReconTarget = value; OnPropertyChanged(); }
        }

        public string DnsReconStatus
        {
            get => _dnsReconStatus;
            set { _dnsReconStatus = value; OnPropertyChanged(); }
        }

        public bool IsDnsReconRunning
        {
            get => _isDnsReconRunning;
            private set
            {
                if (_isDnsReconRunning != value)
                {
                    _isDnsReconRunning = value;
                    OnPropertyChanged();
                }
            }
        }

        public string DnsReconSearchText
        {
            get => _dnsReconSearchText;
            set
            {
                if (_dnsReconSearchText == value) return;
                _dnsReconSearchText = value;
                OnPropertyChanged();
                DnsRecordsView.Refresh();
            }
        }

        public DnsRecord? SelectedDnsRecord
        {
            get => _selectedDnsRecord;
            set { _selectedDnsRecord = value; OnPropertyChanged(); }
        }

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set { _selectedTabIndex = value; OnPropertyChanged(); }
        }

        public ObservableCollection<DnsRecord> DnsRecords { get; } = new();
        public ICollectionView DnsRecordsView { get; }

        public ICommand StartDnsReconCommand { get; }
        public ICommand StopDnsReconCommand { get; }
        public ICommand ExportDnsReconCommand { get; }
        public ICommand RunReconFromTraceCommand { get; }

        // ==========================================
        // ADVANCED SECURITY RECONNAISSANCE PROPERTIES
        // ==========================================

        // Dashboard VM reference
        public DashboardViewModel Dashboard { get; }

        // Navigation drawer navigation command
        public ICommand NavigateCommand { get; }

        // Live Cyber Console Log
        public ObservableCollection<string> ActivityLog { get; } = new()
        {
            $"[{DateTime.Now:HH:mm:ss}] TRACE X Security Intel Suite initialized."
        };

        public void LogActivity(string message)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                ActivityLog.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
                while (ActivityLog.Count > 100)
                {
                    ActivityLog.RemoveAt(ActivityLog.Count - 1);
                }
            });
        }

        // DNS record selections
        private bool _dnsQueryA = true;
        private bool _dnsQueryAAAA = true;
        private bool _dnsQueryMX = true;
        private bool _dnsQueryNS = true;
        private bool _dnsQueryCNAME = true;
        private bool _dnsQueryTXT = true;
        private bool _dnsQuerySOA = true;
        private bool _dnsQuerySRV = true;
        private bool _dnsQueryCAA = true;
        private bool _dnsQueryAXFR = false;
        private bool _dnsBruteForce = false;
        private string _dnsCustomServer = string.Empty;
        private string _dnsScanMode = "Standard";

        public bool DnsQueryA { get => _dnsQueryA; set { _dnsQueryA = value; OnPropertyChanged(); } }
        public bool DnsQueryAAAA { get => _dnsQueryAAAA; set { _dnsQueryAAAA = value; OnPropertyChanged(); } }
        public bool DnsQueryMX { get => _dnsQueryMX; set { _dnsQueryMX = value; OnPropertyChanged(); } }
        public bool DnsQueryNS { get => _dnsQueryNS; set { _dnsQueryNS = value; OnPropertyChanged(); } }
        public bool DnsQueryCNAME { get => _dnsQueryCNAME; set { _dnsQueryCNAME = value; OnPropertyChanged(); } }
        public bool DnsQueryTXT { get => _dnsQueryTXT; set { _dnsQueryTXT = value; OnPropertyChanged(); } }
        public bool DnsQuerySOA { get => _dnsQuerySOA; set { _dnsQuerySOA = value; OnPropertyChanged(); } }
        public bool DnsQuerySRV { get => _dnsQuerySRV; set { _dnsQuerySRV = value; OnPropertyChanged(); } }
        public bool DnsQueryCAA { get => _dnsQueryCAA; set { _dnsQueryCAA = value; OnPropertyChanged(); } }
        public bool DnsQueryAXFR { get => _dnsQueryAXFR; set { _dnsQueryAXFR = value; OnPropertyChanged(); } }
        public bool DnsBruteForce { get => _dnsBruteForce; set { _dnsBruteForce = value; OnPropertyChanged(); } }
        public string DnsCustomServer { get => _dnsCustomServer; set { _dnsCustomServer = value; OnPropertyChanged(); } }
        
        public string DnsScanMode 
        { 
            get => _dnsScanMode; 
            set 
            { 
                _dnsScanMode = value; 
                OnPropertyChanged(); 
                ApplyDnsScanMode(); 
            } 
        }

        // WHOIS properties
        private WhoisService _whoisService = new();
        private CancellationTokenSource? _whoisCts;
        private string _whoisTarget = string.Empty;
        private string _whoisResultText = string.Empty;
        private bool _isWhoisRunning;
        private string _whoisStatus = "Ready";

        public string WhoisTarget { get => _whoisTarget; set { _whoisTarget = value; OnPropertyChanged(); } }
        public string WhoisResultText { get => _whoisResultText; set { _whoisResultText = value; OnPropertyChanged(); } }
        public bool IsWhoisRunning { get => _isWhoisRunning; set { _isWhoisRunning = value; OnPropertyChanged(); } }
        public string WhoisStatus { get => _whoisStatus; set { _whoisStatus = value; OnPropertyChanged(); } }

        public ICommand StartWhoisCommand { get; }
        public ICommand StopWhoisCommand { get; }

        // NSLOOKUP properties
        private string _nslookupTarget = string.Empty;
        private string _nslookupRecordType = "A";
        private string _nslookupServer = string.Empty;
        private string _nslookupResultText = string.Empty;
        private bool _isNslookupRunning;
        private string _nslookupStatus = "Ready";

        public string NslookupTarget { get => _nslookupTarget; set { _nslookupTarget = value; OnPropertyChanged(); } }
        public string NslookupRecordType { get => _nslookupRecordType; set { _nslookupRecordType = value; OnPropertyChanged(); } }
        public string NslookupServer { get => _nslookupServer; set { _nslookupServer = value; OnPropertyChanged(); } }
        public string NslookupResultText { get => _nslookupResultText; set { _nslookupResultText = value; OnPropertyChanged(); } }
        public bool IsNslookupRunning { get => _isNslookupRunning; set { _isNslookupRunning = value; OnPropertyChanged(); } }
        public string NslookupStatus { get => _nslookupStatus; set { _nslookupStatus = value; OnPropertyChanged(); } }

        public ObservableCollection<string> NslookupRecordTypes { get; } = new() { "A", "AAAA", "MX", "NS", "CNAME", "TXT", "SOA", "SRV", "CAA", "ANY" };
        public ICommand RunNslookupCommand { get; }

        // CONFIGURATION / SETTINGS PROPERTIES
        private int _settingsParallelism = 10;
        private int _settingsTimeoutMs = 1000;
        private bool _settingsEnableCache = true;
        private string _settingsDefaultExportPath = string.Empty;
        private string _settingsDefaultDnsServer = string.Empty;

        public int SettingsParallelism { get => _settingsParallelism; set { _settingsParallelism = value; OnPropertyChanged(); } }
        public int SettingsTimeoutMs { get => _settingsTimeoutMs; set { _settingsTimeoutMs = value; OnPropertyChanged(); } }
        public bool SettingsEnableCache { get => _settingsEnableCache; set { _settingsEnableCache = value; OnPropertyChanged(); } }
        public string SettingsDefaultExportPath { get => _settingsDefaultExportPath; set { _settingsDefaultExportPath = value; OnPropertyChanged(); } }
        public string SettingsDefaultDnsServer 
        { 
            get => _settingsDefaultDnsServer; 
            set 
            { 
                _settingsDefaultDnsServer = value; 
                OnPropertyChanged(); 
                if (string.IsNullOrWhiteSpace(DnsCustomServer))
                {
                    DnsCustomServer = value;
                }
            } 
        }

        public int HopColumnCount
        {
            get => _hopColumnCount;
            private set
            {
                if (_hopColumnCount == value) return;
                _hopColumnCount = value;
                OnPropertyChanged();
            }
        }

        public string GlobalSearchText
        {
            get => _globalSearchText;
            set
            {
                if (_globalSearchText == value) return;
                _globalSearchText = value;
                OnPropertyChanged();
                RoutingRowsView.Refresh();
            }
        }

        public string SelectedExportFormat
        {
            get => _selectedExportFormat;
            set
            {
                if (_selectedExportFormat == value) return;
                _selectedExportFormat = value;
                OnPropertyChanged();
            }
        }

        public int SuccessfulTracesCount
        {
            get => _successfulTracesCount;
            private set
            {
                if (_successfulTracesCount == value) return;
                _successfulTracesCount = value;
                OnPropertyChanged();
            }
        }

        public string HopMode
        {
            get => _hopMode;
            set
            {
                if (_hopMode == value) return;
                _hopMode = value;
                OnPropertyChanged();
                ApplyHopMode();
            }
        }

        public int CustomMaxHops
        {
            get => _customMaxHops;
            set
            {
                if (_customMaxHops == value) return;
                if (value <= 0 || value > 255) return;
                _customMaxHops = value;
                OnPropertyChanged();
                if (HopMode == "Custom")
                {
                    MaxHops = _customMaxHops;
                }
            }
        }

        public string DomainsInput
        {
            get => _domainsInput;
            set { _domainsInput = value; OnPropertyChanged(); }
        }

        public int MaxHops
        {
            get => _maxHops;
            set
            {
                if (value > 0 && value <= 255)
                {
                    _maxHops = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public double Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(); }
        }

        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (_isRunning != value)
                {
                    _isRunning = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand ExportCommand { get; }

        public MainViewModel()
        {
            Results = new ObservableCollection<DomainTrace>();
            Status = "Ready";
            Progress = 0;

            Dashboard = new DashboardViewModel(this);

            NavigateCommand = new RelayCommand(destination =>
            {
                if (destination is string dest)
                {
                    switch (dest)
                    {
                        case "Dashboard": SelectedTabIndex = 0; break;
                        case "Trace": SelectedTabIndex = 1; break;
                        case "ByHop": SelectedTabIndex = 2; break;
                        case "DnsRecon": SelectedTabIndex = 3; break;
                        case "Settings": SelectedTabIndex = 4; break;
                    }
                }
            });

            StartCommand = new RelayCommand(async _ => await StartTrace(), _ => !IsRunning);
            StopCommand = new RelayCommand(_ => StopTrace(), _ => IsRunning);
            ExportCommand = new RelayCommand(_ => ExportResults(), _ => Results.Any());

            RoutingRowsView = CollectionViewSource.GetDefaultView(RoutingRows);
            RoutingRowsView.SortDescriptions.Add(new SortDescription(nameof(RoutingRow.Domain), ListSortDirection.Ascending));
            RoutingRowsView.Filter = RoutingFilter;

            DnsRecordsView = CollectionViewSource.GetDefaultView(DnsRecords);
            DnsRecordsView.Filter = DnsRecordFilter;

            StartDnsReconCommand = new RelayCommand(async _ => await StartDnsRecon(), _ => !IsDnsReconRunning);
            StopDnsReconCommand = new RelayCommand(_ => StopDnsRecon(), _ => IsDnsReconRunning);
            ExportDnsReconCommand = new RelayCommand(_ => ExportDnsRecords(), _ => DnsRecords.Any());
            RunReconFromTraceCommand = new RelayCommand(async domain => await RunReconFromTrace(domain));

            StartWhoisCommand = new RelayCommand(async _ => await StartWhois(), _ => !IsWhoisRunning);
            StopWhoisCommand = new RelayCommand(_ => StopWhois(), _ => IsWhoisRunning);
            RunNslookupCommand = new RelayCommand(async _ => await RunNslookup(), _ => !IsNslookupRunning);

            ApplyHopMode();
        }

        private async Task StartTrace()
        {
            if (string.IsNullOrWhiteSpace(DomainsInput))
            {
                MessageBox.Show("Please enter at least one domain.", "Input Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var rawTokens = DomainsInput.Split(new[] { ',', ';', '\n', '\r', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(t => t.Trim())
                                        .Where(t => !string.IsNullOrWhiteSpace(t))
                                        .ToList();

            var normalized = rawTokens
                .Select(TryNormalizeHost)
                .Where(x => x != null)
                .Select(x => x!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalized.Count == 0)
            {
                MessageBox.Show("No valid domains/IPs found. Examples:\n- google.com\n- 8.8.8.8\n- https://example.com/path",
                    "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                Status = "Ready";
                return;
            }

            Status = "Validating domains...";
            var (validDomains, invalidDomains) = await ValidateHostsAsync(normalized, TimeSpan.FromSeconds(2), _cancellationTokenSource?.Token ?? CancellationToken.None);

            if (invalidDomains.Count > 0)
            {
                MessageBox.Show(
                    "Some entries are invalid or could not be resolved and will be skipped:\n\n" +
                    string.Join("\n", invalidDomains.Take(50)) +
                    (invalidDomains.Count > 50 ? $"\n... and {invalidDomains.Count - 50} more" : ""),
                    "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            var domains = validDomains;

            if (domains.Count == 0)
            {
                MessageBox.Show("No valid domains found after validation.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                Status = "Ready";
                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            IsRunning = true;
            Results.Clear();
            RoutingRows.Clear();
            HopFilters.Clear();
            HopColumnCount = 0;
            EnsureHopColumns(MaxHops); // Pre-build all dynamic columns upfront to eliminate massive UI/Grid rendering bottleneck
            GlobalSearchText = string.Empty;
            SuccessfulTracesCount = 0;
            Progress = 0;
            Status = "Running...";
            LogActivity($"Starting traceroute diagnostics on {domains.Count} target(s) (Depth: {MaxHops} hops)...");

            int total = domains.Count;
            int completed = 0;
            var domainMap = new Dictionary<string, DomainTrace>();
            var successfulDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                HopSections.Clear();
                _traceEngine = new ParallelTraceEngine(MaxHops); // respect current MaxHops

                await _traceEngine.StartTraceAsync(
                    domains,
                    (domain, hops) =>
                    {
                        Application.Current.Dispatcher.BeginInvoke(() =>
                        {
                            if (!domainMap.TryGetValue(domain, out var trace))
                            {
                                trace = new DomainTrace { Domain = domain };
                                domainMap[domain] = trace;
                                Results.Add(trace);
                            }

                            trace.Hops = hops
                                .OrderBy(h => h.HopNumber)
                                .Select(h => $"{h.HopNumber}. {h.IP}")
                                .ToList();

                            trace.HopCount = trace.Hops.Count;
                            trace.RoutePreview = string.Join("\n", trace.Hops);
                            // Destination is resolved from tracert header (HopNumber=0). Do not infer it from intermediate hops.
                            trace.Status = !string.IsNullOrWhiteSpace(trace.Destination) ? "Success" : "Incomplete";
                            if (trace.Status == "Success" && successfulDomains.Add(domain))
                            {
                                SuccessfulTracesCount = successfulDomains.Count;
                            }

                            LogActivity($"[Traceroute] Path mapping complete for {domain} ({hops.Count} hops, Status: {trace.Status})");

                            completed++;
                            Progress = (completed / (double)total) * 100;
                        });
                    },
                    (domain, hop) =>
                    {
                        Application.Current.Dispatcher.BeginInvoke(() =>
                        {
                            // HopNumber=0 is a special event: target IP resolved by tracert header.
                            if (hop.HopNumber == 0)
                            {
                                if (!domainMap.TryGetValue(domain, out var t0))
                                {
                                    t0 = new DomainTrace { Domain = domain, Status = "Tracing..." };
                                    domainMap[domain] = t0;
                                    Results.Add(t0);
                                }

                                // Hop.IP is encoded as "resolvedHost|ip" (host may be empty when input is an IP).
                                var resolvedHost = string.Empty;
                                var resolvedIp = string.Empty;
                                var raw = hop.IP ?? string.Empty;
                                var sep = raw.IndexOf('|');
                                if (sep >= 0)
                                {
                                    resolvedHost = raw.Substring(0, sep).Trim();
                                    resolvedIp = raw.Substring(sep + 1).Trim();
                                }
                                else
                                {
                                    resolvedIp = raw.Trim();
                                }

                                if (!string.IsNullOrWhiteSpace(resolvedHost))
                                {
                                    t0.ResolvedDestination = resolvedHost;
                                }

                                if (!string.IsNullOrWhiteSpace(resolvedIp) && resolvedIp != "*")
                                {
                                    t0.Destination = resolvedIp;
                                    SetRoutingDestination(domain, resolvedIp, resolvedHost);
                                    LogActivity($"[Traceroute] Target {domain} resolved to IP {resolvedIp}" + (!string.IsNullOrEmpty(resolvedHost) ? $" ({resolvedHost})" : ""));
                                }
                                return;
                            }

                            if (hop.HopNumber > MaxHops) return; // hard clamp (safety)
                            if (!domainMap.TryGetValue(domain, out var trace))
                            {
                                trace = new DomainTrace { Domain = domain, Status = "Tracing..." };
                                domainMap[domain] = trace;
                                Results.Add(trace);
                            }

                            var hopsList = trace.Hops ?? new List<string>();
                            hopsList.Add($"{hop.HopNumber}. {hop.IP}");
                            trace.Hops = hopsList;
                            trace.HopCount = hop.HopNumber;

                            trace.RoutePreview = string.Join(" → ", trace.Hops.Take(3)) +
                                                 (trace.Hops.Count > 3 ? "..." : string.Empty);
                            trace.Status = "Tracing...";

                            UpsertHopRow(hop.HopNumber, domain, hop.IP ?? "*");

                            UpsertRoutingHop(domain, hop.HopNumber, hop.IP ?? "*", hop.LatencyMs);
                        });
                    },
                    _cancellationTokenSource.Token);

                Status = _cancellationTokenSource.Token.IsCancellationRequested ? "Stopped" : "Completed";
            }
            catch (OperationCanceledException)
            {
                Status = "Stopped";
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
            }
            finally
            {
                IsRunning = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                Dashboard.RefreshStats();
            }
        }

        private void StopTrace()
        {
            try
            {
                _traceEngine?.Stop();
            }
            catch { /* ignore */ }

            _cancellationTokenSource?.Cancel();
            Status = "Stopping...";
        }

        private void ExportResults()
        {
            try
            {
                if (!RoutingRows.Any())
                {
                    MessageBox.Show("No processed routing data available to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var format = (SelectedExportFormat ?? "CSV").Trim().ToUpperInvariant();
                var ext = format switch
                {
                    "JSON" => "json",
                    "TXT" => "txt",
                    _ => "csv"
                };

                var dialog = new SaveFileDialog
                {
                    Title = "Export processed routing IPs",
                    FileName = $"trace_x_routing_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}",
                    DefaultExt = $".{ext}",
                    Filter = "CSV (*.csv)|*.csv|JSON (*.json)|*.json|Text (*.txt)|*.txt|All files (*.*)|*.*"
                };

                if (dialog.ShowDialog() != true) return;

                string content = format switch
                {
                    "JSON" => BuildJsonExport(),
                    "TXT" => BuildTextExport(),
                    _ => BuildCsvExport()
                };

                File.WriteAllText(dialog.FileName, content, Encoding.UTF8);
                MessageBox.Show($"Exported {RoutingRows.Count} rows to:\n{dialog.FileName}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string BuildCsvExport()
        {
            var maxHop = Math.Min(MaxHops, Math.Max(HopColumnCount, RoutingRows.Max(r => r.Hops.Count)));
            var sb = new StringBuilder();
            sb.Append("Domain,Destination");
            for (int hop = 1; hop <= maxHop; hop++)
            {
                sb.Append($",Hop {hop}");
            }
            sb.AppendLine();

            foreach (var row in RoutingRows)
            {
                sb.Append(EscapeCsv(row.Domain)).Append(',').Append(EscapeCsv(row.Destination));
                for (int hop = 1; hop <= maxHop; hop++)
                {
                    sb.Append(',');
                    var cell = row.GetHop(hop)?.IP ?? "*";
                    sb.Append(EscapeCsv(cell));
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private string BuildJsonExport()
        {
            var payload = RoutingRows.Select(r => new
            {
                r.Domain,
                r.Destination,
                Hops = r.Hops.Select(h => new { h.HopNumber, h.IP, h.IsTimeout }).ToList()
            });

            return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        }

        private string BuildTextExport()
        {
            var sb = new StringBuilder();
            foreach (var row in RoutingRows.OrderBy(r => r.Domain, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"Domain: {row.Domain}");
                sb.AppendLine($"Destination: {row.Destination}");
                foreach (var hop in row.Hops.Where(h => h.HopNumber <= MaxHops))
                {
                    sb.AppendLine($"  Hop {hop.HopNumber}: {hop.IP}");
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static string EscapeCsv(string? value)
        {
            value ??= string.Empty;
            var escaped = value.Replace("\"", "\"\"");
            return $"\"{escaped}\"";
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private void UpsertHopRow(int hopNumber, string domain, string ip)
        {
            var section = HopSections.FirstOrDefault(s => s.HopNumber == hopNumber);
            if (section == null)
            {
                section = new HopSection(hopNumber);
                HopSections.Add(section);

                // Keep hop cards ordered
                var ordered = HopSections.OrderBy(s => s.HopNumber).ToList();
                HopSections.Clear();
                foreach (var s in ordered) HopSections.Add(s);
            }

            section.Upsert(domain, ip);
        }

        private void UpsertRoutingHop(string domain, int hopNumber, string ip, int? latencyMs)
        {
            if (hopNumber < 1 || hopNumber > MaxHops) return;
            var row = RoutingRows.FirstOrDefault(r => string.Equals(r.Domain, domain, StringComparison.OrdinalIgnoreCase));
            if (row == null)
            {
                row = new RoutingRow { Domain = domain };
                for (int i = 1; i <= MaxHops; i++)
                {
                    row.Hops.Add(new HopCell { HopNumber = i, IP = "*" });
                }
                RoutingRows.Add(row);
            }

            row.SetHop(hopNumber, ip, latencyMs);

            EnsureHopColumns(hopNumber);
        }

        private void SetRoutingDestination(string domain, string destinationIp, string resolvedHost)
        {
            var row = RoutingRows.FirstOrDefault(r => string.Equals(r.Domain, domain, StringComparison.OrdinalIgnoreCase));
            if (row == null)
            {
                row = new RoutingRow { Domain = domain };
                for (int i = 1; i <= MaxHops; i++)
                {
                    row.Hops.Add(new HopCell { HopNumber = i, IP = "*" });
                }
                RoutingRows.Add(row);
            }

            row.Destination = destinationIp;
            row.ResolvedDestination = resolvedHost;
        }

        private void EnsureHopColumns(int hopNumber)
        {
            hopNumber = Math.Max(0, Math.Min(MaxHops, hopNumber));
            if (hopNumber <= HopColumnCount) return;
            HopColumnCount = hopNumber;

            while (HopFilters.Count < HopColumnCount)
            {
                HopFilters.Add(new HopColumnFilter(HopFilters.Count + 1, () => RoutingRowsView.Refresh()));
            }

            // Ensure existing rows have placeholder hop cells up to HopColumnCount
            foreach (var row in RoutingRows)
            {
                while (row.Hops.Count < HopColumnCount)
                {
                    row.Hops.Add(new HopCell { HopNumber = row.Hops.Count + 1, IP = "*" });
                }
            }
        }

        private static string? TryNormalizeHost(string? token)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;
            token = token.Trim();

            // If user pasted a URL, extract host
            if (token.Contains("://", StringComparison.Ordinal))
            {
                if (Uri.TryCreate(token, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
                    return uri.Host.Trim().TrimEnd('.');
                return null;
            }

            // If user pasted something with path but no scheme, try as http://
            if (token.Contains("/") || token.Contains("\\"))
            {
                var guess = "http://" + token.Replace("\\", "/");
                if (Uri.TryCreate(guess, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
                    return uri.Host.Trim().TrimEnd('.');
                return null;
            }

            // Strip common trailing punctuation
            token = token.Trim().TrimEnd('.', ',', ';');

            // IP?
            if (System.Net.IPAddress.TryParse(token, out _)) return token;

            // Basic hostname check
            return Uri.CheckHostName(token) == UriHostNameType.Dns ? token : null;
        }

        private static async Task<(List<string> Valid, List<string> Invalid)> ValidateHostsAsync(
            List<string> hosts,
            TimeSpan timeout,
            CancellationToken ct)
        {
            // Fast-path: keep IPs, and do DNS lookup for hostnames.
            var valid = new ConcurrentBag<string>();
            var invalid = new ConcurrentBag<string>();

            using var sem = new SemaphoreSlim(20);

            var tasks = hosts.Select(async host =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    if (System.Net.IPAddress.TryParse(host, out _))
                    {
                        valid.Add(host);
                        return;
                    }
                        
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(timeout);

                    try
                    {
                        _ = await Dns.GetHostAddressesAsync(host, cts.Token);
                        valid.Add(host);
                    }
                    catch
                    {
                        invalid.Add(host);
                    }
                }
                finally
                {
                    sem.Release();
                }
            });

            await Task.WhenAll(tasks);
            return (valid.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
                    invalid.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList());
        }

        private bool RoutingFilter(object o)
        {
            if (o is not RoutingRow row) return false;

            // Global search (domain + destination + hops)
            if (!string.IsNullOrWhiteSpace(GlobalSearchText))
            {
                var f = GlobalSearchText.Trim();
                var globalHit =
                    row.Domain.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                    (row.Destination?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    row.Hops.Any(h => h.IP.Contains(f, StringComparison.OrdinalIgnoreCase));
                if (!globalHit) return false;
            }

            // Per-hop filters
            for (var i = 0; i < HopFilters.Count; i++)
            {
                var filter = HopFilters[i];
                if (string.IsNullOrWhiteSpace(filter.FilterText)) continue;

                var hop = row.GetHop(filter.HopNumber);
                if (hop == null) return false;

                var text = filter.FilterText.Trim();
                if (!hop.IP.Contains(text, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private void ApplyHopMode()
        {
            // Keep this minimal and predictable:
            // - "5" => quick view
            // - "Complete" => default full (30)
            // - "Custom" => user-defined
            switch (HopMode)
            {
                case "5":
                    MaxHops = 5;
                    break;
                case "Custom":
                    MaxHops = CustomMaxHops;
                    break;
                default:
                    MaxHops = 30;
                    break;
            }
        }

        private bool DnsRecordFilter(object o)
        {
            if (o is not DnsRecord record) return false;
            if (string.IsNullOrWhiteSpace(DnsReconSearchText)) return true;

            var f = DnsReconSearchText.Trim();
            return record.RecordType.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                   record.Name.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                   record.Value.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                   record.Details.Contains(f, StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyDnsScanMode()
        {
            if (DnsScanMode == "Standard")
            {
                DnsQueryA = true;
                DnsQueryAAAA = true;
                DnsQueryMX = true;
                DnsQueryNS = true;
                DnsQueryCNAME = true;
                DnsQueryTXT = true;
                DnsQuerySOA = true;
                DnsQuerySRV = false;
                DnsQueryCAA = false;
                DnsQueryAXFR = false;
                DnsBruteForce = false;
            }
            else if (DnsScanMode == "Deep")
            {
                DnsQueryA = true;
                DnsQueryAAAA = true;
                DnsQueryMX = true;
                DnsQueryNS = true;
                DnsQueryCNAME = true;
                DnsQueryTXT = true;
                DnsQuerySOA = true;
                DnsQuerySRV = true;
                DnsQueryCAA = true;
                DnsQueryAXFR = true;
                DnsBruteForce = true;
            }
        }

        private async Task StartDnsRecon()
        {
            var host = TryNormalizeHost(DnsReconTarget);
            if (string.IsNullOrWhiteSpace(host))
            {
                MessageBox.Show("Please enter a valid domain or IP for DNS reconnaissance.", "Input Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _dnsReconCts = new CancellationTokenSource();
            IsDnsReconRunning = true;
            DnsReconStatus = "Scanning...";
            DnsRecords.Clear();
            SelectedDnsRecord = null;

            try
            {
                LogActivity($"Initiating DNS reconnaissance scan on target {host} (Mode: {DnsScanMode})...");
                var selectedTypes = new List<string>();
                if (DnsQueryA) selectedTypes.Add("A");
                if (DnsQueryAAAA) selectedTypes.Add("AAAA");
                if (DnsQueryMX) selectedTypes.Add("MX");
                if (DnsQueryNS) selectedTypes.Add("NS");
                if (DnsQueryCNAME) selectedTypes.Add("CNAME");
                if (DnsQueryTXT) selectedTypes.Add("TXT");
                if (DnsQuerySOA) selectedTypes.Add("SOA");
                if (DnsQuerySRV) selectedTypes.Add("SRV");
                if (DnsQueryCAA) selectedTypes.Add("CAA");
                if (DnsQueryAXFR) selectedTypes.Add("AXFR");

                var records = await _dnsReconService.PerformAdvancedDnsReconAsync(
                    host,
                    selectedTypes,
                    DnsCustomServer,
                    DnsBruteForce,
                    _dnsReconCts.Token);

                if (_dnsReconCts.Token.IsCancellationRequested)
                {
                    DnsReconStatus = "Stopped";
                    LogActivity($"DNS scan on target {host} was cancelled.");
                }
                else
                {
                    DnsRecords.Clear();
                    foreach (var record in records)
                    {
                        DnsRecords.Add(record);
                    }
                    DnsReconStatus = $"Scan completed. Found {DnsRecords.Count} records.";
                    LogActivity($"DNS scan complete for {host}. Discovered {DnsRecords.Count} records.");
                }
            }
            catch (Exception ex)
            {
                DnsReconStatus = $"Error: {ex.Message}";
            }
            finally
            {
                IsDnsReconRunning = false;
                _dnsReconCts?.Dispose();
                _dnsReconCts = null;
                Dashboard.RefreshStats();
            }
        }

        private void StopDnsRecon()
        {
            _dnsReconCts?.Cancel();
            DnsReconStatus = "Stopping...";
        }

        private async Task RunReconFromTrace(object? parameter)
        {
            if (parameter is not string domain || string.IsNullOrWhiteSpace(domain)) return;

            DnsReconTarget = domain;
            SelectedTabIndex = 3; // Switch to the DNS Recon tab
            await StartDnsRecon();
        }

        // ==========================================
        // ADVANCED WHOIS & NSLOOKUP SERVICES
        // ==========================================

        private async Task StartWhois()
        {
            var host = TryNormalizeHost(WhoisTarget);
            if (string.IsNullOrWhiteSpace(host))
            {
                MessageBox.Show("Please enter a valid domain or IP for WHOIS lookup.", "Input Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _whoisCts = new CancellationTokenSource();
            IsWhoisRunning = true;
            WhoisStatus = "Querying WHOIS registry...";
            WhoisResultText = string.Empty;
            LogActivity($"Requesting WHOIS registry records for target {host}...");

            try
            {
                var result = await _whoisService.LookupAsync(host, _whoisCts.Token);
                if (_whoisCts.Token.IsCancellationRequested)
                {
                    WhoisStatus = "Stopped";
                    LogActivity($"WHOIS query for {host} was cancelled.");
                }
                else
                {
                    WhoisResultText = result;
                    WhoisStatus = "WHOIS lookup completed.";
                    LogActivity($"WHOIS query for {host} completed successfully.");
                }
            }
            catch (Exception ex)
            {
                WhoisStatus = $"Error: {ex.Message}";
                LogActivity($"WHOIS query for {host} failed: {ex.Message}");
            }
            finally
            {
                IsWhoisRunning = false;
                _whoisCts?.Dispose();
                _whoisCts = null;
            }
        }

        private void StopWhois()
        {
            _whoisCts?.Cancel();
            WhoisStatus = "Stopping...";
        }

        private async Task RunNslookup()
        {
            var host = TryNormalizeHost(NslookupTarget);
            if (string.IsNullOrWhiteSpace(host))
            {
                MessageBox.Show("Please enter a valid domain for NSLOOKUP.", "Input Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsNslookupRunning = true;
            NslookupStatus = "Resolving...";
            NslookupResultText = string.Empty;

            try
            {
                var recordType = NslookupRecordType;
                if (string.IsNullOrWhiteSpace(recordType))
                {
                    recordType = "A";
                }
                var queryType = (QueryType)Enum.Parse(typeof(QueryType), recordType);
                LogActivity($"Initiating NSLOOKUP query on target {host} (Type: {queryType})...");
                
                LookupClient client;
                if (!string.IsNullOrWhiteSpace(NslookupServer) && IPAddress.TryParse(NslookupServer.Trim(), out var ip))
                {
                    client = new LookupClient(new LookupClientOptions(new[] { ip }) { Timeout = TimeSpan.FromSeconds(5) });
                }
                else
                {
                    client = new LookupClient(new LookupClientOptions { Timeout = TimeSpan.FromSeconds(5) });
                }

                var sb = new StringBuilder();
                var nsServerList = client.NameServers != null 
                    ? string.Join(", ", client.NameServers.Select(ns => ns?.ToString() ?? "Unknown")) 
                    : "Default System Resolvers";
                sb.AppendLine($";; Querying server: {nsServerList}");
                sb.AppendLine($";; Got answer for query: {host} {queryType}");
                sb.AppendLine();

                var response = await client.QueryAsync(host, queryType);
                
                if (response == null)
                {
                    sb.AppendLine(";; DNS Error: No response received from server (Timeout or Network Unreachable).");
                    LogActivity($"NSLOOKUP query for {host} returned no response.");
                }
                else if (response.HasError)
                {
                    sb.AppendLine($";; DNS Error: {response.ErrorMessage ?? "Unknown DNS Error"}");
                    LogActivity($"NSLOOKUP query for {host} failed: {response.ErrorMessage ?? "Error"}");
                }
                else
                {
                    if (response.Header != null)
                    {
                        sb.AppendLine($";; HEADER:");
                        sb.AppendLine($";;   status: {response.Header.ResponseCode}, id: {response.Header.Id}");
                        var headerStr = response.Header.ToString();
                        if (!string.IsNullOrEmpty(headerStr))
                        {
                            sb.AppendLine($";;   flags: {string.Join(" ", headerStr.Split(',').Select(s => s.Trim()))}");
                        }
                        sb.AppendLine();
                    }

                    if (response.Answers != null && response.Answers.Any())
                    {
                        sb.AppendLine($";; ANSWER SECTION:");
                        foreach (var answer in response.Answers)
                        {
                            if (answer != null)
                                sb.AppendLine(answer.ToString());
                        }
                        sb.AppendLine();
                    }

                    if (response.Authorities != null && response.Authorities.Any())
                    {
                        sb.AppendLine($";; AUTHORITY SECTION:");
                        foreach (var auth in response.Authorities)
                        {
                            if (auth != null)
                                sb.AppendLine(auth.ToString());
                        }
                        sb.AppendLine();
                    }

                    if (response.Additionals != null && response.Additionals.Any())
                    {
                        sb.AppendLine($";; ADDITIONAL SECTION:");
                        foreach (var add in response.Additionals)
                        {
                            if (add != null)
                                sb.AppendLine(add.ToString());
                        }
                        sb.AppendLine();
                    }

                    var auditTrail = response.AuditTrail;
                    string queryTime = "N/A";
                    if (!string.IsNullOrEmpty(auditTrail))
                    {
                        var queryTimeLine = auditTrail.Split('\n').FirstOrDefault(l => l.Contains("Query time", StringComparison.OrdinalIgnoreCase));
                        if (queryTimeLine != null)
                        {
                            queryTime = queryTimeLine.Split(':').LastOrDefault()?.Trim() ?? "N/A";
                        }
                    }
                    sb.AppendLine($";; Query time: {queryTime}");
                    sb.AppendLine($";; MSG SIZE rcvd: {response.MessageSize} bytes");
                    
                    LogActivity($"NSLOOKUP query for {host} completed successfully.");
                }

                NslookupResultText = sb.ToString();
                NslookupStatus = "Completed.";
            }
            catch (Exception ex)
            {
                NslookupResultText = $"NSLOOKUP Query Failed: {ex.Message}";
                NslookupStatus = "Error.";
                LogActivity($"NSLOOKUP query for {host} failed: {ex.Message}");
            }
            finally
            {
                IsNslookupRunning = false;
            }
        }

        private void ExportDnsRecords()
        {
            try
            {
                if (!DnsRecords.Any())
                {
                    MessageBox.Show("No DNS record data available to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var format = (SelectedExportFormat ?? "CSV").Trim().ToUpperInvariant();
                var ext = format switch
                {
                    "JSON" => "json",
                    "TXT" => "txt",
                    _ => "csv"
                };

                var dialog = new SaveFileDialog
                {
                    Title = "Export enumerated DNS records",
                    FileName = $"dns_recon_{DnsReconTarget}_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}",
                    DefaultExt = $".{ext}",
                    Filter = "CSV (*.csv)|*.csv|JSON (*.json)|*.json|Text (*.txt)|*.txt|All files (*.*)|*.*"
                };

                if (dialog.ShowDialog() != true) return;

                string content = format switch
                {
                    "JSON" => JsonSerializer.Serialize(DnsRecords.Select(r => new { r.RecordType, r.Name, r.Value, r.Details }), new JsonSerializerOptions { WriteIndented = true }),
                    "TXT" => string.Join(Environment.NewLine, DnsRecords.Select(r => $"[{r.RecordType}] {r.Name} => {r.Value} ({r.Details})")),
                    _ => "Type,Name,Value,Details" + Environment.NewLine + string.Join(Environment.NewLine, DnsRecords.Select(r => $"{EscapeCsv(r.RecordType)},{EscapeCsv(r.Name)},{EscapeCsv(r.Value)},{EscapeCsv(r.Details)}"))
                };

                File.WriteAllText(dialog.FileName, content, Encoding.UTF8);
                MessageBox.Show($"Exported {DnsRecords.Count} DNS records to:\n{dialog.FileName}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public sealed class HopCell : INotifyPropertyChanged
    {
        private string _ip = "*";
        private int? _latencyMs;

        public int HopNumber { get; init; }

        public string IP
        {
            get => _ip;
            set
            {
                if (_ip == value) return;
                _ip = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsTimeout));
            }
        }

        public bool IsTimeout => string.IsNullOrWhiteSpace(IP) || IP == "*";

        public int? LatencyMs
        {
            get => _latencyMs;
            set
            {
                if (_latencyMs == value) return;
                _latencyMs = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LatencyText));
            }
        }

        public string LatencyText => LatencyMs is null ? string.Empty : $"{LatencyMs} ms";

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class RoutingRow : INotifyPropertyChanged
    {
        private string _domain = string.Empty;
        private string _destination = string.Empty;
        private string _resolvedDestination = string.Empty;
        private int _maxObservedHop;

        public string Domain
        {
            get => _domain;
            set { _domain = value; OnPropertyChanged(); }
        }

        public string Destination
        {
            get => _destination;
            set { _destination = value; OnPropertyChanged(); }
        }

        public string ResolvedDestination
        {
            get => _resolvedDestination;
            set { _resolvedDestination = value; OnPropertyChanged(); }
        }

        public int MaxObservedHop
        {
            get => _maxObservedHop;
            private set
            {
                if (_maxObservedHop == value) return;
                _maxObservedHop = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsEarlyFail));
            }
        }

        // "Domains that do not go beyond the first hop"
        public bool IsEarlyFail => MaxObservedHop <= 1 && string.IsNullOrWhiteSpace(Destination);

        // index 0 == hop 1
        public ObservableCollection<HopCell> Hops { get; } = new();

        public HopCell? GetHop(int hopNumber)
        {
            var idx = hopNumber - 1;
            if (idx < 0 || idx >= Hops.Count) return null;
            return Hops[idx];
        }

        public void SetHop(int hopNumber, string ip, int? latencyMs)
        {
            var idx = hopNumber - 1;
            while (Hops.Count <= idx)
            {
                Hops.Add(new HopCell { HopNumber = Hops.Count + 1, IP = "*" });
            }

            Hops[idx].IP = ip;
            Hops[idx].LatencyMs = latencyMs;
            if (hopNumber > MaxObservedHop)
            {
                MaxObservedHop = hopNumber;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class HopColumnFilter : INotifyPropertyChanged
    {
        private string _filterText = string.Empty;
        private readonly Action _refresh;

        public int HopNumber { get; }

        public string FilterText
        {
            get => _filterText;
            set
            {
                if (_filterText == value) return;
                _filterText = value;
                OnPropertyChanged();
                _refresh();
            }
        }

        public HopColumnFilter(int hopNumber, Action refresh)
        {
            HopNumber = hopNumber;
            _refresh = refresh;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class HopRow : INotifyPropertyChanged
    {
        private string _domain = string.Empty;
        private string _ip = string.Empty;

        public string Domain
        {
            get => _domain;
            set { _domain = value; OnPropertyChanged(); }
        }

        public string IP
        {
            get => _ip;
            set { _ip = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class HopSection : INotifyPropertyChanged
    {
        private string _filterText = string.Empty;

        public int HopNumber { get; }
        public ObservableCollection<HopRow> Rows { get; } = new();
        public ICollectionView RowsView { get; }

        public string FilterText
        {
            get => _filterText;
            set
            {
                if (_filterText == value) return;
                _filterText = value;
                OnPropertyChanged();
                RowsView.Refresh();
            }
        }

        public HopSection(int hopNumber)
        {
            HopNumber = hopNumber;

            RowsView = CollectionViewSource.GetDefaultView(Rows);
            RowsView.SortDescriptions.Add(new SortDescription(nameof(HopRow.Domain), ListSortDirection.Ascending));
            RowsView.Filter = o =>
            {
                if (o is not HopRow row) return false;
                if (string.IsNullOrWhiteSpace(FilterText)) return true;
                var f = FilterText.Trim();
                return (row.Domain?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false) ||
                       (row.IP?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false);
            };
        }

        public void Upsert(string domain, string ip)
        {
            var existing = Rows.FirstOrDefault(r => string.Equals(r.Domain, domain, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                Rows.Add(new HopRow { Domain = domain, IP = ip });
                return;
            }

            existing.IP = ip;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

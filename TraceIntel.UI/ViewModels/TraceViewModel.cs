using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using TraceIntel.Core.Models;
using TraceIntel.Core.Services;

namespace TraceIntel.UI.ViewModels
{
    public class TraceViewModel : ViewModelBase
    {
        private readonly SettingsViewModel _settingsViewModel;
        private readonly Action<string> _logAction;
        private ParallelTraceEngine? _traceEngine;
        private CancellationTokenSource? _cancellationTokenSource;

        private string _domainsInput = string.Empty;
        private int _maxHops = 30;
        private string _status = "Ready";
        private double _progress;
        private bool _isRunning;
        private int _hopColumnCount;
        private string _globalSearchText = string.Empty;
        private string _selectedExportFormat = "CSV";
        private int _successfulTracesCount;
        private string _hopMode = "Complete";
        private int _customMaxHops = 30;

        public ObservableCollection<DomainTrace> Results { get; } = new();
        public ObservableCollection<HopSection> HopSections { get; } = new();
        public ObservableCollection<RoutingRow> RoutingRows { get; } = new();
        public ICollectionView RoutingRowsView { get; }
        public ObservableCollection<HopColumnFilter> HopFilters { get; } = new();
        public ObservableCollection<string> ExportFormats { get; } = new() { "CSV", "JSON", "TXT" };

        public string DomainsInput
        {
            get => _domainsInput;
            set => SetProperty(ref _domainsInput, value);
        }

        public int MaxHops
        {
            get => _maxHops;
            set
            {
                if (value > 0 && value <= 255)
                {
                    SetProperty(ref _maxHops, value);
                }
            }
        }

        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        public bool IsRunning
        {
            get => _isRunning;
            private set => SetProperty(ref _isRunning, value);
        }

        public int HopColumnCount
        {
            get => _hopColumnCount;
            private set => SetProperty(ref _hopColumnCount, value);
        }

        public string GlobalSearchText
        {
            get => _globalSearchText;
            set
            {
                if (SetProperty(ref _globalSearchText, value))
                {
                    RoutingRowsView.Refresh();
                }
            }
        }

        public string SelectedExportFormat
        {
            get => _selectedExportFormat;
            set => SetProperty(ref _selectedExportFormat, value);
        }

        public int SuccessfulTracesCount
        {
            get => _successfulTracesCount;
            private set => SetProperty(ref _successfulTracesCount, value);
        }

        public string HopMode
        {
            get => _hopMode;
            set
            {
                if (SetProperty(ref _hopMode, value))
                {
                    ApplyHopMode();
                }
            }
        }

        public int CustomMaxHops
        {
            get => _customMaxHops;
            set
            {
                if (value <= 0 || value > 255) return;
                if (SetProperty(ref _customMaxHops, value) && HopMode == "Custom")
                {
                    MaxHops = _customMaxHops;
                }
            }
        }

        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand RunReconFromTraceCommand { get; }

        public event Action? OnTraceCompleted;
        public event Action<string>? OnRequestDnsRecon;

        public TraceViewModel(SettingsViewModel settingsViewModel, Action<string> logAction)
        {
            _settingsViewModel = settingsViewModel;
            _logAction = logAction;

            StartCommand = new RelayCommand(async _ => await StartTrace(), _ => !IsRunning);
            StopCommand = new RelayCommand(_ => StopTrace(), _ => IsRunning);
            ExportCommand = new RelayCommand(_ => ExportResults(), _ => Results.Any());
            RunReconFromTraceCommand = new RelayCommand(domain => OnRequestDnsRecon?.Invoke(domain?.ToString() ?? string.Empty));

            RoutingRowsView = CollectionViewSource.GetDefaultView(RoutingRows);
            RoutingRowsView.SortDescriptions.Add(new SortDescription(nameof(RoutingRow.Domain), ListSortDirection.Ascending));
            RoutingRowsView.Filter = RoutingFilter;

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
            _cancellationTokenSource = new CancellationTokenSource();
            
            var (validDomains, invalidDomains) = await ValidateHostsAsync(normalized, TimeSpan.FromSeconds(2), _cancellationTokenSource.Token);

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
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                return;
            }

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
            _logAction($"Starting traceroute diagnostics on {domains.Count} target(s) (Depth: {MaxHops} hops)...");

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

                            _logAction($"[Traceroute] Path mapping complete for {domain} ({hops.Count} hops, Status: {trace.Status})");

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
                                    _logAction($"[Traceroute] Target {domain} resolved to IP {resolvedIp}" + (!string.IsNullOrEmpty(resolvedHost) ? $" ({resolvedHost})" : ""));
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
                OnTraceCompleted?.Invoke();
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

            return System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
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

        public void EnsureHopColumns(int hopNumber)
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

        public static string? TryNormalizeHost(string? token)
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
    }

    // ==========================================
    // SUPPORTING TRACEROUTE DATA MODELS
    // ==========================================

    public sealed class HopCell : ViewModelBase
    {
        private string _ip = "*";
        private int? _latencyMs;

        public int HopNumber { get; init; }

        public string IP
        {
            get => _ip;
            set
            {
                if (SetProperty(ref _ip, value))
                {
                    OnPropertyChanged(nameof(IsTimeout));
                }
            }
        }

        public bool IsTimeout => string.IsNullOrWhiteSpace(IP) || IP == "*";

        public int? LatencyMs
        {
            get => _latencyMs;
            set
            {
                if (SetProperty(ref _latencyMs, value))
                {
                    OnPropertyChanged(nameof(LatencyText));
                }
            }
        }

        public string LatencyText => LatencyMs is null ? string.Empty : $"{LatencyMs} ms";
    }

    public sealed class RoutingRow : ViewModelBase
    {
        private string _domain = string.Empty;
        private string _destination = string.Empty;
        private string _resolvedDestination = string.Empty;
        private int _maxObservedHop;

        public string Domain
        {
            get => _domain;
            set => SetProperty(ref _domain, value);
        }

        public string Destination
        {
            get => _destination;
            set => SetProperty(ref _destination, value);
        }

        public string ResolvedDestination
        {
            get => _resolvedDestination;
            set => SetProperty(ref _resolvedDestination, value);
        }

        public int MaxObservedHop
        {
            get => _maxObservedHop;
            private set
            {
                if (SetProperty(ref _maxObservedHop, value))
                {
                    OnPropertyChanged(nameof(IsEarlyFail));
                }
            }
        }

        public bool IsEarlyFail => MaxObservedHop <= 1 && string.IsNullOrWhiteSpace(Destination);

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
    }

    public sealed class HopColumnFilter : ViewModelBase
    {
        private string _filterText = string.Empty;
        private readonly Action _refresh;

        public int HopNumber { get; }

        public string FilterText
        {
            get => _filterText;
            set
            {
                if (SetProperty(ref _filterText, value))
                {
                    _refresh();
                }
            }
        }

        public HopColumnFilter(int hopNumber, Action refresh)
        {
            HopNumber = hopNumber;
            _refresh = refresh;
        }
    }

    public sealed class HopRow : ViewModelBase
    {
        private string _domain = string.Empty;
        private string _ip = string.Empty;

        public string Domain
        {
            get => _domain;
            set => SetProperty(ref _domain, value);
        }

        public string IP
        {
            get => _ip;
            set => SetProperty(ref _ip, value);
        }
    }

    public sealed class HopSection : ViewModelBase
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
                if (SetProperty(ref _filterText, value))
                {
                    RowsView.Refresh();
                }
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
    }
}

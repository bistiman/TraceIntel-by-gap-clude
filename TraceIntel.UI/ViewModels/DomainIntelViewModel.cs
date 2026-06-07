using System;
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
using DnsClient;
using TraceIntel.Core.Models;
using TraceIntel.Core.Services;

namespace TraceIntel.UI.ViewModels
{
    public class DomainIntelViewModel : ViewModelBase
    {
        private readonly SettingsViewModel _settingsViewModel;
        private readonly Action<string> _logAction;
        private readonly DnsReconService _dnsReconService = new();
        private readonly WhoisService _whoisService = new();

        private CancellationTokenSource? _dnsReconCts;
        private CancellationTokenSource? _whoisCts;
        private CancellationTokenSource? _nslookupCts;
        private string _selectedExportFormat = "CSV";

        private string _dnsReconTarget = string.Empty;
        private string _dnsReconStatus = "Ready";
        private bool _isDnsReconRunning;
        private string _dnsReconSearchText = string.Empty;
        private DnsRecord? _selectedDnsRecord;

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

        // WHOIS properties
        private string _whoisTarget = string.Empty;
        private string _whoisResultText = string.Empty;
        private bool _isWhoisRunning;
        private string _whoisStatus = "Ready";

        // NSLOOKUP properties
        private string _nslookupTarget = string.Empty;
        private string _nslookupRecordType = "A";
        private string _nslookupServer = string.Empty;
        private string _nslookupResultText = string.Empty;
        private bool _isNslookupRunning;
        private string _nslookupStatus = "Ready";

        public ObservableCollection<DnsRecord> DnsRecords { get; } = new();
        public ICollectionView DnsRecordsView { get; }
        public ObservableCollection<string> ExportFormats { get; } = new() { "CSV", "JSON", "TXT" };
        public ObservableCollection<string> NslookupRecordTypes { get; } = new() { "A", "AAAA", "MX", "NS", "CNAME", "TXT", "SOA", "SRV", "CAA" };

        public int DnsRecordCount => DnsRecords.Count;
        public bool HasDnsRecords => DnsRecords.Count > 0;
        public bool IsScanOptionsReadOnly => DnsScanMode != "Custom";

        public string SelectedExportFormat
        {
            get => _selectedExportFormat;
            set => SetProperty(ref _selectedExportFormat, value);
        }

        public string DnsReconTarget
        {
            get => _dnsReconTarget;
            set => SetProperty(ref _dnsReconTarget, value);
        }

        public string DnsReconStatus
        {
            get => _dnsReconStatus;
            set => SetProperty(ref _dnsReconStatus, value);
        }

        public bool IsDnsReconRunning
        {
            get => _isDnsReconRunning;
            private set => SetProperty(ref _isDnsReconRunning, value);
        }

        public string DnsReconSearchText
        {
            get => _dnsReconSearchText;
            set
            {
                if (SetProperty(ref _dnsReconSearchText, value))
                {
                    DnsRecordsView.Refresh();
                }
            }
        }

        public DnsRecord? SelectedDnsRecord
        {
            get => _selectedDnsRecord;
            set => SetProperty(ref _selectedDnsRecord, value);
        }

        public bool DnsQueryA { get => _dnsQueryA; set => SetProperty(ref _dnsQueryA, value); }
        public bool DnsQueryAAAA { get => _dnsQueryAAAA; set => SetProperty(ref _dnsQueryAAAA, value); }
        public bool DnsQueryMX { get => _dnsQueryMX; set => SetProperty(ref _dnsQueryMX, value); }
        public bool DnsQueryNS { get => _dnsQueryNS; set => SetProperty(ref _dnsQueryNS, value); }
        public bool DnsQueryCNAME { get => _dnsQueryCNAME; set => SetProperty(ref _dnsQueryCNAME, value); }
        public bool DnsQueryTXT { get => _dnsQueryTXT; set => SetProperty(ref _dnsQueryTXT, value); }
        public bool DnsQuerySOA { get => _dnsQuerySOA; set => SetProperty(ref _dnsQuerySOA, value); }
        public bool DnsQuerySRV { get => _dnsQuerySRV; set => SetProperty(ref _dnsQuerySRV, value); }
        public bool DnsQueryCAA { get => _dnsQueryCAA; set => SetProperty(ref _dnsQueryCAA, value); }
        public bool DnsQueryAXFR { get => _dnsQueryAXFR; set => SetProperty(ref _dnsQueryAXFR, value); }
        public bool DnsBruteForce { get => _dnsBruteForce; set => SetProperty(ref _dnsBruteForce, value); }
        
        public string DnsCustomServer
        {
            get => _dnsCustomServer;
            set => SetProperty(ref _dnsCustomServer, value);
        }

        public string DnsScanMode
        {
            get => _dnsScanMode;
            set
            {
                if (SetProperty(ref _dnsScanMode, value))
                {
                    ApplyDnsScanMode();
                    OnPropertyChanged(nameof(IsScanOptionsReadOnly));
                }
            }
        }

        public string WhoisTarget { get => _whoisTarget; set => SetProperty(ref _whoisTarget, value); }
        public string WhoisResultText { get => _whoisResultText; set => SetProperty(ref _whoisResultText, value); }
        public bool IsWhoisRunning { get => _isWhoisRunning; set => SetProperty(ref _isWhoisRunning, value); }
        public string WhoisStatus { get => _whoisStatus; set => SetProperty(ref _whoisStatus, value); }

        public string NslookupTarget { get => _nslookupTarget; set => SetProperty(ref _nslookupTarget, value); }
        public string NslookupRecordType { get => _nslookupRecordType; set => SetProperty(ref _nslookupRecordType, value); }
        public string NslookupServer { get => _nslookupServer; set => SetProperty(ref _nslookupServer, value); }
        public string NslookupResultText { get => _nslookupResultText; set => SetProperty(ref _nslookupResultText, value); }
        public bool IsNslookupRunning { get => _isNslookupRunning; set => SetProperty(ref _isNslookupRunning, value); }
        public string NslookupStatus { get => _nslookupStatus; set => SetProperty(ref _nslookupStatus, value); }

        public ICommand StartDnsReconCommand { get; }
        public ICommand StopDnsReconCommand { get; }
        public ICommand ExportDnsReconCommand { get; }
        public ICommand StartWhoisCommand { get; }
        public ICommand StopWhoisCommand { get; }
        public ICommand RunNslookupCommand { get; }
        public ICommand StopNslookupCommand { get; }
        public ICommand CopyWhoisCommand { get; }
        public ICommand CopyNslookupCommand { get; }
        public ICommand CopyDnsRecordValueCommand { get; }

        public event Action? OnDnsScanCompleted;

        public DomainIntelViewModel(SettingsViewModel settingsViewModel, Action<string> logAction)
        {
            _settingsViewModel = settingsViewModel;
            _logAction = logAction;

            DnsRecordsView = CollectionViewSource.GetDefaultView(DnsRecords);
            DnsRecordsView.Filter = DnsRecordFilter;
            DnsRecordsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DnsRecord.RecordType)));

            DnsRecords.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(DnsRecordCount));
                OnPropertyChanged(nameof(HasDnsRecords));
            };

            if (!string.IsNullOrWhiteSpace(_settingsViewModel.SettingsDefaultDnsServer))
            {
                DnsCustomServer = _settingsViewModel.SettingsDefaultDnsServer;
            }

            StartDnsReconCommand = new RelayCommand(async _ => await StartDnsRecon(), _ => !IsDnsReconRunning);
            StopDnsReconCommand = new RelayCommand(_ => StopDnsRecon(), _ => IsDnsReconRunning);
            ExportDnsReconCommand = new RelayCommand(_ => ExportDnsRecords(), _ => DnsRecords.Any());

            StartWhoisCommand = new RelayCommand(async _ => await StartWhois(), _ => !IsWhoisRunning);
            StopWhoisCommand = new RelayCommand(_ => StopWhois(), _ => IsWhoisRunning);
            RunNslookupCommand = new RelayCommand(async _ => await RunNslookup(), _ => !IsNslookupRunning);
            StopNslookupCommand = new RelayCommand(_ => StopNslookup(), _ => IsNslookupRunning);

            CopyWhoisCommand = new RelayCommand(_ => CopyToClipboard(WhoisResultText, "WHOIS"), _ => !string.IsNullOrWhiteSpace(WhoisResultText));
            CopyNslookupCommand = new RelayCommand(_ => CopyToClipboard(NslookupResultText, "NSLOOKUP"), _ => !string.IsNullOrWhiteSpace(NslookupResultText));
            CopyDnsRecordValueCommand = new RelayCommand(_ => CopyToClipboard(SelectedDnsRecord?.Value, "DNS Record"), _ => SelectedDnsRecord != null && !string.IsNullOrWhiteSpace(SelectedDnsRecord.Value));

            ApplyDnsScanMode();
        }

        private void CopyToClipboard(string? text, string source)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            try
            {
                Clipboard.SetText(text);
                _logAction($"[Clipboard] {source} output copied to clipboard.");
            }
            catch (Exception ex)
            {
                _logAction($"[Clipboard] Failed to copy {source} output: {ex.Message}");
            }
        }

        public async Task StartDnsRecon()
        {
            var host = TraceViewModel.TryNormalizeHost(DnsReconTarget);
            if (string.IsNullOrWhiteSpace(host))
            {
                MessageBox.Show("Please enter a valid domain or IP for DNS reconnaissance.", "Input Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedTypes = BuildSelectedRecordTypes();
            if (DnsScanMode == "Custom" && selectedTypes.Count == 0 && !DnsBruteForce)
            {
                MessageBox.Show("Select at least one record type or enable subdomain brute force.", "DNS Scan", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var resolver = ResolveDnsServer();
            if (!string.IsNullOrWhiteSpace(resolver) && !IsResolvableDnsServer(resolver))
            {
                MessageBox.Show("Custom DNS resolver could not be resolved. Use an IP address or a valid hostname.", "DNS Resolver", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _dnsReconCts = new CancellationTokenSource();
            IsDnsReconRunning = true;
            DnsReconStatus = "Initializing scan...";
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                DnsRecords.Clear();
                SelectedDnsRecord = null;
            });

            try
            {
                _logAction($"Initiating DNS reconnaissance on {host} (Mode: {DnsScanMode})...");
                DnsReconStatus = "Querying DNS records...";

                var timeoutMs = Math.Clamp(_settingsViewModel.SettingsTimeoutMs, 500, 10000);
                var records = await _dnsReconService.PerformAdvancedDnsReconAsync(
                    host,
                    selectedTypes,
                    resolver,
                    DnsBruteForce,
                    timeoutMs,
                    _dnsReconCts.Token);

                if (_dnsReconCts.Token.IsCancellationRequested)
                {
                    DnsReconStatus = "Stopped";
                    _logAction($"DNS scan on {host} was cancelled.");
                }
                else
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        DnsRecords.Clear();
                        foreach (var record in records)
                        {
                            DnsRecords.Add(record);
                        }
                    });

                    var actionable = records.Count(r => !IsStatusOnlyRecord(r));
                    DnsReconStatus = actionable > 0
                        ? $"Complete — {actionable} record(s) across {records.Select(r => r.RecordType).Distinct().Count()} type(s)."
                        : "Complete — no DNS answers returned.";
                    _logAction($"DNS scan complete for {host}. {DnsRecords.Count} row(s) in results.");
                }
            }
            catch (OperationCanceledException)
            {
                DnsReconStatus = "Stopped";
            }
            catch (Exception ex)
            {
                DnsReconStatus = $"Error: {ex.Message}";
                _logAction($"DNS scan failed for {host}: {ex.Message}");
            }
            finally
            {
                IsDnsReconRunning = false;
                _dnsReconCts?.Dispose();
                _dnsReconCts = null;
                OnDnsScanCompleted?.Invoke();
            }
        }

        private List<string> BuildSelectedRecordTypes()
        {
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
            return selectedTypes;
        }

        private string? ResolveDnsServer()
        {
            if (!string.IsNullOrWhiteSpace(DnsCustomServer))
            {
                return DnsCustomServer.Trim();
            }

            return string.IsNullOrWhiteSpace(_settingsViewModel.SettingsDefaultDnsServer)
                ? null
                : _settingsViewModel.SettingsDefaultDnsServer.Trim();
        }

        private static bool IsResolvableDnsServer(string resolver)
        {
            if (IPAddress.TryParse(resolver, out _))
            {
                return true;
            }

            try
            {
                return Dns.GetHostAddresses(resolver).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsStatusOnlyRecord(DnsRecord record)
        {
            return record.Value is "No records" or "Query failed" or "No response" or "Query error";
        }

        private void StopDnsRecon()
        {
            _dnsReconCts?.Cancel();
            DnsReconStatus = "Stopping...";
        }

        private async Task StartWhois()
        {
            var host = TraceViewModel.TryNormalizeHost(WhoisTarget);
            if (string.IsNullOrWhiteSpace(host))
            {
                MessageBox.Show("Please enter a valid domain or IP for WHOIS lookup.", "Input Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _whoisCts = new CancellationTokenSource();
            IsWhoisRunning = true;
            WhoisStatus = "Querying WHOIS registry...";
            WhoisResultText = string.Empty;
            _logAction($"Requesting WHOIS registry records for target {host}...");

            try
            {
                var result = await _whoisService.LookupAsync(host, _whoisCts.Token);
                if (_whoisCts.Token.IsCancellationRequested)
                {
                    WhoisStatus = "Stopped";
                    _logAction($"WHOIS query for {host} was cancelled.");
                }
                else
                {
                    WhoisResultText = result;
                    WhoisStatus = "WHOIS lookup completed.";
                    _logAction($"WHOIS query for {host} completed successfully.");
                }
            }
            catch (Exception ex)
            {
                WhoisStatus = $"Error: {ex.Message}";
                _logAction($"WHOIS query for {host} failed: {ex.Message}");
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

        private void StopNslookup()
        {
            _nslookupCts?.Cancel();
            NslookupStatus = "Stopping...";
        }

        private async Task RunNslookup()
        {
            var host = TraceViewModel.TryNormalizeHost(NslookupTarget);
            if (string.IsNullOrWhiteSpace(host))
            {
                MessageBox.Show("Please enter a valid domain for NSLOOKUP.", "Input Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _nslookupCts = new CancellationTokenSource();
            IsNslookupRunning = true;
            NslookupStatus = "Resolving...";
            NslookupResultText = string.Empty;

            try
            {
                var recordType = string.IsNullOrWhiteSpace(NslookupRecordType) ? "A" : NslookupRecordType;
                if (!Enum.TryParse<QueryType>(recordType, true, out var queryType))
                {
                    NslookupResultText = $"Unsupported record type: {recordType}";
                    NslookupStatus = "Error.";
                    return;
                }

                _logAction($"Initiating NSLOOKUP on {host} (Type: {queryType})...");

                var timeoutMs = Math.Clamp(_settingsViewModel.SettingsTimeoutMs, 500, 10000);
                var client = await _dnsReconService.CreateLookupClientAsync(NslookupServer, timeoutMs, _nslookupCts.Token);

                var sb = new StringBuilder();
                var nsServerList = client.NameServers != null 
                    ? string.Join(", ", client.NameServers.Select(ns => ns?.ToString() ?? "Unknown")) 
                    : "Default System Resolvers";
                sb.AppendLine($";; Querying server: {nsServerList}");
                sb.AppendLine($";; Got answer for query: {host} {queryType}");
                sb.AppendLine();

                var response = await client.QueryAsync(host, queryType, cancellationToken: _nslookupCts.Token);
                
                if (response == null)
                {
                    sb.AppendLine(";; DNS Error: No response received from server (Timeout or Network Unreachable).");
                    _logAction($"NSLOOKUP query for {host} returned no response.");
                }
                else if (response.HasError)
                {
                    sb.AppendLine($";; DNS Error: {response.ErrorMessage ?? "Unknown DNS Error"}");
                    _logAction($"NSLOOKUP query for {host} failed: {response.ErrorMessage ?? "Error"}");
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
                    
                    _logAction($"NSLOOKUP query for {host} completed successfully.");
                }

                NslookupResultText = sb.ToString();
                NslookupStatus = _nslookupCts.Token.IsCancellationRequested ? "Stopped." : "Completed.";
            }
            catch (OperationCanceledException)
            {
                NslookupStatus = "Stopped.";
                _logAction($"NSLOOKUP query for {host} was cancelled.");
            }
            catch (Exception ex)
            {
                NslookupResultText = $"NSLOOKUP Query Failed: {ex.Message}";
                NslookupStatus = "Error.";
                _logAction($"NSLOOKUP query for {host} failed: {ex.Message}");
            }
            finally
            {
                IsNslookupRunning = false;
                _nslookupCts?.Dispose();
                _nslookupCts = null;
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
                    "JSON" => System.Text.Json.JsonSerializer.Serialize(DnsRecords.Select(r => new { r.RecordType, r.Name, r.Value, r.Details }), new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
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

        private static string EscapeCsv(string? value)
        {
            value ??= string.Empty;
            var escaped = value.Replace("\"", "\"\"");
            return $"\"{escaped}\"";
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
    }
}

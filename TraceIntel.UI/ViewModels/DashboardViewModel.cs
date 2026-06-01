using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using TraceIntel.Core.Models;

namespace TraceIntel.UI.ViewModels
{
    public class DashboardViewModel : INotifyPropertyChanged
    {
        private readonly MainViewModel _mainViewModel;

        // Statistics
        private int _totalTraced;
        private int _successCount;
        private int _earlyFailures;
        private double _avgHopDepth;
        private string _latencyChartTitle = "Network Latency Profile (Selected Target)";

        public int TotalTraced
        {
            get => _totalTraced;
            set { _totalTraced = value; OnPropertyChanged(); }
        }

        public int SuccessCount
        {
            get => _successCount;
            set { _successCount = value; OnPropertyChanged(); }
        }

        public int EarlyFailures
        {
            get => _earlyFailures;
            set { _earlyFailures = value; OnPropertyChanged(); }
        }

        public double AvgHopDepth
        {
            get => _avgHopDepth;
            set { _avgHopDepth = value; OnPropertyChanged(); }
        }

        public string LatencyChartTitle
        {
            get => _latencyChartTitle;
            set { _latencyChartTitle = value; OnPropertyChanged(); }
        }

        // LiveCharts series collections
        public ObservableCollection<ISeries> ActivitySeries { get; set; } = new();
        public ObservableCollection<ISeries> DnsRecordSeries { get; set; } = new();
        public Axis[] XAxes { get; set; }
        public Axis[] YAxes { get; set; }

        private readonly LineSeries<double> _latencyLineSeries;

        public DashboardViewModel(MainViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel;

            // ======================
            // INITIALIZE LINE CHART (LATENCY)
            // ======================
            _latencyLineSeries = new LineSeries<double>
            {
                Name = "Latency (ms)",
                Values = new double[] { 14, 22, 19, 45, 85, 110, 95 }, // Realistic network hop profile
                Stroke = new SolidColorPaint(SKColors.MediumPurple) { StrokeThickness = 3 },
                Fill = new SolidColorPaint(SKColors.MediumPurple.WithAlpha(35)),
                GeometrySize = 8,
                GeometryStroke = new SolidColorPaint(SKColors.MediumPurple) { StrokeThickness = 2 }
            };

            ActivitySeries.Add(_latencyLineSeries);

            XAxes = new Axis[]
            {
                new Axis
                {
                    Name = "Hop Number",
                    Labels = new string[] { "Hop 1", "Hop 2", "Hop 3", "Hop 4", "Hop 5", "Hop 6", "Hop 7" },
                    TextSize = 12,
                    NameTextSize = 14,
                    LabelsPaint = new SolidColorPaint(SKColors.WhiteSmoke),
                    NamePaint = new SolidColorPaint(SKColors.WhiteSmoke)
                }
            };

            YAxes = new Axis[]
            {
                new Axis
                {
                    Name = "Latency (ms)",
                    MinLimit = 0,
                    TextSize = 12,
                    NameTextSize = 14,
                    LabelsPaint = new SolidColorPaint(SKColors.WhiteSmoke),
                    NamePaint = new SolidColorPaint(SKColors.WhiteSmoke)
                }
            };

            // ======================
            // INITIALIZE PIE CHART (DNS)
            // ======================
            LoadDefaultDnsPieChart();

            // Refresh statistics
            RefreshStats();
        }

        public void RefreshStats()
        {
            // Calculate real traceroute statistics
            TotalTraced = _mainViewModel.RoutingRows.Count;
            SuccessCount = _mainViewModel.SuccessfulTracesCount;
            EarlyFailures = _mainViewModel.RoutingRows.Count(r => r.IsEarlyFail);

            double sumHops = _mainViewModel.RoutingRows.Sum(r => r.MaxObservedHop);
            AvgHopDepth = TotalTraced > 0 ? Math.Round(sumHops / TotalTraced, 1) : 0;

            // Update Latency Chart based on the last row traced (if any)
            var lastRow = _mainViewModel.RoutingRows.LastOrDefault();
            if (lastRow != null)
            {
                UpdateLatencyChart(lastRow);
            }

            // Update DNS Pie Chart based on real records (if scanned)
            if (_mainViewModel.DnsRecords.Any())
            {
                UpdateDnsPieChart();
            }
            else
            {
                LoadDefaultDnsPieChart();
            }
        }

        public void UpdateLatencyChart(RoutingRow row)
        {
            if (row == null) return;

            LatencyChartTitle = $"Network Latency Profile for: {row.Domain}";

            var validHops = row.Hops.Where(h => h.HopNumber <= row.MaxObservedHop).ToList();
            if (!validHops.Any()) return;

            // Extract latencies, treating timeouts/nulls as 0
            var latencies = validHops.Select(h => h.LatencyMs.HasValue ? (double)h.LatencyMs.Value : 0.0).ToArray();
            var labels = validHops.Select(h => $"Hop {h.HopNumber}").ToArray();

            _latencyLineSeries.Values = latencies;
            XAxes[0].Labels = labels;
        }

        private void UpdateDnsPieChart()
        {
            DnsRecordSeries.Clear();

            var groups = _mainViewModel.DnsRecords
                .GroupBy(r => r.RecordType)
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count);

            var colors = new[] { SKColors.OrangeRed, SKColors.MediumSeaGreen, SKColors.DodgerBlue, SKColors.Goldenrod, SKColors.MediumPurple, SKColors.DeepPink, SKColors.Aquamarine };
            int i = 0;

            foreach (var group in groups)
            {
                var color = colors[i % colors.Length];
                DnsRecordSeries.Add(new PieSeries<double>
                {
                    Values = new double[] { group.Count },
                    Name = group.Type,
                    Fill = new SolidColorPaint(color),
                    DataLabelsPaint = new SolidColorPaint(SKColors.White),
                    DataLabelsSize = 12,
                    DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle
                });
                i++;
            }
        }

        private void LoadDefaultDnsPieChart()
        {
            DnsRecordSeries.Clear();
            DnsRecordSeries.Add(new PieSeries<double>
            {
                Values = new double[] { 40 },
                Name = "A (Default)",
                Fill = new SolidColorPaint(SKColors.MediumPurple)
            });
            DnsRecordSeries.Add(new PieSeries<double>
            {
                Values = new double[] { 25 },
                Name = "AAAA (Default)",
                Fill = new SolidColorPaint(SKColors.DodgerBlue)
            });
            DnsRecordSeries.Add(new PieSeries<double>
            {
                Values = new double[] { 20 },
                Name = "CNAME (Default)",
                Fill = new SolidColorPaint(SKColors.MediumSpringGreen)
            });
            DnsRecordSeries.Add(new PieSeries<double>
            {
                Values = new double[] { 15 },
                Name = "MX (Default)",
                Fill = new SolidColorPaint(SKColors.Orange)
            });
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
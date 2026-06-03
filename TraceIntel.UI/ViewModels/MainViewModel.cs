using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using TraceIntel.UI.ViewModels;

namespace TraceIntel.UI
{
    public class MainViewModel : ViewModelBase
    {
        private ViewModelBase _currentPageViewModel;
        private string _currentPageTitle = "Dashboard Overview";
        private string _currentPageSubtitle = "System statistics and diagnostic graphs";

        // Sub-ViewModels
        public DashboardViewModel Dashboard { get; }
        public TraceViewModel TraceVM { get; }
        public DomainIntelViewModel DomainIntelVM { get; }
        public SettingsViewModel SettingsVM { get; }
        public PingViewModel PingVM { get; }

        public ViewModelBase CurrentPageViewModel
        {
            get => _currentPageViewModel;
            set
            {
                if (SetProperty(ref _currentPageViewModel, value))
                {
                    OnPropertyChanged(nameof(IsDashboardActive));
                    OnPropertyChanged(nameof(IsTraceActive));
                    OnPropertyChanged(nameof(IsDnsReconActive));
                    OnPropertyChanged(nameof(IsSettingsActive));
                    OnPropertyChanged(nameof(IsPingActive));
                }
            }
        }

        public bool IsDashboardActive => CurrentPageViewModel == Dashboard;
        public bool IsTraceActive => CurrentPageViewModel == TraceVM;
        public bool IsDnsReconActive => CurrentPageViewModel == DomainIntelVM;
        public bool IsSettingsActive => CurrentPageViewModel == SettingsVM;
        public bool IsPingActive => CurrentPageViewModel == PingVM;

        public string CurrentPageTitle
        {
            get => _currentPageTitle;
            set => SetProperty(ref _currentPageTitle, value);
        }

        public string CurrentPageSubtitle
        {
            get => _currentPageSubtitle;
            set => SetProperty(ref _currentPageSubtitle, value);
        }

        public ObservableCollection<string> ActivityLog { get; } = new()
        {
            $"[{DateTime.Now:HH:mm:ss}] TRACE X Security Intel Suite initialized."
        };

        public ICommand NavigateCommand { get; }

        public MainViewModel()
        {
            // 1. Instantiate the Settings ViewModel first (shared dependency)
            SettingsVM = new SettingsViewModel();

            // 2. Instantiate other sub-ViewModels passing a logging callback and dependencies
            TraceVM = new TraceViewModel(SettingsVM, LogActivity);
            DomainIntelVM = new DomainIntelViewModel(SettingsVM, LogActivity);
            Dashboard = new DashboardViewModel(this);
            PingVM = new PingViewModel(LogActivity);

            // 3. Setup Default Active Page
            _currentPageViewModel = Dashboard;

            // 4. Setup Navigation Commands
            NavigateCommand = new RelayCommand(destination =>
            {
                if (destination is string dest)
                {
                    Navigate(dest);
                }
            });

            // 5. Connect Sub-ViewModels Events (Mediation)
            TraceVM.OnTraceCompleted += () => Dashboard.RefreshStats();
            DomainIntelVM.OnDnsScanCompleted += () => Dashboard.RefreshStats();

            // Wire request for DNS Recon from Trace right-click menu
            TraceVM.OnRequestDnsRecon += domain =>
            {
                DomainIntelVM.DnsReconTarget = domain;
                Navigate("DnsRecon");
                _ = DomainIntelVM.StartDnsRecon();
            };
        }

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

        private void Navigate(string destination)
        {
            switch (destination)
            {
                case "Dashboard":
                    CurrentPageViewModel = Dashboard;
                    CurrentPageTitle = "Dashboard Overview";
                    CurrentPageSubtitle = "System statistics and diagnostic graphs";
                    Dashboard.RefreshStats();
                    break;
                case "Trace":
                    CurrentPageViewModel = TraceVM;
                    CurrentPageTitle = "Network Path Traceroute";
                    CurrentPageSubtitle = "Run diagnostic probes and map packet hops";
                    break;
                case "DnsRecon":
                    CurrentPageViewModel = DomainIntelVM;
                    CurrentPageTitle = "Domain Intelligence Recon";
                    CurrentPageSubtitle = "Query DNS servers, registrars, and lookups";
                    break;
                case "Settings":
                    CurrentPageViewModel = SettingsVM;
                    CurrentPageTitle = "System Configuration";
                    CurrentPageSubtitle = "Adjust trace depths, parallelism limits, and resolver paths";
                    break;
                case "Ping":
                    CurrentPageViewModel = PingVM;
                    CurrentPageTitle = "Ping Tool";
                    CurrentPageSubtitle = "ICMP diagnostic ping utility";
                    break;
            }
        }
    }
}

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using TraceIntel.Core.Models;

namespace TraceIntel.UI.ViewModels
{
    public class DashboardViewModel : ViewModelBase
    {
        private readonly MainViewModel _mainViewModel;

        // Statistics
        private int _totalTraced;
        private int _successCount;
        private int _earlyFailures;
        private double _avgHopDepth;

        // Active network adapter stats
        private string _localIp = "Detecting...";
        private string _gateway = "Detecting...";
        private string _dnsServer = "Detecting...";
        private string _networkStatus = "Active";

        public int TotalTraced
        {
            get => _totalTraced;
            set => SetProperty(ref _totalTraced, value);
        }

        public int SuccessCount
        {
            get => _successCount;
            set => SetProperty(ref _successCount, value);
        }

        public int EarlyFailures
        {
            get => _earlyFailures;
            set => SetProperty(ref _earlyFailures, value);
        }

        public double AvgHopDepth
        {
            get => _avgHopDepth;
            set => SetProperty(ref _avgHopDepth, value);
        }

        public string LocalIp
        {
            get => _localIp;
            set => SetProperty(ref _localIp, value);
        }

        public string Gateway
        {
            get => _gateway;
            set => SetProperty(ref _gateway, value);
        }

        public string DnsServer
        {
            get => _dnsServer;
            set => SetProperty(ref _dnsServer, value);
        }

        public string NetworkStatus
        {
            get => _networkStatus;
            set => SetProperty(ref _networkStatus, value);
        }

        public ObservableCollection<DomainTrace> CompletedTasks => _mainViewModel.TraceVM.Results;

        public DashboardViewModel(MainViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel;

            // Retrieve active adapter details
            RetrieveNetworkDetails();

            // Refresh statistics
            RefreshStats();
        }

        public void RefreshStats()
        {
            TotalTraced = _mainViewModel.TraceVM.RoutingRows.Count;
            SuccessCount = _mainViewModel.TraceVM.SuccessfulTracesCount;
            EarlyFailures = _mainViewModel.TraceVM.RoutingRows.Count(r => r.IsEarlyFail);

            double sumHops = _mainViewModel.TraceVM.RoutingRows.Sum(r => r.MaxObservedHop);
            AvgHopDepth = TotalTraced > 0 ? Math.Round(sumHops / TotalTraced, 1) : 0;

            OnPropertyChanged(nameof(CompletedTasks));
        }

        private void RetrieveNetworkDetails()
        {
            try
            {
                // Local IPv4 address query
                var host = Dns.GetHostEntry(Dns.GetHostName());
                var ip = host.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                LocalIp = ip?.ToString() ?? "Loopback (127.0.0.1)";

                // Default Gateway query
                var gatewayAddress = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(n => n.GetIPProperties().GatewayAddresses)
                    .Select(g => g.Address)
                    .FirstOrDefault();
                Gateway = gatewayAddress?.ToString() ?? "Not Assigned";

                // DNS Resolver query
                var dnsServer = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(n => n.GetIPProperties().DnsAddresses)
                    .FirstOrDefault();
                DnsServer = dnsServer?.ToString() ?? "System Default";

                NetworkStatus = NetworkInterface.GetIsNetworkAvailable() ? "Active (Online)" : "Offline";
            }
            catch
            {
                LocalIp = "127.0.0.1";
                Gateway = "192.168.1.1";
                DnsServer = "8.8.8.8";
                NetworkStatus = "Offline / Simulation";
            }
        }
    }
}
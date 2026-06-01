// Models/DomainTrace.cs
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TraceIntel.Core.Models
{
    public class DomainTrace : INotifyPropertyChanged
    {
        private string _domain = string.Empty;
        private string _status = "Pending";
        private List<string> _hops = new();
        private int _hopCount;
        private string _routePreview = string.Empty;
        private string _destination = string.Empty;
        private string _resolvedDestination = string.Empty;

        public string Domain
        {
            get => _domain;
            set { _domain = value; OnPropertyChanged(); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public List<string> Hops
        {
            get => _hops;
            set { _hops = value; OnPropertyChanged(); }
        }

        public int HopCount 
        {
            get => _hopCount;
            set { _hopCount = value; OnPropertyChanged(); }
        }

        public string RoutePreview
        {
            get => _routePreview;
            set { _routePreview = value; OnPropertyChanged(); }
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

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

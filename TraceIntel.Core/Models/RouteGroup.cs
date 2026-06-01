// Models/RouteGroup.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TraceIntel.Core.Models
{
    public class RouteGroup : INotifyPropertyChanged
    {
        private string _routePath = string.Empty;
        private int _count;
        private ObservableCollection<string> _domains = new();

        public string RoutePath
        {
            get => _routePath;
            set { _routePath = value; OnPropertyChanged(); }
        }

        public int Count
        {
            get => _count;
            set { _count = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> Domains
        {
            get => _domains;
            set { _domains = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}


using System;
using System.Windows.Input;

namespace TraceIntel.UI.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private int _settingsParallelism = 10;
        private int _settingsTimeoutMs = 1000;
        private bool _settingsEnableCache = true;
        private string _settingsDefaultExportPath = string.Empty;
        private string _settingsDefaultDnsServer = string.Empty;
        // New advanced options
        private string _settingsProxy = string.Empty;
        private string _settingsApiKey = string.Empty;
        private string _settingsAdvancedJson = string.Empty;

        public int SettingsParallelism
        {
            get => _settingsParallelism;
            set => SetProperty(ref _settingsParallelism, value);
        }

        public int SettingsTimeoutMs
        {
            get => _settingsTimeoutMs;
            set => SetProperty(ref _settingsTimeoutMs, value);
        }

        public bool SettingsEnableCache
        {
            get => _settingsEnableCache;
            set => SetProperty(ref _settingsEnableCache, value);
        }

        public string SettingsDefaultExportPath
        {
            get => _settingsDefaultExportPath;
            set => SetProperty(ref _settingsDefaultExportPath, value);
        }

        public string SettingsDefaultDnsServer
        {
            get => _settingsDefaultDnsServer;
            set => SetProperty(ref _settingsDefaultDnsServer, value);
        }

        // Advanced properties
        public string SettingsProxy
        {
            get => _settingsProxy;
            set => SetProperty(ref _settingsProxy, value);
        }

        public string SettingsApiKey
        {
            get => _settingsApiKey;
            set => SetProperty(ref _settingsApiKey, value);
        }

        public string SettingsAdvancedJson
        {
            get => _settingsAdvancedJson;
            set => SetProperty(ref _settingsAdvancedJson, value);
        }

        // Commands for persisting settings (placeholder implementation)
        public ICommand SaveSettingsCommand { get; }
        public ICommand ResetSettingsCommand { get; }

        public SettingsViewModel()
        {
            SaveSettingsCommand = new RelayCommand(_ => SaveSettings(), _ => true);
            ResetSettingsCommand = new RelayCommand(_ => ResetSettings(), _ => true);
        }

        private void SaveSettings()
        {
            // TODO: Persist settings to file or user profile.
        }

        private void ResetSettings()
        {
            // Reset to defaults defined above
            SettingsParallelism = 10;
            SettingsTimeoutMs = 1000;
            SettingsEnableCache = true;
            SettingsDefaultExportPath = string.Empty;
            SettingsDefaultDnsServer = string.Empty;
            SettingsProxy = string.Empty;
            SettingsApiKey = string.Empty;
            SettingsAdvancedJson = string.Empty;
        }
    }
}

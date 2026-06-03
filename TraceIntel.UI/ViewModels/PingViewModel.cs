using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using TraceIntel.Core.Services;

namespace TraceIntel.UI.ViewModels
{
    public class PingViewModel : ViewModelBase
    {
        private readonly PingService _pingService;
        private readonly Action<string> _logAction;

        private string _pingTarget = string.Empty;
        private string _pingStatus = "Ready";
        private string _pingLatency = "--";
        private bool _isPinging;
        private CancellationTokenSource? _pingCts;

        public string PingTarget
        {
            get => _pingTarget;
            set => SetProperty(ref _pingTarget, value);
        }

        public string PingStatus
        {
            get => _pingStatus;
            set => SetProperty(ref _pingStatus, value);
        }

        public string PingLatency
        {
            get => _pingLatency;
            set => SetProperty(ref _pingLatency, value);
        }

        public bool IsPinging
        {
            get => _isPinging;
            set
            {
                if (SetProperty(ref _isPinging, value))
                {
                    OnPropertyChanged(nameof(IsNotPinging));
                }
            }
        }

        public bool IsNotPinging => !IsPinging;

        public ObservableCollection<string> PingOutput { get; } = new ObservableCollection<string>();

        public ICommand StartPingCommand { get; }
        public ICommand StopPingCommand { get; }

        public PingViewModel(Action<string> logAction)
        {
            _pingService = new PingService();
            _logAction = logAction;

            StartPingCommand = new RelayCommand(async _ => await StartPingAsync(), _ => IsNotPinging && !string.IsNullOrWhiteSpace(PingTarget));
            StopPingCommand = new RelayCommand(_ => StopPing(), _ => IsPinging);
        }

        private async Task StartPingAsync()
        {
            IsPinging = true;
            PingStatus = "Pinging...";
            PingLatency = "--";
            PingOutput.Clear();
            _logAction?.Invoke($"Started ping to {PingTarget}");
            PingOutput.Add($"Pinging {PingTarget}...");

            _pingCts = new CancellationTokenSource();

            try
            {
                while (!_pingCts.Token.IsCancellationRequested)
                {
                    var result = await _pingService.PingAsync(PingTarget, _pingCts.Token);
                    
                    if (result.IsSuccess)
                    {
                        PingStatus = "Success";
                        PingLatency = $"{result.RoundtripTime} ms";
                        System.Media.SystemSounds.Beep.Play();
                    }
                    else
                    {
                        PingStatus = "Failed";
                        PingLatency = "Timeout";
                    }

                    PingOutput.Add(result.StatusMessage);

                    // Wait 1 second before next ping
                    await Task.Delay(1000, _pingCts.Token);
                }
            }
            catch (TaskCanceledException)
            {
                // Expected when stopped
            }
            catch (Exception ex)
            {
                PingStatus = "Error";
                PingOutput.Add($"Error: {ex.Message}");
            }
            finally
            {
                IsPinging = false;
                PingStatus = "Stopped";
                _logAction?.Invoke($"Stopped ping to {PingTarget}");
            }
        }

        private void StopPing()
        {
            _pingCts?.Cancel();
        }
    }
}

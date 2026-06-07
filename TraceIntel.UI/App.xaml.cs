using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Diagnostics;

namespace TraceIntel.UI;

public partial class App : Application
{
    public App()
    {
        Console.WriteLine("[App] Constructor started");

        // Enable WPF tracing to catch binding errors
        PresentationTraceSources.DataBindingSource.Listeners.Add(new ConsoleTraceListener(true));
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;

        // Catch unhandled exceptions during startup
        this.DispatcherUnhandledException += (s, e) =>
        {
            try
            {
                Console.WriteLine($"[App] Dispatcher Exception: {e.Exception.Message}");
                MessageBox.Show($"Unhandled Exception: {e.Exception.Message}\n\n{e.Exception.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[App] Error displaying exception dialog: {ex.Message}");
            }
            finally
            {
                e.Handled = true;
            }
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Console.WriteLine($"[App] AppDomain Exception");
            if (e.ExceptionObject is Exception ex)
            {
                try
                {
                    MessageBox.Show($"Fatal Exception: {ex.Message}\n\n{ex.StackTrace}", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($"[App] Error displaying fatal exception dialog: {ex2.Message}");
                }
            }
        };

        Console.WriteLine("[App] Constructor completed");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        Console.WriteLine("[App] OnStartup called");
        try
        {
            base.OnStartup(e);
            Console.WriteLine("[App] OnStartup completed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[App] OnStartup failed: {ex.Message}\n{ex.StackTrace}");
            throw;
        }
    }
}

using System;
using System.Windows;

namespace TraceIntel.UI;

public partial class App : Application
{
    public App()
    {
        Console.WriteLine("[App] Constructor started");

        // Catch unhandled exceptions during startup
        this.DispatcherUnhandledException += (s, e) =>
        {
            Console.WriteLine($"[App] Dispatcher Exception: {e.Exception.Message}");
            MessageBox.Show($"Unhandled Exception: {e.Exception.Message}\n\n{e.Exception.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = false;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Console.WriteLine($"[App] AppDomain Exception");
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show($"Fatal Exception: {ex.Message}\n\n{ex.StackTrace}", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
        
        Console.WriteLine("[App] Constructor completed");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        Console.WriteLine("[App] OnStartup called");
        base.OnStartup(e);
        Console.WriteLine("[App] OnStartup completed");
    }
}

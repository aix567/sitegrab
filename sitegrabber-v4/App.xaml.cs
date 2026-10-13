// language: C#, file: App.xaml.cs
// Global exception net — every unhandled exception used to kill the whole WPF
// process silently (that's what the Event Viewer crash dumps were). Now it's
// logged and shown to the user instead; the app stays up.

using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace SiteGrabberV3;

public partial class App : Application
{
    public static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SiteGrabber");

    protected override void OnStartup(StartupEventArgs e)
    {
        Directory.CreateDirectory(LogDir);

        DispatcherUnhandledException            += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException    += OnUnobservedTaskException;

        base.OnStartup(e);

        var splash = new SplashWindow();
        splash.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        var result = MessageBox.Show(
            $"SiteGrabber hit an unexpected error and recovered:\n\n{e.Exception.Message}\n\n" +
            $"Details were written to:\n{LogDir}\\crash.log\n\nOpen that folder now?",
            "SiteGrabber — error", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            try { System.Diagnostics.Process.Start("explorer.exe", LogDir); }
            catch { /* best effort */ }
        }
        e.Handled = true; // keep the app alive
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex) LogCrash(ex);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        e.SetObserved(); // prevent process termination from a faulted background Task
    }

    private static void LogCrash(Exception ex)
    {
        try
        {
            File.AppendAllText(Path.Combine(LogDir, "crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch { /* logging must never itself throw */ }
    }
}

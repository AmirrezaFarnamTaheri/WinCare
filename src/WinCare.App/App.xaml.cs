using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;
using ProtocolActivatedEventArgs = Windows.ApplicationModel.Activation.ProtocolActivatedEventArgs;
using WinCare.Infrastructure.Observability;

namespace WinCare.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private MainWindow? _window;

    public MainWindow? MainWindow => _window;

    public App()
    {
        StartupTelemetry.Mark("AppConstructed");
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        StartupTelemetry.Mark("WindowCreated");
        _window.Activate();
        if (AppInstance.GetCurrent().GetActivatedEventArgs().Data is ProtocolActivatedEventArgs protocolArgs)
        {
            _window.HandleProtocolActivation(protocolArgs.Uri);
        }
        else if (!string.IsNullOrWhiteSpace(args.Arguments))
        {
            _window.HandleProtocolActivation(args.Arguments);
        }
        _ = InitializeRuntimeAsync();
    }

    private static async Task InitializeRuntimeAsync()
    {
        try
        {
            await Services.AppRuntime.Current.InitializePluginsAsync();
        }
        catch (Exception ex)
        {
            // A failed plugin discovery must never crash the app: the catalog of built-in
            // commands remains available, and the failure is surfaced via telemetry/logging.
            StartupTelemetry.Mark("PluginInitializationFailed");
            System.Diagnostics.Debug.WriteLine($"[App] Plugin initialization failed: {ex}");
        }
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Persist a diagnostic dump for every unhandled exception; Debug-only output is not
        // a user-facing error path and is invisible in release builds.
        string crashLog = string.Empty;
        try
        {
            string logsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinCare", "logs");
            Directory.CreateDirectory(logsDir);
            crashLog = Path.Combine(logsDir, $"crash-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(crashLog, e.Exception.ToString());
        }
        catch
        {
            crashLog = string.Empty; // Last-resort logging must never throw.
        }

        // Give the user a diagnosable message rather than a raw crash whenever a window is
        // available; otherwise the persisted crash log is the only recovery surface.
        if (Current is App app && app.MainWindow?.Content is FrameworkElement root && root.XamlRoot is { } xamlRoot)
        {
            try
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = xamlRoot,
                    Title = "WinCare ran into a problem",
                    Content = e.Exception is DllNotFoundException or InvalidOperationException
                        ? e.Exception.Message
                        : "An unexpected error occurred. Details were written to:\n" + (crashLog.Length > 0 ? crashLog : "the WinCare logs folder."),
                    CloseButtonText = "Close",
                };
                _ = dialog.ShowAsync();
            }
            catch
            {
                // Dialog presentation is best-effort during exception unwinding.
            }
        }

        e.Handled = true;
    }
}

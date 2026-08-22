using Microsoft.UI.Xaml;
using WinCare.Infrastructure.Observability;

namespace WinCare.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private Window? _window;

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
        if (!string.IsNullOrWhiteSpace(args.Arguments))
        {
            _window.HandleProtocolActivation(args.Arguments);
        }
        _ = Services.AppRuntime.Current.InitializePluginsAsync();
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine(e.Exception);
    }
}

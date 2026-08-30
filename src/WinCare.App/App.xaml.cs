using Microsoft.UI.Xaml;
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
            System.Diagnostics.Debug.WriteLine($"[App] Plugin initialization failed: {ex}");
        }
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine(e.Exception);
    }
}

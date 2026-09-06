using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using ProtocolActivatedEventArgs = Windows.ApplicationModel.Activation.ProtocolActivatedEventArgs;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;
using WinCare.Infrastructure.Observability;

namespace WinCare.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private const string PortableSmokeArgument = "--smoke-test";
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
        bool runPortableSmoke = string.Equals(
            args.Arguments?.Trim(),
            PortableSmokeArgument,
            StringComparison.OrdinalIgnoreCase);

        _window = new MainWindow();
        StartupTelemetry.Mark("WindowCreated");
        _window.Activate();

        if (runPortableSmoke)
        {
            _ = RunPortableSmokeTestAsync();
            return;
        }

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

    private static async Task RunPortableSmokeTestAsync()
    {
        try
        {
            Services.AppRuntime runtime = Services.AppRuntime.Current;
            uint abiVersion = runtime.NativeCore.GetAbiVersion();
            if (abiVersion != CommandDispatcher.SupportedAbiVersion)
            {
                throw new InvalidOperationException(
                    $"Native ABI mismatch during packaged smoke test: expected {CommandDispatcher.SupportedAbiVersion}, got {abiVersion}.");
            }

            await runtime.InitializePluginsAsync().ConfigureAwait(true);
            CommandResult result = await runtime.Dispatcher.ExecuteAsync(
                CommandRequest.Preview("system"),
                new CommandExecutionOptions(
                    ReviewApproved: false,
                    Deadline: DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15)),
                CancellationToken.None).ConfigureAwait(true);

            if (result.Status != CommandResultStatus.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Packaged system preview failed with {result.Status} ({result.Code}).");
            }

            StartupTelemetry.Mark("PortableSmokePassed");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] Portable smoke test failed: {ex}");
            Environment.Exit(1);
        }
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
        // Unknown unhandled exceptions may invalidate UI, plugin, approval, or operation state.
        // Persist diagnostics and let the process terminate rather than continuing in an
        // indeterminate state. Recoverable failures must be caught at their owning boundary.
        try
        {
            string logsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinCare", "logs");
            Directory.CreateDirectory(logsDir);
            string crashLog = Path.Combine(logsDir, $"crash-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.log");
            File.WriteAllText(crashLog, e.Exception.ToString());
            System.Diagnostics.Debug.WriteLine($"[App] Fatal unhandled exception written to {crashLog}: {e.Exception}");
        }
        catch (Exception logException)
        {
            System.Diagnostics.Debug.WriteLine($"[App] Fatal exception logging also failed: {logException}");
        }

        e.Handled = false;
    }
}

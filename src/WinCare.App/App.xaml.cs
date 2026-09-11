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

    /// <summary>
    /// F-003: the smoke test must exercise every navigation route — the exact journeys that
    /// crashed the installed artifact — not only startup and backend availability.
    /// </summary>
    private static readonly string[] SmokeNavigationKeys =
    [
        "home", "checkup", "system-care", "security", "repair-recovery",
        "all-tools", "activity", "plugin-store", "ai-doctor", "settings", "help", "about",
    ];

    public MainWindow? MainWindow => _window;

    public App()
    {
        StartupTelemetry.Mark("AppConstructed");
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] processArguments = Environment.GetCommandLineArgs();
        string? directArgument = processArguments
            .Skip(1)
            .FirstOrDefault(argument => !string.IsNullOrWhiteSpace(argument));
        bool runPortableSmoke = processArguments
            .Skip(1)
            .Any(argument => string.Equals(
                argument,
                PortableSmokeArgument,
                StringComparison.OrdinalIgnoreCase));

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
        else if (!string.IsNullOrWhiteSpace(directArgument))
        {
            _window.HandleProtocolActivation(directArgument);
        }
        _ = InitializeRuntimeAsync();
    }

    /// <summary>
    /// F-003 diagnostics: the smoke test appends each stage to a trace file. Stowed XAML
    /// exceptions bypass the managed handler, so the last trace line identifies the
    /// failing stage when the process is terminated by a WinRT fail-fast.
    /// </summary>
    private static void TraceSmoke(string stage)
    {
        try
        {
            string logsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinCare", "logs");
            Directory.CreateDirectory(logsDir);
            File.AppendAllText(
                Path.Combine(logsDir, "smoke-trace.log"),
                $"{DateTime.UtcNow:O} {stage}{Environment.NewLine}");
        }
        catch
        {
            // Tracing must never be the reason the smoke run fails.
        }
    }

    private static async Task RunPortableSmokeTestAsync()
    {
        TraceSmoke("smoke-start");
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
            TraceSmoke("plugins-initialized");
            CommandResult result = await runtime.Dispatcher.ExecuteAsync(
                CommandRequest.Preview("system"),
                new CommandExecutionOptions(
                    ReviewApproved: false,
                    Deadline: DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15)),
                CancellationToken.None).ConfigureAwait(true);
            TraceSmoke($"system-preview:{result.Status}");

            if (result.Status != CommandResultStatus.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Packaged system preview failed with {result.Status} ({result.Code}).");
            }

            // F-003: visit every real navigation destination in the shipped artifact. Page
            // construction and XAML binding initialization run here, so missing converters
            // or collection projection failures crash the smoke run and fail the gate.
            MainWindow window = ((App)Current)._window
                ?? throw new InvalidOperationException("Smoke test could not access the main window.");
            foreach (string key in SmokeNavigationKeys)
            {
                TraceSmoke($"navigating:{key}");
                window.ShellPage.NavigateTo(key);
                await Task.Delay(200).ConfigureAwait(true); // pump the UI thread: layout, Loading, bindings
                TraceSmoke($"navigated:{key}");
                StartupTelemetry.Mark("PortableSmokeNavigated:" + key);
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

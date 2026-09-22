using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.AppLifecycle;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using ProtocolActivatedEventArgs = Windows.ApplicationModel.Activation.ProtocolActivatedEventArgs;
using WinCare.Application.Commands;
using WinCare.Application.Navigation;
using WinCare.Domain.Commands;
using WinCare.Infrastructure.Observability;

namespace WinCare.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private const string PortableSmokeArgument = "--smoke-test";
    private const string CaptureScreensArgument = "--capture-screens";
    private MainWindow? _window;

    /// <summary>
    /// Routes rendered into documentation runtime captures by <c>--capture-screens</c>.
    /// The image file name is the route id plus <c>.png</c> (e.g. "home" ->
    /// "home.png"); tools/capture_screenshots.py renames them to docs/images/runtime-*.png
    /// and records the provenance manifest the documentation is synced from.
    /// </summary>
    public static readonly (string Route, int SettleDelayMs)[] CaptureRoutes =
    [
        ("home", 700),
        ("checkup", 2500), // let layout, bindings, and background results settle before render
    ];

    /// <summary>
    /// Fixed device-independent size for documentation captures. <see cref="MainWindow"/>
    /// resizes to this and skips saved-placement restore in capture mode, and the provenance
    /// sidecar reports the same geometry, so the three cannot drift.
    /// </summary>
    public static readonly (int Width, int Height) CaptureWindowSizeDips = (1280, 800);

    /// <summary>
    /// Exercises every navigation route during packaged smoke testing. Derived from
    /// <see cref="NavigationCatalog.Items"/> so the smoke test cannot drift from the routing
    /// table the shell actually builds from.
    /// </summary>
    private static readonly string[] SmokeNavigationKeys = NavigationCatalog.Items
        .Select(item => item.Id)
        .ToArray();

    public MainWindow? MainWindow => _window;

    /// <summary>Launch argument handled by <see cref="RunCaptureScreensAsync"/>, exposed for the contract gate.</summary>
    public static string CaptureArgument => CaptureScreensArgument;

    /// <summary>Whether this session renders documentation captures, so the shell stays
    /// deterministic: the first-run tour never overlays a route being captured.</summary>
    public static bool IsCaptureSession { get; private set; }

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

        string? captureOutputDirectory = ResolveCaptureOutputDirectory(processArguments);
        IsCaptureSession = captureOutputDirectory is not null;
        if (IsCaptureSession)
        {
            // Must run before the window is created: MainWindow's field initializer forces the
            // AppRuntime singleton and AppPreferences' static constructor resolves the data root,
            // and both must see the capture environment.
            ConfigureCaptureEnvironment();
        }
        _window = new MainWindow(IsCaptureSession);
        StartupTelemetry.Mark("WindowCreated");
        _window.Activate();

        if (runPortableSmoke)
        {
            _ = RunPortableSmokeTestAsync();
            return;
        }

        if (captureOutputDirectory is not null)
        {
            _ = RunCaptureScreensAsync(captureOutputDirectory);
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
    /// Returns the directory passed to <c>--capture-screens &lt;dir&gt;</c>. When the flag is
    /// present without a usable directory the process fails closed with a nonzero exit code:
    /// a malformed capture invocation must never fall through into an ordinary app launch,
    /// which would silently capture nothing and report success to the caller.
    /// </summary>
    private static string? ResolveCaptureOutputDirectory(string[] processArguments)
    {
        for (int i = 1; i < processArguments.Length; i++)
        {
            if (!string.Equals(processArguments[i], CaptureScreensArgument, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (i + 1 < processArguments.Length && !string.IsNullOrWhiteSpace(processArguments[i + 1]))
            {
                return processArguments[i + 1];
            }
            Console.Error.WriteLine(
                $"{CaptureScreensArgument} requires a writable output directory; no path was given.");
            Environment.Exit(2);
            return null;
        }
        return null;
    }

    /// <summary>
    /// Appends each smoke test stage to a diagnostic trace log.
    /// </summary>
    private static void TraceSmoke(string stage) => TraceStage("smoke-trace.log", stage);

    /// <summary>
    /// Appends each documentation-capture stage to a diagnostic trace log.
    /// </summary>
    private static void TraceCapture(string stage) => TraceStage("capture-trace.log", stage);

    private static void TraceStage(string fileName, string stage)
    {
        try
        {
            string logsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinCare", "logs");
            Directory.CreateDirectory(logsDir);
            File.AppendAllText(
                Path.Combine(logsDir, fileName),
                $"{DateTime.UtcNow:O} {stage}{Environment.NewLine}");
        }
        catch
        {
            // Tracing must never be the reason the run fails.
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

            // Exercise each navigation destination to verify UI controls and bindings.
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

    /// <summary>
    /// Points the process at an isolated, empty data root and installs the read-only command
    /// plane, so a documentation capture renders a pristine first-run surface and can never
    /// dispatch a mutation. Runs before the first <see cref="Services.AppRuntime.Current"/>
    /// access and before <see cref="AppPreferences"/>' static constructor.
    /// </summary>
    private static void ConfigureCaptureEnvironment()
    {
        string captureDataRoot = Path.Combine(
            Path.GetTempPath(),
            $"wincare-capture-data-{Guid.NewGuid():N}");
        Directory.CreateDirectory(captureDataRoot);
        WinCare.Application.Storage.AppDataRoot.Override = captureDataRoot;
        Services.AppRuntime.EnableCaptureMode();
        TraceCapture($"capture-data-root:{captureDataRoot}");
    }

    /// <summary>
    /// Renders each documentation route from <see cref="CaptureRoutes"/> to a PNG in
    /// <paramref name="outputDirectory"/> and exits with 0 only when every file was written.
    /// Runs the packaged plugin initialization first so captures reflect real plugin state.
    /// </summary>
    private static async Task RunCaptureScreensAsync(string outputDirectory)
    {
        TraceCapture($"capture-start:{outputDirectory}");
        try
        {
            Services.AppRuntime runtime = Services.AppRuntime.Current;
            await runtime.InitializePluginsAsync().ConfigureAwait(true);
            TraceCapture("plugins-initialized");

            MainWindow window = ((App)Current)._window
                ?? throw new InvalidOperationException("Capture run could not access the main window.");

            // Sidecar the tool reads to record the policy-required provenance fields. Version and
            // architecture come from the running assembly and process, not from the source tree,
            // so the manifest describes this exact executable.
            var captureDispatcher = runtime.Dispatcher as CaptureModeCommandDispatcher;
            File.WriteAllText(
                Path.Combine(outputDirectory, "capture-meta.json"),
                $$"""
                  {
                    "version": "{{typeof(App).Assembly.GetName().Version?.ToString(3) ?? "unknown"}}",
                    "appearance": "{{window.CaptureAppearance}}",
                    "windowSizeDips": "{{CaptureWindowSizeDips.Width}}x{{CaptureWindowSizeDips.Height}}",
                    "architecture": "{{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}}"
                  }
                  """);

            foreach ((string route, int settleDelayMs) in CaptureRoutes)
            {
                TraceCapture($"capturing:{route}");
                window.ShellPage.NavigateTo(route);
                await Task.Delay(settleDelayMs).ConfigureAwait(true); // layout, bindings, probe results
                await SaveWindowCapturePngAsync(window, Path.Combine(outputDirectory, $"{route}.png"))
                    .ConfigureAwait(true);
                TraceCapture($"captured:{route}");
            }

            if (captureDispatcher is { RejectedRequests.Count: > 0 })
            {
                // The rejecting proxy already aborted the offending dispatch; this is the
                // belt-and-braces record so a suppressed exception cannot look like success.
                string attempted = string.Join(", ", captureDispatcher.RejectedRequests.Select(r => r.CommandId));
                throw new InvalidOperationException(
                    $"Documentation capture attempted to execute commands: {attempted}. "
                    + "Capture must render documented routes only.");
            }

            StartupTelemetry.Mark("DocumentationCapturesPassed");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] Documentation capture failed: {ex}");
            Environment.Exit(1);
        }
    }

    private static async Task SaveWindowCapturePngAsync(MainWindow window, string filePath)
    {
        if (window.Content is not FrameworkElement root)
        {
            throw new InvalidOperationException("Window content is not a FrameworkElement; cannot capture.");
        }

        var renderTarget = new RenderTargetBitmap();
        await renderTarget.RenderAsync(root).AsTask().ConfigureAwait(true);
        byte[] pixelBytes = ToByteArray(await renderTarget.GetPixelsAsync().AsTask().ConfigureAwait(true));

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(
            BitmapEncoder.PngEncoderId, stream).AsTask().ConfigureAwait(true);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            (uint)renderTarget.PixelWidth,
            (uint)renderTarget.PixelHeight,
            96,
            96,
            pixelBytes);
        await encoder.FlushAsync().AsTask().ConfigureAwait(true);

        byte[] pngBytes = await ToByteArrayAsync(stream).ConfigureAwait(true);
        await File.WriteAllBytesAsync(filePath, pngBytes).ConfigureAwait(true);
    }

    private static byte[] ToByteArray(IBuffer buffer)
    {
        DataReader reader = DataReader.FromBuffer(buffer);
        byte[] bytes = new byte[buffer.Length];
        reader.ReadBytes(bytes);
        return bytes;
    }

    private static async Task<byte[]> ToByteArrayAsync(Windows.Storage.Streams.IRandomAccessStream stream)
    {
        stream.Seek(0);
        var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size).AsTask().ConfigureAwait(true);
        byte[] bytes = new byte[stream.Size];
        reader.ReadBytes(bytes);
        return bytes;
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

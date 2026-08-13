using WinCare.Application.Activity;
using WinCare.Application.Commands;
using WinCare.Infrastructure.Commands;
using WinCare.Infrastructure.Native;

namespace WinCare.App.Services;

/// <summary>
/// Process-lifetime composition root for shared native services.
/// </summary>
public sealed class AppRuntime
{
    private static readonly Lazy<AppRuntime> CurrentValue = new(() => new AppRuntime());

    private AppRuntime()
    {
        Journal = new ActivityJournalService();
        NativeCore = new NativeCoreService();
        CommandExecutor = new WindowsCommandExecutor(NativeCore);
        Dispatcher = CommandRuntime.CreateDefault(CommandExecutor, NativeCore, Journal);
    }

    public static AppRuntime Current => CurrentValue.Value;

    public ActivityJournalService Journal { get; }

    public NativeCoreService NativeCore { get; }

    public WindowsCommandExecutor CommandExecutor { get; }

    public CommandDispatcher Dispatcher { get; }
}

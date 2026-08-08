using WinCare.Application.Commands.Handlers;

namespace WinCare.Application.Commands;

/// <summary>
/// Composition entry point for the native command plane.
/// </summary>
public static class CommandRuntime
{
    /// <summary>
    /// Creates the default dispatcher wired to the frozen native catalog and the
    /// implemented runtime handlers.
    /// </summary>
    public static CommandDispatcher CreateDefault() =>
        new(
            WinCare.CommandCatalog.CommandCatalog.Load(),
            [
                new CatalogCommandHandler(),
                new PresetsCommandHandler(),
            ],
            TimeProvider.System);
}

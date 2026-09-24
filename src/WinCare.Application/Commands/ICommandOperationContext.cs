using System.Net.Http;
using WinCare.Application.Activity;
using WinCare.Application.Native;

namespace WinCare.Application.Commands;

/// <summary>
/// The kernel service surface handed to extension-pack command handlers. A pack programs against
/// this context instead of reaching into platform plumbing, so the host can swap an implementation
/// (a different state backend, a sandboxed process runner, an instrumented HTTP client) without any
/// pack change.
/// </summary>
/// <remarks>
/// The dispatcher remains the sole authority for activity journaling: a command is journalled
/// exactly once per dispatch by the kernel, so handlers do not write their own records. <see cref="Journal" />
/// is exposed for packs that need to record side evidence outside a dispatch, and stays
/// <c>null</c> when the host provides no journal.
/// </remarks>
public interface ICommandOperationContext
{
    /// <summary>
    /// Gets the durable, root-bounded state store.
    /// </summary>
    ICommandStateStore State { get; }

    /// <summary>
    /// Gets the shell-free bounded native process runner.
    /// </summary>
    IBoundedProcessRunner Process { get; }

    /// <summary>
    /// Gets the native core interop service, when one is loaded.
    /// </summary>
    INativeCoreService? NativeCore { get; }

    /// <summary>
    /// Gets the shared, connection-pooled HTTP client.
    /// </summary>
    HttpClient HttpClient { get; }

    /// <summary>
    /// Gets the activity journal, when the host publishes one. The dispatcher journals every
    /// dispatch itself; packs should only write records for evidence that lives outside a command
    /// dispatch.
    /// </summary>
    IActivityJournalService? Journal => null;
}

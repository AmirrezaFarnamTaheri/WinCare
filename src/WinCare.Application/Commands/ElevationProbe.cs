namespace WinCare.Application.Commands;

/// <summary>
/// The single authority for the "is this process elevated?" question.
/// </summary>
/// <remarks>
/// <para>
/// WinCare asks this question in more than one place: the dispatcher's administrator-access
/// admission gate, the preset execution digest, and the native executor's per-rule privilege
/// check. When each of those carried its own probe, the same request could get two different
/// answers - a host that injected a deterministic answer for the gate was still refused by
/// the executor's private copy, and a preset's plan digest could disagree with the gate that
/// admitted it. One shared answer removes that disagreement entirely.
/// </para>
/// <para>
/// The value is cached because a running process cannot gain or lose administrator rights:
/// elevation is fixed when the token is created. Caching also avoids re-reading the process
/// token on every command, which is not free on a busy path.
/// </para>
/// </remarks>
public static class ElevationProbe
{
    private static readonly object Gate = new();
    private static bool? _cached;

    /// <summary>
    /// Gets whether the current process runs with administrator privileges.
    /// </summary>
    public static bool IsElevated
    {
        get
        {
            lock (Gate)
            {
                _cached ??= Probe();
                return _cached.Value;
            }
        }
    }

    /// <summary>
    /// Re-reads the process token and returns the fresh answer, then caches it.
    /// </summary>
    /// <remarks>
    /// A process cannot change its own elevation, so a fresh probe and the cached value must
    /// always agree. The refresh exists so that this invariant can be asserted directly
    /// instead of being assumed.
    /// </remarks>
    public static bool Refresh()
    {
        lock (Gate)
        {
            _cached = null;
        }
        return IsElevated;
    }

    /// <summary>
    /// Reads the process token. A token that cannot be read is reported as not elevated:
    /// the probe fails closed, because the only safe reading of "unknown" is "no".
    /// </summary>
    private static bool Probe()
    {
        try
        {
            using System.Security.Principal.WindowsIdentity identity =
                System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }
}

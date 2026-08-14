namespace WinCare.Infrastructure.Native;

/// <summary>
/// Status codes returned by the versioned <c>wincare_core</c> C ABI.
/// </summary>
public enum NativeCoreStatus
{
    /// <summary>Operation succeeded.</summary>
    Ok = 0,
    /// <summary>A null pointer was supplied.</summary>
    NullPointer = 1,
    /// <summary>Path bytes were not valid UTF-8.</summary>
    InvalidUtf8 = 2,
    /// <summary>The input file was not found.</summary>
    NotFound = 3,
    /// <summary>The input exceeded the caller-specified byte limit.</summary>
    FileTooLarge = 4,
    /// <summary>An underlying I/O error occurred.</summary>
    IoError = 5,
    /// <summary>The supplied output buffer was too small.</summary>
    BufferTooSmall = 6,
    /// <summary>Operation completed partially due to limit or unreadable entries.</summary>
    Truncated = 7,
    /// <summary>An unexpected native panic or internal error was caught at the C ABI boundary.</summary>
    InternalError = -99,
}

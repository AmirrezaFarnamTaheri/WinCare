using System.Runtime.InteropServices;

namespace WinCare.Infrastructure.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSysSnapshot
{
    public float CpuUsagePct;
    public ulong RamUsedBytes;
    public ulong RamTotalBytes;
    public ulong DiskFreeBytes;
    public ulong DiskTotalBytes;
    public byte NetActive;
    // Per-metric validity and probed-volume identity from native snapshot.
    public uint ValidMask;
    public uint DiskVolume;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeCleanResult
{
    public ulong BytesReclaimed;
    public uint FilesRemoved;
    public int ErrorCode;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeDirStats
{
    public ulong TotalBytes;
    public ulong FileCount;
    public ulong DirCount;
    public byte IsComplete;
}

/// <summary>Native cleaner call boundary, injectable only for adapter tests.</summary>
internal unsafe delegate int CleanTempFilesNativeCall(byte dryRun, NativeCleanResult* outResult);

internal static class WinCareCoreNative
{
    private const string LibraryName = "wincare_core";

    [DllImport(LibraryName, EntryPoint = "wincare_core_abi_version", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern uint WinCareCoreAbiVersion();

    [DllImport(LibraryName, EntryPoint = "wincare_core_version", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreVersion(byte* buffer, nuint bufferLength, nuint* written);

    [DllImport(LibraryName, EntryPoint = "wincare_core_sha256_file", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreSha256File(
        byte* pathUtf8,
        nuint pathLength,
        ulong maxBytes,
        byte* output,
        nuint outputLength);

    [DllImport(LibraryName, EntryPoint = "wincare_core_dir_size", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreDirSize(
        byte* pathUtf8,
        nuint pathLength,
        ulong* sizeOut);

    [DllImport(LibraryName, EntryPoint = "wincare_core_dir_stats", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreDirStats(
        byte* pathUtf8,
        nuint pathLength,
        NativeDirStats* statsOut);

    [DllImport(LibraryName, EntryPoint = "wincare_secure_shred_file", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareSecureShredFile(
        byte* pathUtf8,
        nuint pathLength,
        uint passes);

    [DllImport(LibraryName, EntryPoint = "wincare_core_sys_info", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreSysInfo(
        byte* buffer,
        nuint bufferLength,
        nuint* written);

    [DllImport(LibraryName, EntryPoint = "wincare_sys_snapshot_all", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareSysSnapshotAll(NativeSysSnapshot* outSnapshot);

    [DllImport(LibraryName, EntryPoint = "wincare_clean_temp_files", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCleanTempFiles(byte dryRun, NativeCleanResult* outResult);

    [DllImport(LibraryName, EntryPoint = "wincare_core_volume_seek_penalty", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreVolumeSeekPenalty(byte driveLetter, byte* incursSeekPenalty);

    [DllImport(LibraryName, EntryPoint = "wincare_core_optimize_memory_lists", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreOptimizeMemoryLists(uint mask, ulong* freedBytes);

    [DllImport(LibraryName, EntryPoint = "wincare_core_shell_notify", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int WinCareCoreShellNotify();

    [DllImport(LibraryName, EntryPoint = "wincare_core_is_window_cloaked", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreIsWindowCloaked(nint hwnd, uint* isCloaked);

    [DllImport(LibraryName, EntryPoint = "wincare_core_estimate_model_vram_fit", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreEstimateModelVramFit(
        ulong parameterCount,
        uint quantizationBits,
        uint contextLength,
        uint layerCount,
        ulong availableVramBytes,
        ulong availableSystemRamBytes,
        uint* outFitClassification,
        ulong* outTotalRequiredBytes);

    [DllImport(LibraryName, EntryPoint = "wincare_core_parse_hls_playlist_info", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreParseHlsPlaylistInfo(
        byte* bytes,
        nuint len,
        uint* outIsMaster,
        uint* outSegmentCount,
        double* outTargetDuration,
        ulong* outMaxBandwidth);

    [DllImport(LibraryName, EntryPoint = "wincare_core_calculate_box_gravity_anchor", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreCalculateBoxGravityAnchor(
        int rectX,
        int rectY,
        int oldW,
        int oldH,
        int newW,
        int newH,
        uint gravity,
        int* outX,
        int* outY);

    [DllImport(LibraryName, EntryPoint = "wincare_core_calculate_ratio_layout_split", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreCalculateRatioLayoutSplit(
        int availLeft,
        int availTop,
        int availWidth,
        int availHeight,
        double ratio,
        int spacing,
        int* outPrimary,
        int* outRemainder,
        uint* outIsVerticalSplit);

    [DllImport(LibraryName, EntryPoint = "wincare_core_calculate_edge_snap", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreCalculateEdgeSnap(
        int winX,
        int winY,
        int winW,
        int winH,
        int screenX,
        int screenY,
        int screenW,
        int screenH,
        int snapThreshold,
        int* outX,
        int* outY,
        uint* outSnapFlags);

    [DllImport(LibraryName, EntryPoint = "wincare_core_vector_cosine_similarity", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreVectorCosineSimilarity(
        float* vecA,
        float* vecB,
        nuint len,
        float* outSimilarity);

    [DllImport(LibraryName, EntryPoint = "wincare_core_piece_map_popcount", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCorePieceMapPopcount(
        byte* data,
        nuint len,
        ulong* outCount);

    [DllImport(LibraryName, EntryPoint = "wincare_core_calculate_smart_overlap", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreCalculateSmartOverlap(
        int candX,
        int candY,
        int candW,
        int candH,
        int* rects,
        nuint count,
        ulong* outOverlap);

    [DllImport(LibraryName, EntryPoint = "wincare_core_decay_affinity_score", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreDecayAffinityScore(
        double currentScore,
        ulong elapsedMs,
        ulong halfLifeMs,
        double* outScore);

    [DllImport(LibraryName, EntryPoint = "wincare_core_evaluate_rate_limit_tokens", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreEvaluateRateLimitTokens(
        ulong nowNanos,
        ulong allocatedUntil,
        ulong bytesPerSecond,
        ulong byteCount,
        ulong waitByteCount,
        ulong maxBurstBytes,
        ulong* outTakeBytes,
        ulong* outWaitNanos,
        ulong* outNewAllocatedUntil);

    [DllImport(LibraryName, EntryPoint = "wincare_core_unlink_posix", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreUnlinkPosix(
        byte* pathUtf8,
        nuint pathLength);

    [DllImport(LibraryName, EntryPoint = "wincare_core_secure_trash_memory", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreSecureTrashMemory(
        byte* buffer,
        nuint length);

    [DllImport(LibraryName, EntryPoint = "wincare_core_burn_stack", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreBurnStack(
        nuint length);

    [DllImport(LibraryName, EntryPoint = "wincare_core_boyer_moore_search", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreBoyerMooreSearch(
        byte* data,
        nuint dataLen,
        byte* pattern,
        nuint patternLen,
        long* outIndex);

    [DllImport(LibraryName, EntryPoint = "wincare_core_crc16_ccitt", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreCrc16Ccitt(
        byte* data,
        nuint dataLen,
        ushort initialCrc,
        ushort* outCrc);

    [DllImport(LibraryName, EntryPoint = "wincare_core_calculate_relative_window_delta", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreCalculateRelativeWindowDelta(
        int startX,
        int startY,
        int currX,
        int currY,
        int winX,
        int winY,
        int winW,
        int winH,
        uint mode,
        int* outRect);

    [DllImport(LibraryName, EntryPoint = "wincare_core_calculate_entropy", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreCalculateEntropy(
        byte* data,
        nuint dataLen,
        double* outEntropy);

    [DllImport(LibraryName, EntryPoint = "wincare_core_query_file_identity", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreQueryFileIdentity(
        byte* pathUtf8,
        nuint pathLength,
        ulong* outVolumeSerial,
        ulong* outFileIdHigh,
        ulong* outFileIdLow);
}

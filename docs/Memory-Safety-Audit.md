# wincare-core Memory Safety & FFI Invariant Audit

This document records the formal safety audit and invariants for all `unsafe` operations and exported C-ABI functions in `native/wincare-core/src/lib.rs`, fulfilling the safety requirement documented in `OVERHAUL_REPORT.md` §3 and `docs/Kinetic-Mission-Control-Spec.md` §2.

## Architectural Boundaries & Principles

1. **C-ABI Exception Confinement (`std::panic::catch_unwind`)**:
   Every exported C-ABI function (`pub unsafe extern "C" fn wincare_core_*`) must wrap all execution inside `std::panic::catch_unwind`. Rust panics must never cross the FFI boundary into the calling .NET host process. Panics are intercepted and mapped to `Status::InternalError.code()` (`-1`).

2. **Pointer Validity & Null Guards**:
   Raw input pointers (`*const u8`, `*const c_char`, `*mut u8`, `*mut c_void`) are validated for null checks before dereferencing. If a null pointer is encountered, the function immediately returns an error status without reading or writing memory.

3. **Buffer Boundaries & Slice Conversion (`std::slice::from_raw_parts`)**:
   Slice construction from raw pointers requires explicit caller length bounds. When converting strings, bounds are verified or UTF-8 validity is enforced using `CStr::from_ptr` or `std::str::from_utf8`.

4. **Win32 API Interaction & RAII Handles**:
   Win32 handles obtained via Win32 APIs (e.g., `OpenProcess`, `CreateFileW`, `FindFirstFileW`, `FindNextFileW`) must be released via appropriate close functions (`CloseHandle`, `FindClose`) or wrapped in RAII guard types.

## Audited Subsystems

### 1. Directory Traversal & Storage Size (`accumulate_dir_size`, `wincare_core_dir_size`)
- **Safety Invariant**: Directory traversal avoids following symlinks or reparse points (`is_reparse_point` check) to prevent directory loop attacks or traversing unintended mount volumes.
- **Iteration Ceiling**: Maximum traversal count is bounded by `MAX_DIR_ENTRIES = 500_000` to prevent denial-of-service or unbounded memory consumption during directory scanning.
- **Buffer Safety**: Path strings passed from C# P/Invoke are checked for valid UTF-8 and null-terminated strings.

### 2. Working Set & Memory Compaction (`wincare_core_empty_working_set`)
- **Safety Invariant**: Process handles are opened with `PROCESS_SET_QUOTA | PROCESS_QUERY_INFORMATION` rights. If handle creation fails or is denied due to privilege boundaries, the error is caught safely without leaking handles.
- **PSAPI Call**: `K32EmptyWorkingSet` is invoked with a verified, non-null handle. The handle is unconditionally closed via `CloseHandle` in a cleanup block.

### 3. Native Telemetry & System Probes (`wincare_core_system_information`)
- **Safety Invariant**: Win32 `GetSystemInfo` and `GlobalMemoryStatusEx` write to pre-allocated Rust structs whose memory layout and alignment strictly match Win32 API requirements (`cbSize` initialized).
- **Output Pointers**: Output buffer pointers provided by the host are verified for non-null and size adequacy before copying telemetry data.

### 4. Shannon Entropy & Stream Processing (`wincare_core_calculate_entropy`)
- **Safety Invariant**: Byte buffers passed for entropy calculation are verified to point to valid, initialized memory of the specified length.
- **Mathematical Safety**: Floating-point operations use finite checks (`f64::is_finite`) and clamp probabilities to prevent division by zero or NaN propagation.

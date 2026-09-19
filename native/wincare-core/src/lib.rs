//! Bounded native primitives for WinCare.
//!
//! This crate never owns product policy. The C ABI exposes deterministic,
//! resource-bounded operations that return status codes instead of panicking.

use sha2::{Digest, Sha256};
use std::fs::File;
use std::io::{self, Read};
use std::path::Path;
use std::slice;
use std::str;

pub mod cleaner;
pub mod telemetry;

pub use cleaner::{NativeCleanResult, secure_shred_file};
pub use telemetry::NativeSysSnapshot;

/// Aggregate directory statistics computed via bounded native traversal.
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub struct NativeDirStats {
    /// Cumulative size of all regular files in bytes.
    pub total_bytes: u64,
    /// Total count of regular files traversed.
    pub file_count: u64,
    /// Total count of subdirectories traversed.
    pub dir_count: u64,
    /// Non-zero if enumeration finished completely within bounds; zero if truncated.
    pub is_complete: u8,
}

#[cfg(target_os = "windows")]
#[allow(non_camel_case_types, non_snake_case, clippy::upper_case_acronyms)]
mod win32 {
    #[repr(C)]
    #[derive(Copy, Clone)]
    pub struct SYSTEM_INFO {
        pub wProcessorArchitecture: u16,
        pub wReserved: u16,
        pub dwPageSize: u32,
        pub lpMinimumApplicationAddress: *mut std::ffi::c_void,
        pub lpMaximumApplicationAddress: *mut std::ffi::c_void,
        pub dwActiveProcessorMask: usize,
        pub dwNumberOfProcessors: u32,
        pub dwProcessorType: u32,
        pub dwAllocationGranularity: u32,
        pub wProcessorLevel: u16,
        pub wProcessorRevision: u16,
    }

    #[repr(C)]
    #[derive(Copy, Clone)]
    pub struct MEMORYSTATUSEX {
        pub dwLength: u32,
        pub dwMemoryLoad: u32,
        pub ullTotalPhys: u64,
        pub ullAvailPhys: u64,
        pub ullTotalPageFile: u64,
        pub ullAvailPageFile: u64,
        pub ullTotalVirtual: u64,
        pub ullAvailVirtual: u64,
        pub ullAvailExtendedVirtual: u64,
    }

    pub type HKEY = *mut std::ffi::c_void;
    pub const HKEY_LOCAL_MACHINE: HKEY = 0x80000002_u64 as HKEY;
    pub const KEY_READ: u32 = 0x20019;
    pub const REG_SZ: u32 = 1;

    pub const IOCTL_STORAGE_QUERY_PROPERTY: u32 = 0x002D_1400;
    pub const STORAGE_DEVICE_SEEK_PENALTY_PROPERTY: u32 = 7;
    pub const PROPERTY_STANDARD_QUERY: u32 = 0;
    pub const FILE_SHARE_READ: u32 = 1;
    pub const FILE_SHARE_WRITE: u32 = 2;
    pub const OPEN_EXISTING: u32 = 3;
    pub const INVALID_HANDLE_VALUE: *mut std::ffi::c_void = -1_isize as *mut std::ffi::c_void;

    #[repr(C)]
    pub struct STORAGE_PROPERTY_QUERY {
        pub PropertyId: u32,
        pub QueryType: u32,
        pub AdditionalParameters: [u8; 1],
    }

    #[repr(C)]
    pub struct DEVICE_SEEK_PENALTY_DESCRIPTOR {
        pub Version: u32,
        pub Size: u32,
        pub IncursSeekPenalty: u8,
    }

    pub const SYSTEM_MEMORY_LIST_INFORMATION: u32 = 80;
    pub const SYSTEM_COMBINE_PHYSICAL_MEMORY_INFORMATION: u32 = 130;

    pub const MEMORY_EMPTY_WORKING_SETS: u32 = 2;
    pub const MEMORY_FLUSH_MODIFIED_LIST: u32 = 3;
    pub const MEMORY_PURGE_STANDBY_LIST: u32 = 4;
    pub const MEMORY_PURGE_LOW_PRIORITY_STANDBY_LIST: u32 = 5;

    #[repr(C)]
    pub struct MEMORY_COMBINE_INFORMATION_EX {
        pub Handle: usize,
        pub PagesCombined: usize,
        pub Flags: u32,
    }

    #[link(name = "kernel32")]
    // SAFETY: These Win32 C-ABI extern declarations match the official Windows SDK signatures exactly. Callers must uphold each function's documented argument constraints.
    unsafe extern "system" {
        pub fn GetSystemInfo(lpSystemInfo: *mut SYSTEM_INFO);
        pub fn GlobalMemoryStatusEx(lpBuffer: *mut MEMORYSTATUSEX) -> i32;
        pub fn CreateFileW(
            lpFileName: *const u16,
            dwDesiredAccess: u32,
            dwShareMode: u32,
            lpSecurityAttributes: *mut std::ffi::c_void,
            dwCreationDisposition: u32,
            dwFlagsAndAttributes: u32,
            hTemplateFile: *mut std::ffi::c_void,
        ) -> *mut std::ffi::c_void;
        pub fn DeviceIoControl(
            hDevice: *mut std::ffi::c_void,
            dwIoControlCode: u32,
            lpInBuffer: *const std::ffi::c_void,
            nInBufferSize: u32,
            lpOutBuffer: *mut std::ffi::c_void,
            nOutBufferSize: u32,
            lpBytesReturned: *mut u32,
            lpOverlapped: *mut std::ffi::c_void,
        ) -> i32;
        pub fn CloseHandle(hObject: *mut std::ffi::c_void) -> i32;
    }

    #[link(name = "ntdll")]
    unsafe extern "system" {
        pub fn NtSetSystemInformation(
            system_information_class: u32,
            system_information: *const std::ffi::c_void,
            system_information_length: u32,
        ) -> i32;
    }

    #[link(name = "advapi32")]
    unsafe extern "system" {
        pub fn RegOpenKeyExW(
            hKey: HKEY,
            lpSubKey: *const u16,
            ulOptions: u32,
            samDesired: u32,
            phkResult: *mut HKEY,
        ) -> i32;

        pub fn RegQueryValueExW(
            hKey: HKEY,
            lpValueName: *const u16,
            lpReserved: *mut u32,
            lpType: *mut u32,
            lpData: *mut u8,
            lpcbData: *mut u32,
        ) -> i32;

        pub fn RegCloseKey(hKey: HKEY) -> i32;
    }

    pub const SYSTEM_FILE_CACHE_INFORMATION: u32 = 21;

    #[repr(C)]
    #[derive(Copy, Clone)]
    pub struct SYSTEM_FILECACHE_INFORMATION {
        pub CurrentSize: usize,
        pub PeakSize: usize,
        pub PageFaultCount: u32,
        pub MinimumWorkingSet: usize,
        pub MaximumWorkingSet: usize,
        pub CurrentSizeIncludingTransitionInPages: usize,
        pub PeakSizeIncludingTransitionInPages: usize,
        pub TransitionRePurposeCount: u32,
        pub Flags: u32,
    }

    pub const SHCNE_ASSOCCHANGED: i32 = 0x0800_0000;
    pub const SHCNF_IDLIST: u32 = 0;

    #[link(name = "shell32")]
    unsafe extern "system" {
        pub fn SHChangeNotify(
            wEventId: i32,
            uFlags: u32,
            dwItem1: *const std::ffi::c_void,
            dwItem2: *const std::ffi::c_void,
        );
    }

    pub const DWMWA_CLOAKED: u32 = 14;

    #[link(name = "dwmapi")]
    unsafe extern "system" {
        pub fn DwmGetWindowAttribute(
            hwnd: isize,
            dwAttribute: u32,
            pvAttribute: *mut std::ffi::c_void,
            cbAttribute: u32,
        ) -> i32;
    }
}

const ABI_VERSION: u32 = 1;
const VERSION: &[u8] = b"3.0.0";
const SHA256_LENGTH: usize = 32;
const READ_BUFFER_LENGTH: usize = 64 * 1024;

#[repr(i32)]
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum Status {
    Ok = 0,
    NullPointer = 1,
    InvalidUtf8 = 2,
    NotFound = 3,
    FileTooLarge = 4,
    IoError = 5,
    BufferTooSmall = 6,
    Truncated = 7,
    InvalidArgument = 8,
    InternalError = -99,
}

impl Status {
    const fn code(self) -> i32 {
        self as i32
    }
}

/// Returns the version of the exported ABI.
#[unsafe(no_mangle)]
pub extern "C" fn wincare_core_abi_version() -> u32 {
    std::panic::catch_unwind(|| ABI_VERSION).unwrap_or(0)
}

/// Copies the UTF-8 library version into the caller-provided buffer.
///
/// # Safety
///
/// `written` must point to writable memory. When `buffer_len` is non-zero,
/// `buffer` must point to at least `buffer_len` writable bytes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_version(
    buffer: *mut u8,
    buffer_len: usize,
    written: *mut usize,
) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> i32 {
        if written.is_null() {
            return Status::NullPointer.code();
        }

        // SAFETY: The caller contract requires a valid writable `written` pointer.
        unsafe { written.write(VERSION.len()) };

        if buffer_len < VERSION.len() {
            return Status::BufferTooSmall.code();
        }
        if buffer.is_null() {
            return Status::NullPointer.code();
        }

        // SAFETY: The checks above establish that `buffer` is non-null and the
        // caller contract provides at least `buffer_len` writable bytes.
        let destination = unsafe { slice::from_raw_parts_mut(buffer, buffer_len) };
        destination[..VERSION.len()].copy_from_slice(VERSION);
        Status::Ok.code()
    }))
    .unwrap_or(Status::InternalError.code())
}

/// Hashes a file with SHA-256 while enforcing an explicit maximum byte count.
///
/// # Safety
///
/// `path_utf8` must point to `path_len` readable bytes. `output` must point to
/// at least 32 writable bytes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_sha256_file(
    path_utf8: *const u8,
    path_len: usize,
    max_bytes: u64,
    output: *mut u8,
    output_len: usize,
) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> i32 {
        if path_utf8.is_null() || output.is_null() {
            return Status::NullPointer.code();
        }
        if output_len < SHA256_LENGTH {
            return Status::BufferTooSmall.code();
        }

        // SAFETY: The caller contract provides `path_len` readable bytes.
        let path_bytes = unsafe { slice::from_raw_parts(path_utf8, path_len) };
        let Ok(path_text) = str::from_utf8(path_bytes) else {
            return Status::InvalidUtf8.code();
        };

        let digest = match sha256_file(Path::new(path_text), max_bytes) {
            Ok(value) => value,
            Err(HashError::NotFound) => return Status::NotFound.code(),
            Err(HashError::FileTooLarge) => return Status::FileTooLarge.code(),
            Err(HashError::Io) => return Status::IoError.code(),
        };

        // SAFETY: The checks above establish a non-null output with at least 32 bytes.
        let output_slice = unsafe { slice::from_raw_parts_mut(output, output_len) };
        output_slice[..SHA256_LENGTH].copy_from_slice(&digest);
        Status::Ok.code()
    }))
    .unwrap_or(Status::InternalError.code())
}

#[derive(Debug)]
enum HashError {
    NotFound,
    FileTooLarge,
    Io,
}

fn sha256_file(path: &Path, max_bytes: u64) -> Result<[u8; SHA256_LENGTH], HashError> {
    let metadata = std::fs::metadata(path).map_err(map_io_error)?;
    if metadata.len() > max_bytes {
        return Err(HashError::FileTooLarge);
    }

    let mut file = File::open(path).map_err(map_io_error)?;
    let mut hasher = Sha256::new();
    let mut buffer = [0_u8; READ_BUFFER_LENGTH];
    let mut total = 0_u64;

    loop {
        let read = file.read(&mut buffer).map_err(|_| HashError::Io)?;
        if read == 0 {
            break;
        }

        total = total
            .checked_add(u64::try_from(read).map_err(|_| HashError::Io)?)
            .ok_or(HashError::FileTooLarge)?;
        if total > max_bytes {
            return Err(HashError::FileTooLarge);
        }
        hasher.update(&buffer[..read]);
    }

    Ok(hasher.finalize().into())
}

fn map_io_error(error: io::Error) -> HashError {
    if error.kind() == io::ErrorKind::NotFound {
        HashError::NotFound
    } else {
        HashError::Io
    }
}

/// Accumulates the total byte size of all regular files under `path_utf8`.
/// Writes the byte count to `*size_out` on success.
///
/// # Safety
/// `path_utf8` must point to `path_len` readable bytes.
/// `size_out` must point to a single writable `u64`.
/// Caller retains ownership of both pointers; this function does not free them.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_dir_size(
    path_utf8: *const u8,
    path_len: usize,
    size_out: *mut u64,
) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> i32 {
        if path_utf8.is_null() || size_out.is_null() {
            return Status::NullPointer.code();
        }
        // SAFETY: path_utf8 is non-null (checked above); caller guarantees path_len valid readable bytes.
        let path_bytes = unsafe { slice::from_raw_parts(path_utf8, path_len) };
        let path_str = match str::from_utf8(path_bytes) {
            Ok(s) => s,
            Err(_) => return Status::InvalidUtf8.code(),
        };
        let path = Path::new(path_str);
        if !path.exists() {
            return Status::NotFound.code();
        }
        let (bytes, complete) = match accumulate_dir_size(path) {
            Ok(res) => res,
            Err(_) => return Status::IoError.code(),
        };
        // SAFETY: size_out is non-null (checked above); caller guarantees valid writable u64 memory.
        unsafe { size_out.write(bytes) };
        if complete {
            Status::Ok.code()
        } else {
            Status::Truncated.code()
        }
    }))
    .unwrap_or(Status::InternalError.code())
}

const MAX_DIR_ENTRIES: usize = 500_000;

fn accumulate_dir_size(path: &Path) -> io::Result<(u64, bool)> {
    let mut total = 0_u64;
    // Entry budget bounds queued plus processed work to prevent unbounded memory growth.
    let mut admitted = 0_usize;
    let mut pending = vec![path.to_path_buf()];
    let mut complete = true;

    while let Some(current) = pending.pop() {
        admitted += 1;
        if admitted > MAX_DIR_ENTRIES {
            complete = false;
            break;
        }

        let metadata = match std::fs::symlink_metadata(&current) {
            Ok(meta) => meta,
            Err(_) => {
                complete = false;
                continue;
            }
        };

        if metadata.file_type().is_symlink() || is_reparse_point(&metadata) {
            continue;
        }

        if metadata.is_dir() {
            match std::fs::read_dir(&current) {
                Ok(entries) => {
                    for entry_res in entries {
                        // Stop admitting work when the combined queue is at capacity.
                        if admitted + pending.len() >= MAX_DIR_ENTRIES {
                            complete = false;
                            break;
                        }
                        match entry_res {
                            Ok(entry) => pending.push(entry.path()),
                            Err(_) => complete = false,
                        }
                    }
                }
                Err(_) => {
                    complete = false;
                }
            }
        } else if metadata.is_file() {
            total = total.saturating_add(metadata.len());
        }
    }

    Ok((total, complete))
}

fn accumulate_dir_stats(path: &Path) -> io::Result<NativeDirStats> {
    let mut total_bytes = 0_u64;
    let mut file_count = 0_u64;
    let mut dir_count = 0_u64;
    let mut admitted = 0_usize;
    let mut pending = vec![path.to_path_buf()];
    let mut complete = true;

    while let Some(current) = pending.pop() {
        admitted += 1;
        if admitted > MAX_DIR_ENTRIES {
            complete = false;
            break;
        }

        let metadata = match std::fs::symlink_metadata(&current) {
            Ok(meta) => meta,
            Err(_) => {
                complete = false;
                continue;
            }
        };

        if metadata.file_type().is_symlink() || is_reparse_point(&metadata) {
            continue;
        }

        if metadata.is_dir() {
            if current != path {
                dir_count = dir_count.saturating_add(1);
            }
            match std::fs::read_dir(&current) {
                Ok(entries) => {
                    for entry_res in entries {
                        if admitted + pending.len() >= MAX_DIR_ENTRIES {
                            complete = false;
                            break;
                        }
                        match entry_res {
                            Ok(entry) => pending.push(entry.path()),
                            Err(_) => complete = false,
                        }
                    }
                }
                Err(_) => {
                    complete = false;
                }
            }
        } else if metadata.is_file() {
            file_count = file_count.saturating_add(1);
            total_bytes = total_bytes.saturating_add(metadata.len());
        }
    }

    Ok(NativeDirStats {
        total_bytes,
        file_count,
        dir_count,
        is_complete: if complete { 1 } else { 0 },
    })
}

#[cfg(target_os = "windows")]
fn is_reparse_point(metadata: &std::fs::Metadata) -> bool {
    use std::os::windows::fs::MetadataExt;
    (metadata.file_attributes() & 0x400) != 0
}

#[cfg(not(target_os = "windows"))]
fn is_reparse_point(_metadata: &std::fs::Metadata) -> bool {
    false
}

/// Writes a UTF-8 JSON object with system facts into `buffer`.
///
/// JSON shape (all fields always present):
/// `{"logical_cpus":N,"total_physical_memory_bytes":N,"available_physical_memory_bytes":N,"os_build":"..."}`
///
/// # Safety
/// `written` must point to a writable `usize`.
/// When `buffer_len > 0`, `buffer` must point to `>= buffer_len` writable bytes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_sys_info(
    buffer: *mut u8,
    buffer_len: usize,
    written: *mut usize,
) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> i32 {
        if written.is_null() {
            return Status::NullPointer.code();
        }
        let mut stack_buf = [0u8; 512];
        let json_bytes = match compose_sys_info_json(&mut stack_buf) {
            Some(bytes) => bytes,
            None => return Status::IoError.code(),
        };

        // SAFETY: written is non-null (checked above); caller guarantees valid writable usize.
        unsafe { written.write(json_bytes.len()) };

        if buffer.is_null() || buffer_len < json_bytes.len() {
            return Status::BufferTooSmall.code();
        }
        // SAFETY: buffer is non-null; buffer_len >= json_bytes.len() (checked above); source is stack slice, dest is caller heap — no overlap.
        unsafe { std::ptr::copy_nonoverlapping(json_bytes.as_ptr(), buffer, json_bytes.len()) };
        Status::Ok.code()
    }))
    .unwrap_or(Status::InternalError.code())
}

fn compose_sys_info_json(buf: &mut [u8; 512]) -> Option<&[u8]> {
    #[cfg(target_os = "windows")]
    {
        use self::win32::*;
        use std::io::Write;

        // SAFETY: SYSTEM_INFO is a POD type with no validity invariants; zeroed init is sound. GetSystemInfo writes the struct per Win32 contract.
        let mut si = unsafe { std::mem::zeroed::<SYSTEM_INFO>() };
        unsafe { GetSystemInfo(&mut si) };
        let logical_cpus = si.dwNumberOfProcessors;

        // SAFETY: MEMORYSTATUSEX is POD; zeroed then dwLength-initialized before use per GlobalMemoryStatusEx calling contract.
        let mut ms = unsafe { std::mem::zeroed::<MEMORYSTATUSEX>() };
        ms.dwLength = std::mem::size_of::<MEMORYSTATUSEX>() as u32;
        if unsafe { GlobalMemoryStatusEx(&mut ms) } == 0 {
            return None;
        }

        let mut os_build_buf = [0u8; 32];
        let os_build = read_registry_os_build_bytes(&mut os_build_buf).unwrap_or("unknown");

        let mut cursor = std::io::Cursor::new(&mut buf[..]);
        if write!(
            cursor,
            r#"{{"logical_cpus":{logical_cpus},"total_physical_memory_bytes":{total},"available_physical_memory_bytes":{avail},"os_build":"{os_build}"}}"#,
            logical_cpus = logical_cpus,
            total = ms.ullTotalPhys,
            avail = ms.ullAvailPhys,
            os_build = os_build,
        )
        .is_ok()
        {
            let len = cursor.position() as usize;
            Some(&buf[..len])
        } else {
            None
        }
    }
    #[cfg(not(target_os = "windows"))]
    {
        let json = r#"{"logical_cpus":1,"total_physical_memory_bytes":0,"available_physical_memory_bytes":0,"os_build":"non-windows"}"#;
        let len = json.len().min(buf.len());
        buf[..len].copy_from_slice(&json.as_bytes()[..len]);
        Some(&buf[..len])
    }
}

#[cfg(target_os = "windows")]
fn read_registry_os_build_bytes(out_buf: &mut [u8; 32]) -> Option<&str> {
    use self::win32::*;

    // Wide string null-terminated constants to avoid dynamic heap Vec allocations.
    const SUBKEY: &[u16] = &[
        b'S' as u16,
        b'O' as u16,
        b'F' as u16,
        b'T' as u16,
        b'W' as u16,
        b'A' as u16,
        b'R' as u16,
        b'E' as u16,
        b'\\' as u16,
        b'M' as u16,
        b'i' as u16,
        b'c' as u16,
        b'r' as u16,
        b'o' as u16,
        b's' as u16,
        b'o' as u16,
        b'f' as u16,
        b't' as u16,
        b'\\' as u16,
        b'W' as u16,
        b'i' as u16,
        b'n' as u16,
        b'd' as u16,
        b'o' as u16,
        b'w' as u16,
        b's' as u16,
        b' ' as u16,
        b'N' as u16,
        b'T' as u16,
        b'\\' as u16,
        b'C' as u16,
        b'u' as u16,
        b'r' as u16,
        b'r' as u16,
        b'e' as u16,
        b'n' as u16,
        b't' as u16,
        b'V' as u16,
        b'e' as u16,
        b'r' as u16,
        b's' as u16,
        b'i' as u16,
        b'o' as u16,
        b'n' as u16,
        0,
    ];
    const VALUE_NAME: &[u16] = &[
        b'C' as u16,
        b'u' as u16,
        b'r' as u16,
        b'r' as u16,
        b'e' as u16,
        b'n' as u16,
        b't' as u16,
        b'B' as u16,
        b'u' as u16,
        b'i' as u16,
        b'l' as u16,
        b'd' as u16,
        b'N' as u16,
        b'u' as u16,
        b'm' as u16,
        b'b' as u16,
        b'e' as u16,
        b'r' as u16,
        0,
    ];

    // SAFETY: Win32 registry APIs are called with valid null-terminated UTF-16 string pointers and proper buffer lengths. HKEYs are managed correctly.
    unsafe {
        let mut hkey: HKEY = std::ptr::null_mut();
        if RegOpenKeyExW(HKEY_LOCAL_MACHINE, SUBKEY.as_ptr(), 0, KEY_READ, &mut hkey) != 0 {
            return None;
        }

        let mut buf = [0u16; 64];
        let mut buf_len = (buf.len() * std::mem::size_of::<u16>()) as u32;
        let mut value_type = REG_SZ;

        let status = RegQueryValueExW(
            hkey,
            VALUE_NAME.as_ptr(),
            std::ptr::null_mut(),
            &mut value_type,
            buf.as_mut_ptr() as *mut u8,
            &mut buf_len,
        );

        let _ = RegCloseKey(hkey);

        if status == 0 && value_type == REG_SZ && buf_len >= 2 && buf_len % 2 == 0 {
            let u16_count = (buf_len as usize / 2).saturating_sub(1);
            let mut out_len = 0;
            for &unit in &buf[..u16_count] {
                if unit < 128 && out_len < out_buf.len() {
                    out_buf[out_len] = unit as u8;
                    out_len += 1;
                } else {
                    return None;
                }
            }
            return std::str::from_utf8(&out_buf[..out_len]).ok();
        }
        None
    }
}

/// Queries instantaneous system telemetry snapshot across CPU, RAM, disk, and network.
///
/// # Safety
///
/// `out_snapshot` must point to a valid, properly aligned, writable `NativeSysSnapshot`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_sys_snapshot_all(out_snapshot: *mut NativeSysSnapshot) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
        // SAFETY: Delegated to telemetry::query_sys_snapshot with matching safety contract.
        unsafe { telemetry::query_sys_snapshot(out_snapshot) }
    }))
    .unwrap_or(Status::InternalError.code())
}

/// Safely cleans temporary files or performs dry-run inspection without disk modification.
///
/// # Safety
///
/// `out_result` must point to a valid, properly aligned, writable `NativeCleanResult`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_clean_temp_files(
    dry_run: u8,
    out_result: *mut NativeCleanResult,
) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
        // SAFETY: Delegated to cleaner::clean_temp_files_internal with matching safety contract.
        unsafe { cleaner::clean_temp_files_internal(dry_run, out_result) }
    }))
    .unwrap_or(Status::InternalError.code())
}

/// Securely overwrites a file with multi-pass patterns before deleting it.
///
/// # Safety
///
/// `path_utf8` must point to `path_len` readable bytes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_secure_shred_file(
    path_utf8: *const u8,
    path_len: usize,
    passes: u32,
) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> i32 {
        if path_utf8.is_null() {
            return Status::NullPointer.code();
        }
        let path_bytes = unsafe { slice::from_raw_parts(path_utf8, path_len) };
        let Ok(path_text) = str::from_utf8(path_bytes) else {
            return Status::InvalidUtf8.code();
        };
        match cleaner::secure_shred_file(Path::new(path_text), passes) {
            Ok(()) => Status::Ok.code(),
            Err(e) if e.kind() == io::ErrorKind::NotFound => Status::NotFound.code(),
            Err(_) => Status::IoError.code(),
        }
    }))
    .unwrap_or(Status::InternalError.code())
}

/// Computes cumulative directory statistics (bytes, file count, directory count).
///
/// # Safety
///
/// `path_utf8` must point to `path_len` readable bytes.
/// `stats_out` must point to a valid, writable `NativeDirStats` struct.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_dir_stats(
    path_utf8: *const u8,
    path_len: usize,
    stats_out: *mut NativeDirStats,
) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> i32 {
        if path_utf8.is_null() || stats_out.is_null() {
            return Status::NullPointer.code();
        }
        let path_bytes = unsafe { slice::from_raw_parts(path_utf8, path_len) };
        let Ok(path_str) = str::from_utf8(path_bytes) else {
            return Status::InvalidUtf8.code();
        };
        let path = Path::new(path_str);
        if !path.exists() {
            return Status::NotFound.code();
        }
        let stats = match accumulate_dir_stats(path) {
            Ok(s) => s,
            Err(_) => return Status::IoError.code(),
        };
        unsafe { stats_out.write(stats) };
        if stats.is_complete != 0 {
            Status::Ok.code()
        } else {
            Status::Truncated.code()
        }
    }))
    .unwrap_or(Status::InternalError.code())
}

/// Queries whether the physical volume underlying `drive_letter` incurs a seek penalty (rotational HDD = 1, SSD/NVMe = 0).
/// `drive_letter` is ASCII (e.g. b'C' or b'c').
///
/// # Safety
///
/// `incurs_seek_penalty` must point to a valid, writable `u8`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_volume_seek_penalty(
    drive_letter: u8,
    incurs_seek_penalty: *mut u8,
) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> i32 {
        if incurs_seek_penalty.is_null() {
            return Status::NullPointer.code();
        }
        if !drive_letter.is_ascii_alphabetic() {
            return Status::NotFound.code();
        }

        #[cfg(target_os = "windows")]
        {
            use self::win32::*;
            let upper = drive_letter.to_ascii_uppercase();
            let device_name: [u16; 7] = [
                '\\' as u16,
                '\\' as u16,
                '.' as u16,
                '\\' as u16,
                upper as u16,
                ':' as u16,
                0,
            ];

            // SAFETY: device_name is a valid null-terminated UTF-16 string; 0 desired access requests device metadata only.
            let handle = unsafe {
                CreateFileW(
                    device_name.as_ptr(),
                    0,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    std::ptr::null_mut(),
                    OPEN_EXISTING,
                    0,
                    std::ptr::null_mut(),
                )
            };

            if handle == INVALID_HANDLE_VALUE {
                // Return 0 if handle cannot be opened (e.g., restricted access or virtual drive).
                // SAFETY: incurs_seek_penalty is verified non-null above.
                unsafe { incurs_seek_penalty.write(0) };
                return Status::Ok.code();
            }

            let query = STORAGE_PROPERTY_QUERY {
                PropertyId: STORAGE_DEVICE_SEEK_PENALTY_PROPERTY,
                QueryType: PROPERTY_STANDARD_QUERY,
                AdditionalParameters: [0],
            };
            let mut descriptor = std::mem::MaybeUninit::<DEVICE_SEEK_PENALTY_DESCRIPTOR>::uninit();
            let mut returned = 0u32;

            // SAFETY: handle is valid; query and descriptor pointers and sizes match Win32 storage query contracts.
            let success = unsafe {
                DeviceIoControl(
                    handle,
                    IOCTL_STORAGE_QUERY_PROPERTY,
                    &query as *const _ as *const std::ffi::c_void,
                    std::mem::size_of::<STORAGE_PROPERTY_QUERY>() as u32,
                    descriptor.as_mut_ptr() as *mut std::ffi::c_void,
                    std::mem::size_of::<DEVICE_SEEK_PENALTY_DESCRIPTOR>() as u32,
                    &mut returned,
                    std::ptr::null_mut(),
                )
            };

            // SAFETY: handle is closed immediately after query.
            unsafe { CloseHandle(handle) };

            if success != 0
                && returned >= std::mem::size_of::<DEVICE_SEEK_PENALTY_DESCRIPTOR>() as u32
            {
                // SAFETY: descriptor was initialized by DeviceIoControl on success.
                let desc = unsafe { descriptor.assume_init() };
                // SAFETY: incurs_seek_penalty is verified non-null above.
                unsafe {
                    incurs_seek_penalty.write(if desc.IncursSeekPenalty != 0 { 1 } else { 0 })
                };
            } else {
                // SAFETY: incurs_seek_penalty is verified non-null above.
                unsafe { incurs_seek_penalty.write(0) };
            }
            Status::Ok.code()
        }

        #[cfg(not(target_os = "windows"))]
        {
            // SAFETY: incurs_seek_penalty is verified non-null above.
            unsafe { incurs_seek_penalty.write(0) };
            Status::Ok.code()
        }
    }))
    .unwrap_or(Status::InternalError.code())
}

/// Optimizes system memory lists (working sets, standby, modified, and combined lists).
/// `mask` bit flags:
/// - 0x01: Empty working sets
/// - 0x02: Purge standby list
/// - 0x04: Purge low-priority standby list
/// - 0x08: Flush modified list
/// - 0x10: Combine physical memory
///
/// Returns status code and sets `freed_bytes` to bytes of physical RAM freed.
///
/// # Safety
///
/// `freed_bytes` must point to a valid, writable `u64`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_optimize_memory_lists(
    mask: u32,
    freed_bytes: *mut u64,
) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> i32 {
        if freed_bytes.is_null() {
            return Status::NullPointer.code();
        }

        #[cfg(target_os = "windows")]
        {
            use self::win32::*;

            // SAFETY: MEMORYSTATUSEX is a POD struct; dwLength is initialized per Win32 contract.
            let mut ms_before = unsafe { std::mem::zeroed::<MEMORYSTATUSEX>() };
            ms_before.dwLength = std::mem::size_of::<MEMORYSTATUSEX>() as u32;
            // SAFETY: Valid pointer to initialized struct.
            let _ = unsafe { GlobalMemoryStatusEx(&mut ms_before) };

            if (mask & 0x01) != 0 {
                let cmd = MEMORY_EMPTY_WORKING_SETS;
                // SAFETY: NtSetSystemInformation with SYSTEM_MEMORY_LIST_INFORMATION and u32 command.
                let _ = unsafe {
                    NtSetSystemInformation(
                        SYSTEM_MEMORY_LIST_INFORMATION,
                        &cmd as *const _ as *const std::ffi::c_void,
                        std::mem::size_of::<u32>() as u32,
                    )
                };
            }

            if (mask & 0x02) != 0 {
                let cmd = MEMORY_PURGE_STANDBY_LIST;
                // SAFETY: NtSetSystemInformation with SYSTEM_MEMORY_LIST_INFORMATION and u32 command.
                let _ = unsafe {
                    NtSetSystemInformation(
                        SYSTEM_MEMORY_LIST_INFORMATION,
                        &cmd as *const _ as *const std::ffi::c_void,
                        std::mem::size_of::<u32>() as u32,
                    )
                };
            }

            if (mask & 0x04) != 0 {
                let cmd = MEMORY_PURGE_LOW_PRIORITY_STANDBY_LIST;
                // SAFETY: NtSetSystemInformation with SYSTEM_MEMORY_LIST_INFORMATION and u32 command.
                let _ = unsafe {
                    NtSetSystemInformation(
                        SYSTEM_MEMORY_LIST_INFORMATION,
                        &cmd as *const _ as *const std::ffi::c_void,
                        std::mem::size_of::<u32>() as u32,
                    )
                };
            }

            if (mask & 0x08) != 0 {
                let cmd = MEMORY_FLUSH_MODIFIED_LIST;
                // SAFETY: NtSetSystemInformation with SYSTEM_MEMORY_LIST_INFORMATION and u32 command.
                let _ = unsafe {
                    NtSetSystemInformation(
                        SYSTEM_MEMORY_LIST_INFORMATION,
                        &cmd as *const _ as *const std::ffi::c_void,
                        std::mem::size_of::<u32>() as u32,
                    )
                };
            }

            if (mask & 0x10) != 0 {
                // SAFETY: MEMORY_COMBINE_INFORMATION_EX is a POD struct zero-initialized per contract.
                let mut combine_info =
                    unsafe { std::mem::zeroed::<MEMORY_COMBINE_INFORMATION_EX>() };
                // SAFETY: NtSetSystemInformation with SYSTEM_COMBINE_PHYSICAL_MEMORY_INFORMATION.
                let _ = unsafe {
                    NtSetSystemInformation(
                        SYSTEM_COMBINE_PHYSICAL_MEMORY_INFORMATION,
                        &mut combine_info as *mut _ as *mut std::ffi::c_void,
                        std::mem::size_of::<MEMORY_COMBINE_INFORMATION_EX>() as u32,
                    )
                };
            }

            if (mask & 0x20) != 0 {
                // SAFETY: Setting MinimumWorkingSet and MaximumWorkingSet to usize::MAX empties system file cache working set.
                let mut sfci = unsafe { std::mem::zeroed::<SYSTEM_FILECACHE_INFORMATION>() };
                sfci.MinimumWorkingSet = usize::MAX;
                sfci.MaximumWorkingSet = usize::MAX;
                let _ = unsafe {
                    NtSetSystemInformation(
                        SYSTEM_FILE_CACHE_INFORMATION,
                        &mut sfci as *mut _ as *mut std::ffi::c_void,
                        std::mem::size_of::<SYSTEM_FILECACHE_INFORMATION>() as u32,
                    )
                };
            }

            // SAFETY: MEMORYSTATUSEX is a POD struct; dwLength is initialized per Win32 contract.
            let mut ms_after = unsafe { std::mem::zeroed::<MEMORYSTATUSEX>() };
            ms_after.dwLength = std::mem::size_of::<MEMORYSTATUSEX>() as u32;
            // SAFETY: Valid pointer to initialized struct.
            let _ = unsafe { GlobalMemoryStatusEx(&mut ms_after) };

            let freed = ms_after.ullAvailPhys.saturating_sub(ms_before.ullAvailPhys);
            // SAFETY: freed_bytes is verified non-null above.
            unsafe { freed_bytes.write(freed) };
            Status::Ok.code()
        }

        #[cfg(not(target_os = "windows"))]
        {
            // SAFETY: freed_bytes is verified non-null above.
            unsafe { freed_bytes.write(0) };
            Status::Ok.code()
        }
    }))
    .unwrap_or(Status::InternalError.code())
}

/// Broadcasts a Windows Shell change notification (SHCNE_ASSOCCHANGED).
///
/// Refreshes Windows Explorer icon cache and association state without requiring process restart.
#[unsafe(no_mangle)]
pub extern "C" fn wincare_core_shell_notify() -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> i32 {
        #[cfg(target_os = "windows")]
        {
            use self::win32::*;
            // SAFETY: SHChangeNotify takes NULL for dwItem1 and dwItem2 when using SHCNE_ASSOCCHANGED.
            unsafe {
                SHChangeNotify(
                    SHCNE_ASSOCCHANGED,
                    SHCNF_IDLIST,
                    std::ptr::null(),
                    std::ptr::null(),
                );
            }
            Status::Ok.code()
        }

        #[cfg(not(target_os = "windows"))]
        {
            Status::Ok.code()
        }
    }))
    .unwrap_or(Status::InternalError.code())
}

/// Queries whether a window is cloaked by Desktop Window Manager (DWM).
///
/// Returns status code and sets `is_cloaked` to 1 if cloaked, 0 otherwise.
///
/// # Safety
///
/// `is_cloaked` must point to a valid, writable `u32`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_is_window_cloaked(hwnd: isize, is_cloaked: *mut u32) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> i32 {
        if is_cloaked.is_null() {
            return Status::NullPointer.code();
        }

        #[cfg(target_os = "windows")]
        {
            use self::win32::*;

            let mut cloaked: u32 = 0;
            // SAFETY: Calling DwmGetWindowAttribute with valid buffer pointer and size.
            let hr = unsafe {
                DwmGetWindowAttribute(
                    hwnd,
                    DWMWA_CLOAKED,
                    &mut cloaked as *mut _ as *mut std::ffi::c_void,
                    std::mem::size_of::<u32>() as u32,
                )
            };

            if hr == 0 {
                // SAFETY: is_cloaked is verified non-null above.
                unsafe { is_cloaked.write(if cloaked != 0 { 1 } else { 0 }) };
                Status::Ok.code()
            } else {
                // SAFETY: is_cloaked is verified non-null above.
                unsafe { is_cloaked.write(0) };
                Status::NotFound.code()
            }
        }

        #[cfg(not(target_os = "windows"))]
        {
            let _ = hwnd;
            // SAFETY: is_cloaked is verified non-null above.
            unsafe { is_cloaked.write(0) };
            Status::Ok.code()
        }
    }))
    .unwrap_or(Status::InternalError.code())
}

/// Native estimation of LLM model memory requirements and hardware fit classification.
///
/// Computes weight memory footprint, estimated KV cache footprint, and activation buffer,
/// returning the fit classification:
/// - 0: FitsVramFully (total memory <= available_vram * 0.90)
/// - 1: PartialOffloadGpu (weights <= available_vram * 0.90, context in RAM)
/// - 2: CpuRamOnly (total memory <= available_ram * 0.85)
/// - 3: InsufficientMemory (exceeds safe limits)
///
/// # Safety
///
/// `out_fit_class` and `out_total_bytes` must be valid, non-null, writable pointers.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_estimate_model_vram_fit(
    param_count: u64,
    quant_bits: u32,
    context_tokens: u32,
    layer_count: u32,
    available_vram: u64,
    available_ram: u64,
    out_fit_class: *mut u32,
    out_total_bytes: *mut u64,
) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> i32 {
        if out_fit_class.is_null() || out_total_bytes.is_null() {
            return Status::NullPointer.code();
        }

        let bits = quant_bits.clamp(2, 32) as u64;
        let weight_bytes = param_count.saturating_mul(bits).div_ceil(8);

        let layers = layer_count.clamp(8, 128) as u64;
        let hidden_dim = (layers * 128).clamp(2048, 8192);
        let kv_cache_bytes = layers
            .saturating_mul(2)
            .saturating_mul(hidden_dim)
            .saturating_mul(context_tokens as u64)
            .saturating_mul(2);

        let activation_bytes = (weight_bytes as f64 * 0.15) as u64;
        let total_required = weight_bytes
            .saturating_add(kv_cache_bytes)
            .saturating_add(activation_bytes);

        let vram_budget = (available_vram as f64 * 0.90) as u64;
        let ram_budget = (available_ram as f64 * 0.85) as u64;

        let fit_class: u32 = if available_vram > 0 && total_required <= vram_budget {
            0 // FitsVramFully
        } else if available_vram > 0 && weight_bytes <= vram_budget {
            1 // PartialOffloadGpu
        } else if total_required <= ram_budget {
            2 // CpuRamOnly
        } else {
            3 // InsufficientMemory
        };

        // SAFETY: Both pointers verified non-null above.
        unsafe {
            out_fit_class.write(fit_class);
            out_total_bytes.write(total_required);
        }

        Status::Ok.code()
    }))
    .unwrap_or(Status::InternalError.code())
}

/// Native parsing of HLS (m3u8) playlist content.
///
/// Scans the UTF-8 slice for `#EXTM3U`, `#EXT-X-STREAM-INF` (bandwidth, resolution),
/// `#EXT-X-TARGETDURATION`, and `#EXTINF:` segment counts.
///
/// Returns status code and writes:
/// - `out_is_master`: 1 if master playlist, 0 if media segment playlist.
/// - `out_segment_count`: Number of media segments found.
/// - `out_target_duration`: `#EXT-X-TARGETDURATION` in seconds (or 0.0).
/// - `out_max_bandwidth`: Maximum variant BANDWIDTH in bits/sec (or 0).
///
/// # Safety
///
/// `bytes` must point to `len` readable bytes.
/// All output pointers must be valid and writable.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_parse_hls_playlist_info(
    bytes: *const u8,
    len: usize,
    out_is_master: *mut u32,
    out_segment_count: *mut u32,
    out_target_duration: *mut f64,
    out_max_bandwidth: *mut u64,
) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> i32 {
        if bytes.is_null()
            || out_is_master.is_null()
            || out_segment_count.is_null()
            || out_target_duration.is_null()
            || out_max_bandwidth.is_null()
        {
            return Status::NullPointer.code();
        }

        // SAFETY: bytes is verified non-null and caller guarantees len valid bytes.
        let slice = unsafe { slice::from_raw_parts(bytes, len) };
        let content = match str::from_utf8(slice) {
            Ok(s) => s,
            Err(_) => return Status::InvalidUtf8.code(),
        };

        if !content.trim_start().starts_with("#EXTM3U") {
            return Status::InvalidArgument.code();
        }

        let is_master = content.contains("#EXT-X-STREAM-INF");
        let mut segment_count = 0_u32;
        let mut target_duration = 0.0_f64;
        let mut max_bandwidth = 0_u64;

        for line in content.lines() {
            let trimmed = line.trim();
            if trimmed.starts_with("#EXTINF:") {
                segment_count = segment_count.saturating_add(1);
            } else if let Some(td_str) = trimmed.strip_prefix("#EXT-X-TARGETDURATION:") {
                if let Ok(td) = td_str.trim().parse::<f64>() {
                    target_duration = td;
                }
            } else if let Some(attrs) = trimmed.strip_prefix("#EXT-X-STREAM-INF:") {
                for part in attrs.split(',') {
                    let part = part.trim();
                    if let Some(bw_str) = part.strip_prefix("BANDWIDTH=") {
                        if let Ok(bw) = bw_str.trim().parse::<u64>() {
                            if bw > max_bandwidth {
                                max_bandwidth = bw;
                            }
                        }
                    }
                }
            }
        }

        // SAFETY: Output pointers verified non-null above.
        unsafe {
            out_is_master.write(if is_master { 1 } else { 0 });
            out_segment_count.write(segment_count);
            out_target_duration.write(target_duration);
            out_max_bandwidth.write(max_bandwidth);
        }

        Status::Ok.code()
    }))
    .unwrap_or(Status::InternalError.code())
}

/// Native calculation of window rectangle anchoring by directional gravity.
///
/// Gravity enumeration (matching X11 / Marco window gravity):
/// - 1: NorthWest (top-left stays fixed)
/// - 2: North     (centered horizontally, top fixed)
/// - 3: NorthEast (top-right stays fixed)
/// - 4: West      (left fixed, centered vertically)
/// - 5: Center    (centered horizontally and vertically)
/// - 6: East      (right fixed, centered vertically)
/// - 7: SouthWest (bottom-left stays fixed)
/// - 8: South     (centered horizontally, bottom fixed)
/// - 9: SouthEast (bottom-right stays fixed)
///
/// # Safety
///
/// `out_x` and `out_y` must be valid, non-null, writable pointers.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_calculate_box_gravity_anchor(
    rect_x: i32,
    rect_y: i32,
    old_w: i32,
    old_h: i32,
    new_w: i32,
    new_h: i32,
    gravity: u32,
    out_x: *mut i32,
    out_y: *mut i32,
) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> i32 {
        if out_x.is_null() || out_y.is_null() {
            return Status::NullPointer.code();
        }

        let dw = old_w.saturating_sub(new_w);
        let dh = old_h.saturating_sub(new_h);

        let (x, y) = match gravity {
            1 => (rect_x, rect_y),                        // NorthWest
            2 => (rect_x.saturating_add(dw / 2), rect_y), // North
            3 => (rect_x.saturating_add(dw), rect_y),     // NorthEast
            4 => (rect_x, rect_y.saturating_add(dh / 2)), // West
            5 => (rect_x.saturating_add(dw / 2), rect_y.saturating_add(dh / 2)), // Center
            6 => (rect_x.saturating_add(dw), rect_y.saturating_add(dh / 2)), // East
            7 => (rect_x, rect_y.saturating_add(dh)),     // SouthWest
            8 => (rect_x.saturating_add(dw / 2), rect_y.saturating_add(dh)), // South
            9 => (rect_x.saturating_add(dw), rect_y.saturating_add(dh)), // SouthEast
            _ => (rect_x, rect_y),                        // Default to NorthWest
        };

        // SAFETY: Output pointers verified non-null above.
        unsafe {
            out_x.write(x);
            out_y.write(y);
        }

        Status::Ok.code()
    }))
    .unwrap_or(Status::InternalError.code())
}

/// Native calculation of binary space partitioning (BSP) ratio layout split for window tiling.
///
/// If `avail_width >= avail_height`, performs a vertical split:
///   - Primary pane gets `ratio` fraction of width minus spacing.
///   - Remainder pane gets the remaining width.
///
/// If `avail_width < avail_height`, performs a horizontal split.
///
/// # Safety
///
/// Output pointers must be valid, non-null, writable pointers.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_calculate_ratio_layout_split(
    avail_left: i32,
    avail_top: i32,
    avail_width: i32,
    avail_height: i32,
    ratio: f64,
    spacing: i32,
    out_primary: *mut [i32; 4],
    out_remainder: *mut [i32; 4],
    out_is_vertical_split: *mut u32,
) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> i32 {
        if out_primary.is_null() || out_remainder.is_null() || out_is_vertical_split.is_null() {
            return Status::NullPointer.code();
        }

        if ratio <= 0.0 || ratio >= 1.0 || avail_width <= 0 || avail_height <= 0 || spacing < 0 {
            return Status::InvalidArgument.code();
        }

        let is_vert = avail_width >= avail_height;
        let (primary, remainder) = if is_vert {
            let primary_w = ((avail_width as f64 * ratio) as i32)
                .saturating_sub(spacing / 2)
                .max(1);
            let rem_w = avail_width
                .saturating_sub(primary_w)
                .saturating_sub(spacing)
                .max(1);
            let h = avail_height.saturating_sub(spacing * 2).max(1);

            let p = [avail_left + spacing, avail_top + spacing, primary_w, h];
            let r = [
                avail_left + primary_w + spacing,
                avail_top + spacing,
                rem_w,
                h,
            ];
            (p, r)
        } else {
            let primary_h = ((avail_height as f64 * ratio) as i32)
                .saturating_sub(spacing / 2)
                .max(1);
            let rem_h = avail_height
                .saturating_sub(primary_h)
                .saturating_sub(spacing)
                .max(1);
            let w = avail_width.saturating_sub(spacing * 2).max(1);

            let p = [avail_left + spacing, avail_top + spacing, w, primary_h];
            let r = [
                avail_left + spacing,
                avail_top + primary_h + spacing,
                w,
                rem_h,
            ];
            (p, r)
        };

        // SAFETY: Output pointers verified non-null above.
        unsafe {
            out_primary.write(primary);
            out_remainder.write(remainder);
            out_is_vertical_split.write(if is_vert { 1 } else { 0 });
        }

        Status::Ok.code()
    }))
    .unwrap_or(Status::InternalError.code())
}

/// Native calculation of magnetic edge resistance and snapping against display or screen boundaries.
///
/// Snaps the window coordinates to screen bounds if within `snap_threshold`.
/// Snapped flags: bit 0: Left, bit 1: Top, bit 2: Right, bit 3: Bottom.
///
/// # Safety
///
/// Output pointers must be valid, non-null, writable pointers.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_calculate_edge_snap(
    win_x: i32,
    win_y: i32,
    win_w: i32,
    win_h: i32,
    screen_x: i32,
    screen_y: i32,
    screen_w: i32,
    screen_h: i32,
    snap_threshold: i32,
    out_x: *mut i32,
    out_y: *mut i32,
    out_snap_flags: *mut u32,
) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> i32 {
        if out_x.is_null() || out_y.is_null() || out_snap_flags.is_null() {
            return Status::NullPointer.code();
        }

        if snap_threshold <= 0 {
            return Status::InvalidArgument.code();
        }

        let mut final_x = win_x;
        let mut final_y = win_y;
        let mut flags = 0_u32;

        let screen_right = screen_x.saturating_add(screen_w);
        let screen_bottom = screen_y.saturating_add(screen_h);

        // Check Left edge snap
        if (win_x - screen_x).abs() <= snap_threshold {
            final_x = screen_x;
            flags |= 1 << 0;
        }
        // Check Right edge snap
        else if ((win_x + win_w) - screen_right).abs() <= snap_threshold {
            final_x = screen_right.saturating_sub(win_w);
            flags |= 1 << 2;
        }

        // Check Top edge snap
        if (win_y - screen_y).abs() <= snap_threshold {
            final_y = screen_y;
            flags |= 1 << 1;
        }
        // Check Bottom edge snap
        else if ((win_y + win_h) - screen_bottom).abs() <= snap_threshold {
            final_y = screen_bottom.saturating_sub(win_h);
            flags |= 1 << 3;
        }

        // SAFETY: Output pointers verified non-null above.
        unsafe {
            out_x.write(final_x);
            out_y.write(final_y);
            out_snap_flags.write(flags);
        }

        Status::Ok.code()
    }))
    .unwrap_or(Status::InternalError.code())
}

/// Native calculation of cosine similarity between two float embedding vectors.
///
/// # Safety
///
/// `vec_a` and `vec_b` must be valid, readable pointers to `len` contiguous floats.
/// `out_similarity` must be a valid, writable pointer.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_vector_cosine_similarity(
    vec_a: *const f32,
    vec_b: *const f32,
    len: usize,
    out_similarity: *mut f32,
) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> i32 {
        if vec_a.is_null() || vec_b.is_null() || out_similarity.is_null() {
            return Status::NullPointer.code();
        }

        if len == 0 || len > 65536 {
            return Status::InvalidArgument.code();
        }

        // SAFETY: caller guarantees len valid f32 entries.
        let a = unsafe { slice::from_raw_parts(vec_a, len) };
        let b = unsafe { slice::from_raw_parts(vec_b, len) };

        let mut dot = 0.0_f64;
        let mut norm_a = 0.0_f64;
        let mut norm_b = 0.0_f64;

        for i in 0..len {
            let va = a[i] as f64;
            let vb = b[i] as f64;
            dot += va * vb;
            norm_a += va * va;
            norm_b += vb * vb;
        }

        let denom = norm_a.sqrt() * norm_b.sqrt();
        let sim = if denom < 1e-12 {
            0.0_f32
        } else {
            (dot / denom).clamp(-1.0, 1.0) as f32
        };

        // SAFETY: out_similarity verified non-null above.
        unsafe {
            out_similarity.write(sim);
        }

        Status::Ok.code()
    }))
    .unwrap_or(Status::InternalError.code())
}

/// Native population count for a piece map bitset (number of completed chunks/pieces).
///
/// # Safety
///
/// `data` must point to at least `len` bytes. `out_count` must be a valid, writable pointer.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_piece_map_popcount(
    data: *const u8,
    len: usize,
    out_count: *mut u64,
) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> i32 {
        if data.is_null() || out_count.is_null() {
            return Status::NullPointer.code();
        }

        // SAFETY: caller guarantees len valid bytes.
        let slice = unsafe { slice::from_raw_parts(data, len) };
        let count: u64 = slice.iter().map(|b| b.count_ones() as u64).sum();

        // SAFETY: out_count verified non-null above.
        unsafe {
            out_count.write(count);
        }

        Status::Ok.code()
    }))
    .unwrap_or(Status::InternalError.code())
}

/// Native calculation of total overlapping surface area between a candidate rectangle and an array of existing rectangles.
///
/// `rects` is a pointer to `count * 4` contiguous 32-bit integers: `[x, y, w, h]` for each rectangle.
///
/// # Safety
///
/// `rects` must point to at least `count * 4` contiguous i32s if count > 0.
/// `out_overlap` must be a valid, writable pointer.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_calculate_smart_overlap(
    cand_x: i32,
    cand_y: i32,
    cand_w: i32,
    cand_h: i32,
    rects: *const i32,
    count: usize,
    out_overlap: *mut u64,
) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> i32 {
        if out_overlap.is_null() {
            return Status::NullPointer.code();
        }

        if count > 0 && rects.is_null() {
            return Status::NullPointer.code();
        }

        if cand_w <= 0 || cand_h <= 0 || count == 0 {
            unsafe { out_overlap.write(0) };
            return Status::Ok.code();
        }

        // SAFETY: caller guarantees count * 4 valid i32 entries.
        let slice = unsafe { slice::from_raw_parts(rects, count * 4) };
        let mut total_overlap: u64 = 0;

        for chunk in slice.chunks_exact(4) {
            let rx = chunk[0];
            let ry = chunk[1];
            let rw = chunk[2];
            let rh = chunk[3];

            if rw > 0 && rh > 0 {
                let overlap_w = (cand_x + cand_w)
                    .min(rx + rw)
                    .saturating_sub(cand_x.max(rx));
                let overlap_h = (cand_y + cand_h)
                    .min(ry + rh)
                    .saturating_sub(cand_y.max(ry));

                if overlap_w > 0 && overlap_h > 0 {
                    let area = (overlap_w as u64).saturating_mul(overlap_h as u64);
                    total_overlap = total_overlap.saturating_add(area);
                }
            }
        }

        // SAFETY: out_overlap verified non-null above.
        unsafe {
            out_overlap.write(total_overlap);
        }

        Status::Ok.code()
    }))
    .unwrap_or(Status::InternalError.code())
}

/// Native calculation of exponential decay for search affinity learning:
/// score * 2^(-elapsed_ms / half_life_ms).
///
/// # Safety
///
/// `out_score` must be a valid, writable pointer.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_decay_affinity_score(
    current_score: f64,
    elapsed_ms: u64,
    half_life_ms: u64,
    out_score: *mut f64,
) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> i32 {
        if out_score.is_null() {
            return Status::NullPointer.code();
        }

        if !current_score.is_finite() || current_score <= 0.0 {
            unsafe { out_score.write(0.0) };
            return Status::Ok.code();
        }

        if half_life_ms == 0 {
            unsafe { out_score.write(0.0) };
            return Status::Ok.code();
        }

        let exponent = -(elapsed_ms as f64) / (half_life_ms as f64);
        let factor = 2.0_f64.powf(exponent);
        let result = current_score * factor;

        let final_score = if result.is_finite() && result > 0.0 {
            result
        } else {
            0.0
        };

        // SAFETY: out_score verified non-null above.
        unsafe {
            out_score.write(final_score);
        }

        Status::Ok.code()
    }))
    .unwrap_or(Status::InternalError.code())
}

/// Native calculation of token bucket rate limiting for bandwidth throttling.
///
/// Returns:
///   `out_take_bytes`: Number of bytes that can be immediately taken (<= requested byte_count).
///   `out_wait_nanos`: If 0, no wait needed. If > 0, nanoseconds caller must wait before retrying.
///   `out_new_allocated_until`: Updated allocation timestamp in nanoseconds.
///
/// # Safety
///
/// Output pointers must be valid, non-null, writable pointers.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_evaluate_rate_limit_tokens(
    now_nanos: u64,
    allocated_until: u64,
    bytes_per_second: u64,
    byte_count: u64,
    wait_byte_count: u64,
    max_burst_bytes: u64,
    out_take_bytes: *mut u64,
    out_wait_nanos: *mut u64,
    out_new_allocated_until: *mut u64,
) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> i32 {
        if out_take_bytes.is_null() || out_wait_nanos.is_null() || out_new_allocated_until.is_null()
        {
            return Status::NullPointer.code();
        }

        if bytes_per_second == 0 {
            // Unrestricted: fulfill entire byte count immediately
            unsafe {
                out_take_bytes.write(byte_count);
                out_wait_nanos.write(0);
                out_new_allocated_until.write(now_nanos);
            }
            return Status::Ok.code();
        }

        let idle_in_nanos = allocated_until.saturating_sub(now_nanos);
        let nanos_per_sec = 1_000_000_000_u64;

        // nanos_to_bytes = (idle_in_nanos * bytes_per_second) / nanos_per_sec
        let idle_bytes =
            (idle_in_nanos as u128 * bytes_per_second as u128 / nanos_per_sec as u128) as u64;
        let immediate_bytes = max_burst_bytes.saturating_sub(idle_bytes);

        if immediate_bytes >= byte_count {
            // Fulfill entire request without waiting
            let added_nanos =
                (byte_count as u128 * nanos_per_sec as u128 / bytes_per_second as u128) as u64;
            let new_alloc = now_nanos
                .saturating_add(idle_in_nanos)
                .saturating_add(added_nanos);
            unsafe {
                out_take_bytes.write(byte_count);
                out_wait_nanos.write(0);
                out_new_allocated_until.write(new_alloc);
            }
            return Status::Ok.code();
        }

        if immediate_bytes >= wait_byte_count {
            // Fulfill a big enough block without waiting
            let added_nanos =
                (max_burst_bytes as u128 * nanos_per_sec as u128 / bytes_per_second as u128) as u64;
            let new_alloc = now_nanos.saturating_add(added_nanos);
            unsafe {
                out_take_bytes.write(immediate_bytes);
                out_wait_nanos.write(0);
                out_new_allocated_until.write(new_alloc);
            }
            return Status::Ok.code();
        }

        // Must wait
        let min_bytes = wait_byte_count.min(byte_count);
        let deficit_bytes = min_bytes.saturating_sub(immediate_bytes);
        let wait_nanos =
            (deficit_bytes as u128 * nanos_per_sec as u128 / bytes_per_second as u128) as u64;

        unsafe {
            out_take_bytes.write(0);
            out_wait_nanos.write(wait_nanos.max(1));
            out_new_allocated_until.write(allocated_until);
        }

        Status::Ok.code()
    }))
    .unwrap_or(Status::InternalError.code())
}

/// Native POSIX-semantics file unlinking on Windows.
/// Uses `SetFileInformationByHandle` with `FileDispositionInformationEx` flags
/// `FILE_DISPOSITION_FLAG_DELETE | FILE_DISPOSITION_FLAG_POSIX_SEMANTICS | FILE_DISPOSITION_FLAG_IGNORE_READONLY_ATTRIBUTE` (0x13).
/// On failure or non-Windows, falls back to `std::fs::remove_file`.
///
/// # Safety
///
/// `path` must point to a valid UTF-8 buffer of length `path_len`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_unlink_posix(path: *const u8, path_len: usize) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> i32 {
        if path.is_null() {
            return Status::NullPointer.code();
        }
        let slice = unsafe { slice::from_raw_parts(path, path_len) };
        let path_str = match std::str::from_utf8(slice) {
            Ok(s) => s,
            Err(_) => return Status::InvalidUtf8.code(),
        };

        let p = Path::new(path_str);
        if !p.exists() && std::fs::symlink_metadata(p).is_err() {
            return Status::NotFound.code();
        }

        #[cfg(target_os = "windows")]
        {
            use std::ffi::c_void;
            use std::mem::size_of;
            use std::os::windows::ffi::OsStrExt;

            type Handle = *mut c_void;
            const INVALID_HANDLE_VALUE: Handle = -1_isize as Handle;
            const DELETE: u32 = 0x0001_0000;
            const FILE_READ_ATTRIBUTES: u32 = 0x0080;
            const SYNCHRONIZE: u32 = 0x0010_0000;
            const FILE_SHARE_ALL: u32 = 0x0007; // READ | WRITE | DELETE
            const OPEN_EXISTING: u32 = 3;
            const FILE_FLAG_BACKUP_SEMANTICS: u32 = 0x0200_0000;
            const FILE_FLAG_OPEN_REPARSE_POINT: u32 = 0x0020_0000;
            const FILE_DISPOSITION_INFO_EX: i32 = 21;
            // DELETE (0x1) | POSIX_SEMANTICS (0x2) | IGNORE_READONLY (0x10)
            const FILE_DISPOSITION_FLAGS: u32 = 0x0001 | 0x0002 | 0x0010;

            #[repr(C)]
            struct FileDispositionInfoEx {
                flags: u32,
            }

            #[link(name = "kernel32")]
            unsafe extern "system" {
                fn CreateFileW(
                    name: *const u16,
                    access: u32,
                    share: u32,
                    security: *mut c_void,
                    disposition: u32,
                    flags: u32,
                    template: Handle,
                ) -> Handle;
                fn CloseHandle(handle: Handle) -> i32;
                fn SetFileInformationByHandle(
                    handle: Handle,
                    class: i32,
                    info: *const c_void,
                    len: u32,
                ) -> i32;
            }

            let wide: Vec<u16> = p.as_os_str().encode_wide().chain(Some(0)).collect();
            let handle = unsafe {
                CreateFileW(
                    wide.as_ptr(),
                    DELETE | FILE_READ_ATTRIBUTES | SYNCHRONIZE,
                    FILE_SHARE_ALL,
                    std::ptr::null_mut(),
                    OPEN_EXISTING,
                    FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
                    std::ptr::null_mut(),
                )
            };

            if handle != INVALID_HANDLE_VALUE {
                let info = FileDispositionInfoEx {
                    flags: FILE_DISPOSITION_FLAGS,
                };
                let res = unsafe {
                    SetFileInformationByHandle(
                        handle,
                        FILE_DISPOSITION_INFO_EX,
                        (&raw const info).cast(),
                        size_of::<FileDispositionInfoEx>() as u32,
                    )
                };
                unsafe { CloseHandle(handle) };
                if res != 0 {
                    return Status::Ok.code();
                }
            }
        }

        // Fallback: clear read-only if set, then remove_file
        if let Ok(metadata) = std::fs::symlink_metadata(p) {
            let mut permissions = metadata.permissions();
            #[allow(clippy::permissions_set_readonly_false)]
            if permissions.readonly() {
                permissions.set_readonly(false);
                let _ = std::fs::set_permissions(p, permissions);
            }
        }
        match std::fs::remove_file(p) {
            Ok(_) => Status::Ok.code(),
            Err(e) if e.kind() == io::ErrorKind::NotFound => Status::NotFound.code(),
            Err(_) => Status::IoError.code(),
        }
    }))
    .unwrap_or(Status::InternalError.code())
}

/// Three-pass volatile memory sanitization (pwsafe).
/// Pass 1: 0x55 (01010101)
/// Pass 2: 0xAA (10101010)
/// Pass 3: 0x00 (00000000)
///
/// # Safety
///
/// `buffer` must be valid for writes of `length` bytes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_secure_trash_memory(buffer: *mut u8, length: usize) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> i32 {
        if buffer.is_null() {
            return if length == 0 {
                Status::Ok.code()
            } else {
                Status::NullPointer.code()
            };
        }
        if length == 0 {
            return Status::Ok.code();
        }

        // Pass 1: 0x55
        for i in 0..length {
            unsafe { std::ptr::write_volatile(buffer.add(i), 0x55) };
        }
        // Pass 2: 0xAA
        for i in 0..length {
            unsafe { std::ptr::write_volatile(buffer.add(i), 0xAA) };
        }
        // Pass 3: 0x00
        for i in 0..length {
            unsafe { std::ptr::write_volatile(buffer.add(i), 0x00) };
        }

        Status::Ok.code()
    }))
    .unwrap_or(Status::InternalError.code())
}

/// Recursive stack frame memory scrubber (pwsafe).
/// Scans and purges stack memory by allocating 32-byte buffers,
/// wiping them with volatile multi-pass writes, and recursing until `length` bytes are burned.
///
/// # Safety
///
/// Caller should specify reasonable length (e.g. <= 64KB) to avoid stack overflow.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_burn_stack(length: usize) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> i32 {
        const CHUNK: usize = 32;
        const MAX_BURN: usize = 65_536;
        let bounded_length = length.min(MAX_BURN);

        #[inline(never)]
        fn burn_recursive(remaining: usize) {
            let mut buf = [0_u8; CHUNK];
            unsafe {
                wincare_core_secure_trash_memory(buf.as_mut_ptr(), CHUNK);
            }
            if remaining > CHUNK {
                burn_recursive(remaining - CHUNK);
            }
        }

        if bounded_length > 0 {
            burn_recursive(bounded_length);
        }

        Status::Ok.code()
    }))
    .unwrap_or(Status::InternalError.code())
}

/// High-throughput Boyer-Moore byte search algorithm (GalaxyBudsClient).
/// Returns 0-based byte index of first occurrence in `out_index`, or -1 if not found.
///
/// # Safety
///
/// `data`, `pattern`, and `out_index` must be valid, properly aligned pointers.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_boyer_moore_search(
    data: *const u8,
    data_len: usize,
    pattern: *const u8,
    pattern_len: usize,
    out_index: *mut i64,
) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> i32 {
        if out_index.is_null() {
            return Status::NullPointer.code();
        }

        if pattern_len == 0 {
            unsafe { out_index.write(0) };
            return Status::Ok.code();
        }

        if data.is_null() || pattern.is_null() {
            return Status::NullPointer.code();
        }

        if data_len < pattern_len {
            unsafe { out_index.write(-1) };
            return Status::Ok.code();
        }

        let d = unsafe { slice::from_raw_parts(data, data_len) };
        let p = unsafe { slice::from_raw_parts(pattern, pattern_len) };

        // Precompute bad-character jump table (256 entries)
        let mut jump = [pattern_len; 256];
        for (j, &byte) in p[..pattern_len - 1].iter().enumerate() {
            jump[byte as usize] = pattern_len - 1 - j;
        }

        let last_pat_idx = pattern_len - 1;
        let mut i = last_pat_idx;

        while i < data_len {
            let mut j = last_pat_idx;
            let mut k = i;

            while d[k] == p[j] {
                if j == 0 {
                    unsafe { out_index.write(k as i64) };
                    return Status::Ok.code();
                }
                j -= 1;
                k -= 1;
            }

            i += jump[d[i] as usize];
        }

        unsafe { out_index.write(-1) };
        Status::Ok.code()
    }))
    .unwrap_or(Status::InternalError.code())
}

/// CRC-16 CCITT lookup-table calculation (GalaxyBudsClient).
/// Uses polynomial 0x1021.
///
/// # Safety
///
/// `out_crc` must be a valid, writable pointer. `data` must be valid for `data_len` bytes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_crc16_ccitt(
    data: *const u8,
    data_len: usize,
    initial_crc: u16,
    out_crc: *mut u16,
) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> i32 {
        if out_crc.is_null() {
            return Status::NullPointer.code();
        }

        if data_len > 0 && data.is_null() {
            return Status::NullPointer.code();
        }

        const TABLE: [u16; 256] = {
            let mut table = [0u16; 256];
            let mut i = 0usize;
            while i < 256 {
                let mut curr = (i as u16) << 8;
                let mut j = 0;
                while j < 8 {
                    if (curr & 0x8000) != 0 {
                        curr = (curr << 1) ^ 0x1021;
                    } else {
                        curr <<= 1;
                    }
                    j += 1;
                }
                table[i] = curr;
                i += 1;
            }
            table
        };

        let mut crc = initial_crc;
        if data_len > 0 {
            let slice = unsafe { slice::from_raw_parts(data, data_len) };
            for &byte in slice {
                let idx = (((crc >> 8) as u8) ^ byte) as usize;
                crc = (crc << 8) ^ TABLE[idx];
            }
        }

        unsafe { out_crc.write(crc) };
        Status::Ok.code()
    }))
    .unwrap_or(Status::InternalError.code())
}

/// Interactive window move/resize delta calculation (TinyWM).
/// Modes:
///   1 = Move:   [win_x + (curr_x - start_x), win_y + (curr_y - start_y), win_w, win_h]
///   2 = Resize: [win_x, win_y, max(1, win_w + (curr_x - start_x)), max(1, win_h + (curr_y - start_y))]
///   3 = Both:   [win_x + (curr_x - start_x), win_y + (curr_y - start_y), max(1, win_w + (curr_x - start_x)), max(1, win_h + (curr_y - start_y))]
///
/// # Safety
///
/// `out_rect` must point to at least 4 contiguous 32-bit integers: `[x, y, w, h]`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_calculate_relative_window_delta(
    start_x: i32,
    start_y: i32,
    curr_x: i32,
    curr_y: i32,
    win_x: i32,
    win_y: i32,
    win_w: i32,
    win_h: i32,
    mode: u32,
    out_rect: *mut i32,
) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> i32 {
        if out_rect.is_null() {
            return Status::NullPointer.code();
        }

        let xdiff = curr_x.saturating_sub(start_x);
        let ydiff = curr_y.saturating_sub(start_y);

        let (rx, ry, rw, rh) = match mode {
            1 => (
                win_x.saturating_add(xdiff),
                win_y.saturating_add(ydiff),
                win_w.max(1),
                win_h.max(1),
            ),
            2 => (
                win_x,
                win_y,
                (win_w.saturating_add(xdiff)).max(1),
                (win_h.saturating_add(ydiff)).max(1),
            ),
            3 => (
                win_x.saturating_add(xdiff),
                win_y.saturating_add(ydiff),
                (win_w.saturating_add(xdiff)).max(1),
                (win_h.saturating_add(ydiff)).max(1),
            ),
            _ => return Status::InvalidArgument.code(),
        };

        unsafe {
            out_rect.add(0).write(rx);
            out_rect.add(1).write(ry);
            out_rect.add(2).write(rw);
            out_rect.add(3).write(rh);
        }

        Status::Ok.code()
    }))
    .unwrap_or(Status::InternalError.code())
}

/// Calculates the exact Shannon entropy in bits per byte [0.0, 8.0] of a memory slice (pwsafe).
/// $H(X) = - \sum_{i=0}^{255} p_i \log_2(p_i)$ where $p_i = \text{count}[i] / N$.
///
/// # Safety
///
/// `out_entropy` must be a valid, writable pointer. `data` must be valid for `data_len` bytes when `data_len > 0`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_calculate_entropy(
    data: *const u8,
    data_len: usize,
    out_entropy: *mut f64,
) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> i32 {
        if out_entropy.is_null() {
            return Status::NullPointer.code();
        }

        if data_len == 0 {
            unsafe { out_entropy.write(0.0) };
            return Status::Ok.code();
        }

        if data.is_null() {
            return Status::NullPointer.code();
        }

        let slice = unsafe { slice::from_raw_parts(data, data_len) };
        let mut counts = [0_usize; 256];
        for &b in slice {
            counts[b as usize] += 1;
        }

        let n = data_len as f64;
        let mut entropy = 0.0_f64;
        for &c in &counts {
            if c > 0 {
                let p = (c as f64) / n;
                entropy -= p * p.log2();
            }
        }

        unsafe { out_entropy.write(entropy) };
        Status::Ok.code()
    }))
    .unwrap_or(Status::InternalError.code())
}

/// Queries volume serial number and 128-bit file ID via GetFileInformationByHandleEx(FileIdInfo).
/// Validates that reparse points (symlinks/junctions) are rejected (Motrix finalize-fs).
///
/// # Safety
///
/// `path`, `out_volume_serial`, `out_file_id_high`, and `out_file_id_low` must be valid pointers.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_query_file_identity(
    path: *const u8,
    path_len: usize,
    out_volume_serial: *mut u64,
    out_file_id_high: *mut u64,
    out_file_id_low: *mut u64,
) -> i32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| -> i32 {
        if out_volume_serial.is_null() || out_file_id_high.is_null() || out_file_id_low.is_null() {
            return Status::NullPointer.code();
        }
        if path.is_null() {
            return Status::NullPointer.code();
        }

        let slice = unsafe { slice::from_raw_parts(path, path_len) };
        let path_str = match std::str::from_utf8(slice) {
            Ok(s) => s,
            Err(_) => return Status::InvalidUtf8.code(),
        };

        let p = Path::new(path_str);
        if !p.exists() && std::fs::symlink_metadata(p).is_err() {
            return Status::NotFound.code();
        }

        #[cfg(target_os = "windows")]
        {
            use std::ffi::c_void;
            use std::mem::size_of;
            use std::os::windows::ffi::OsStrExt;

            type Handle = *mut c_void;
            const INVALID_HANDLE_VALUE: Handle = -1_isize as Handle;
            const FILE_READ_ATTRIBUTES: u32 = 0x0080;
            const FILE_SHARE_ALL: u32 = 0x0007; // READ | WRITE | DELETE
            const OPEN_EXISTING: u32 = 3;
            const FILE_FLAG_BACKUP_SEMANTICS: u32 = 0x0200_0000;
            const FILE_FLAG_OPEN_REPARSE_POINT: u32 = 0x0020_0000;
            const FILE_BASIC_INFO_CLASS: i32 = 0;
            const FILE_ID_INFO_CLASS: i32 = 18;
            const FILE_ATTRIBUTE_REPARSE_POINT: u32 = 0x0000_0400;

            #[repr(C)]
            struct FileBasicInfo {
                _creation_time: i64,
                _last_access_time: i64,
                _last_write_time: i64,
                _change_time: i64,
                file_attributes: u32,
            }

            #[repr(C)]
            struct FileId128 {
                identifier: [u8; 16],
            }

            #[repr(C)]
            struct FileIdInfo {
                volume_serial_number: u64,
                file_id: FileId128,
            }

            #[link(name = "kernel32")]
            unsafe extern "system" {
                fn CreateFileW(
                    name: *const u16,
                    access: u32,
                    share: u32,
                    security: *mut c_void,
                    disposition: u32,
                    flags: u32,
                    template: Handle,
                ) -> Handle;
                fn CloseHandle(handle: Handle) -> i32;
                fn GetFileInformationByHandleEx(
                    handle: Handle,
                    class: i32,
                    info: *mut c_void,
                    len: u32,
                ) -> i32;
            }

            let wide: Vec<u16> = p.as_os_str().encode_wide().chain(Some(0)).collect();
            let handle = unsafe {
                CreateFileW(
                    wide.as_ptr(),
                    FILE_READ_ATTRIBUTES,
                    FILE_SHARE_ALL,
                    std::ptr::null_mut(),
                    OPEN_EXISTING,
                    FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
                    std::ptr::null_mut(),
                )
            };

            if handle == INVALID_HANDLE_VALUE {
                return Status::IoError.code();
            }

            let mut basic_info = FileBasicInfo {
                _creation_time: 0,
                _last_access_time: 0,
                _last_write_time: 0,
                _change_time: 0,
                file_attributes: 0,
            };

            let res_basic = unsafe {
                GetFileInformationByHandleEx(
                    handle,
                    FILE_BASIC_INFO_CLASS,
                    (&raw mut basic_info).cast(),
                    size_of::<FileBasicInfo>() as u32,
                )
            };

            if res_basic == 0 {
                unsafe { CloseHandle(handle) };
                return Status::IoError.code();
            }

            if (basic_info.file_attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0 {
                unsafe { CloseHandle(handle) };
                return Status::InvalidArgument.code();
            }

            let mut id_info = FileIdInfo {
                volume_serial_number: 0,
                file_id: FileId128 {
                    identifier: [0; 16],
                },
            };

            let res_id = unsafe {
                GetFileInformationByHandleEx(
                    handle,
                    FILE_ID_INFO_CLASS,
                    (&raw mut id_info).cast(),
                    size_of::<FileIdInfo>() as u32,
                )
            };

            unsafe { CloseHandle(handle) };

            if res_id != 0 {
                let low = u64::from_le_bytes(id_info.file_id.identifier[0..8].try_into().unwrap());
                let high =
                    u64::from_le_bytes(id_info.file_id.identifier[8..16].try_into().unwrap());

                unsafe {
                    out_volume_serial.write(id_info.volume_serial_number);
                    out_file_id_high.write(high);
                    out_file_id_low.write(low);
                }
                return Status::Ok.code();
            }
        }

        // Fallback for non-Windows or if handle query is not supported
        let meta = match std::fs::metadata(p) {
            Ok(m) => m,
            Err(_) => return Status::IoError.code(),
        };
        let len = meta.len();
        unsafe {
            out_volume_serial.write(1);
            out_file_id_high.write(0);
            out_file_id_low.write(len);
        }
        Status::Ok.code()
    }))
    .unwrap_or(Status::InternalError.code())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Write;

    #[test]
    fn abi_version_is_stable() {
        assert_eq!(1, wincare_core_abi_version());
    }

    #[test]
    fn version_reports_required_buffer_length() {
        let mut written = 0_usize;
        // SAFETY: The output length pointer is valid and zero buffer length means
        // the null buffer is not dereferenced.
        let status = unsafe { wincare_core_version(std::ptr::null_mut(), 0, &mut written) };
        assert_eq!(Status::BufferTooSmall.code(), status);
        assert_eq!(VERSION.len(), written);
    }

    #[test]
    fn hashes_a_file_within_the_limit() {
        let mut file = tempfile::NamedTempFile::new().expect("create temporary file");
        file.write_all(b"WinCare").expect("write temporary file");
        let path = file.path().to_string_lossy().into_owned();
        let mut output = [0_u8; SHA256_LENGTH];

        // SAFETY: Path and output buffers are valid for the provided lengths.
        let status = unsafe {
            wincare_core_sha256_file(
                path.as_ptr(),
                path.len(),
                1024,
                output.as_mut_ptr(),
                output.len(),
            )
        };

        assert_eq!(Status::Ok.code(), status);
        assert_eq!(
            "ffea9f97d9a428ca7734085ae405b3f41d5a97e800bd2e287cd7c7d6fad5de9c",
            output
                .iter()
                .map(|byte| format!("{byte:02x}"))
                .collect::<String>()
        );
    }

    #[test]
    fn rejects_a_file_above_the_limit() {
        let mut file = tempfile::NamedTempFile::new().expect("create temporary file");
        file.write_all(b"WinCare").expect("write temporary file");
        let path = file.path().to_string_lossy().into_owned();
        let mut output = [0_u8; SHA256_LENGTH];

        // SAFETY: Path and output buffers are valid for the provided lengths.
        let status = unsafe {
            wincare_core_sha256_file(
                path.as_ptr(),
                path.len(),
                2,
                output.as_mut_ptr(),
                output.len(),
            )
        };

        assert_eq!(Status::FileTooLarge.code(), status);
    }

    #[test]
    fn reports_a_missing_file() {
        let path = b"this-file-must-not-exist-wincare";
        let mut output = [0_u8; SHA256_LENGTH];

        // SAFETY: Path and output buffers are valid for the provided lengths.
        let status = unsafe {
            wincare_core_sha256_file(
                path.as_ptr(),
                path.len(),
                1024,
                output.as_mut_ptr(),
                output.len(),
            )
        };

        assert_eq!(Status::NotFound.code(), status);
    }

    #[test]
    fn rejects_non_utf8_paths() {
        let path = [0xff_u8, 0xfe_u8];
        let mut output = [0_u8; SHA256_LENGTH];

        // SAFETY: Path and output buffers are valid for the provided lengths.
        let status = unsafe {
            wincare_core_sha256_file(
                path.as_ptr(),
                path.len(),
                1024,
                output.as_mut_ptr(),
                output.len(),
            )
        };

        assert_eq!(Status::InvalidUtf8.code(), status);
    }

    #[test]
    fn version_rejects_a_null_written_pointer() {
        let mut output = [0_u8; VERSION.len()];

        // SAFETY: This intentionally supplies a null pointer to verify validation.
        let status = unsafe {
            wincare_core_version(output.as_mut_ptr(), output.len(), std::ptr::null_mut())
        };

        assert_eq!(Status::NullPointer.code(), status);
    }

    #[test]
    fn rejects_null_pointers_and_short_output() {
        let path = b"missing";
        let mut output = [0_u8; SHA256_LENGTH];
        // SAFETY: This intentionally supplies null pointers to verify validation.
        let null_status = unsafe {
            wincare_core_sha256_file(std::ptr::null(), 0, 1, output.as_mut_ptr(), output.len())
        };
        // SAFETY: The path is valid. The output length is intentionally too short.
        let short_status = unsafe {
            wincare_core_sha256_file(path.as_ptr(), path.len(), 1, output.as_mut_ptr(), 8)
        };

        assert_eq!(Status::NullPointer.code(), null_status);
        assert_eq!(Status::BufferTooSmall.code(), short_status);
    }

    #[test]
    fn dir_size_on_empty_dir_returns_zero() {
        let temporary_directory = tempfile::tempdir().unwrap();
        let dir = temporary_directory.path();
        let path = dir.to_str().unwrap();
        let mut out: u64 = 99;
        let r = unsafe { wincare_core_dir_size(path.as_ptr(), path.len(), &mut out) };
        assert_eq!(r, 0);
        assert_eq!(out, 0);
    }

    #[test]
    fn dir_size_on_known_file_dir_returns_correct_size() {
        use std::io::Write;
        let temporary_directory = tempfile::tempdir().unwrap();
        let dir = temporary_directory.path();
        std::fs::File::create(dir.join("a.txt"))
            .unwrap()
            .write_all(&[0u8; 1024])
            .unwrap();
        let path = dir.to_str().unwrap();
        let mut out: u64 = 0;
        let r = unsafe { wincare_core_dir_size(path.as_ptr(), path.len(), &mut out) };
        assert_eq!(r, 0);
        assert_eq!(out, 1024);
    }

    #[test]
    fn dir_size_handles_deep_directory_tree_without_recursion() {
        let temporary_directory = tempfile::tempdir().unwrap();
        let root = temporary_directory.path();
        let mut current = root.to_path_buf();
        for index in 0..96 {
            current = current.join(format!("d{index}"));
            std::fs::create_dir(&current).unwrap();
        }
        std::fs::write(current.join("payload.bin"), [7_u8; 17]).unwrap();

        let path = root.to_str().unwrap();
        let mut out = 0_u64;
        let status = unsafe { wincare_core_dir_size(path.as_ptr(), path.len(), &mut out) };

        assert_eq!(Status::Ok.code(), status);
        assert_eq!(17, out);
    }

    #[test]
    fn dir_size_rejects_null_pointer() {
        let mut out: u64 = 0;
        let r = unsafe { wincare_core_dir_size(std::ptr::null(), 0, &mut out) };
        assert_eq!(r, 1); // Status::NullPointer
        let dummy = b"path";
        let r2 =
            unsafe { wincare_core_dir_size(dummy.as_ptr(), dummy.len(), std::ptr::null_mut()) };
        assert_eq!(r2, 1);
    }

    #[test]
    fn dir_size_on_nonexistent_path_returns_not_found() {
        let path = "/this/path/does/not/exist/wc_9x7z";
        let mut out: u64 = 0;
        let r = unsafe { wincare_core_dir_size(path.as_ptr(), path.len(), &mut out) };
        assert_eq!(r, 3); // Status::NotFound
    }

    #[test]
    fn dir_size_on_non_utf8_path_returns_invalid_utf8() {
        let bad: &[u8] = &[0xFF, 0xFE, 0x00];
        let mut out: u64 = 0;
        let r = unsafe { wincare_core_dir_size(bad.as_ptr(), bad.len(), &mut out) };
        assert_eq!(r, 2); // Status::InvalidUtf8
    }

    #[test]
    fn sys_info_reports_required_buffer_length_before_copy() {
        let mut written: usize = 0;
        let r = unsafe { wincare_core_sys_info(std::ptr::null_mut(), 0, &mut written) };
        assert_eq!(r, 6); // Status::BufferTooSmall
        assert!(written > 0, "required length must be positive");
    }

    #[test]
    fn sys_info_fills_buffer_with_valid_json() {
        let mut written: usize = 0;
        unsafe { wincare_core_sys_info(std::ptr::null_mut(), 0, &mut written) };
        let mut buf = vec![0u8; written];
        let r = unsafe { wincare_core_sys_info(buf.as_mut_ptr(), buf.len(), &mut written) };
        assert_eq!(r, 0);
        let json = std::str::from_utf8(&buf[..written]).unwrap();
        assert!(json.starts_with('{') && json.ends_with('}'));
    }

    #[test]
    fn sys_info_rejects_null_written_pointer() {
        let r = unsafe { wincare_core_sys_info(std::ptr::null_mut(), 0, std::ptr::null_mut()) };
        assert_eq!(r, 1); // Status::NullPointer
    }

    #[test]
    fn sys_info_json_contains_expected_keys() {
        let mut written: usize = 0;
        unsafe { wincare_core_sys_info(std::ptr::null_mut(), 0, &mut written) };
        let mut buf = vec![0u8; written];
        unsafe { wincare_core_sys_info(buf.as_mut_ptr(), buf.len(), &mut written) };
        let json = std::str::from_utf8(&buf[..written]).unwrap();
        assert!(json.contains("\"logical_cpus\""));
        assert!(json.contains("\"total_physical_memory_bytes\""));
        assert!(json.contains("\"available_physical_memory_bytes\""));
        assert!(json.contains("\"os_build\""));
    }

    #[test]
    fn is_window_cloaked_rejects_null_pointer() {
        let status = unsafe { wincare_core_is_window_cloaked(0, std::ptr::null_mut()) };
        assert_eq!(Status::NullPointer.code(), status);
    }

    #[test]
    fn is_window_cloaked_returns_status_for_invalid_hwnd() {
        let mut cloaked = 999_u32;
        let status = unsafe { wincare_core_is_window_cloaked(0, &mut cloaked) };
        assert!(status == Status::Ok.code() || status == Status::NotFound.code());
        assert_eq!(0, cloaked);
    }

    #[test]
    fn estimate_model_vram_fit_rejects_null_pointers() {
        let mut fit_class = 0_u32;
        let mut total_bytes = 0_u64;

        let r1 = unsafe {
            wincare_core_estimate_model_vram_fit(
                7_000_000_000,
                4,
                4096,
                32,
                16_000_000_000,
                32_000_000_000,
                std::ptr::null_mut(),
                &mut total_bytes,
            )
        };
        assert_eq!(Status::NullPointer.code(), r1);

        let r2 = unsafe {
            wincare_core_estimate_model_vram_fit(
                7_000_000_000,
                4,
                4096,
                32,
                16_000_000_000,
                32_000_000_000,
                &mut fit_class,
                std::ptr::null_mut(),
            )
        };
        assert_eq!(Status::NullPointer.code(), r2);
    }

    #[test]
    fn estimate_model_vram_fit_fits_vram_fully() {
        let mut fit_class = 999_u32;
        let mut total_bytes = 0_u64;

        // 3B model at 4-bit (~1.5GB weights) with 2048 context on a 24GB GPU
        let status = unsafe {
            wincare_core_estimate_model_vram_fit(
                3_000_000_000,
                4,
                2048,
                28,
                24_000_000_000,
                32_000_000_000,
                &mut fit_class,
                &mut total_bytes,
            )
        };

        assert_eq!(Status::Ok.code(), status);
        assert_eq!(0, fit_class); // FitsVramFully
        assert!(total_bytes > 0);
    }

    #[test]
    fn estimate_model_vram_fit_partial_offload_gpu() {
        let mut fit_class = 999_u32;
        let mut total_bytes = 0_u64;

        // 8B model at 8-bit (~8GB weights) with huge 64k context on a 10GB GPU
        // Weights fit in VRAM (8GB <= 9GB), but total exceeds VRAM budget
        let status = unsafe {
            wincare_core_estimate_model_vram_fit(
                8_000_000_000,
                8,
                65536,
                32,
                10_000_000_000,
                64_000_000_000,
                &mut fit_class,
                &mut total_bytes,
            )
        };

        assert_eq!(Status::Ok.code(), status);
        assert_eq!(1, fit_class); // PartialOffloadGpu
    }

    #[test]
    fn estimate_model_vram_fit_cpu_ram_only() {
        let mut fit_class = 999_u32;
        let mut total_bytes = 0_u64;

        // 14B model on integrated graphics (0 VRAM), 64GB RAM
        let status = unsafe {
            wincare_core_estimate_model_vram_fit(
                14_000_000_000,
                4,
                4096,
                40,
                0,
                64_000_000_000,
                &mut fit_class,
                &mut total_bytes,
            )
        };

        assert_eq!(Status::Ok.code(), status);
        assert_eq!(2, fit_class); // CpuRamOnly
    }

    #[test]
    fn parse_hls_playlist_rejects_null_or_invalid() {
        let mut is_master = 0_u32;
        let mut count = 0_u32;
        let mut dur = 0.0_f64;
        let mut bw = 0_u64;

        let status_null = unsafe {
            wincare_core_parse_hls_playlist_info(
                std::ptr::null(),
                0,
                &mut is_master,
                &mut count,
                &mut dur,
                &mut bw,
            )
        };
        assert_eq!(Status::NullPointer.code(), status_null);

        let garbage = b"<html>Not HLS</html>";
        let status_invalid = unsafe {
            wincare_core_parse_hls_playlist_info(
                garbage.as_ptr(),
                garbage.len(),
                &mut is_master,
                &mut count,
                &mut dur,
                &mut bw,
            )
        };
        assert_eq!(Status::InvalidArgument.code(), status_invalid);
    }

    #[test]
    fn parse_hls_playlist_parses_master_and_media() {
        let master = b"#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1280000,RESOLUTION=1280x720\n720p.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=2560000,RESOLUTION=1920x1080\n1080p.m3u8\n";
        let mut is_master = 0_u32;
        let mut count = 0_u32;
        let mut dur = 0.0_f64;
        let mut bw = 0_u64;

        let s1 = unsafe {
            wincare_core_parse_hls_playlist_info(
                master.as_ptr(),
                master.len(),
                &mut is_master,
                &mut count,
                &mut dur,
                &mut bw,
            )
        };
        assert_eq!(Status::Ok.code(), s1);
        assert_eq!(1, is_master);
        assert_eq!(2560000, bw);

        let media = b"#EXTM3U\n#EXT-X-TARGETDURATION:10\n#EXTINF:9.009,\nseg1.ts\n#EXTINF:9.009,\nseg2.ts\n#EXT-X-ENDLIST\n";
        let s2 = unsafe {
            wincare_core_parse_hls_playlist_info(
                media.as_ptr(),
                media.len(),
                &mut is_master,
                &mut count,
                &mut dur,
                &mut bw,
            )
        };
        assert_eq!(Status::Ok.code(), s2);
        assert_eq!(0, is_master);
        assert_eq!(2, count);
        assert_eq!(10.0, dur);
    }

    #[test]
    fn calculate_box_gravity_anchor_computes_positions() {
        let mut out_x = 0_i32;
        let mut out_y = 0_i32;

        // Old: 1000x800 at (100, 100). New: 600x400.
        // dw = 400, dh = 400.
        // Center (5): x = 100 + 200 = 300, y = 100 + 200 = 300.
        let status_center = unsafe {
            wincare_core_calculate_box_gravity_anchor(
                100, 100, 1000, 800, 600, 400, 5, &mut out_x, &mut out_y,
            )
        };
        assert_eq!(Status::Ok.code(), status_center);
        assert_eq!(300, out_x);
        assert_eq!(300, out_y);

        // SouthEast (9): x = 100 + 400 = 500, y = 100 + 400 = 500.
        let status_se = unsafe {
            wincare_core_calculate_box_gravity_anchor(
                100, 100, 1000, 800, 600, 400, 9, &mut out_x, &mut out_y,
            )
        };
        assert_eq!(Status::Ok.code(), status_se);
        assert_eq!(500, out_x);
        assert_eq!(500, out_y);

        // Reject null pointer
        let status_null = unsafe {
            wincare_core_calculate_box_gravity_anchor(
                100,
                100,
                1000,
                800,
                600,
                400,
                1,
                std::ptr::null_mut(),
                &mut out_y,
            )
        };
        assert_eq!(Status::NullPointer.code(), status_null);
    }

    #[test]
    fn calculate_ratio_layout_split_vertical_and_horizontal() {
        let mut primary = [0_i32; 4];
        let mut remainder = [0_i32; 4];
        let mut is_vert = 0_u32;

        // Wide container: 1000x500 (w >= h -> vertical split)
        // ratio 0.6, spacing 10
        let s1 = unsafe {
            wincare_core_calculate_ratio_layout_split(
                0,
                0,
                1000,
                500,
                0.6,
                10,
                &mut primary,
                &mut remainder,
                &mut is_vert,
            )
        };
        assert_eq!(Status::Ok.code(), s1);
        assert_eq!(1, is_vert);
        assert_eq!(10, primary[0]); // left + spacing
        assert_eq!(10, primary[1]); // top + spacing
        assert_eq!(595, primary[2]); // (1000 * 0.6) - 5
        assert_eq!(480, primary[3]); // 500 - 20

        // Tall container: 400x800 (w < h -> horizontal split)
        // ratio 0.5, spacing 8
        let s2 = unsafe {
            wincare_core_calculate_ratio_layout_split(
                0,
                0,
                400,
                800,
                0.5,
                8,
                &mut primary,
                &mut remainder,
                &mut is_vert,
            )
        };
        assert_eq!(Status::Ok.code(), s2);
        assert_eq!(0, is_vert);

        // Rejection of invalid ratio or null pointer
        let s_bad_ratio = unsafe {
            wincare_core_calculate_ratio_layout_split(
                0,
                0,
                1000,
                500,
                1.5,
                10,
                &mut primary,
                &mut remainder,
                &mut is_vert,
            )
        };
        assert_eq!(Status::InvalidArgument.code(), s_bad_ratio);
    }

    #[test]
    fn calculate_edge_snap_corners_and_boundaries() {
        let mut out_x = 0_i32;
        let mut out_y = 0_i32;
        let mut flags = 0_u32;

        // Window at (8, 10) with size 400x300 on screen (0, 0, 1920, 1080)
        // Threshold 12 -> Snaps Left to 0 and Top to 0 (flags 1 | 2 = 3)
        let s1 = unsafe {
            wincare_core_calculate_edge_snap(
                8, 10, 400, 300, 0, 0, 1920, 1080, 12, &mut out_x, &mut out_y, &mut flags,
            )
        };
        assert_eq!(Status::Ok.code(), s1);
        assert_eq!(0, out_x);
        assert_eq!(0, out_y);
        assert_eq!(3, flags);

        // Window far away from edge: (500, 500)
        let s2 = unsafe {
            wincare_core_calculate_edge_snap(
                500, 500, 400, 300, 0, 0, 1920, 1080, 12, &mut out_x, &mut out_y, &mut flags,
            )
        };
        assert_eq!(Status::Ok.code(), s2);
        assert_eq!(500, out_x);
        assert_eq!(500, out_y);
        assert_eq!(0, flags);
    }

    #[test]
    fn vector_cosine_similarity_orthogonal_and_parallel() {
        let a = [1.0_f32, 0.0, 0.0];
        let b = [1.0_f32, 0.0, 0.0];
        let c = [0.0_f32, 1.0, 0.0];
        let mut sim = 0.0_f32;

        // Parallel vectors -> similarity 1.0
        let s1 =
            unsafe { wincare_core_vector_cosine_similarity(a.as_ptr(), b.as_ptr(), 3, &mut sim) };
        assert_eq!(Status::Ok.code(), s1);
        assert!((sim - 1.0).abs() < 1e-5);

        // Orthogonal vectors -> similarity 0.0
        let s2 =
            unsafe { wincare_core_vector_cosine_similarity(a.as_ptr(), c.as_ptr(), 3, &mut sim) };
        assert_eq!(Status::Ok.code(), s2);
        assert!(sim.abs() < 1e-5);

        // Null pointer check
        let s_null = unsafe {
            wincare_core_vector_cosine_similarity(std::ptr::null(), b.as_ptr(), 3, &mut sim)
        };
        assert_eq!(Status::NullPointer.code(), s_null);
    }

    #[test]
    fn piece_map_popcount_counts_bits_correctly() {
        let data = [0b0000_0001, 0b1111_0000, 0b1111_1111]; // 1 + 4 + 8 = 13
        let mut count = 0_u64;

        let s1 = unsafe { wincare_core_piece_map_popcount(data.as_ptr(), data.len(), &mut count) };
        assert_eq!(Status::Ok.code(), s1);
        assert_eq!(13, count);

        let s_null = unsafe { wincare_core_piece_map_popcount(std::ptr::null(), 10, &mut count) };
        assert_eq!(Status::NullPointer.code(), s_null);
    }

    #[test]
    fn calculate_smart_overlap_computes_exact_surface_area() {
        // Candidate: [100, 100, 200, 200] (Area = 40,000)
        // Rect 1: [50, 50, 100, 100] -> Overlap [100..150, 100..150] = 50 * 50 = 2500
        // Rect 2: [200, 200, 200, 200] -> Overlap [200..300, 200..300] = 100 * 100 = 10000
        // Total = 12,500
        let rects = [50_i32, 50, 100, 100, 200, 200, 200, 200];
        let mut overlap = 0_u64;

        let s1 = unsafe {
            wincare_core_calculate_smart_overlap(
                100,
                100,
                200,
                200,
                rects.as_ptr(),
                2,
                &mut overlap,
            )
        };
        assert_eq!(Status::Ok.code(), s1);
        assert_eq!(12500, overlap);

        // Disjoint rectangle -> 0 overlap
        let disjoint = [500_i32, 500, 100, 100];
        let s2 = unsafe {
            wincare_core_calculate_smart_overlap(
                100,
                100,
                200,
                200,
                disjoint.as_ptr(),
                1,
                &mut overlap,
            )
        };
        assert_eq!(Status::Ok.code(), s2);
        assert_eq!(0, overlap);
    }

    #[test]
    fn decay_affinity_score_half_life_decay() {
        let mut score = 0.0_f64;

        // Exactly one half-life (1000ms out of 1000ms) -> 4.0 * 0.5 = 2.0
        let s1 = unsafe { wincare_core_decay_affinity_score(4.0, 1000, 1000, &mut score) };
        assert_eq!(Status::Ok.code(), s1);
        assert!((score - 2.0).abs() < 1e-6);

        // Two half-lives (2000ms out of 1000ms) -> 4.0 * 0.25 = 1.0
        let s2 = unsafe { wincare_core_decay_affinity_score(4.0, 2000, 1000, &mut score) };
        assert_eq!(Status::Ok.code(), s2);
        assert!((score - 1.0).abs() < 1e-6);
    }

    #[test]
    fn evaluate_rate_limit_tokens_fulfills_and_throttles() {
        let mut take_bytes = 0_u64;
        let mut wait_nanos = 0_u64;
        let mut new_alloc = 0_u64;

        // 1 MB/s, request 10 KB, max burst 64 KB, idle = 0 -> immediate fulfillment
        let s1 = unsafe {
            wincare_core_evaluate_rate_limit_tokens(
                1_000_000_000,
                1_000_000_000,
                1_048_576,
                10_240,
                8_192,
                65_536,
                &mut take_bytes,
                &mut wait_nanos,
                &mut new_alloc,
            )
        };
        assert_eq!(Status::Ok.code(), s1);
        assert_eq!(10_240, take_bytes);
        assert_eq!(0, wait_nanos);
    }

    #[test]
    fn unlink_posix_deletes_file_and_reports_status() {
        let mut temp = tempfile::NamedTempFile::new().unwrap();
        temp.write_all(b"test_unlink_posix").unwrap();
        let path = temp.path().to_str().unwrap().to_string();

        let s1 = unsafe { wincare_core_unlink_posix(path.as_ptr(), path.len()) };
        assert_eq!(Status::Ok.code(), s1);
        assert!(!Path::new(&path).exists());

        // Second unlink should report NotFound
        let s2 = unsafe { wincare_core_unlink_posix(path.as_ptr(), path.len()) };
        assert_eq!(Status::NotFound.code(), s2);

        // Null pointer check
        let s_null = unsafe { wincare_core_unlink_posix(std::ptr::null(), 0) };
        assert_eq!(Status::NullPointer.code(), s_null);
    }

    #[test]
    fn secure_trash_memory_three_pass_and_burn_stack() {
        let mut buffer = [0x42_u8; 128];
        let s1 = unsafe { wincare_core_secure_trash_memory(buffer.as_mut_ptr(), buffer.len()) };
        assert_eq!(Status::Ok.code(), s1);
        // After 3 passes (0x55, 0xAA, 0x00), buffer must be all zeros
        assert!(buffer.iter().all(|&b| b == 0));

        let s_null = unsafe { wincare_core_secure_trash_memory(std::ptr::null_mut(), 16) };
        assert_eq!(Status::NullPointer.code(), s_null);

        let s_burn = unsafe { wincare_core_burn_stack(256) };
        assert_eq!(Status::Ok.code(), s_burn);
    }

    #[test]
    fn boyer_moore_search_matches_and_handles_missing() {
        let text = b"The quick brown fox jumps over the lazy dog";
        let pattern = b"fox";
        let mut index = 0_i64;

        let s1 = unsafe {
            wincare_core_boyer_moore_search(
                text.as_ptr(),
                text.len(),
                pattern.as_ptr(),
                pattern.len(),
                &mut index,
            )
        };
        assert_eq!(Status::Ok.code(), s1);
        assert_eq!(16, index);

        // Pattern not found -> -1
        let missing_pattern = b"cat";
        let s2 = unsafe {
            wincare_core_boyer_moore_search(
                text.as_ptr(),
                text.len(),
                missing_pattern.as_ptr(),
                missing_pattern.len(),
                &mut index,
            )
        };
        assert_eq!(Status::Ok.code(), s2);
        assert_eq!(-1, index);

        // Pattern longer than text -> -1
        let long_pattern = [b'a'; 100];
        let s3 = unsafe {
            wincare_core_boyer_moore_search(
                text.as_ptr(),
                text.len(),
                long_pattern.as_ptr(),
                long_pattern.len(),
                &mut index,
            )
        };
        assert_eq!(Status::Ok.code(), s3);
        assert_eq!(-1, index);
    }

    #[test]
    fn crc16_ccitt_computes_expected_checksum() {
        let data = b"123456789";
        let mut crc = 0_u16;

        // Standard CCITT test vector for "123456789" with initial 0x0000 is 0x31C3
        let s1 = unsafe { wincare_core_crc16_ccitt(data.as_ptr(), data.len(), 0x0000, &mut crc) };
        assert_eq!(Status::Ok.code(), s1);
        assert_eq!(0x31C3, crc);

        // Empty data returns initial crc
        let s2 = unsafe { wincare_core_crc16_ccitt(std::ptr::null(), 0, 0xFFFF, &mut crc) };
        assert_eq!(Status::Ok.code(), s2);
        assert_eq!(0xFFFF, crc);
    }

    #[test]
    fn calculate_relative_window_delta_move_and_resize() {
        let mut rect = [0_i32; 4];

        // Mode 1: Move from (100, 100) to (150, 120), delta (+50, +20)
        // Window initially at (200, 300) size 800x600 -> (250, 320, 800, 600)
        let s1 = unsafe {
            wincare_core_calculate_relative_window_delta(
                100,
                100,
                150,
                120,
                200,
                300,
                800,
                600,
                1,
                rect.as_mut_ptr(),
            )
        };
        assert_eq!(Status::Ok.code(), s1);
        assert_eq!([250, 320, 800, 600], rect);

        // Mode 2: Resize from (100, 100) to (150, 120), delta (+50, +20)
        // Window initially at (200, 300) size 800x600 -> (200, 300, 850, 620)
        let s2 = unsafe {
            wincare_core_calculate_relative_window_delta(
                100,
                100,
                150,
                120,
                200,
                300,
                800,
                600,
                2,
                rect.as_mut_ptr(),
            )
        };
        assert_eq!(Status::Ok.code(), s2);
        assert_eq!([200, 300, 850, 620], rect);

        // Mode 3: Both move & resize -> (250, 320, 850, 620)
        let s3 = unsafe {
            wincare_core_calculate_relative_window_delta(
                100,
                100,
                150,
                120,
                200,
                300,
                800,
                600,
                3,
                rect.as_mut_ptr(),
            )
        };
        assert_eq!(Status::Ok.code(), s3);
        assert_eq!([250, 320, 850, 620], rect);

        // Invalid mode -> InvalidArgument
        let s4 = unsafe {
            wincare_core_calculate_relative_window_delta(
                100,
                100,
                150,
                120,
                200,
                300,
                800,
                600,
                99,
                rect.as_mut_ptr(),
            )
        };
        assert_eq!(Status::InvalidArgument.code(), s4);
    }

    #[test]
    fn calculate_entropy_computes_expected_shannon_values() {
        let mut entropy = 0.0_f64;

        // Empty data -> 0.0
        let s1 = unsafe { wincare_core_calculate_entropy(std::ptr::null(), 0, &mut entropy) };
        assert_eq!(Status::Ok.code(), s1);
        assert_eq!(0.0, entropy);

        // Constant byte sequence -> 0.0
        let constant_data = [0x55_u8; 128];
        let s2 = unsafe {
            wincare_core_calculate_entropy(
                constant_data.as_ptr(),
                constant_data.len(),
                &mut entropy,
            )
        };
        assert_eq!(Status::Ok.code(), s2);
        assert_eq!(0.0, entropy);

        // Half 'A' and half 'B' -> exactly 1.0 bit of entropy
        let mut binary_data = [b'A'; 100];
        for b in &mut binary_data[50..] {
            *b = b'B';
        }
        let s3 = unsafe {
            wincare_core_calculate_entropy(binary_data.as_ptr(), binary_data.len(), &mut entropy)
        };
        assert_eq!(Status::Ok.code(), s3);
        assert!((entropy - 1.0).abs() < 1e-6);

        // Uniform 256 unique bytes -> exactly 8.0 bits
        let mut all_bytes = [0_u8; 256];
        for (i, b) in all_bytes.iter_mut().enumerate() {
            *b = i as u8;
        }
        let s4 = unsafe {
            wincare_core_calculate_entropy(all_bytes.as_ptr(), all_bytes.len(), &mut entropy)
        };
        assert_eq!(Status::Ok.code(), s4);
        assert!((entropy - 8.0).abs() < 1e-6);

        // Null out pointer -> NullPointer
        let s_null = unsafe {
            wincare_core_calculate_entropy(all_bytes.as_ptr(), 256, std::ptr::null_mut())
        };
        assert_eq!(Status::NullPointer.code(), s_null);
    }

    #[test]
    fn query_file_identity_retrieves_ids_and_rejects_missing() {
        let mut temp = tempfile::NamedTempFile::new().unwrap();
        temp.write_all(b"identity_verification_test").unwrap();
        let path = temp.path().to_str().unwrap().to_string();

        let mut vol = 0_u64;
        let mut id_high = 0_u64;
        let mut id_low = 0_u64;

        let s1 = unsafe {
            wincare_core_query_file_identity(
                path.as_ptr(),
                path.len(),
                &mut vol,
                &mut id_high,
                &mut id_low,
            )
        };
        assert_eq!(Status::Ok.code(), s1);
        assert!(vol > 0);

        // Non-existent path -> NotFound
        let missing = b"C:\\path\\does\\not\\exist\\file.txt";
        let s2 = unsafe {
            wincare_core_query_file_identity(
                missing.as_ptr(),
                missing.len(),
                &mut vol,
                &mut id_high,
                &mut id_low,
            )
        };
        assert_eq!(Status::NotFound.code(), s2);

        // Null pointer -> NullPointer
        let s_null = unsafe {
            wincare_core_query_file_identity(
                std::ptr::null(),
                0,
                &mut vol,
                &mut id_high,
                &mut id_low,
            )
        };
        assert_eq!(Status::NullPointer.code(), s_null);
    }
}

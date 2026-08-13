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

#[cfg(target_os = "windows")]
#[allow(non_snake_case, clippy::upper_case_acronyms)]
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

    #[link(name = "kernel32")]
    // SAFETY: These Win32 C-ABI extern declarations match the official Windows SDK signatures exactly. Callers must uphold each function's documented argument constraints.
    unsafe extern "system" {
        pub fn GetSystemInfo(lpSystemInfo: *mut SYSTEM_INFO);
        pub fn GlobalMemoryStatusEx(lpBuffer: *mut MEMORYSTATUSEX) -> i32;
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
}

const ABI_VERSION: u32 = 1;
const VERSION: &[u8] = b"2.4.0";
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
        let total = accumulate_dir_size(path);
        match total {
            Ok(bytes) => {
                // SAFETY: size_out is non-null (checked above); caller guarantees valid writable u64 memory.
                unsafe { size_out.write(bytes) };
                Status::Ok.code()
            }
            Err(_) => Status::IoError.code(),
        }
    }))
    .unwrap_or(Status::InternalError.code())
}

const MAX_DIR_ENTRIES: usize = 500_000;

fn accumulate_dir_size(path: &Path) -> io::Result<u64> {
    let mut total = 0_u64;
    let mut entries_count = 0_usize;
    let mut pending = vec![path.to_path_buf()];

    while let Some(current) = pending.pop() {
        entries_count = entries_count.saturating_add(1);
        if entries_count > MAX_DIR_ENTRIES {
            break;
        }

        let metadata = match std::fs::symlink_metadata(&current) {
            Ok(meta) => meta,
            Err(_) => continue,
        };

        if metadata.file_type().is_symlink() || is_reparse_point(&metadata) {
            continue;
        }

        if metadata.is_dir() {
            if let Ok(entries) = std::fs::read_dir(&current) {
                for entry in entries.flatten() {
                    pending.push(entry.path());
                }
            }
        } else if metadata.is_file() {
            total = total.saturating_add(metadata.len());
        }
    }

    Ok(total)
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

        let os_build = read_registry_os_build().unwrap_or_else(|| "unknown".to_owned());

        let json = format!(
            r#"{{"logical_cpus":{logical_cpus},"total_physical_memory_bytes":{total},"available_physical_memory_bytes":{avail},"os_build":"{os_build}"}}"#,
            logical_cpus = logical_cpus,
            total = ms.ullTotalPhys,
            avail = ms.ullAvailPhys,
            os_build = os_build,
        );
        let len = json.len().min(buf.len());
        buf[..len].copy_from_slice(&json.as_bytes()[..len]);
        Some(&buf[..len])
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
fn read_registry_os_build() -> Option<String> {
    use self::win32::*;

    // SAFETY: Win32 registry APIs are called with valid null-terminated UTF-16 string pointers and proper buffer lengths. HKEYs are managed correctly.
    unsafe {
        let mut hkey: HKEY = std::ptr::null_mut();
        let subkey: Vec<u16> = "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\0"
            .encode_utf16()
            .collect();
        if RegOpenKeyExW(HKEY_LOCAL_MACHINE, subkey.as_ptr(), 0, KEY_READ, &mut hkey) != 0 {
            return None;
        }

        let value_name: Vec<u16> = "CurrentBuildNumber\0".encode_utf16().collect();
        // Use an aligned [u16] buffer to avoid undefined behaviour from u8-to-u16 pointer casting.
        let mut buf = [0u16; 64];
        let mut buf_len = (buf.len() * std::mem::size_of::<u16>()) as u32;
        let mut value_type = REG_SZ;

        let status = RegQueryValueExW(
            hkey,
            value_name.as_ptr(),
            std::ptr::null_mut(),
            &mut value_type,
            buf.as_mut_ptr() as *mut u8,
            &mut buf_len,
        );

        let _ = RegCloseKey(hkey);

        // Require success, REG_SZ type, at least one u16 code unit, and even length.
        if status == 0 && value_type == REG_SZ && buf_len >= 2 && buf_len % 2 == 0 {
            // Exclude the null-terminator code unit.
            let u16_count = (buf_len as usize / 2).saturating_sub(1);
            return String::from_utf16(&buf[..u16_count]).ok();
        }
        None
    }
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
        let dir = std::env::temp_dir().join("wc_test_empty_dir_size");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.to_str().unwrap();
        let mut out: u64 = 99;
        let r = unsafe { wincare_core_dir_size(path.as_ptr(), path.len(), &mut out) };
        assert_eq!(r, 0);
        assert_eq!(out, 0);
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn dir_size_on_known_file_dir_returns_correct_size() {
        use std::io::Write;
        let dir = std::env::temp_dir().join("wc_test_dir_size_known");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        std::fs::File::create(dir.join("a.txt"))
            .unwrap()
            .write_all(&[0u8; 1024])
            .unwrap();
        let path = dir.to_str().unwrap();
        let mut out: u64 = 0;
        let r = unsafe { wincare_core_dir_size(path.as_ptr(), path.len(), &mut out) };
        assert_eq!(r, 0);
        assert_eq!(out, 1024);
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn dir_size_handles_deep_directory_tree_without_recursion() {
        let root = std::env::temp_dir().join("wc_test_dir_size_deep");
        let _ = std::fs::remove_dir_all(&root);
        let mut current = root.clone();
        std::fs::create_dir_all(&current).unwrap();
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
        let _ = std::fs::remove_dir_all(&root);
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
}

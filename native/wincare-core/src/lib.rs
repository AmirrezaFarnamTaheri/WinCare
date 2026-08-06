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
}

impl Status {
    const fn code(self) -> i32 {
        self as i32
    }
}

/// Returns the version of the exported ABI.
#[unsafe(no_mangle)]
pub extern "C" fn wincare_core_abi_version() -> u32 {
    ABI_VERSION
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
}

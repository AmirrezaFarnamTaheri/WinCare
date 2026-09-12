//! Native safe temp file cleaner for WinCare.
#![allow(missing_docs)]

use std::io;
use std::path::{Path, PathBuf};

/// A cleanup failure without an operating-system error code, such as a rejected
/// reparse-point root. `error_code` otherwise contains the first OS error code.
const CLEANUP_UNCLASSIFIED_ERROR: i32 = -1;

#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct NativeCleanResult {
    pub bytes_reclaimed: u64,
    pub files_removed: u32,
    pub error_code: i32,
}

/// Cleans safe user temp files, supporting dry-run inspection mode.
///
/// # Safety
///
/// `out` must point to a valid, properly aligned, writable `NativeCleanResult`.
pub unsafe fn clean_temp_files_internal(dry_run: u8, out: *mut NativeCleanResult) -> i32 {
    if out.is_null() {
        return 1; // NullPointer
    }

    let mut reclaimed: u64 = 0;
    let mut count: u32 = 0;
    let mut err_code: i32 = 0;

    let temp_dirs = get_safe_temp_directories();
    for dir in temp_dirs {
        clean_directory_contents(
            &dir,
            dry_run != 0,
            &mut reclaimed,
            &mut count,
            &mut err_code,
        );
    }

    // SAFETY: Verified non-null above.
    unsafe {
        out.write(NativeCleanResult {
            bytes_reclaimed: reclaimed,
            files_removed: count,
            error_code: err_code,
        });
    }

    0
}

fn get_safe_temp_directories() -> Vec<PathBuf> {
    let mut dirs = Vec::new();
    if let Ok(user_temp) = std::env::var("TEMP") {
        dirs.push(PathBuf::from(user_temp));
    } else {
        dirs.push(std::env::temp_dir());
    }
    dirs
}

#[cfg(not(target_os = "windows"))]
fn clean_directory_contents(
    dir: &Path,
    dry_run: bool,
    reclaimed: &mut u64,
    count: &mut u32,
    error_code: &mut i32,
) {
    // Check the root itself with symlink_metadata. `Path::is_dir` and
    // `read_dir` follow junctions, so checking only children would let a
    // reparse-point TEMP root redirect cleanup outside the intended location.
    let root_metadata = match std::fs::symlink_metadata(dir) {
        Ok(metadata) => metadata,
        Err(error) => {
            record_error(error_code, &error);
            return;
        }
    };
    if is_reparse_point(&root_metadata) || !root_metadata.is_dir() {
        record_unclassified_error(error_code);
        return;
    }

    let entries = match std::fs::read_dir(dir) {
        Ok(entries) => entries,
        Err(error) => {
            record_error(error_code, &error);
            return;
        }
    };

    for entry in entries {
        let entry = match entry {
            Ok(entry) => entry,
            Err(error) => {
                record_error(error_code, &error);
                continue;
            }
        };
        let path = entry.path();
        let metadata = match std::fs::symlink_metadata(&path) {
            Ok(metadata) => metadata,
            Err(error) => {
                record_error(error_code, &error);
                continue;
            }
        };
        if is_reparse_point(&metadata) {
            continue;
        }

        if metadata.is_file() {
            if let Err(error) = try_clean_file(&path, metadata.len(), dry_run, reclaimed, count) {
                record_error(error_code, &error);
            }
        } else if metadata.is_dir() {
            match std::fs::read_dir(&path) {
                Ok(sub_entries) => {
                    for sub in sub_entries {
                        let sub = match sub {
                            Ok(sub) => sub,
                            Err(error) => {
                                record_error(error_code, &error);
                                continue;
                            }
                        };
                        let sub_path = sub.path();
                        let sub_meta = match std::fs::symlink_metadata(&sub_path) {
                            Ok(metadata) => metadata,
                            Err(error) => {
                                record_error(error_code, &error);
                                continue;
                            }
                        };
                        if is_reparse_point(&sub_meta) {
                            continue;
                        }

                        if sub_meta.is_file()
                            && let Err(error) =
                                try_clean_file(&sub_path, sub_meta.len(), dry_run, reclaimed, count)
                        {
                            record_error(error_code, &error);
                        }
                    }
                }
                Err(error) => record_error(error_code, &error),
            }
            if !dry_run && let Err(error) = std::fs::remove_dir(&path) {
                record_error(error_code, &error);
            }
        }
    }
}

fn record_error(error_code: &mut i32, error: &io::Error) {
    if *error_code == 0 {
        *error_code = error.raw_os_error().unwrap_or(CLEANUP_UNCLASSIFIED_ERROR);
    }
}

#[cfg(not(target_os = "windows"))]
fn record_unclassified_error(error_code: &mut i32) {
    if *error_code == 0 {
        *error_code = CLEANUP_UNCLASSIFIED_ERROR;
    }
}

#[cfg(any(not(target_os = "windows"), test))]
#[inline]
fn try_clean_file(
    path: &Path,
    size: u64,
    dry_run: bool,
    reclaimed: &mut u64,
    count: &mut u32,
) -> io::Result<()> {
    if !dry_run {
        std::fs::remove_file(path)?;
    }
    *reclaimed = reclaimed.saturating_add(size);
    *count = count.saturating_add(1);
    Ok(())
}

// Windows path APIs cannot make the check-then-delete sequence safe when a
// directory can be replaced by a junction between calls. Enumerate from an
// already-open directory handle, reopen each child relative to that handle
// with OBJ_DONT_REPARSE, compare its file identity to the enumerated identity,
// then delete the exact opened handle. This deliberately retains the cleaner's
// shallow (root + one directory level) contract while closing its path race.
#[cfg(target_os = "windows")]
fn clean_directory_contents(
    dir: &Path,
    dry_run: bool,
    reclaimed: &mut u64,
    count: &mut u32,
    error_code: &mut i32,
) {
    if let Err(error) = nofollow_windows::clean(dir, dry_run, reclaimed, count) {
        record_error(error_code, &error);
    }
}

#[cfg(target_os = "windows")]
mod nofollow_windows {
    use super::*;
    use std::ffi::c_void;
    use std::mem::{offset_of, size_of, zeroed};
    use std::os::windows::ffi::OsStrExt;
    use std::ptr;

    type Handle = *mut c_void;
    const INVALID_HANDLE_VALUE: Handle = -1_isize as Handle;
    const FILE_LIST_DIRECTORY: u32 = 0x0001;
    const FILE_READ_ATTRIBUTES: u32 = 0x0080;
    const DELETE: u32 = 0x0001_0000;
    const SYNCHRONIZE: u32 = 0x0010_0000;
    const FILE_SHARE_ALL: u32 = 0x0007;
    const OPEN_EXISTING: u32 = 3;
    const FILE_FLAG_BACKUP_SEMANTICS: u32 = 0x0200_0000;
    const FILE_FLAG_OPEN_REPARSE_POINT: u32 = 0x0020_0000;
    const FILE_ATTRIBUTE_REPARSE_POINT: u32 = 0x0400;
    const OBJ_CASE_INSENSITIVE: u32 = 0x0040;
    const OBJ_DONT_REPARSE: u32 = 0x1000;
    const FILE_OPEN: u32 = 1;
    const FILE_DIRECTORY_FILE: u32 = 0x0001;
    const FILE_NON_DIRECTORY_FILE: u32 = 0x0040;
    const FILE_SYNCHRONOUS_IO_NONALERT: u32 = 0x0020;
    const FILE_OPEN_REPARSE_POINT: u32 = 0x0020_0000;
    const FILE_ID_BOTH_DIRECTORY_INFORMATION: u32 = 37;
    const STATUS_BUFFER_OVERFLOW: i32 = 0x8000_0005_u32 as i32;
    const STATUS_NO_MORE_FILES: i32 = 0x8000_0006_u32 as i32;
    const FILE_DISPOSITION_INFO_EX: i32 = 21;
    const FILE_DISPOSITION_FLAG_DELETE: u32 = 0x0001;

    #[repr(C)]
    struct IoStatusBlock {
        status: i32,
        information: usize,
    }
    #[repr(C)]
    struct UnicodeString {
        length: u16,
        maximum_length: u16,
        buffer: *mut u16,
    }
    #[repr(C)]
    struct ObjectAttributes {
        length: u32,
        root_directory: Handle,
        object_name: *mut UnicodeString,
        attributes: u32,
        security_descriptor: *mut c_void,
        security_quality_of_service: *mut c_void,
    }
    #[repr(C)]
    struct ByHandleFileInformation {
        attributes: u32,
        creation_low: u32,
        creation_high: u32,
        access_low: u32,
        access_high: u32,
        write_low: u32,
        write_high: u32,
        volume_serial: u32,
        size_high: u32,
        size_low: u32,
        links: u32,
        index_high: u32,
        index_low: u32,
    }
    #[repr(C)]
    struct FileDispositionInfoEx {
        flags: u32,
    }
    #[repr(C)]
    struct FileIdBothDirectoryInfo {
        next_entry_offset: u32,
        file_index: u32,
        creation_time: i64,
        last_access_time: i64,
        last_write_time: i64,
        change_time: i64,
        end_of_file: i64,
        allocation_size: i64,
        file_attributes: u32,
        file_name_length: u32,
        ea_size: u32,
        short_name_length: u8,
        short_name: [u16; 12],
        file_id: i64,
        file_name: [u16; 0],
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
        fn GetFileInformationByHandle(handle: Handle, info: *mut ByHandleFileInformation) -> i32;
        fn SetFileInformationByHandle(
            handle: Handle,
            class: i32,
            info: *const c_void,
            len: u32,
        ) -> i32;
    }
    #[link(name = "ntdll")]
    unsafe extern "system" {
        fn NtCreateFile(
            handle: *mut Handle,
            access: u32,
            attributes: *mut ObjectAttributes,
            iosb: *mut IoStatusBlock,
            allocation: *mut i64,
            file_attributes: u32,
            share: u32,
            disposition: u32,
            options: u32,
            ea: *mut c_void,
            ea_len: u32,
        ) -> i32;
        fn NtQueryDirectoryFile(
            handle: Handle,
            event: Handle,
            apc: *mut c_void,
            apc_context: *mut c_void,
            iosb: *mut IoStatusBlock,
            information: *mut c_void,
            length: u32,
            class: u32,
            single: u8,
            file_name: *mut UnicodeString,
            restart: u8,
        ) -> i32;
    }

    struct OwnedHandle(Handle);
    impl Drop for OwnedHandle {
        fn drop(&mut self) {
            unsafe {
                let _ = CloseHandle(self.0);
            }
        }
    }

    pub fn clean(dir: &Path, dry: bool, reclaimed: &mut u64, count: &mut u32) -> io::Result<()> {
        let root = open_root(dir)?;
        clean_open_directory(&root, dry, reclaimed, count, true)
    }

    fn open_root(path: &Path) -> io::Result<OwnedHandle> {
        let wide: Vec<u16> = path.as_os_str().encode_wide().chain(Some(0)).collect();
        let handle = unsafe {
            CreateFileW(
                wide.as_ptr(),
                FILE_LIST_DIRECTORY | FILE_READ_ATTRIBUTES | SYNCHRONIZE,
                FILE_SHARE_ALL,
                ptr::null_mut(),
                OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
                ptr::null_mut(),
            )
        };
        if handle == INVALID_HANDLE_VALUE {
            return Err(io::Error::last_os_error());
        }
        let handle = OwnedHandle(handle);
        let info = information(&handle)?;
        if info.attributes & FILE_ATTRIBUTE_REPARSE_POINT != 0 {
            return Err(io::Error::other("cleanup root is a reparse point"));
        }
        Ok(handle)
    }

    fn clean_open_directory(
        dir: &OwnedHandle,
        dry: bool,
        reclaimed: &mut u64,
        count: &mut u32,
        allow_children: bool,
    ) -> io::Result<()> {
        let mut restart = 1u8;
        let mut buffer = vec![0u8; 64 * 1024];
        loop {
            let mut iosb: IoStatusBlock = unsafe { zeroed() };
            let status = unsafe {
                NtQueryDirectoryFile(
                    dir.0,
                    ptr::null_mut(),
                    ptr::null_mut(),
                    ptr::null_mut(),
                    &mut iosb,
                    buffer.as_mut_ptr().cast(),
                    buffer.len() as u32,
                    FILE_ID_BOTH_DIRECTORY_INFORMATION,
                    0,
                    ptr::null_mut(),
                    restart,
                )
            };
            restart = 0;
            if status == STATUS_NO_MORE_FILES {
                break;
            }
            if status < 0 && status != STATUS_BUFFER_OVERFLOW {
                return Err(nt_status_error(status));
            }
            let mut offset = 0usize;
            while offset < iosb.information {
                let entry = unsafe {
                    &*(buffer
                        .as_ptr()
                        .add(offset)
                        .cast::<FileIdBothDirectoryInfo>())
                };
                let name_offset = offset_of!(FileIdBothDirectoryInfo, file_name);
                let name_bytes = entry.file_name_length as usize;
                if name_bytes % 2 != 0 || name_offset + name_bytes > buffer.len() {
                    return Err(io::Error::other("invalid directory entry"));
                }
                let name = unsafe {
                    std::slice::from_raw_parts(
                        buffer.as_ptr().add(offset + name_offset).cast::<u16>(),
                        name_bytes / 2,
                    )
                };
                if name != ['.' as u16]
                    && name != ['.' as u16, '.' as u16]
                    && entry.file_attributes & FILE_ATTRIBUTE_REPARSE_POINT == 0
                {
                    if entry.file_attributes & 0x10 != 0 && allow_children {
                        let child = open_relative(dir, name, true)?;
                        if information(&child)?.index() == entry.file_id as u64 {
                            clean_open_directory(&child, dry, reclaimed, count, false)?;
                            if !dry {
                                delete_handle(&child)?;
                            }
                        }
                    } else if entry.file_attributes & 0x10 == 0 {
                        let child = open_relative(dir, name, false)?;
                        let info = information(&child)?;
                        if info.index() == entry.file_id as u64
                            && info.attributes & FILE_ATTRIBUTE_REPARSE_POINT == 0
                        {
                            let size = info.size();
                            if !dry {
                                delete_handle(&child)?;
                            }
                            *reclaimed = reclaimed.saturating_add(size);
                            *count = count.saturating_add(1);
                        }
                    }
                }
                let next = entry.next_entry_offset as usize;
                if next == 0 {
                    break;
                }
                offset = offset
                    .checked_add(next)
                    .ok_or_else(|| io::Error::other("directory offset overflow"))?;
            }
        }
        Ok(())
    }

    fn open_relative(
        parent: &OwnedHandle,
        name: &[u16],
        directory: bool,
    ) -> io::Result<OwnedHandle> {
        if name.len() > (u16::MAX as usize / 2)
            || name
                .iter()
                .any(|c| *c == 0 || *c == b'\\' as u16 || *c == b'/' as u16)
        {
            return Err(io::Error::other("unsafe child name"));
        }
        let mut name_copy = name.to_vec();
        let mut unicode = UnicodeString {
            length: (name_copy.len() * 2) as u16,
            maximum_length: (name_copy.len() * 2) as u16,
            buffer: name_copy.as_mut_ptr(),
        };
        let mut attributes = ObjectAttributes {
            length: size_of::<ObjectAttributes>() as u32,
            root_directory: parent.0,
            object_name: &mut unicode,
            attributes: OBJ_CASE_INSENSITIVE | OBJ_DONT_REPARSE,
            security_descriptor: ptr::null_mut(),
            security_quality_of_service: ptr::null_mut(),
        };
        let mut handle = ptr::null_mut();
        let mut iosb: IoStatusBlock = unsafe { zeroed() };
        let options = FILE_SYNCHRONOUS_IO_NONALERT
            | FILE_OPEN_REPARSE_POINT
            | if directory {
                FILE_DIRECTORY_FILE
            } else {
                FILE_NON_DIRECTORY_FILE
            };
        let status = unsafe {
            NtCreateFile(
                &mut handle,
                DELETE
                    | FILE_READ_ATTRIBUTES
                    | SYNCHRONIZE
                    | if directory { FILE_LIST_DIRECTORY } else { 0 },
                &mut attributes,
                &mut iosb,
                ptr::null_mut(),
                0,
                FILE_SHARE_ALL,
                FILE_OPEN,
                options,
                ptr::null_mut(),
                0,
            )
        };
        if status < 0 {
            return Err(nt_status_error(status));
        }
        Ok(OwnedHandle(handle))
    }

    fn information(handle: &OwnedHandle) -> io::Result<ByHandleFileInformation> {
        let mut info: ByHandleFileInformation = unsafe { zeroed() };
        if unsafe { GetFileInformationByHandle(handle.0, &mut info) } == 0 {
            return Err(io::Error::last_os_error());
        }
        Ok(info)
    }
    impl ByHandleFileInformation {
        fn index(&self) -> u64 {
            (u64::from(self.index_high) << 32) | u64::from(self.index_low)
        }
        fn size(&self) -> u64 {
            (u64::from(self.size_high) << 32) | u64::from(self.size_low)
        }
    }
    fn delete_handle(handle: &OwnedHandle) -> io::Result<()> {
        let info = FileDispositionInfoEx {
            flags: FILE_DISPOSITION_FLAG_DELETE,
        };
        if unsafe {
            SetFileInformationByHandle(
                handle.0,
                FILE_DISPOSITION_INFO_EX,
                (&raw const info).cast(),
                size_of::<FileDispositionInfoEx>() as u32,
            )
        } == 0
        {
            return Err(io::Error::last_os_error());
        }
        Ok(())
    }

    fn nt_status_error(status: i32) -> io::Error {
        // NT native calls do not promise to set the thread's Win32 last-error
        // value. Preserve the actual status so the C ABI result cannot report
        // a stale, unrelated error code.
        io::Error::from_raw_os_error(status)
    }
}

#[cfg(not(target_os = "windows"))]
fn is_reparse_point(_: &std::fs::Metadata) -> bool {
    false
}

#[cfg(all(test, target_os = "windows"))]
mod tests {
    use super::*;
    use std::process::Command;
    use std::time::{SystemTime, UNIX_EPOCH};

    #[test]
    fn cleaner_does_not_traverse_junctions() {
        let nonce = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .expect("clock should be after Unix epoch")
            .as_nanos();
        let base = std::env::temp_dir().join(format!("wincare-cleaner-junction-{nonce}"));
        let cleanup_root = base.join("cleanup-root");
        let outside = base.join("outside");
        let junction = cleanup_root.join("outside-link");
        let sentinel = outside.join("sentinel.txt");

        std::fs::create_dir_all(&cleanup_root).expect("cleanup root should be created");
        std::fs::create_dir_all(&outside).expect("outside target should be created");
        std::fs::write(&sentinel, b"keep me").expect("sentinel should be created");

        let status = Command::new("cmd")
            .args(["/C", "mklink", "/J"])
            .arg(&junction)
            .arg(&outside)
            .status()
            .expect("mklink should start");
        assert!(status.success(), "junction creation should succeed");

        let mut reclaimed = 0;
        let mut count = 0;
        let mut error_code = 0;
        clean_directory_contents(
            &cleanup_root,
            false,
            &mut reclaimed,
            &mut count,
            &mut error_code,
        );

        assert!(
            sentinel.exists(),
            "cleaner must not traverse the junction target"
        );
        assert_eq!(0, reclaimed);
        assert_eq!(0, count);
        assert_eq!(0, error_code);

        let _ = std::fs::remove_dir(&junction);
        let _ = std::fs::remove_dir_all(&base);
    }

    #[test]
    fn cleaner_rejects_a_junction_as_its_root() {
        let temp = tempfile::tempdir().expect("temporary directory should be created");
        let outside = temp.path().join("outside");
        let junction = temp.path().join("temp-root-link");
        let sentinel = outside.join("sentinel.txt");

        std::fs::create_dir_all(&outside).expect("outside target should be created");
        std::fs::write(&sentinel, b"keep me").expect("sentinel should be created");
        let status = Command::new("cmd")
            .args(["/C", "mklink", "/J"])
            .arg(&junction)
            .arg(&outside)
            .status()
            .expect("mklink should start");
        assert!(status.success(), "junction creation should succeed");

        let mut reclaimed = 0;
        let mut count = 0;
        let mut error_code = 0;
        clean_directory_contents(
            &junction,
            false,
            &mut reclaimed,
            &mut count,
            &mut error_code,
        );

        assert!(
            sentinel.exists(),
            "cleaner must not traverse a junction root"
        );
        assert_eq!(0, reclaimed);
        assert_eq!(0, count);
        assert_eq!(CLEANUP_UNCLASSIFIED_ERROR, error_code);

        let _ = std::fs::remove_dir(&junction);
    }

    #[test]
    fn cleaner_reports_a_missing_root() {
        let temp = tempfile::tempdir().expect("temporary directory should be created");
        let missing = temp.path().join("missing-root");
        let mut reclaimed = 0;
        let mut count = 0;
        let mut error_code = 0;

        clean_directory_contents(&missing, false, &mut reclaimed, &mut count, &mut error_code);

        assert_eq!(0, reclaimed);
        assert_eq!(0, count);
        assert_ne!(0, error_code, "missing roots must not look successful");
    }

    #[test]
    fn cleaner_preserves_a_file_removal_failure() {
        let temp = tempfile::tempdir().expect("temporary directory should be created");
        let missing = temp.path().join("missing-file");
        let mut reclaimed = 0;
        let mut count = 0;

        let error = try_clean_file(&missing, 1, false, &mut reclaimed, &mut count)
            .expect_err("removing a missing file must fail");

        assert_eq!(io::ErrorKind::NotFound, error.kind());
        assert_eq!(0, reclaimed);
        assert_eq!(0, count);
    }

    #[test]
    fn cleaner_dry_run_accounts_for_a_handle_verified_file_without_deleting_it() {
        let temp = tempfile::tempdir().expect("temporary directory should be created");
        let file = temp.path().join("candidate.tmp");
        std::fs::write(&file, b"four").expect("candidate should be created");
        let mut reclaimed = 0;
        let mut count = 0;
        let mut error_code = 0;

        clean_directory_contents(
            temp.path(),
            true,
            &mut reclaimed,
            &mut count,
            &mut error_code,
        );

        assert!(file.exists(), "dry-run must never delete the candidate");
        assert_eq!(4, reclaimed);
        assert_eq!(1, count);
        assert_eq!(0, error_code);
    }
}

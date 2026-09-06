//! Native safe temp file cleaner for WinCare.
#![allow(missing_docs)]

use std::path::{Path, PathBuf};

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
    let err_code: i32 = 0;

    let temp_dirs = get_safe_temp_directories();
    for dir in temp_dirs {
        if !dir.exists() || !dir.is_dir() {
            continue;
        }

        clean_directory_contents(&dir, dry_run != 0, &mut reclaimed, &mut count);
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
        let p = PathBuf::from(user_temp);
        if p.exists() {
            dirs.push(p);
        }
    } else {
        dirs.push(std::env::temp_dir());
    }
    dirs
}

fn clean_directory_contents(dir: &Path, dry_run: bool, reclaimed: &mut u64, count: &mut u32) {
    let Ok(entries) = std::fs::read_dir(dir) else {
        return;
    };

    for entry in entries.flatten() {
        let path = entry.path();
        if let Ok(metadata) = entry.metadata() {
            if is_reparse_point(&metadata) {
                continue;
            }

            if metadata.is_file() {
                try_clean_file(&path, metadata.len(), dry_run, reclaimed, count);
            } else if metadata.is_dir() {
                if let Ok(sub_entries) = std::fs::read_dir(&path) {
                    for sub in sub_entries.flatten() {
                        let sub_path = sub.path();
                        if let Ok(sub_meta) = sub.metadata() {
                            if is_reparse_point(&sub_meta) {
                                continue;
                            }

                            if sub_meta.is_file() {
                                try_clean_file(
                                    &sub_path,
                                    sub_meta.len(),
                                    dry_run,
                                    reclaimed,
                                    count,
                                );
                            }
                        }
                    }
                }
                if !dry_run {
                    let _ = std::fs::remove_dir(&path);
                }
            }
        }
    }
}

#[cfg(target_os = "windows")]
fn is_reparse_point(metadata: &std::fs::Metadata) -> bool {
    use std::os::windows::fs::MetadataExt;
    const FILE_ATTRIBUTE_REPARSE_POINT: u32 = 0x400;
    metadata.file_attributes() & FILE_ATTRIBUTE_REPARSE_POINT != 0
}

#[cfg(not(target_os = "windows"))]
fn is_reparse_point(_: &std::fs::Metadata) -> bool {
    false
}

#[inline]
fn try_clean_file(path: &Path, size: u64, dry_run: bool, reclaimed: &mut u64, count: &mut u32) {
    if dry_run || std::fs::remove_file(path).is_ok() {
        *reclaimed = reclaimed.saturating_add(size);
        *count = count.saturating_add(1);
    }
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
        clean_directory_contents(&cleanup_root, false, &mut reclaimed, &mut count);

        assert!(sentinel.exists(), "cleaner must not traverse the junction target");
        assert_eq!(0, reclaimed);
        assert_eq!(0, count);

        let _ = std::fs::remove_dir(&junction);
        let _ = std::fs::remove_dir_all(&base);
    }
}

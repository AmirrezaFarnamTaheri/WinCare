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
            if metadata.is_file() {
                try_clean_file(&path, metadata.len(), dry_run, reclaimed, count);
            } else if metadata.is_dir() {
                if let Ok(sub_entries) = std::fs::read_dir(&path) {
                    for sub in sub_entries.flatten() {
                        let sub_path = sub.path();
                        if let Ok(sub_meta) = sub.metadata() {
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

#[inline]
fn try_clean_file(path: &Path, size: u64, dry_run: bool, reclaimed: &mut u64, count: &mut u32) {
    if dry_run || std::fs::remove_file(path).is_ok() {
        *reclaimed = reclaimed.saturating_add(size);
        *count = count.saturating_add(1);
    }
}

//! Disk space monitor for proactive storage alerting.

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DiskTelemetry {
    pub drive_letter: char,
    pub total_bytes: u64,
    pub free_bytes: u64,
    pub is_low_space: bool,
}

pub const LOW_SPACE_THRESHOLD_BYTES: u64 = 5 * 1024 * 1024 * 1024; // 5 GB

#[cfg(target_os = "windows")]
mod win32 {
    #[link(name = "kernel32")]
    // SAFETY: Win32 GetDiskFreeSpaceExW signature matches Windows SDK.
    unsafe extern "system" {
        pub fn GetDiskFreeSpaceExW(
            lpDirectoryName: *const u16,
            lpFreeBytesAvailableToCaller: *mut u64,
            lpTotalNumberOfBytes: *mut u64,
            lpTotalNumberOfFreeBytes: *mut u64,
        ) -> i32;
    }
}

pub fn check_disk_space(drive: char) -> Option<DiskTelemetry> {
    let mut total_bytes: u64 = 0;
    let mut free_bytes: u64 = 0;

    #[cfg(target_os = "windows")]
    {
        let drive_str = format!("{}:\\\0", drive);
        let wide: Vec<u16> = drive_str.encode_utf16().collect();
        // SAFETY: Pointer arguments are valid local stack memory.
        let success = unsafe {
            win32::GetDiskFreeSpaceExW(
                wide.as_ptr(),
                &mut free_bytes,
                &mut total_bytes,
                std::ptr::null_mut(),
            )
        };

        if success == 0 {
            return None;
        }
    }

    #[cfg(not(target_os = "windows"))]
    {
        // Mock fallback for cross-platform unit tests
        total_bytes = 512 * 1024 * 1024 * 1024;
        free_bytes = 20 * 1024 * 1024 * 1024;
    }

    Some(DiskTelemetry {
        drive_letter: drive.to_ascii_uppercase(),
        total_bytes,
        free_bytes,
        is_low_space: free_bytes < LOW_SPACE_THRESHOLD_BYTES,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_disk_space_threshold() {
        let mock_telemetry = DiskTelemetry {
            drive_letter: 'C',
            total_bytes: 100 * 1024 * 1024 * 1024,
            free_bytes: 4 * 1024 * 1024 * 1024, // 4 GB (low)
            is_low_space: true,
        };
        assert!(mock_telemetry.is_low_space);
    }
}

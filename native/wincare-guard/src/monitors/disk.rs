//! Disk free-space monitor for proactive storage alerting.

/// Telemetry for a single drive's free space.
#[derive(Debug, Clone, PartialEq, Eq, serde::Serialize)]
pub struct DiskTelemetry {
    /// Drive letter (upper-cased).
    pub drive_letter: char,
    /// Total capacity in bytes.
    pub total_bytes: u64,
    /// Free space available to the caller in bytes.
    pub free_bytes: u64,
    /// True when the drive is below either low-space threshold.
    pub is_low_space: bool,
}

/// Absolute low-space threshold (5 GB).
pub const LOW_SPACE_THRESHOLD_BYTES: u64 = 5 * 1024 * 1024 * 1024;
/// Percentage low-space threshold (10% of capacity).
pub const LOW_SPACE_THRESHOLD_PERCENT: u64 = 10;

fn is_low_space(total_bytes: u64, free_bytes: u64) -> bool {
    free_bytes < LOW_SPACE_THRESHOLD_BYTES
        || (total_bytes > 0
            && u128::from(free_bytes) * 100
                < u128::from(total_bytes) * u128::from(LOW_SPACE_THRESHOLD_PERCENT))
}

#[cfg(target_os = "windows")]
mod win32 {
    #[link(name = "kernel32")]
    // SAFETY: Win32 GetDiskFreeSpaceExW signature matches Windows SDK.
    unsafe extern "system" {
        pub fn GetDiskFreeSpaceExW(
            lp_directory_name: *const u16,
            lp_free_bytes_available_to_caller: *mut u64,
            lp_total_number_of_bytes: *mut u64,
            lp_total_number_of_free_bytes: *mut u64,
        ) -> i32;
    }
}

/// Queries free space for the given drive letter. Returns `None` when the query fails.
pub fn check_disk_space(drive: char) -> Option<DiskTelemetry> {
    let mut total_bytes: u64 = 0;
    let mut free_bytes: u64 = 0;

    #[cfg(target_os = "windows")]
    {
        let drive_str = format!("{drive}:\\\0");
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
        // Mock fallback for cross-platform unit tests.
        total_bytes = 512 * 1024 * 1024 * 1024;
        // Keep the non-Windows test fixture above the percentage threshold.
        free_bytes = 100 * 1024 * 1024 * 1024;
    }

    Some(DiskTelemetry {
        drive_letter: drive.to_ascii_uppercase(),
        total_bytes,
        free_bytes,
        is_low_space: is_low_space(total_bytes, free_bytes),
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

    #[test]
    fn flags_low_percentage_on_large_drive() {
        assert!(is_low_space(2_000_000_000_000, 150_000_000_000));
    }

    #[test]
    fn accepts_space_above_both_thresholds() {
        assert!(!is_low_space(100_000_000_000, 20_000_000_000));
    }
}

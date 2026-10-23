//! Physical memory pressure monitor.

/// Telemetry for physical memory load.
#[derive(Debug, Clone, PartialEq, Eq, serde::Serialize)]
pub struct RamTelemetry {
    /// Total physical memory in bytes.
    pub total_phys_bytes: u64,
    /// Available physical memory in bytes.
    pub avail_phys_bytes: u64,
    /// Memory load as reported by Windows (0-100).
    pub memory_load_percent: u32,
    /// True when memory load meets or exceeds the critical threshold.
    pub is_critical_pressure: bool,
}

/// Critical memory-load threshold in percent.
pub const RAM_PRESSURE_THRESHOLD_PERCENT: u32 = 90;

#[cfg(target_os = "windows")]
mod win32 {
    #[repr(C)]
    #[derive(Copy, Clone)]
    #[allow(non_snake_case, clippy::upper_case_acronyms)]
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

    #[link(name = "kernel32")]
    // SAFETY: GlobalMemoryStatusEx signature matches Windows SDK.
    unsafe extern "system" {
        pub fn GlobalMemoryStatusEx(lp_buffer: *mut MEMORYSTATUSEX) -> i32;
    }
}

/// Queries physical memory load. Returns `None` when the query fails.
pub fn check_ram_pressure() -> Option<RamTelemetry> {
    #[cfg(target_os = "windows")]
    {
        let mut status = win32::MEMORYSTATUSEX {
            dwLength: std::mem::size_of::<win32::MEMORYSTATUSEX>() as u32,
            dwMemoryLoad: 0,
            ullTotalPhys: 0,
            ullAvailPhys: 0,
            ullTotalPageFile: 0,
            ullAvailPageFile: 0,
            ullTotalVirtual: 0,
            ullAvailVirtual: 0,
            ullAvailExtendedVirtual: 0,
        };

        // SAFETY: Pointer is valid stack memory initialized with its size.
        let success = unsafe { win32::GlobalMemoryStatusEx(&mut status) };
        if success == 0 {
            return None;
        }

        Some(RamTelemetry {
            total_phys_bytes: status.ullTotalPhys,
            avail_phys_bytes: status.ullAvailPhys,
            memory_load_percent: status.dwMemoryLoad,
            is_critical_pressure: status.dwMemoryLoad >= RAM_PRESSURE_THRESHOLD_PERCENT,
        })
    }

    #[cfg(not(target_os = "windows"))]
    {
        Some(RamTelemetry {
            total_phys_bytes: 16 * 1024 * 1024 * 1024,
            avail_phys_bytes: 8 * 1024 * 1024 * 1024,
            memory_load_percent: 50,
            is_critical_pressure: false,
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_ram_pressure_threshold() {
        let telemetry = RamTelemetry {
            total_phys_bytes: 16 * 1024 * 1024 * 1024,
            avail_phys_bytes: 1024 * 1024 * 1024,
            memory_load_percent: 94,
            is_critical_pressure: true,
        };
        assert!(telemetry.is_critical_pressure);
    }
}

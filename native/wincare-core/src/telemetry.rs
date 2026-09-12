//! Native zero-allocation system telemetry probes for WinCare.
#![allow(missing_docs)]

#[cfg(target_os = "windows")]
use std::sync::Mutex;

#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct NativeSysSnapshot {
    pub cpu_usage_pct: f32,
    pub ram_used_bytes: u64,
    pub ram_total_bytes: u64,
    pub disk_free_bytes: u64,
    pub disk_total_bytes: u64,
    pub net_active: u8,
    /// Per-metric validity bitmask (bit0 CPU, bit1 RAM, bit2 disk, bit3 network).
    /// Callers should not display a metric whose validity bit is clear.
    pub valid_mask: u32,
    /// ASCII drive letter of the probed volume (e.g. b'C'), 0 when unknown.
    pub disk_volume: u32,
}

/// Validity bits for the valid_mask field.
pub const SYS_VALID_CPU: u32 = 1 << 0;
pub const SYS_VALID_RAM: u32 = 1 << 1;
pub const SYS_VALID_DISK: u32 = 1 << 2;
pub const SYS_VALID_NET: u32 = 1 << 3;

#[cfg(target_os = "windows")]
#[allow(non_camel_case_types, non_snake_case, clippy::upper_case_acronyms)]
mod win32_telemetry {
    #[repr(C)]
    #[derive(Copy, Clone, Default)]
    pub struct FILETIME {
        pub dwLowDateTime: u32,
        pub dwHighDateTime: u32,
    }

    impl FILETIME {
        pub fn as_u64(&self) -> u64 {
            ((self.dwHighDateTime as u64) << 32) | (self.dwLowDateTime as u64)
        }
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

    #[link(name = "kernel32")]
    unsafe extern "system" {
        pub fn GetSystemTimes(
            lpIdleTime: *mut FILETIME,
            lpKernelTime: *mut FILETIME,
            lpUserTime: *mut FILETIME,
        ) -> i32;

        pub fn GlobalMemoryStatusEx(lpBuffer: *mut MEMORYSTATUSEX) -> i32;

        pub fn GetWindowsDirectoryW(lpBuffer: *mut u16, uSize: u32) -> u32;

        pub fn GetDiskFreeSpaceExW(
            lpDirectoryName: *const u16,
            lpFreeBytesAvailableToCaller: *mut u64,
            lpTotalNumberOfBytes: *mut u64,
            lpTotalNumberOfFreeBytes: *mut u64,
        ) -> i32;
    }

    #[link(name = "wininet")]
    unsafe extern "system" {
        pub fn InternetGetConnectedState(lpdwFlags: *mut u32, dwReserved: u32) -> i32;
    }
}

/// The CPU counters must be read and updated as one snapshot. Independent
/// atomics can combine the idle value from one sample with kernel and user
/// values from another concurrent sample, producing a fabricated percentage.
#[cfg(target_os = "windows")]
static PREVIOUS_CPU_TIMES: Mutex<Option<CpuTimes>> = Mutex::new(None);

#[cfg(target_os = "windows")]
#[derive(Clone, Copy)]
struct CpuTimes {
    idle: u64,
    kernel: u64,
    user: u64,
}

/// Queries instantaneous system telemetry into a caller-allocated POD struct.
///
/// # Safety
///
/// `out_snapshot` must point to a valid, properly aligned, writable `NativeSysSnapshot`.
pub unsafe fn query_sys_snapshot(out: *mut NativeSysSnapshot) -> i32 {
    if out.is_null() {
        return 1; // NullPointer
    }

    #[cfg(target_os = "windows")]
    {
        use win32_telemetry::*;

        // 1. Memory: a failed API call leaves the metric unknown.
        let mut valid_mask: u32 = 0;
        let mut ms = unsafe { std::mem::zeroed::<MEMORYSTATUSEX>() };
        ms.dwLength = std::mem::size_of::<MEMORYSTATUSEX>() as u32;
        let (ram_total, ram_used) = if unsafe { GlobalMemoryStatusEx(&mut ms) } != 0 {
            valid_mask |= SYS_VALID_RAM;
            (
                ms.ullTotalPhys,
                ms.ullTotalPhys.saturating_sub(ms.ullAvailPhys),
            )
        } else {
            (0, 0)
        };

        // 2. CPU Usage
        // Hold the lock across the Win32 read and update so concurrent callers
        // cannot reorder samples or mix fields from separate observations.
        let mut previous_cpu_times = PREVIOUS_CPU_TIMES
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        let mut idle = FILETIME::default();
        let mut kernel = FILETIME::default();
        let mut user = FILETIME::default();

        let mut cpu_valid = false;
        let cpu_usage = if unsafe { GetSystemTimes(&mut idle, &mut kernel, &mut user) } != 0 {
            let current = CpuTimes {
                idle: idle.as_u64(),
                kernel: kernel.as_u64(),
                user: user.as_u64(),
            };
            let usage = if let Some(previous) = *previous_cpu_times {
                let delta_idle = current.idle.saturating_sub(previous.idle);
                let delta_kernel = current.kernel.saturating_sub(previous.kernel);
                let delta_user = current.user.saturating_sub(previous.user);
                let total_sys = delta_kernel.saturating_add(delta_user);

                if total_sys > 0 && total_sys >= delta_idle {
                    let busy = total_sys.saturating_sub(delta_idle);
                    cpu_valid = true;
                    ((busy as f64 / total_sys as f64) * 100.0).clamp(0.0, 100.0) as f32
                } else {
                    0.0
                }
            } else {
                // Baseline established on frame 0; the first sample is unknown, not zero.
                0.0
            };
            *previous_cpu_times = Some(current);
            usage
        } else {
            0.0
        };
        if cpu_valid {
            valid_mask |= SYS_VALID_CPU;
        }

        // 3. Disk Space (probed on the actual Windows system volume, not a hardcoded C:\)
        let mut sys_root = [0u16; 260];
        let sys_len = unsafe { GetWindowsDirectoryW(sys_root.as_mut_ptr(), 260) } as usize;
        let disk_volume_letter: u32 = if sys_len > 0 && sys_len < 260 {
            sys_root[0] as u32
        } else {
            0
        };
        let root_path: [u16; 4] = [disk_volume_letter as u16, b':' as u16, b'\\' as u16, 0];
        let mut free_bytes: u64 = 0;
        let mut total_bytes: u64 = 0;
        let mut total_free_bytes: u64 = 0;
        let (disk_free, disk_total) = if disk_volume_letter != 0
            && unsafe {
                GetDiskFreeSpaceExW(
                    root_path.as_ptr(),
                    &mut free_bytes,
                    &mut total_bytes,
                    &mut total_free_bytes,
                )
            } != 0
        {
            valid_mask |= SYS_VALID_DISK;
            (total_free_bytes, total_bytes)
        } else {
            (0, 0)
        };

        // 4. Network Connectivity
        let mut net_flags: u32 = 0;
        let net_active = if unsafe { InternetGetConnectedState(&mut net_flags, 0) } != 0 {
            valid_mask |= SYS_VALID_NET;
            1u8
        } else {
            0u8
        };

        // SAFETY: Pointer validity verified at start of function.
        unsafe {
            out.write(NativeSysSnapshot {
                cpu_usage_pct: cpu_usage,
                ram_used_bytes: ram_used,
                ram_total_bytes: ram_total,
                disk_free_bytes: disk_free,
                disk_total_bytes: disk_total,
                net_active,
                valid_mask,
                disk_volume: disk_volume_letter,
            });
        }

        0 // Ok
    }

    #[cfg(not(target_os = "windows"))]
    {
        unsafe {
            // Non-Windows stubs report unknown metrics instead of fabricated values.
            out.write(NativeSysSnapshot {
                cpu_usage_pct: 0.0,
                ram_used_bytes: 0,
                ram_total_bytes: 0,
                disk_free_bytes: 0,
                disk_total_bytes: 0,
                net_active: 0,
                valid_mask: 0,
                disk_volume: 0,
            });
        }
        0
    }
}

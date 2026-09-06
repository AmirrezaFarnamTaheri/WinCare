//! Native zero-allocation system telemetry probes for WinCare.
#![allow(missing_docs)]

use std::sync::atomic::{AtomicU64, Ordering};

#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct NativeSysSnapshot {
    pub cpu_usage_pct: f32,
    pub ram_used_bytes: u64,
    pub ram_total_bytes: u64,
    pub disk_free_bytes: u64,
    pub disk_total_bytes: u64,
    pub net_active: u8,
}

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

static PREV_IDLE: AtomicU64 = AtomicU64::new(0);
static PREV_KERNEL: AtomicU64 = AtomicU64::new(0);
static PREV_USER: AtomicU64 = AtomicU64::new(0);

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

        // 1. Memory
        let mut ms = unsafe { std::mem::zeroed::<MEMORYSTATUSEX>() };
        ms.dwLength = std::mem::size_of::<MEMORYSTATUSEX>() as u32;
        let (ram_total, ram_used) = if unsafe { GlobalMemoryStatusEx(&mut ms) } != 0 {
            (
                ms.ullTotalPhys,
                ms.ullTotalPhys.saturating_sub(ms.ullAvailPhys),
            )
        } else {
            (0, 0)
        };

        // 2. CPU Usage
        let mut idle = FILETIME::default();
        let mut kernel = FILETIME::default();
        let mut user = FILETIME::default();

        let cpu_usage = if unsafe { GetSystemTimes(&mut idle, &mut kernel, &mut user) } != 0 {
            let cur_idle = idle.as_u64();
            let cur_kernel = kernel.as_u64();
            let cur_user = user.as_u64();

            let last_idle = PREV_IDLE.swap(cur_idle, Ordering::Relaxed);
            let last_kernel = PREV_KERNEL.swap(cur_kernel, Ordering::Relaxed);
            let last_user = PREV_USER.swap(cur_user, Ordering::Relaxed);

            if last_kernel > 0 || last_user > 0 {
                let delta_idle = cur_idle.saturating_sub(last_idle);
                let delta_kernel = cur_kernel.saturating_sub(last_kernel);
                let delta_user = cur_user.saturating_sub(last_user);
                let total_sys = delta_kernel.saturating_add(delta_user);

                if total_sys > 0 && total_sys >= delta_idle {
                    let busy = total_sys.saturating_sub(delta_idle);
                    ((busy as f64 / total_sys as f64) * 100.0).clamp(0.0, 100.0) as f32
                } else {
                    0.0
                }
            } else {
                // Baseline established on frame 0; return 0.0 without blocking thread
                0.0
            }
        } else {
            0.0
        };

        // 3. Disk Space (Root drive C:\)
        let root_c: [u16; 4] = [b'C' as u16, b':' as u16, b'\\' as u16, 0];
        let mut free_bytes: u64 = 0;
        let mut total_bytes: u64 = 0;
        let mut total_free_bytes: u64 = 0;
        let (disk_free, disk_total) = if unsafe {
            GetDiskFreeSpaceExW(
                root_c.as_ptr(),
                &mut free_bytes,
                &mut total_bytes,
                &mut total_free_bytes,
            )
        } != 0
        {
            (total_free_bytes, total_bytes)
        } else {
            (0, 0)
        };

        // 4. Network Connectivity
        let mut net_flags: u32 = 0;
        let net_active = if unsafe { InternetGetConnectedState(&mut net_flags, 0) } != 0 {
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
            });
        }

        0 // Ok
    }

    #[cfg(not(target_os = "windows"))]
    {
        unsafe {
            out.write(NativeSysSnapshot {
                cpu_usage_pct: 0.0,
                ram_used_bytes: 0,
                ram_total_bytes: 16 * 1024 * 1024 * 1024,
                disk_free_bytes: 100 * 1024 * 1024 * 1024,
                disk_total_bytes: 500 * 1024 * 1024 * 1024,
                net_active: 1,
            });
        }
        0
    }
}

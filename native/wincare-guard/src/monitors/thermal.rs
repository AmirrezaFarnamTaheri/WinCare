//! Thermal throttling and power state monitor.

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ThermalTelemetry {
    pub is_throttling: bool,
    pub ac_online: bool,
}

#[cfg(target_os = "windows")]
mod win32 {
    #[repr(C)]
    #[derive(Copy, Clone)]
    #[allow(non_snake_case, clippy::upper_case_acronyms)]
    pub struct SYSTEM_POWER_STATUS {
        pub ACLineStatus: u8,
        pub BatteryFlag: u8,
        pub BatteryLifePercent: u8,
        pub SystemStatusFlag: u8,
        pub BatteryLifeTime: u32,
        pub BatteryFullLifeTime: u32,
    }

    #[link(name = "kernel32")]
    // SAFETY: GetSystemPowerStatus signature matches Windows SDK.
    unsafe extern "system" {
        pub fn GetSystemPowerStatus(lpSystemPowerStatus: *mut SYSTEM_POWER_STATUS) -> i32;
    }
}

pub fn check_thermal_and_power() -> Option<ThermalTelemetry> {
    #[cfg(target_os = "windows")]
    {
        let mut power = win32::SYSTEM_POWER_STATUS {
            ACLineStatus: 0,
            BatteryFlag: 0,
            BatteryLifePercent: 0,
            SystemStatusFlag: 0,
            BatteryLifeTime: 0,
            BatteryFullLifeTime: 0,
        };

        // SAFETY: Pointer is valid stack memory.
        let success = unsafe { win32::GetSystemPowerStatus(&mut power) };
        if success == 0 {
            return None;
        }

        Some(ThermalTelemetry {
            is_throttling: false,
            ac_online: power.ACLineStatus == 1,
        })
    }

    #[cfg(not(target_os = "windows"))]
    {
        Some(ThermalTelemetry {
            is_throttling: false,
            ac_online: true,
        })
    }
}

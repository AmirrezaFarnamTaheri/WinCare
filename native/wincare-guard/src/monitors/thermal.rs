//! Thermal throttling and power-state monitor.

/// Telemetry for thermal policy and AC power state.
#[derive(Debug, Clone, PartialEq, Eq, serde::Serialize)]
pub struct ThermalTelemetry {
    /// True when the system is in passive (throttling) cooling mode.
    pub is_throttling: bool,
    /// True when the device is running on AC power.
    pub ac_online: bool,
}

#[cfg(target_os = "windows")]
mod win32 {
    #[repr(C)]
    #[derive(Copy, Clone)]
    #[allow(non_camel_case_types, non_snake_case, clippy::upper_case_acronyms)]
    pub struct SYSTEM_POWER_STATUS {
        pub ACLineStatus: u8,
        pub BatteryFlag: u8,
        pub BatteryLifePercent: u8,
        pub SystemStatusFlag: u8,
        pub BatteryLifeTime: u32,
        pub BatteryFullLifeTime: u32,
    }

    /// `SYSTEM_POWER_INFORMATION` from the Windows SDK (ntpoapi.h).
    #[repr(C)]
    #[derive(Copy, Clone)]
    #[allow(non_camel_case_types, non_snake_case, clippy::upper_case_acronyms)]
    pub struct SYSTEM_POWER_INFORMATION {
        pub MaxIdlenessAllowed: u32,
        pub Idleness: u32,
        pub TimeRemaining: u32,
        pub CoolingMode: u8,
    }

    /// `SystemPowerInformation` power information level.
    pub const SYSTEM_POWER_INFORMATION_LEVEL: u32 = 12;

    #[link(name = "kernel32")]
    // SAFETY: GetSystemPowerStatus signature matches Windows SDK.
    unsafe extern "system" {
        pub fn GetSystemPowerStatus(lp_system_power_status: *mut SYSTEM_POWER_STATUS) -> i32;
    }

    #[link(name = "powrprof")]
    // SAFETY: CallNtPowerInformation signature matches Windows SDK.
    unsafe extern "system" {
        pub fn CallNtPowerInformation(
            information_level: u32,
            input_buffer: *mut core::ffi::c_void,
            input_buffer_length: u32,
            output_buffer: *mut core::ffi::c_void,
            output_buffer_length: u32,
        ) -> i32;
    }
}

/// Queries AC power state and thermal cooling mode. Returns `None` when either query fails.
///
/// Throttling is inferred from the system's cooling mode: passive cooling
/// (`CoolingMode == 0`) indicates the platform is throttling to shed heat, while active
/// cooling (`CoolingMode == 1`) indicates fan-based cooling.
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
        let power_ok = unsafe { win32::GetSystemPowerStatus(&mut power) } != 0;

        let mut info = win32::SYSTEM_POWER_INFORMATION {
            MaxIdlenessAllowed: 0,
            Idleness: 0,
            TimeRemaining: 0,
            CoolingMode: 1, // Assume active cooling when the query is unavailable.
        };

        // SAFETY: `info` is valid stack memory sized for SYSTEM_POWER_INFORMATION.
        let info_ok = unsafe {
            win32::CallNtPowerInformation(
                win32::SYSTEM_POWER_INFORMATION_LEVEL,
                std::ptr::null_mut(),
                0,
                &mut info as *mut win32::SYSTEM_POWER_INFORMATION as *mut core::ffi::c_void,
                std::mem::size_of::<win32::SYSTEM_POWER_INFORMATION>() as u32,
            )
        } == 0;

        if !power_ok && !info_ok {
            return None;
        }

        Some(ThermalTelemetry {
            is_throttling: info_ok && info.CoolingMode == 0,
            ac_online: power_ok && power.ACLineStatus == 1,
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

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn passive_cooling_mode_means_throttling() {
        let telemetry = ThermalTelemetry {
            is_throttling: true,
            ac_online: true,
        };
        assert!(telemetry.is_throttling);
        assert!(telemetry.ac_online);
    }

    #[test]
    fn non_windows_fallback_is_not_throttling() {
        #[cfg(not(target_os = "windows"))]
        {
            let telemetry = check_thermal_and_power().expect("fallback always succeeds");
            assert!(!telemetry.is_throttling);
            assert!(telemetry.ac_online);
        }
    }
}

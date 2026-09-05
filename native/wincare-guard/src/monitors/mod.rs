//! System resource monitors sampled by the guard daemon.

/// Disk free-space monitoring.
pub mod disk;
/// Physical memory pressure monitoring.
pub mod ram;
/// Thermal throttling and power-state monitoring.
pub mod thermal;

/// A combined health snapshot from a single sampling tick.
#[derive(Debug, Clone, PartialEq, Eq, serde::Serialize)]
pub struct SystemHealthSnapshot {
    /// Disk telemetry, when the drive could be queried.
    pub disk: Option<disk::DiskTelemetry>,
    /// RAM telemetry, when memory could be queried.
    pub ram: Option<ram::RamTelemetry>,
    /// Thermal telemetry, when power state could be queried.
    pub thermal: Option<thermal::ThermalTelemetry>,
    /// True when any monitored resource crossed a critical threshold.
    pub has_critical_alerts: bool,
}

/// Samples disk, RAM, and thermal state in a single bounded tick.
pub fn sample_system_health() -> SystemHealthSnapshot {
    let disk = disk::check_disk_space('C');
    let ram = ram::check_ram_pressure();
    let thermal = thermal::check_thermal_and_power();

    let is_disk_critical = disk.as_ref().map(|d| d.is_low_space).unwrap_or(false);
    let is_ram_critical = ram
        .as_ref()
        .map(|r| r.is_critical_pressure)
        .unwrap_or(false);

    SystemHealthSnapshot {
        disk,
        ram,
        thermal,
        has_critical_alerts: is_disk_critical || is_ram_critical,
    }
}

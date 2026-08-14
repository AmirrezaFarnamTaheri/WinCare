pub mod disk;
pub mod ram;
pub mod thermal;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SystemHealthSnapshot {
    pub disk: Option<disk::DiskTelemetry>,
    pub ram: Option<ram::RamTelemetry>,
    pub thermal: Option<thermal::ThermalTelemetry>,
    pub has_critical_alerts: bool,
}

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

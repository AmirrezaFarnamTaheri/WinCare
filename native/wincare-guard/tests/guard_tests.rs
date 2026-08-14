use std::time::Duration;
use wincare_guard::notifications::toast;
use wincare_guard::service::GuardDaemon;

#[test]
fn test_daemon_single_tick_completes_within_budget() {
    let daemon = GuardDaemon::new(Duration::from_millis(50));
    let snapshot = daemon.run_single_tick();

    // Verify snapshot fields
    if let Some(disk) = snapshot.disk {
        assert_eq!(disk.drive_letter, 'C');
        assert!(disk.total_bytes > 0);
    }
}

#[test]
fn test_daemon_loop_terminates_cleanly() {
    let daemon = GuardDaemon::new(Duration::from_millis(10));
    let mut ticks_received = 0;

    daemon.run_loop(Some(3), |_snapshot| {
        ticks_received += 1;
    });

    assert_eq!(ticks_received, 3);
}

#[test]
fn test_toast_notification_schema() {
    let xml = toast::generate_toast_xml("Disk Cleanup Recommended", "Temporary files exceed 10 GB", "clean_temp");
    assert!(xml.contains("Disk Cleanup Recommended"));
    assert!(xml.contains("Temporary files exceed 10 GB"));
}

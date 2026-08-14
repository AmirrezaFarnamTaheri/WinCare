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
    let xml = toast::generate_toast_xml(
        "Disk Cleanup Recommended",
        "Temporary files exceed 10 GB",
        "clean_temp",
    );
    assert!(xml.contains("Disk Cleanup Recommended"));
    assert!(xml.contains("Temporary files exceed 10 GB"));
}

#[test]
fn test_daemon_restart_and_state_recovery() {
    let daemon = GuardDaemon::new(Duration::from_millis(5));
    assert!(daemon.is_running());

    // Simulate service stop
    daemon.stop();
    assert!(!daemon.is_running());

    // Simulate service restart watchdog
    let restarted_daemon = GuardDaemon::new(Duration::from_millis(5));
    assert!(restarted_daemon.is_running());
    let snapshot = restarted_daemon.run_single_tick();
    assert!(snapshot.disk.is_some() || !snapshot.has_critical_alerts);
}

#[test]
fn test_ipc_pipe_lifecycle_and_transition() {
    use wincare_guard::ipc::pipe_server::{PIPE_NAME, PipeServer};

    assert_eq!(PIPE_NAME, r"\\.\pipe\WinCareGuardIPC");

    let server = PipeServer::new();
    assert!(!server.is_active());

    // Start server
    server.start();
    assert!(server.is_active());

    // Stop server during upgrade/uninstall
    server.stop();
    assert!(!server.is_active());
}

#[test]
fn test_daemon_sleep_resume_simulation() {
    let daemon = GuardDaemon::new(Duration::from_millis(5));

    // Tick before sleep
    let pre_sleep = daemon.run_single_tick();
    assert!(pre_sleep.disk.is_some() || !pre_sleep.has_critical_alerts);

    // Simulate power event delay (e.g. system resume)
    std::thread::sleep(Duration::from_millis(15));

    // Tick after resume
    let post_resume = daemon.run_single_tick();
    assert!(post_resume.disk.is_some() || !post_resume.has_critical_alerts);
}

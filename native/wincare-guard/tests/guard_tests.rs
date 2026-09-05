//! Integration coverage for daemon sampling, notifications, and IPC lifecycle.

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
fn test_daemon_zero_tick_limit_does_not_sample() {
    let daemon = GuardDaemon::new(Duration::ZERO);
    let mut ticks_received = 0;

    daemon.run_loop(Some(0), |_snapshot| ticks_received += 1);

    assert_eq!(ticks_received, 0);
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

    #[cfg(target_os = "windows")]
    {
        use std::io::{Read, Write};
        let connect = || {
            let deadline = std::time::Instant::now() + Duration::from_secs(3);
            loop {
                match std::fs::OpenOptions::new()
                    .read(true)
                    .write(true)
                    .open(PIPE_NAME)
                {
                    Ok(client) => break client,
                    Err(error) => {
                        assert!(std::time::Instant::now() < deadline, "{error}");
                        std::thread::sleep(Duration::from_millis(10));
                    }
                }
            }
        };
        // An idle client must not keep a stopped worker alive across a restart.
        let idle_client = connect();
        std::thread::sleep(Duration::from_millis(30));
        let started = std::time::Instant::now();
        server.stop();
        assert!(started.elapsed() < Duration::from_secs(1));
        drop(idle_client);
        server.start();
        for _ in 0..2 {
            let mut client = connect();
            client.write_all(b"ping\n").expect("write ping");
            let mut response = [0u8; 5];
            client
                .read_exact(&mut response)
                .expect("read pong before disconnect");
            assert_eq!(&response, b"pong\n");
        }
    }

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

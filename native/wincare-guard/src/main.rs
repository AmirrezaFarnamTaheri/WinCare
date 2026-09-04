//! WinCare Guard — lightweight proactive system-health daemon.

/// Named-pipe IPC server used by the WinCare app to query guard health.
pub mod ipc;
/// System resource monitors (disk, RAM, thermal) sampled by the daemon.
pub mod monitors;
/// Windows toast notification XML generation.
pub mod notifications;
/// Daemon loop and lifecycle management.
pub mod service;

use ipc::pipe_server::PipeServer;
use notifications::toast::generate_toast_xml;
use service::GuardDaemon;
use std::time::Duration;

fn main() {
    let args: Vec<String> = std::env::args().collect();
    let is_once = args.iter().any(|a| a == "--once");

    // Serve the named-pipe health endpoint for the WinCare app alongside the polling loop.
    let pipe_server = PipeServer::new();
    pipe_server.start();

    let daemon = GuardDaemon::new(Duration::from_secs(30));

    if is_once {
        let snapshot = daemon.run_single_tick();
        println!("WinCare Guard Health Snapshot: {:?}", snapshot);
        pipe_server.stop();
        return;
    }

    println!("WinCare Guard Daemon starting on Windows (Polling interval: 30s)...");
    daemon.run_loop(None, |snapshot| {
        if snapshot.has_critical_alerts {
            eprintln!("⚠ Alert: Critical system resource threshold reached!");
            raise_critical_alert(snapshot);
        }
    });

    pipe_server.stop();
}

/// Turns a critical snapshot into a toast XML notification and queues it on disk so the
/// WinCare app can display it, then echoes it to stderr for operator visibility.
fn raise_critical_alert(snapshot: &monitors::SystemHealthSnapshot) {
    let message = if snapshot.disk.as_ref().map(|d| d.is_low_space).unwrap_or(false) {
        "Drive C is running low on free space"
    } else if snapshot
        .ram
        .as_ref()
        .map(|r| r.is_critical_pressure)
        .unwrap_or(false)
    {
        "System memory pressure is critically high"
    } else {
        "A system resource threshold was reached"
    };

    let xml = generate_toast_xml("WinCare Guard Alert", message, "guard_alert");

    #[cfg(target_os = "windows")]
    if let Some(directory) = notifications_directory() {
        if let Ok(_created) = std::fs::create_dir_all(&directory) {
            let path = directory.join(format!("guard-alert-{}.xml", unix_millis()));
            let _ = std::fs::write(&path, xml);
        }
    }

    eprintln!("{xml}");
}

/// Resolves the per-user notification queue directory.
#[cfg(target_os = "windows")]
fn notifications_directory() -> Option<std::path::PathBuf> {
    std::env::var_os("LOCALAPPDATA").map(std::path::PathBuf::from)
}

/// Returns a coarse epoch-millis timestamp used for unique queue filenames.
fn unix_millis() -> u128 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|duration| duration.as_millis())
        .unwrap_or(0)
}

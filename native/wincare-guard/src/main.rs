//! WinCare Guard — Lightweight Proactive System Health Windows Daemon

pub mod ipc;
pub mod monitors;
pub mod notifications;
pub mod service;

use std::time::Duration;
use service::GuardDaemon;

fn main() {
    let args: Vec<String> = std::env::args().collect();
    let is_once = args.iter().any(|a| a == "--once");

    let daemon = GuardDaemon::new(Duration::from_secs(30));

    if is_once {
        let snapshot = daemon.run_single_tick();
        println!("WinCare Guard Health Snapshot: {:?}", snapshot);
        return;
    }

    println!("WinCare Guard Daemon starting on Windows (Polling interval: 30s)...");
    daemon.run_loop(None, |snapshot| {
        if snapshot.has_critical_alerts {
            eprintln!("⚠ Alert: Critical system resource threshold reached!");
        }
    });
}

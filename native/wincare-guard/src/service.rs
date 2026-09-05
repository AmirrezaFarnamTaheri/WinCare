//! Background daemon loop for WinCare Guard.

use crate::monitors;
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::Duration;

/// The guard daemon: a polling loop that samples system health on a fixed interval.
pub struct GuardDaemon {
    running: Arc<AtomicBool>,
    poll_interval: Duration,
}

impl GuardDaemon {
    /// Creates a daemon that samples system health every `poll_interval`.
    pub fn new(poll_interval: Duration) -> Self {
        Self {
            running: Arc::new(AtomicBool::new(true)),
            poll_interval,
        }
    }

    /// Signals the daemon loop to stop after the current tick.
    pub fn stop(&self) {
        self.running.store(false, Ordering::Release);
    }

    /// Reports whether the daemon loop is still running.
    pub fn is_running(&self) -> bool {
        self.running.load(Ordering::Acquire)
    }

    /// Samples system health once without entering the loop.
    pub fn run_single_tick(&self) -> monitors::SystemHealthSnapshot {
        monitors::sample_system_health()
    }

    /// Runs the polling loop, invoking `on_tick` with each sampled snapshot until the
    /// daemon is stopped or `max_ticks` (when set) is reached.
    pub fn run_loop<F>(&self, max_ticks: Option<usize>, mut on_tick: F)
    where
        F: FnMut(&monitors::SystemHealthSnapshot),
    {
        if max_ticks == Some(0) {
            return;
        }

        let mut count = 0;
        while self.is_running() {
            let snapshot = self.run_single_tick();
            on_tick(&snapshot);

            count += 1;
            if let Some(max) = max_ticks {
                if count >= max {
                    break;
                }
            }

            std::thread::sleep(self.poll_interval);
        }
    }
}

//! Service Control Manager & background daemon loop.

use crate::monitors;
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::Duration;

pub struct GuardDaemon {
    running: Arc<AtomicBool>,
    poll_interval: Duration,
}

impl GuardDaemon {
    pub fn new(poll_interval: Duration) -> Self {
        Self {
            running: Arc::new(AtomicBool::new(true)),
            poll_interval,
        }
    }

    pub fn stop(&self) {
        self.running.store(false, Ordering::Release);
    }

    pub fn is_running(&self) -> bool {
        self.running.load(Ordering::Acquire)
    }

    pub fn run_single_tick(&self) -> monitors::SystemHealthSnapshot {
        monitors::sample_system_health()
    }

    pub fn run_loop<F>(&self, max_ticks: Option<usize>, mut on_tick: F)
    where
        F: FnMut(&monitors::SystemHealthSnapshot),
    {
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

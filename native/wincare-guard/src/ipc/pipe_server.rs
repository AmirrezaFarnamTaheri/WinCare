//! Named Pipe IPC Server for WinCare Guard.

use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};

pub const PIPE_NAME: &str = r"\\.\pipe\WinCareGuardIPC";

#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
pub struct GuardIpcMessage {
    pub event_type: String,
    pub timestamp_utc: String,
    pub payload_json: String,
}

pub struct PipeServer {
    is_running: Arc<AtomicBool>,
}

impl PipeServer {
    pub fn new() -> Self {
        Self {
            is_running: Arc::new(AtomicBool::new(false)),
        }
    }

    pub fn start(&self) {
        self.is_running.store(true, Ordering::SeqCst);
    }

    pub fn stop(&self) {
        self.is_running.store(false, Ordering::SeqCst);
    }

    pub fn is_active(&self) -> bool {
        self.is_running.load(Ordering::SeqCst)
    }
}

impl Default for PipeServer {
    fn default() -> Self {
        Self::new()
    }
}

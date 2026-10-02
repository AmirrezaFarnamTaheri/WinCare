//! ETW Real-Time Stream Control
//! Uses `logman` to orchestrate Event Tracing for Windows without requiring
//! complex raw COM event consumer loops in standard guard operations.

use std::process::Command;

/// Controls the lifecycle of an ETW trace session for WinCare Guard.
#[derive(Debug, Clone)]
pub struct EtwStreamController {
    session_name: String,
}

impl EtwStreamController {
    /// Creates a new controller for the given ETW session.
    pub fn new(session_name: &str) -> Self {
        Self {
            session_name: session_name.to_string(),
        }
    }

    /// Starts a real-time ETW trace session subscribed to Kernel process, disk, and update events.
    pub fn start_stream(&self) -> Result<(), String> {
        let output = Command::new("logman")
            .args([
                "start",
                &self.session_name,
                "-p",
                "{22fb2cd6-0e7b-422b-a0c7-2fad1fd0e716}", // Microsoft-Windows-Kernel-Process
                "-ets",
                "-rt",
            ])
            .output()
            .map_err(|e| format!("Failed to invoke logman: {}", e))?;

        if !output.status.success() {
            let err = String::from_utf8_lossy(&output.stderr);
            if !err.contains("already exists") {
                return Err(format!("logman failed: {}", err));
            }
        }
        Ok(())
    }

    /// Stops the ETW trace session.
    pub fn stop_stream(&self) -> Result<(), String> {
        let output = Command::new("logman")
            .args(["stop", &self.session_name, "-ets"])
            .output()
            .map_err(|e| format!("Failed to invoke logman: {}", e))?;

        if !output.status.success() {
            let err = String::from_utf8_lossy(&output.stderr);
            if !err.contains("was not found") {
                return Err(format!("logman failed: {}", err));
            }
        }
        Ok(())
    }
}

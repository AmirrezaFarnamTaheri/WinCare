//! WinCare Guard library.
//!
//! Provides the background system-health daemon: bounded resource monitors
//! (disk, RAM, thermal), a named-pipe IPC endpoint, and Windows toast
//! notification XML generation. The binary entry point is [`main`] in this
//! crate; everything here is also usable as a library for tests and tooling.

/// Named-pipe IPC server used by the WinCare app to query guard health.
pub mod ipc;
/// System resource monitors (disk, RAM, thermal) sampled by the daemon.
pub mod monitors;
/// Windows toast notification XML generation.
pub mod notifications;
/// Daemon loop and lifecycle management.
pub mod service;

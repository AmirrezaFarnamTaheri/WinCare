//! IPC surface for the guard daemon.

/// Windows named-pipe server listening on `\\.\pipe\WinCareGuardIPC`.
pub mod pipe_server;

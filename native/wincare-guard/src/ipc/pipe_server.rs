//! Named-pipe IPC server for WinCare Guard.
//!
//! The daemon listens on [`PIPE_NAME`] and answers one newline-terminated command per
//! connection. Supported commands are `ping` (liveness probe, answered with `pong`) and
//! `health` (answered with a JSON serialization of the latest
//! [`monitors::SystemHealthSnapshot`]). Reads and writes are bounded so a misbehaving
//! client cannot force unbounded allocation.

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::thread::JoinHandle;
use std::time::Duration;

#[cfg(target_os = "windows")]
use crate::monitors;

/// Well-known pipe path used by the C# `GuardPipeClient`.
pub const PIPE_NAME: &str = r"\\.\pipe\WinCareGuardIPC";

/// Maximum bytes accepted in a single client command line.
#[cfg(target_os = "windows")]
const MAX_COMMAND_BYTES: usize = 4096;

/// Maximum bytes written back in a single response.
#[cfg(target_os = "windows")]
const MAX_RESPONSE_BYTES: usize = 64 * 1024;

/// Delay between attempts to (re)create the pipe instance.
#[cfg(target_os = "windows")]
const RECREATE_DELAY: Duration = Duration::from_millis(500);

#[cfg(target_os = "windows")]
mod win32 {
    //! Minimal Win32 named-pipe bindings. Private module, so its items are not
    //! subject to the crate's `missing_docs` lint.

    use std::ffi::c_void;

    /// A raw Win32 `HANDLE` value.
    pub type Handle = *mut c_void;

    /// Win32 sentinel for an invalid handle.
    pub const INVALID_HANDLE_VALUE: Handle = -1isize as Handle;

    pub const PIPE_ACCESS_DUPLEX: u32 = 0x0000_0003;
    pub const PIPE_TYPE_BYTE: u32 = 0x0000_0000;
    pub const PIPE_READMODE_BYTE: u32 = 0x0000_0000;
    pub const PIPE_WAIT: u32 = 0x0000_0000;
    pub const PIPE_UNLIMITED_INSTANCES: u32 = 255;

    pub const GENERIC_READ: u32 = 0x8000_0000;
    pub const GENERIC_WRITE: u32 = 0x4000_0000;
    pub const OPEN_EXISTING: u32 = 3;

    /// `ERROR_PIPE_CONNECTED`: a client connected between create and connect.
    pub const ERROR_PIPE_CONNECTED: u32 = 535;

    #[link(name = "kernel32")]
    // SAFETY: These declarations match the official Windows SDK signatures exactly; the
    // caller is responsible for passing valid handles and buffers.
    unsafe extern "system" {
        pub fn CreateNamedPipeW(
            lp_name: *const u16,
            dw_open_mode: u32,
            dw_pipe_mode: u32,
            n_max_instances: u32,
            n_out_buffer_size: u32,
            n_in_buffer_size: u32,
            n_default_timeout: u32,
            lp_security_attributes: *mut c_void,
        ) -> Handle;
        pub fn ConnectNamedPipe(h_named_pipe: Handle, lp_overlapped: *mut c_void) -> i32;
        pub fn ReadFile(
            h_file: Handle,
            lp_buffer: *mut u8,
            n_number_of_bytes_to_read: u32,
            lp_number_of_bytes_read: *mut u32,
            lp_overlapped: *mut c_void,
        ) -> i32;
        pub fn WriteFile(
            h_file: Handle,
            lp_buffer: *const u8,
            n_number_of_bytes_to_write: u32,
            lp_number_of_bytes_written: *mut u32,
            lp_overlapped: *mut c_void,
        ) -> i32;
        pub fn DisconnectNamedPipe(h_named_pipe: Handle) -> i32;
        pub fn CloseHandle(h_object: Handle) -> i32;
        pub fn CreateFileW(
            lp_file_name: *const u16,
            dw_desired_access: u32,
            dw_share_mode: u32,
            lp_security_attributes: *mut c_void,
            dw_creation_disposition: u32,
            dw_flags_and_attributes: u32,
            h_template_file: Handle,
        ) -> Handle;
        pub fn GetLastError() -> u32;
    }
}

/// A listening named-pipe server with a clean, flag-driven lifecycle.
///
/// The accept loop runs on a background thread. [`PipeServer::start`] and
/// [`PipeServer::stop`] are idempotent; `stop` also opens a transient client connection
/// ("kick") to unblock a worker parked in a blocking `ConnectNamedPipe`.
pub struct PipeServer {
    is_running: Arc<AtomicBool>,
    worker: Mutex<Option<JoinHandle<()>>>,
}

impl PipeServer {
    /// Creates a stopped server. No pipe is created until [`PipeServer::start`].
    pub fn new() -> Self {
        Self {
            is_running: Arc::new(AtomicBool::new(false)),
            worker: Mutex::new(None),
        }
    }

    /// Starts the accept loop in a background thread. No-op if already running.
    pub fn start(&self) {
        if self.is_running.swap(true, Ordering::SeqCst) {
            return;
        }

        let running = Arc::clone(&self.is_running);
        let handle = std::thread::spawn(move || accept_loop(running));

        match self.worker.lock() {
            Ok(mut guard) => *guard = Some(handle),
            Err(_) => {
                // A poisoned mutex means a prior panic; keep the flag consistent so
                // `stop` can still be called safely. The detached thread only holds
                // an Arc to the flag and its own pipe handle.
                self.is_running.store(false, Ordering::SeqCst);
            }
        }
    }

    /// Stops the accept loop, unblocking a pending connect if necessary, and joins the
    /// worker thread. No-op if not running.
    pub fn stop(&self) {
        if !self.is_running.swap(false, Ordering::SeqCst) {
            // Already stopped; defensively join any lingering worker.
            if let Ok(mut guard) = self.worker.lock() {
                if let Some(handle) = guard.take() {
                    let _ = handle.join();
                }
            }
            return;
        }

        // A worker may be parked in a blocking `ConnectNamedPipe`; open a transient
        // client connection to unblock it. Retry briefly so a worker that has not yet
        // created its pipe instance is not missed (the kick then fails harmlessly).
        for _ in 0..100 {
            kick_pipe();
            let finished = match self.worker.lock() {
                Ok(guard) => guard.as_ref().map(|h| h.is_finished()).unwrap_or(true),
                Err(_) => true,
            };
            if finished {
                break;
            }
            std::thread::sleep(Duration::from_millis(20));
        }

        if let Ok(mut guard) = self.worker.lock() {
            if let Some(handle) = guard.take() {
                if !handle.is_finished() {
                    // Do not block shutdown on a worker that could not be unblocked.
                    // The thread owns its resources and exits at process teardown.
                    return;
                }
                let _ = handle.join();
            }
        }
    }

    /// Reports whether the accept loop is running.
    pub fn is_active(&self) -> bool {
        self.is_running.load(Ordering::SeqCst)
    }
}

impl Default for PipeServer {
    fn default() -> Self {
        Self::new()
    }
}

/// Opens a transient client connection to [`PIPE_NAME`], used to unblock a worker parked
/// in `ConnectNamedPipe`. Failures are intentionally ignored.
#[cfg(target_os = "windows")]
fn kick_pipe() {
    use win32 as w;

    let wide: Vec<u16> = PIPE_NAME.encode_utf16().chain(std::iter::once(0)).collect();

    // SAFETY: `wide` is a NUL-terminated UTF-16 pipe name; all other arguments are
    // constants and null security attributes per the CreateFileW contract.
    let handle = unsafe {
        w::CreateFileW(
            wide.as_ptr(),
            w::GENERIC_READ | w::GENERIC_WRITE,
            0,
            std::ptr::null_mut(),
            w::OPEN_EXISTING,
            0,
            std::ptr::null_mut(),
        )
    };

    if handle != w::INVALID_HANDLE_VALUE {
        // SAFETY: `handle` is a valid, open client handle owned by this function.
        unsafe { let _ = w::CloseHandle(handle); }
    }
}

#[cfg(not(target_os = "windows"))]
fn kick_pipe() {}

/// Accept loop: repeatedly create a pipe instance, accept one client, serve it, and repeat
/// until the running flag is cleared.
#[cfg(target_os = "windows")]
fn accept_loop(running: Arc<AtomicBool>) {
    while running.load(Ordering::SeqCst) {
        let Some(handle) = create_and_connect_pipe() else {
            std::thread::sleep(RECREATE_DELAY);
            continue;
        };

        serve_connection(handle);

        // SAFETY: `handle` is a live, valid pipe handle owned by this iteration.
        unsafe {
            let _ = win32::DisconnectNamedPipe(handle);
            let _ = win32::CloseHandle(handle);
        }
    }
}

#[cfg(not(target_os = "windows"))]
fn accept_loop(_running: Arc<AtomicBool>) {
    // Named-pipe IPC is Windows-only; non-Windows builds never serve a client.
}

/// Creates the named pipe and waits for a single client to connect.
///
/// Returns `None` when the pipe could not be created or connected (including when the
/// stop flag cleared while waiting).
#[cfg(target_os = "windows")]
fn create_and_connect_pipe() -> Option<win32::Handle> {
    use win32 as w;

    let wide: Vec<u16> = PIPE_NAME.encode_utf16().chain(std::iter::once(0)).collect();

    // SAFETY: `wide` is a NUL-terminated UTF-16 pipe name; all other arguments are
    // constants and null security attributes per the CreateNamedPipeW contract.
    let handle = unsafe {
        w::CreateNamedPipeW(
            wide.as_ptr(),
            w::PIPE_ACCESS_DUPLEX,
            w::PIPE_TYPE_BYTE | w::PIPE_READMODE_BYTE | w::PIPE_WAIT,
            w::PIPE_UNLIMITED_INSTANCES,
            (MAX_RESPONSE_BYTES as u32).min(u32::MAX),
            (MAX_COMMAND_BYTES as u32).min(u32::MAX),
            0,
            std::ptr::null_mut(),
        )
    };

    if handle == w::INVALID_HANDLE_VALUE {
        return None;
    }

    // SAFETY: `handle` is valid and the overlapped pointer is null (blocking connect).
    let connected = unsafe { w::ConnectNamedPipe(handle, std::ptr::null_mut()) };
    if connected == 0 {
        // A client may connect between creation and ConnectNamedPipe; that is a success.
        // SAFETY: GetLastError is safe to call and reflects the last failed Win32 call.
        let error = unsafe { w::GetLastError() };
        if error != w::ERROR_PIPE_CONNECTED {
            // SAFETY: `handle` is a valid, open handle.
            unsafe { let _ = w::CloseHandle(handle); }
            return None;
        }
    }

    Some(handle)
}

/// Reads one command line from a connected client, dispatches it, and writes a bounded
/// newline-terminated response.
#[cfg(target_os = "windows")]
fn serve_connection(handle: win32::Handle) {
    use win32 as w;

    let mut request = Vec::<u8>::with_capacity(MAX_COMMAND_BYTES);
    let mut chunk = [0_u8; 512];

    loop {
        let mut read = 0_u32;
        // SAFETY: `chunk` is a valid buffer, `handle` is a live pipe handle, and the
        // overlapped pointer is null (blocking read).
        let ok = unsafe {
            w::ReadFile(
                handle,
                chunk.as_mut_ptr(),
                chunk.len() as u32,
                &mut read,
                std::ptr::null_mut(),
            )
        };
        if ok == 0 || read == 0 {
            break;
        }

        let bytes = &chunk[..read as usize];
        if let Some(position) = bytes.iter().position(|byte| *byte == b'\n') {
            request.extend_from_slice(&bytes[..position]);
            break;
        }

        request.extend_from_slice(bytes);
        if request.len() >= MAX_COMMAND_BYTES {
            request.truncate(MAX_COMMAND_BYTES);
            break;
        }
    }

    let response = dispatch(&request);
    let response_bytes = response.as_bytes();
    let capped = response_bytes.len().min(MAX_RESPONSE_BYTES) as u32;
    let mut written = 0_u32;

    // SAFETY: `response_bytes` is a valid byte slice and `handle` is a live pipe handle.
    unsafe {
        let _ = w::WriteFile(
            handle,
            response_bytes.as_ptr(),
            capped,
            &mut written,
            std::ptr::null_mut(),
        );
    }
}

/// Maps a raw command line to a newline-terminated response.
#[cfg(target_os = "windows")]
fn dispatch(command: &[u8]) -> String {
    let command = String::from_utf8_lossy(command).trim().to_ascii_lowercase();

    if command == "ping" {
        return "pong\n".to_string();
    }

    if command == "health" {
        let snapshot = monitors::sample_system_health();
        match serde_json::to_string(&snapshot) {
            Ok(mut json) => {
                json.push('\n');
                json
            }
            Err(_) => "{}\n".to_string(),
        }
    } else {
        "unknown command\n".to_string()
    }
}

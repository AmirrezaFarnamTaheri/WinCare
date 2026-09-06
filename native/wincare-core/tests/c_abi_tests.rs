//! C-ABI integration tests for wincare-core telemetry and cleaner primitives.
#![allow(missing_docs)]

use std::mem::MaybeUninit;
use wincare_core::{
    NativeCleanResult, NativeSysSnapshot, wincare_clean_temp_files, wincare_sys_snapshot_all,
};

#[test]
fn test_sys_snapshot_c_abi() {
    let mut snapshot = MaybeUninit::<NativeSysSnapshot>::uninit();
    // SAFETY: snapshot pointer is valid and properly aligned.
    let status = unsafe { wincare_sys_snapshot_all(snapshot.as_mut_ptr()) };
    assert_eq!(
        status, 0,
        "wincare_sys_snapshot_all must return 0 on success"
    );
    let snapshot = unsafe { snapshot.assume_init() };
    assert!(snapshot.ram_total_bytes > 0);
}

#[test]
fn test_clean_temp_dry_run_c_abi() {
    let mut result = MaybeUninit::<NativeCleanResult>::uninit();
    // SAFETY: result pointer is valid and properly aligned.
    let status = unsafe { wincare_clean_temp_files(1, result.as_mut_ptr()) };
    assert_eq!(
        status, 0,
        "wincare_clean_temp_files dry-run must return 0 on success"
    );
    let result = unsafe { result.assume_init() };
    assert_eq!(result.error_code, 0);
}

use wincare_core::*;

#[test]
fn test_wincare_core_abi_version() {
    assert_eq!(1, wincare_core_abi_version());
}

#[test]
fn test_wincare_core_version() {
    let mut written: usize = 0;
    let mut buf = [0u8; 128];

    // null written_ptr returns NullPointer (1)
    let status_null =
        unsafe { wincare_core_version(std::ptr::null_mut(), 0, std::ptr::null_mut()) };
    assert_eq!(status_null, 1);

    // probe returns 6 with written>0
    let status_probe = unsafe { wincare_core_version(std::ptr::null_mut(), 0, &mut written) };
    assert_eq!(status_probe, 6);
    assert!(written > 0);

    // roundtrip returns 0 and valid string
    let status_ok = unsafe { wincare_core_version(buf.as_mut_ptr(), buf.len(), &mut written) };
    assert_eq!(status_ok, 0);
    let version_str = std::str::from_utf8(&buf[..written]).unwrap();
    assert_eq!(version_str, "2.4.0");
}

#[test]
fn test_wincare_core_sha256_file() {
    let mut output = [0u8; 32];
    let path = b"test";

    // null path returns -1 (1)
    let status_null_path = unsafe {
        wincare_core_sha256_file(std::ptr::null(), 0, 1024, output.as_mut_ptr(), output.len())
    };
    assert_eq!(status_null_path, 1);

    // null output returns -1 (1)
    let status_null_out = unsafe {
        wincare_core_sha256_file(path.as_ptr(), path.len(), 1024, std::ptr::null_mut(), 0)
    };
    assert_eq!(status_null_out, 1);

    // output too small returns 6
    let status_small = unsafe {
        wincare_core_sha256_file(path.as_ptr(), path.len(), 1024, output.as_mut_ptr(), 10)
    };
    assert_eq!(status_small, 6);

    // nonexistent path returns NotFound (3)
    let status_missing = unsafe {
        wincare_core_sha256_file(
            path.as_ptr(),
            path.len(),
            1024,
            output.as_mut_ptr(),
            output.len(),
        )
    };
    assert_eq!(status_missing, 3);
}

#[test]
fn test_wincare_core_dir_size() {
    let mut size_out: u64 = 0;
    let path = b"missing_dir";

    // null path returns -1 (1)
    let status_null_path = unsafe { wincare_core_dir_size(std::ptr::null(), 0, &mut size_out) };
    assert_eq!(status_null_path, 1);

    // null size_out returns -1 (1)
    let status_null_out =
        unsafe { wincare_core_dir_size(path.as_ptr(), path.len(), std::ptr::null_mut()) };
    assert_eq!(status_null_out, 1);

    // nonexistent path returns 2 (3)
    let status_missing = unsafe { wincare_core_dir_size(path.as_ptr(), path.len(), &mut size_out) };
    assert_eq!(status_missing, 3);
}

#[test]
fn test_wincare_core_sys_info() {
    let mut written: usize = 0;
    let mut buf = [0u8; 1024];

    // null written returns -1 (1)
    let status_null =
        unsafe { wincare_core_sys_info(buf.as_mut_ptr(), buf.len(), std::ptr::null_mut()) };
    assert_eq!(status_null, 1);

    // probe returns 6 with required>0
    let status_probe = unsafe { wincare_core_sys_info(std::ptr::null_mut(), 0, &mut written) };
    assert_eq!(status_probe, 6);
    assert!(written > 0);

    // roundtrip returns 0 with valid JSON containing 'logical_cpus' field
    let status_ok = unsafe { wincare_core_sys_info(buf.as_mut_ptr(), buf.len(), &mut written) };
    assert_eq!(status_ok, 0);
    let json_str = std::str::from_utf8(&buf[..written]).unwrap();
    assert!(json_str.contains("logical_cpus"));
}

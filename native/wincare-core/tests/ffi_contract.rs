//! Contract test for the versioned wincare-core ABI surface.
use wincare_core::wincare_core_abi_version;

#[test]
fn exported_abi_version_remains_one() {
    assert_eq!(1, wincare_core_abi_version());
}

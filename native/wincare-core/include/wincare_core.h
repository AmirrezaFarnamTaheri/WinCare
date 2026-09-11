#ifndef WINCARE_CORE_H
#define WINCARE_CORE_H

#include <stddef.h>
#include <stdint.h>

#ifdef _WIN32
#define WINCARE_API __declspec(dllexport)
#else
#define WINCARE_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef enum wincore_status {
    WINCARE_STATUS_OK = 0,
    WINCARE_STATUS_NULL_POINTER = 1,
    WINCARE_STATUS_INVALID_UTF8 = 2,
    WINCARE_STATUS_NOT_FOUND = 3,
    WINCARE_STATUS_FILE_TOO_LARGE = 4,
    WINCARE_STATUS_IO_ERROR = 5,
    WINCARE_STATUS_BUFFER_TOO_SMALL = 6,
    WINCARE_STATUS_TRUNCATED = 7,
    WINCARE_STATUS_INTERNAL_ERROR = -99
} wincore_status;

WINCARE_API uint32_t wincare_core_abi_version(void);
WINCARE_API int32_t wincare_core_version(uint8_t *buffer, size_t buffer_len, size_t *written);
WINCARE_API int32_t wincare_core_sha256_file(
    const uint8_t *path_utf8,
    size_t path_len,
    uint64_t max_bytes,
    uint8_t *output,
    size_t output_len);

WINCARE_API int32_t wincare_core_dir_size(
    const uint8_t *path_utf8,
    size_t path_len,
    uint64_t *size_out);

WINCARE_API int32_t wincare_core_sys_info(
    uint8_t *buffer,
    size_t buffer_len,
    size_t *written);

/* F-039: aggregate telemetry snapshot and cleaner exports, previously absent from the
 * published header although exported by the DLL and consumed through P/Invoke.
 * Layout must stay in sync with the C# mirror in WinCareCoreNative.cs. */

#define WINCARE_SYS_VALID_CPU  0x1u
#define WINCARE_SYS_VALID_RAM  0x2u
#define WINCARE_SYS_VALID_DISK 0x4u
#define WINCARE_SYS_VALID_NET  0x8u

typedef struct wincare_sys_snapshot {
    float cpu_usage_pct;
    uint64_t ram_used_bytes;
    uint64_t ram_total_bytes;
    uint64_t disk_free_bytes;
    uint64_t disk_total_bytes;
    uint8_t net_active;
    uint32_t valid_mask;  /* per-metric validity; zero bits mean unknown, not zero */
    uint32_t disk_volume; /* ASCII drive letter of the probed volume, 0 = unknown */
} wincare_sys_snapshot;

typedef struct wincare_clean_result {
    uint64_t bytes_reclaimed;
    uint32_t files_removed;
    int32_t error_code;
} wincare_clean_result;

WINCARE_API int32_t wincare_sys_snapshot_all(wincare_sys_snapshot *out_snapshot);

WINCARE_API int32_t wincare_clean_temp_files(
    uint8_t dry_run,
    wincare_clean_result *out_result);

#ifdef __cplusplus
}
#endif

#endif

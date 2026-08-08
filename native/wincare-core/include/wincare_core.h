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
    WINCARE_STATUS_BUFFER_TOO_SMALL = 6
} wincore_status;

WINCARE_API uint32_t wincare_core_abi_version(void);
WINCARE_API int32_t wincare_core_version(uint8_t *buffer, size_t buffer_len, size_t *written);
WINCARE_API int32_t wincare_core_sha256_file(
    const uint8_t *path_utf8,
    size_t path_len,
    uint64_t max_bytes,
    uint8_t *output,
    size_t output_len);

#ifdef __cplusplus
}
#endif

#endif

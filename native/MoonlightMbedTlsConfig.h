#pragma once
#include "mbedtls/mbedtls_config.h"
#define MBEDTLS_ALLOW_PRIVATE_ACCESS
// AppContainer has no filesystem-backed key store or CryptoAPI entropy provider.
#undef MBEDTLS_FS_IO
#undef MBEDTLS_PSA_CRYPTO_STORAGE_C
#undef MBEDTLS_PSA_ITS_FILE_C
#undef MBEDTLS_TIMING_C
#define MBEDTLS_NO_PLATFORM_ENTROPY
#define MBEDTLS_ENTROPY_HARDWARE_ALT
#define MBEDTLS_THREADING_C
#define MBEDTLS_THREADING_ALT

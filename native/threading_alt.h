#pragma once
#include <windows.h>
typedef struct { SRWLOCK lock; } mbedtls_threading_mutex_t;

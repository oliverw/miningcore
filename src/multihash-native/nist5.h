#ifndef NIST5_H
#define NIST5_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

void nist5_hash(const char* input, char* output, uint32_t len);

#ifdef __cplusplus
}
#endif

#endif
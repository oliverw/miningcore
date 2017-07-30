#ifndef SHAVITE_H
#define SHAVITE_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

void shavite3_hash(const char* input, char* output, uint32_t len);

#ifdef __cplusplus
}
#endif

#endif

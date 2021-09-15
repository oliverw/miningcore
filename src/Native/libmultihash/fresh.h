#ifndef FRESH_H
#define FRESH_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

void fresh_hash(const char* input, char* output, uint32_t len);

#ifdef __cplusplus
}
#endif

#endif

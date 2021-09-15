#ifndef X15_H
#define X15_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

void x15_hash(const char* input, char* output, uint32_t len);

#ifdef __cplusplus
}
#endif

#endif

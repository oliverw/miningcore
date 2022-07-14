#ifndef X17_H
#define X17_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

void x17_hash(const char* input, char* output, uint32_t len);

#ifdef __cplusplus
}
#endif

#endif

#ifndef X13_H
#define X13_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

void x13_hash(const char* input, char* output, uint32_t len);
void x13_bcd_hash(const char* input, char* output);

#ifdef __cplusplus
}
#endif

#endif

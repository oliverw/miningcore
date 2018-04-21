#ifndef X16R_H
#define X16R_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

void x16r_hash(const char* input, char* output, uint32_t len);

#ifdef __cplusplus
}
#endif

#endif

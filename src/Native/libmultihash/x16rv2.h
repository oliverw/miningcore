#ifndef X16RV2_H
#define X16RV2_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

void x16rv2_hash(const char* input, char* output, uint32_t len);

#ifdef __cplusplus
}
#endif

#endif

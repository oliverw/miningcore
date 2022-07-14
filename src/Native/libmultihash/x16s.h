#ifndef X16S_H
#define X16S_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

void x16s_hash(const char* input, char* output, uint32_t len);

#ifdef __cplusplus
}
#endif

#endif

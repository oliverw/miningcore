#ifndef LYRA2V3_H
#define LYRA2V3_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

void lyra2v3_hash(const char* input, char* output, uint32_t len);

#ifdef __cplusplus
}
#endif

#endif
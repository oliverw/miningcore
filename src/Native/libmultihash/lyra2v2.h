#ifndef LYRA2VE_H
#define LYRA2VE_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

void lyra2v2_hash(const char* input, char* output, uint32_t len);

#ifdef __cplusplus
}
#endif

#endif
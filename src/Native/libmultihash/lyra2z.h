#ifndef LYRA2Z_H
#define LYRA2Z_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

void lyra2z_hash(const char* input, char* output, uint32_t len);

#ifdef __cplusplus
}
#endif

#endif
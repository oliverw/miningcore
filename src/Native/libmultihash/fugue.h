#ifndef FUGUE_H
#define FUGUE_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

void fugue_hash(const char* input, char* output, uint32_t len);

#ifdef __cplusplus
}
#endif

#endif

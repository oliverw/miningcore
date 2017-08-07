#ifndef BLAKE_H
#define BLAKE_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

void blake_hash(const char* input, char* output, uint32_t len);

#ifdef __cplusplus
}
#endif

#endif

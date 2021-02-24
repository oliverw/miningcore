#ifndef LYRA2RE_H
#define LYRA2RE_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

void lyra2re_hash(const char* input, char* output, uint32_t len);

#ifdef __cplusplus
}
#endif

#endif
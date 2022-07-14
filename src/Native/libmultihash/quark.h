#ifndef QUARK_H
#define QUARK_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

void quark_hash(const char* input, char* output, uint32_t len);

#ifdef __cplusplus
}
#endif

#endif

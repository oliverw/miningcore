#ifndef X25X_H
#define X25X_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

void x25x_hash(const char* input, char* output, uint32_t len);

#ifdef __cplusplus
}
#endif

#endif

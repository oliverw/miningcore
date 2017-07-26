#ifndef X11_H
#define X11_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

void x11_hash(const char* input, char* output, uint32_t len);

#ifdef __cplusplus
}
#endif

#endif

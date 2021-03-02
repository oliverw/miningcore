#ifndef SHA256CSM_H
#define SHA256CSM_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

void sha256csm_hash(const char* input, char* output, uint32_t len);

#ifdef __cplusplus
}
#endif

#endif

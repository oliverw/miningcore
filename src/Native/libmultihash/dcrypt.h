#ifndef DCRYPT_H
#define DCRYPT_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

void dcrypt_hash(const char* input, char* hash, uint32_t len);


#ifdef __cplusplus
}
#endif

#endif

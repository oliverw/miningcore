#ifndef KECCAK_FIPS202_H
#define KECCAK_FIPS202_H

#ifdef __cplusplus
extern "C" {
#endif


#include <stdint.h>
#include <stdlib.h>

#define decshake(bits) \
  int shake##bits(uint8_t*, size_t, const uint8_t*, size_t);

#define decsha3(bits) \
  int sha3_##bits(uint8_t*, size_t, const uint8_t*, size_t);

decshake(128)
decshake(256)
decsha3(224)
decsha3(256)
decsha3(384)
decsha3(512)

#ifdef __cplusplus
}
#endif

#endif
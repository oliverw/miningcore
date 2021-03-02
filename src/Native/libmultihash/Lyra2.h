#ifndef LYRA2_H_
#define LYRA2_H_

#include <stdint.h>

typedef unsigned char byte;

//Block length required so Blake2's Initialization Vector (IV) is not overwritten (THIS SHOULD NOT BE MODIFIED)
#define BLOCK_LEN_BLAKE2_SAFE_INT64 8                                   //512 bits (=64 bytes, =8 uint64_t)
#define BLOCK_LEN_BLAKE2_SAFE_BYTES (BLOCK_LEN_BLAKE2_SAFE_INT64 * 8)   //same as above, in bytes


#ifdef BLOCK_LEN_BITS
        #define BLOCK_LEN_INT64 (BLOCK_LEN_BITS/64)      //Block length: 768 bits (=96 bytes, =12 uint64_t)
        #define BLOCK_LEN_BYTES (BLOCK_LEN_BITS/8)       //Block length, in bytes
#else   //default block lenght: 768 bits
        #define BLOCK_LEN_INT64 12                       //Block length: 768 bits (=96 bytes, =12 uint64_t)
        #define BLOCK_LEN_BYTES (BLOCK_LEN_INT64 * 8)    //Block length, in bytes
#endif

int LYRA2(void *K, int64_t kLen, const void *pwd, int32_t pwdlen, const void *salt, int32_t saltlen, int64_t timeCost, const int64_t nRows, const int16_t nCols);
int LYRA2_3(void *K, int64_t kLen, const void *pwd, int32_t pwdlen, const void *salt, int32_t saltlen, int64_t timeCost, const int16_t nRows, const int16_t nCols);

#endif
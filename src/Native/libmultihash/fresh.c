#include "fresh.h"
#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include <stdio.h>

#include "sha3/sph_shavite.h"
#include "sha3/sph_simd.h"
#include "sha3/sph_echo.h"

void fresh_hash(const char* input, char* output, uint32_t len)
{
    sph_shavite512_context	ctx_shavite1;
    sph_simd512_context		ctx_simd1;
    sph_echo512_context		ctx_echo1;

    //these uint512 in the c++ source of the client are backed by an array of uint32
    uint32_t hashA[16], hashB[16];

    sph_shavite512_init (&ctx_shavite1);
    sph_shavite512 (&ctx_shavite1, input, len);
    sph_shavite512_close(&ctx_shavite1, hashA);

    sph_simd512_init (&ctx_simd1);
    sph_simd512 (&ctx_simd1, hashA, 64);
    sph_simd512_close(&ctx_simd1, hashB);

    sph_shavite512_init (&ctx_shavite1);
    sph_shavite512 (&ctx_shavite1, hashB, 64);
    sph_shavite512_close(&ctx_shavite1, hashA);

    sph_simd512_init (&ctx_simd1);
    sph_simd512 (&ctx_simd1, hashA, 64);
    sph_simd512_close(&ctx_simd1, hashB);

    sph_echo512_init (&ctx_echo1);
    sph_echo512 (&ctx_echo1, hashB, 64);
    sph_echo512_close(&ctx_echo1, hashA);

    memcpy(output, hashA, 32);

}

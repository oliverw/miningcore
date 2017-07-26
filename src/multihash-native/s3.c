#include "s3.h"
#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include <stdio.h>

#include "sha3/sph_skein.h"
#include "sha3/sph_shavite.h"
#include "sha3/sph_simd.h"

void s3_hash(const char* input, char* output, uint32_t len)
{
    sph_shavite512_context      ctx_shavite1;
    sph_simd512_context         ctx_simd1;
    sph_skein512_context        ctx_skein;

    //these uint512 in the c++ source of the client are backed by an array of uint32
    uint32_t hashA[16], hashB[16];

    sph_shavite512_init (&ctx_shavite1);
    sph_shavite512 (&ctx_shavite1, input, 80);
    sph_shavite512_close(&ctx_shavite1, hashA);

    sph_simd512_init (&ctx_simd1);
    sph_simd512 (&ctx_simd1, hashA, 64);
    sph_simd512_close(&ctx_simd1, hashB);

    sph_skein512_init(&ctx_skein);
    sph_skein512 (&ctx_skein, hashB, 64);
    sph_skein512_close (&ctx_skein, hashA);

    memcpy(output, hashA, 32);
}

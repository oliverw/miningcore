#include "qubit.h"

#include <string.h>
#include <stdlib.h>

#include "sha3/sph_cubehash.h"
#include "sha3/sph_luffa.h"
#include "sha3/sph_shavite.h"
#include "sha3/sph_simd.h"
#include "sha3/sph_echo.h"

void qubit_hash(const char* input, char* output, uint32_t len)
{
    sph_luffa512_context    ctx_luffa;
    sph_cubehash512_context ctx_cubehash;
    sph_shavite512_context  ctx_shavite;
    sph_simd512_context     ctx_simd;
    sph_echo512_context     ctx_echo;
    
    char hash1[64];
    char hash2[64];
    
    sph_luffa512_init(&ctx_luffa);
    sph_luffa512(&ctx_luffa, (const void*) input, len);
    sph_luffa512_close(&ctx_luffa, (void*) &hash1); // 1
    
    sph_cubehash512_init(&ctx_cubehash);
    sph_cubehash512(&ctx_cubehash, (const void*) &hash1, 64); // 1
    sph_cubehash512_close(&ctx_cubehash, (void*) &hash2); // 2
    
    sph_shavite512_init(&ctx_shavite);
    sph_shavite512(&ctx_shavite, (const void*) &hash2, 64); // 3
    sph_shavite512_close(&ctx_shavite, (void*) &hash1); // 4
    
    sph_simd512_init(&ctx_simd);
    sph_simd512(&ctx_simd, (const void*) &hash1, 64); // 4
    sph_simd512_close(&ctx_simd, (void*) &hash2); // 5
    
    sph_echo512_init(&ctx_echo);
    sph_echo512(&ctx_echo, (const void*) &hash2, 64); // 5
    sph_echo512_close(&ctx_echo, (void*) &hash1); // 6
    
    memcpy(output, &hash1, 32);
}


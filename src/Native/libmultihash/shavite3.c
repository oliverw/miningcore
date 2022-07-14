#include "shavite3.h"

#include <string.h>
#include <stdlib.h>

#include "sha3/sph_shavite.h"

void shavite3_hash(const char* input, char* output, uint32_t len)
{
    char hash1[64];
    char hash2[64];
    
    sph_shavite512_context ctx_shavite;
    
    sph_shavite512_init(&ctx_shavite);
    sph_shavite512(&ctx_shavite, (const void*) input, len);
    sph_shavite512_close(&ctx_shavite, (void*) &hash1);
    
    sph_shavite512(&ctx_shavite, (const void*) &hash1, 64);
    sph_shavite512_close(&ctx_shavite, (void*) &hash2);

    memcpy(output, &hash2, 32);
}


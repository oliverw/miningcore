#include "hefty1.h"

#include "sha3/sph_hefty1.h"
#include "sha3/sph_keccak.h"
#include "sha3/sph_groestl.h"
#include "sha3/sph_blake.h"
#include "sha256.h"

void hefty1_hash(const char* input, char* output, uint32_t len)
{
    HEFTY1_CTX              ctx_hefty1;
    SHA256_CTX              ctx_sha256;
    sph_keccak512_context   ctx_keccak;
    sph_groestl512_context  ctx_groestl;
    sph_blake512_context    ctx_blake;
    
    char hash32_1[32];
    char hash32_2[32];
    char hash64_3[64];
    char hash64_4[64];
    char hash64_5[64];
    
    HEFTY1_Init(&ctx_hefty1);
    HEFTY1_Update(&ctx_hefty1, (const void*) input, len);
    HEFTY1_Final((unsigned char*) &hash32_1, &ctx_hefty1); // 1
    
    SHA256_Init(&ctx_sha256);
    SHA256_Update(&ctx_sha256, (const void*) input, len);
    SHA256_Update(&ctx_sha256, (unsigned char*) &hash32_1, 32); // 1
    SHA256_Final((unsigned char*) &hash32_2, &ctx_sha256); // 2
    
    sph_keccak512_init(&ctx_keccak);
    sph_keccak512(&ctx_keccak, (const void*) input, len);
    sph_keccak512(&ctx_keccak, (unsigned char*) &hash32_1, 32); //1
    sph_keccak512_close(&ctx_keccak, (void*) &hash64_3); // 3
    
    sph_groestl512_init(&ctx_groestl);
    sph_groestl512(&ctx_groestl, (const void*) input, len);
    sph_groestl512(&ctx_groestl, (unsigned char*) &hash32_1, 32); // 1
    sph_groestl512_close(&ctx_groestl, (void*) &hash64_4); // 4
    
    sph_blake512_init(&ctx_blake);
    sph_blake512(&ctx_blake, (const void*) input, len);
    sph_blake512(&ctx_blake, (unsigned char*) &hash32_1, 32); // 1
    sph_blake512_close(&ctx_blake, (void*) &hash64_5); // 5
    
    memset(output, 0, 32);
    
    char* hash[4] = { hash32_2, hash64_3, hash64_4, hash64_5 };
    
    uint32_t i;
    uint32_t j;
    
    #define OUTPUT_BIT (i * 4 + j)
    
    for(i = 0; i < 64; i++) {
        for(j = 0; j < 4; j++) {
            if((*(hash[j] + (i / 8)) & (0x80 >> (i % 8))) != 0)
                *(output + (OUTPUT_BIT / 8)) |= 0x80 >> (OUTPUT_BIT % 8);
        }
    }
}


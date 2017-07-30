#include "groestl.h"
#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include <stdio.h>

#include "sha3/sph_groestl.h"
#include "sha256.h"

void groestl_hash(const char* input, char* output, uint32_t len)
{
    char hash1[64];
    char hash2[64];
    
    sph_groestl512_context ctx_groestl;
    sph_groestl512_init(&ctx_groestl);
    sph_groestl512(&ctx_groestl, input, len);
    sph_groestl512_close(&ctx_groestl, &hash1);
    
    sph_groestl512(&ctx_groestl, hash1, 64);
    sph_groestl512_close(&ctx_groestl, &hash2);
    
    memcpy(output, &hash2, 32);
}

void groestlmyriad_hash(const char* input, char* output, uint32_t len)
{
    char temp[64];
    
    sph_groestl512_context ctx_groestl;
    sph_groestl512_init(&ctx_groestl);
    sph_groestl512(&ctx_groestl, input, len);
    sph_groestl512_close(&ctx_groestl, &temp);
    
    SHA256_CTX ctx_sha256;
    SHA256_Init(&ctx_sha256);
    SHA256_Update(&ctx_sha256, &temp, 64);
    SHA256_Final((unsigned char*) output, &ctx_sha256);
}


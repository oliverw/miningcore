#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "sha256.h"
#include <stdlib.h>

void sha256csm_hash(const char* input, char* output, uint32_t len)
{
    unsigned char hash[64];

    char emptybuffer[112];
    memset(emptybuffer, 0, sizeof(emptybuffer));
    memcpy(emptybuffer, input, 80);

    SHA256_CTX ctx_sha256;
    SHA256_Init(&ctx_sha256);
    SHA256_Update(&ctx_sha256, emptybuffer, 112);
    SHA256_Final((unsigned char*)output, &ctx_sha256);

    SHA256_Init(&ctx_sha256);
    SHA256_Update(&ctx_sha256, output, 32);
    SHA256_Final((unsigned char*)output, &ctx_sha256);
} 

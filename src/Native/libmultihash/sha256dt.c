#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include <stdio.h>

#include "sha256.h"
#include "sha256t.h"

#include <stdlib.h>

void sha256dt_hash(const char* input, char* output)
{
    char temp[32];

    SHA256_CTX ctx;
    SHA256t_Init(&ctx);
    SHA256_Update(&ctx, input, 80);
    SHA256_Final((unsigned char*) &temp, &ctx);

    SHA256t_Init(&ctx);
    SHA256_Update(&ctx, &temp, 32);
    SHA256_Final((unsigned char*) output, &ctx);
}

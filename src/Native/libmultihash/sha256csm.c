#include "sha256csm.h"
#include <stdlib.h>
#include <stdint.h>
#include <string.h>

#include "sha3/sph_groestl.h"
#include "sha3/sph_sha2.h"


void sha256csm_hash(const char* input, char* output, uint32_t len)
{
    char emptybuffer[112] = {0};
    memset(emptybuffer, 0, sizeof(emptybuffer));
    memcpy(emptybuffer, input, 80);

    sph_sha256_context ctx_sha2;

    sph_sha256_init(&ctx_sha2);
    sph_sha256(&ctx_sha2, emptybuffer, 112);
    sph_sha256_close(&ctx_sha2, (unsigned char*)output);

    sph_sha256_init(&ctx_sha2);
    sph_sha256(&ctx_sha2, output, 32);
    sph_sha256_close(&ctx_sha2, (unsigned char*)output);
}

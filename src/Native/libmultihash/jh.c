#include "jh.h"

#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include <stdio.h>
#include "sha3/sph_jh.h"


void jh_hash(const char* input, char* output, uint32_t len) {

    sph_jh256_context ctx_jh;
    sph_jh256_init(&ctx_jh);
    sph_jh256 (&ctx_jh, input, len);
    sph_jh256_close(&ctx_jh, output);

}


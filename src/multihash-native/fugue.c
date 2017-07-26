#include "fugue.h"

#include "sha3/sph_fugue.h"

void fugue_hash(const char* input, char* output, uint32_t len)
{
    sph_fugue256_context ctx_fugue;
    sph_fugue256_init(&ctx_fugue);
    sph_fugue256(&ctx_fugue, input, len);
    sph_fugue256_close(&ctx_fugue, output);
}


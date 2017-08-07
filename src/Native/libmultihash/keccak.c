#include "keccak.h"

#include "sha3/sph_types.h"
#include "sha3/sph_keccak.h"


void keccak_hash(const char* input, char* output, uint32_t size)
{
    sph_keccak256_context ctx_keccak;
    sph_keccak256_init(&ctx_keccak);
    sph_keccak256 (&ctx_keccak, input, size);//80);
    sph_keccak256_close(&ctx_keccak, output);
}


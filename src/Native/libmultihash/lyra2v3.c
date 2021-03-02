#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include <stdio.h>
#include "sha3/sph_blake.h"
#include "sha3/sph_cubehash.h"
#include "sha3/sph_bmw.h"
#include "Lyra2.h"

void lyra2v3_hash(const char* input, char* output, uint32_t len)
{
	uint32_t hashA[8], hashB[8];

	sph_blake256_context      ctx_blake;
	sph_cubehash256_context   ctx_cube;
	sph_bmw256_context        ctx_bmw;

	sph_blake256_set_rounds(14);

	sph_blake256_init(&ctx_blake);
	sph_blake256(&ctx_blake, input, len);
	sph_blake256_close(&ctx_blake, hashA);
	
	LYRA2_3(hashB, 32, hashA, 32, hashA, 32, 1, 4, 4);
	
	sph_cubehash256_init(&ctx_cube);
	sph_cubehash256(&ctx_cube, hashB, 32);
	sph_cubehash256_close(&ctx_cube, hashA);
	
	LYRA2_3(hashB, 32, hashA, 32, hashA, 32, 1, 4, 4);

	sph_bmw256_init(&ctx_bmw);
	sph_bmw256(&ctx_bmw, hashB, 32);
	sph_bmw256_close(&ctx_bmw, hashA);

	memcpy(output, hashA, 32);
}


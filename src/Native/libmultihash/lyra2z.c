#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include <stdio.h>

#include "Lyra2-z.h"

#include "sha3/sph_blake.h"

#define _ALIGN(x) __attribute__ ((aligned(x)))

extern uint64_t lyra2z_height;

void lyra2z_hash(const char* input, char* output, uint32_t len)
{
	uint32_t _ALIGN(64) hashB[8], hash[8];
	sph_blake256_context ctx_blake;
/*
	uint64_t height = lyra2z_height;
	// initial implementation was pure lyra2 (no blake)

	if (height < 100) {
		fprintf(stderr, "submit error, height=%u, len=%u\n", (uint32_t) height, len);
		memset(hash, 0xff, 32);
		return;
	}
	LYRA2z((void*)hash, 32, (void*)input, len, (void*)input, len, 2, height, 256);
*/
	sph_blake256_set_rounds(14);

	sph_blake256_init(&ctx_blake);
	sph_blake256(&ctx_blake, input, len);
	sph_blake256_close(&ctx_blake, hashB);

	LYRA2z(hash, 32, hashB, 32, hashB, 32, 8, 8, 8);

	memcpy(output, hash, 32);
}


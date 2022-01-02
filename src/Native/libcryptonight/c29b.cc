#include "c29.h"  

// Cuck(at)oo Cycle, a memory-hard proof-of-work
// Copyright (c) 2013-2019 John Tromp
#define EDGE_BLOCK_BITS 6
#define EDGE_BLOCK_SIZE (1 << EDGE_BLOCK_BITS)
#define EDGE_BLOCK_MASK (EDGE_BLOCK_SIZE - 1)
#define NEDGES ((uint32_t)1 << EDGEBITS)
#define EDGEMASK ((uint32_t)NEDGES - 1)
static uint64_t v0;
static uint64_t v1;
static uint64_t v2;
static uint64_t v3;
static uint64_t rotl(uint64_t x, uint64_t b) {
	return (x << b) | (x >> (64 - b));
}
static void sip_round() {
	v0 += v1; v2 += v3; v1 = rotl(v1,13);
	v3 = rotl(v3,16); v1 ^= v0; v3 ^= v2;
	v0 = rotl(v0,32); v2 += v1; v0 += v3;
	v1 = rotl(v1,17);   v3 = rotl(v3,21);
	v1 ^= v2; v3 ^= v0; v2 = rotl(v2,32);
}
static void hash24(const uint64_t nonce) {
	v3 ^= nonce;
	sip_round(); sip_round();
	v0 ^= nonce;
	v2 ^= 0xff;
	sip_round(); sip_round(); sip_round(); sip_round();
}
static uint64_t xor_lanes() {
	return (v0 ^ v1) ^ (v2  ^ v3);
}
static uint64_t sipblock(siphash_keys *keys, const uint32_t edge,uint64_t  *buf) {
	v0=keys->k0;
	v1=keys->k1;
	v2=keys->k2;
	v3=keys->k3;

	uint32_t edge0 = edge & ~EDGE_BLOCK_MASK;
	for (uint32_t i=0; i < EDGE_BLOCK_SIZE; i++) {
		hash24(edge0 + i);
		buf[i] = xor_lanes();
	}
	const uint64_t last = buf[EDGE_BLOCK_MASK];
	for (uint32_t i=0; i < EDGE_BLOCK_MASK; i++)
		buf[i] ^= last;
	return buf[edge & EDGE_BLOCK_MASK];
}

int c29b_verify(uint32_t edges[PROOFSIZEb], siphash_keys *keys) {
	uint32_t xor0 = 0, xor1 = 0;
	uint64_t sips[EDGE_BLOCK_SIZE];
	uint32_t uvs[2*PROOFSIZEb];

	for (uint32_t n = 0; n < PROOFSIZEb; n++) {
		if (edges[n] > EDGEMASK)
			return POW_TOO_BIG;
		if (n && edges[n] <= edges[n-1])
			return POW_TOO_SMALL;
		uint64_t edge = sipblock(keys, edges[n], sips);
		xor0 ^= uvs[2*n  ] = edge & EDGEMASK;
		xor1 ^= uvs[2*n+1] = (edge >> 32) & EDGEMASK;
		}
	if (xor0 | xor1)              // optional check for obviously bad proofs
		return POW_NON_MATCHING;
	uint32_t n = 0, i = 0, j;
	do {                        // follow cycle
		for (uint32_t k = j = i; (k = (k+2) % (2*PROOFSIZEb)) != i; ) {
			if (uvs[k] == uvs[i]) { // find other edge endpoint identical to one at i
				if (j != i)           // already found one before
					return POW_BRANCH;
				j = k;
			}
		}
		if (j == i) return POW_DEAD_END;  // no matching endpoint
		i = j^1;
		n++;
	} while (i != 0);           // must cycle back to start or we would have found branch
	return n == PROOFSIZEb ? POW_OK : POW_SHORT_CYCLE;
}

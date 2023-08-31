/*
  This file is part of ethash.

  ethash is free software: you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  ethash is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.	See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with cpp-ethereum.	If not, see <http://www.gnu.org/licenses/>.
*/
/** @file internal.c
* @author Tim Hughes <tim@twistedfury.com>
* @author Matthew Wampler-Doty
* @date 2015
*/

#include <assert.h>
#include <inttypes.h>
#include <stddef.h>
#include <errno.h>
#include <math.h>
#include "mmap.h"
#include "ethash.h"
#include "fnv.h"
#include "endian.h"
#include "internal.h"
#include "data_sizes.h"
#include "io.h"
#include <assert.h>
#include <string.h>


#define CHUNK_START 1 << 0
#define CHUNK_END 1 << 1
#define PARENT 1 << 2
#define ROOT 1 << 3
#define KEYED_HASH 1 << 4
#define DERIVE_KEY_CONTEXT 1 << 5
#define DERIVE_KEY_MATERIAL 1 << 6

#ifdef WITH_CRYPTOPP

#include "sha3_cryptopp.h"

#else
#include "sha3.h"
#endif // WITH_CRYPTOPP

uint64_t ethash_get_datasize(uint64_t const block_number)
{
	assert(block_number / ETHASH_EPOCH_LENGTH < 2048);
	return dag_sizes[block_number / ETHASH_EPOCH_LENGTH];
}

uint64_t ethash_get_cachesize(uint64_t const block_number)
{
	assert(block_number / ETHASH_EPOCH_LENGTH < 2048);
	return cache_sizes[block_number / ETHASH_EPOCH_LENGTH];
}

// Follows Sergio's "STRICT MEMORY HARD HASHING FUNCTIONS" (2014)
// https://bitslog.files.wordpress.com/2013/12/memohash-v0-3.pdf
// SeqMemoHash(s, R, N)
bool static ethash_compute_cache_nodes(
	node* const nodes,
	uint64_t cache_size,
	ethash_h256_t const* seed
)
{
	if (cache_size % sizeof(node) != 0) {
		return false;
	}
	uint32_t const num_nodes = (uint32_t) (cache_size / sizeof(node));

	SHA3_512(nodes[0].bytes, (uint8_t*)seed, 32);

	for (uint32_t i = 1; i != num_nodes; ++i) {
		SHA3_512(nodes[i].bytes, nodes[i - 1].bytes, 64);
	}

	for (uint32_t j = 0; j != ETHASH_CACHE_ROUNDS; j++) {
		for (uint32_t i = 0; i != num_nodes; i++) {
			uint32_t const idx = nodes[i].words[0] % num_nodes;
			node data;
			data = nodes[(num_nodes - 1 + i) % num_nodes];
			for (uint32_t w = 0; w != NODE_WORDS; ++w) {
				data.words[w] ^= nodes[idx].words[w];
			}
			SHA3_512(nodes[i].bytes, data.bytes, sizeof(data));
		}
	}

	// now perform endian conversion
	fix_endian_arr32(nodes->words, num_nodes * NODE_WORDS);
	return true;
}

void ethash_calculate_dag_item(
	node* const ret,
	uint32_t node_index,
	ethash_light_t const light
)
{
	uint32_t num_parent_nodes = (uint32_t) (light->cache_size / sizeof(node));
	node const* cache_nodes = (node const *) light->cache;
	node const* init = &cache_nodes[node_index % num_parent_nodes];
	memcpy(ret, init, sizeof(node));
	ret->words[0] ^= node_index;
	SHA3_512(ret->bytes, ret->bytes, sizeof(node));
#if defined(_M_X64) && ENABLE_SSE
	__m128i const fnv_prime = _mm_set1_epi32(FNV_PRIME);
	__m128i xmm0 = ret->xmm[0];
	__m128i xmm1 = ret->xmm[1];
	__m128i xmm2 = ret->xmm[2];
	__m128i xmm3 = ret->xmm[3];
#endif

	for (uint32_t i = 0; i != ETHASH_DATASET_PARENTS; ++i) {
		uint32_t parent_index = fnv_hash(node_index ^ i, ret->words[i % NODE_WORDS]) % num_parent_nodes;
		node const *parent = &cache_nodes[parent_index];

#if defined(_M_X64) && ENABLE_SSE
		{
			xmm0 = _mm_mullo_epi32(xmm0, fnv_prime);
			xmm1 = _mm_mullo_epi32(xmm1, fnv_prime);
			xmm2 = _mm_mullo_epi32(xmm2, fnv_prime);
			xmm3 = _mm_mullo_epi32(xmm3, fnv_prime);
			xmm0 = _mm_xor_si128(xmm0, parent->xmm[0]);
			xmm1 = _mm_xor_si128(xmm1, parent->xmm[1]);
			xmm2 = _mm_xor_si128(xmm2, parent->xmm[2]);
			xmm3 = _mm_xor_si128(xmm3, parent->xmm[3]);

			// have to write to ret as values are used to compute index
			ret->xmm[0] = xmm0;
			ret->xmm[1] = xmm1;
			ret->xmm[2] = xmm2;
			ret->xmm[3] = xmm3;
		}
		#else
		{
			for (unsigned w = 0; w != NODE_WORDS; ++w) {
				ret->words[w] = fnv_hash(ret->words[w], parent->words[w]);
			}
		}
#endif
	}
	SHA3_512(ret->bytes, ret->bytes, sizeof(node));
}

bool ethash_compute_full_data(
	void* mem,
	uint64_t full_size,
	ethash_light_t const light,
	ethash_callback_t callback
)
{
	if (full_size % (sizeof(uint32_t) * MIX_WORDS) != 0 ||
		(full_size % sizeof(node)) != 0) {
		return false;
	}
	uint32_t const max_n = (uint32_t)(full_size / sizeof(node));
	node* full_nodes = mem;
	double const progress_change = 1.0f / max_n;
	double progress = 0.0f;
	// now compute full nodes
	for (uint32_t n = 0; n != max_n; ++n) {
		if (callback &&
			n % (max_n / 100) == 0 &&
			callback((unsigned int)(ceil(progress * 100.0f))) != 0) {

			return false;
		}
		progress += progress_change;
		ethash_calculate_dag_item(&(full_nodes[n]), n, light);
	}
	return true;
}

static bool ethash_hash(
	ethash_return_value_t* ret,
	node const* full_nodes,
	ethash_light_t const light,
	uint64_t full_size,
	ethash_h256_t const header_hash,
	uint64_t const nonce
)
{
	if (full_size % MIX_WORDS != 0) {
		return false;
	}

	// pack hash and nonce together into first 40 bytes of s_mix
	assert(sizeof(node) * 8 == 512);
	node s_mix[MIX_NODES + 1];
	memcpy(s_mix[0].bytes, &header_hash, 32);
	fix_endian64(s_mix[0].double_words[4], nonce);

	// compute sha3-512 hash and replicate across mix
    blake3_hash_512(s_mix->bytes, s_mix->bytes, 40);
	fix_endian_arr32(s_mix[0].words, 16);

	node* const mix = s_mix + 1;
	for (uint32_t w = 0; w != MIX_WORDS; ++w) {
		mix->words[w] = s_mix[0].words[w % NODE_WORDS];
	}

	unsigned const page_size = sizeof(uint32_t) * MIX_WORDS;
	unsigned const num_full_pages = (unsigned) (full_size / page_size);

	for (unsigned i = 0; i != ETHASH_ACCESSES; ++i) {
		uint32_t const index = fnv_hash(s_mix->words[0] ^ i, mix->words[i % MIX_WORDS]) % num_full_pages;

		for (unsigned n = 0; n != MIX_NODES; ++n) {
			node const* dag_node;
			if (full_nodes) {
				dag_node = &full_nodes[MIX_NODES * index + n];
			} else {
				node tmp_node;
				ethash_calculate_dag_item(&tmp_node, index * MIX_NODES + n, light);
				dag_node = &tmp_node;
			}

#if defined(_M_X64) && ENABLE_SSE
			{
				__m128i fnv_prime = _mm_set1_epi32(FNV_PRIME);
				__m128i xmm0 = _mm_mullo_epi32(fnv_prime, mix[n].xmm[0]);
				__m128i xmm1 = _mm_mullo_epi32(fnv_prime, mix[n].xmm[1]);
				__m128i xmm2 = _mm_mullo_epi32(fnv_prime, mix[n].xmm[2]);
				__m128i xmm3 = _mm_mullo_epi32(fnv_prime, mix[n].xmm[3]);
				mix[n].xmm[0] = _mm_xor_si128(xmm0, dag_node->xmm[0]);
				mix[n].xmm[1] = _mm_xor_si128(xmm1, dag_node->xmm[1]);
				mix[n].xmm[2] = _mm_xor_si128(xmm2, dag_node->xmm[2]);
				mix[n].xmm[3] = _mm_xor_si128(xmm3, dag_node->xmm[3]);
			}
			#else
			{
				for (unsigned w = 0; w != NODE_WORDS; ++w) {
					mix[n].words[w] = fnv_hash(mix[n].words[w], dag_node->words[w]);
				}
			}
#endif
		}

	}

	// compress mix
	for (uint32_t w = 0; w != MIX_WORDS; w += 4) {
		uint32_t reduction = mix->words[w + 0];
		reduction = reduction * FNV_PRIME ^ mix->words[w + 1];
		reduction = reduction * FNV_PRIME ^ mix->words[w + 2];
		reduction = reduction * FNV_PRIME ^ mix->words[w + 3];
		mix->words[w / 4] = reduction;
	}

	fix_endian_arr32(mix->words, MIX_WORDS / 4);
	memcpy(&ret->mix_hash, mix->bytes, 32);
	// final Keccak hash
	blake3_hash_256(s_mix->bytes, (uint8_t*)&ret->result, 64 + 32); // Keccak-256(s + compressed_mix)
	return true;
}

void ethash_quick_hash(
	ethash_h256_t* return_hash,
	ethash_h256_t const* header_hash,
	uint64_t nonce,
	ethash_h256_t const* mix_hash
)
{
	uint8_t buf[64 + 32];
	memcpy(buf, header_hash, 32);
	fix_endian64_same(nonce);
	memcpy(&(buf[32]), &nonce, 8);
	SHA3_512(buf, buf, 40);
	memcpy(&(buf[64]), mix_hash, 32);
	SHA3_256(return_hash, buf, 64 + 32);
}

ethash_h256_t ethash_get_seedhash(uint64_t block_number)
{
	ethash_h256_t ret;
	ethash_h256_reset(&ret);
	uint64_t const epochs = block_number / ETHASH_EPOCH_LENGTH;
	for (uint32_t i = 0; i < epochs; ++i)
		SHA3_256(&ret, (uint8_t*)&ret, 32);
	return ret;
}

bool ethash_quick_check_difficulty(
	ethash_h256_t const* header_hash,
	uint64_t const nonce,
	ethash_h256_t const* mix_hash,
	ethash_h256_t const* boundary
)
{

	ethash_h256_t return_hash;
	ethash_quick_hash(&return_hash, header_hash, nonce, mix_hash);
	return ethash_check_difficulty(&return_hash, boundary);
}

ethash_light_t ethash_light_new_internal(uint64_t cache_size, ethash_h256_t const* seed)
{
	struct ethash_light *ret;
	ret = calloc(sizeof(*ret), 1);
	if (!ret) {
		return NULL;
	}
	ret->cache = malloc((size_t)cache_size);
	if (!ret->cache) {
		goto fail_free_light;
	}
	node* nodes = (node*)ret->cache;
	if (!ethash_compute_cache_nodes(nodes, cache_size, seed)) {
		goto fail_free_cache_mem;
	}
	ret->cache_size = cache_size;
	return ret;

fail_free_cache_mem:
	free(ret->cache);
fail_free_light:
	free(ret);
	return NULL;
}

ethash_light_t ethash_light_new(uint64_t block_number)
{
	ethash_h256_t seedhash = ethash_get_seedhash(block_number);
	ethash_light_t ret;
	ret = ethash_light_new_internal(ethash_get_cachesize(block_number), &seedhash);
	ret->block_number = block_number;
	return ret;
}

void ethash_light_delete(ethash_light_t light)
{
	if (light->cache) {
		free(light->cache);
	}
	free(light);
}

ethash_return_value_t ethash_light_compute_internal(
	ethash_light_t light,
	uint64_t full_size,
	ethash_h256_t const header_hash,
	uint64_t nonce
)
{
  	ethash_return_value_t ret;
	ret.success = true;
	if (!ethash_hash(&ret, NULL, light, full_size, header_hash, nonce)) {
		ret.success = false;
	}
	return ret;
}

ethash_return_value_t ethash_light_compute(
	ethash_light_t light,
	ethash_h256_t const header_hash,
	uint64_t nonce
)
{
	uint64_t full_size = ethash_get_datasize(light->block_number);
	return ethash_light_compute_internal(light, full_size, header_hash, nonce);
}

static bool ethash_mmap(struct ethash_full* ret, FILE* f)
{
	int fd;
	char* mmapped_data;
	errno = 0;
	ret->file = f;
	if ((fd = ethash_fileno(ret->file)) == -1) {
		return false;
	}
	mmapped_data= mmap(
		NULL,
		(size_t)ret->file_size + ETHASH_DAG_MAGIC_NUM_SIZE,
		PROT_READ | PROT_WRITE,
		MAP_SHARED,
		fd,
		0
	);
	if (mmapped_data == MAP_FAILED) {
		return false;
	}
	ret->data = (node*)(mmapped_data + ETHASH_DAG_MAGIC_NUM_SIZE);
	return true;
}

ethash_full_t ethash_full_new_internal(
	char const* dirname,
	ethash_h256_t const seed_hash,
	uint64_t full_size,
	ethash_light_t const light,
	ethash_callback_t callback
)
{
	struct ethash_full* ret;
	FILE *f = NULL;
	ret = calloc(sizeof(*ret), 1);
	if (!ret) {
		return NULL;
	}
	ret->file_size = (size_t)full_size;
	switch (ethash_io_prepare(dirname, seed_hash, &f, (size_t)full_size, false)) {
	case ETHASH_IO_FAIL:
		// ethash_io_prepare will do all ETHASH_CRITICAL() logging in fail case
		goto fail_free_full;
	case ETHASH_IO_MEMO_MATCH:
		if (!ethash_mmap(ret, f)) {
			ETHASH_CRITICAL("mmap failure()");
			goto fail_close_file;
		}
		return ret;
	case ETHASH_IO_MEMO_SIZE_MISMATCH:
		// if a DAG of same filename but unexpected size is found, silently force new file creation
		if (ethash_io_prepare(dirname, seed_hash, &f, (size_t)full_size, true) != ETHASH_IO_MEMO_MISMATCH) {
			ETHASH_CRITICAL("Could not recreate DAG file after finding existing DAG with unexpected size.");
			goto fail_free_full;
		}
		// fallthrough to the mismatch case here, DO NOT go through match
	case ETHASH_IO_MEMO_MISMATCH:
		if (!ethash_mmap(ret, f)) {
			ETHASH_CRITICAL("mmap failure()");
			goto fail_close_file;
		}
		break;
	}

	if (!ethash_compute_full_data(ret->data, full_size, light, callback)) {
		ETHASH_CRITICAL("Failure at computing DAG data.");
		goto fail_free_full_data;
	}

	// after the DAG has been filled then we finalize it by writting the magic number at the beginning
	if (fseek(f, 0, SEEK_SET) != 0) {
		ETHASH_CRITICAL("Could not seek to DAG file start to write magic number.");
		goto fail_free_full_data;
	}
	uint64_t const magic_num = ETHASH_DAG_MAGIC_NUM;
	if (fwrite(&magic_num, ETHASH_DAG_MAGIC_NUM_SIZE, 1, f) != 1) {
		ETHASH_CRITICAL("Could not write magic number to DAG's beginning.");
		goto fail_free_full_data;
	}
	if (fflush(f) != 0) {// make sure the magic number IS there
		ETHASH_CRITICAL("Could not flush memory mapped data to DAG file. Insufficient space?");
		goto fail_free_full_data;
	}
	return ret;

fail_free_full_data:
	// could check that munmap(..) == 0 but even if it did not can't really do anything here
	munmap(ret->data, (size_t)full_size);
fail_close_file:
	fclose(ret->file);
fail_free_full:
	free(ret);
	return NULL;
}

ethash_full_t ethash_full_new(ethash_light_t light, ethash_callback_t callback)
{
	char strbuf[256];
	if (!ethash_get_default_dirname(strbuf, 256)) {
		return NULL;
	}
	uint64_t full_size = ethash_get_datasize(light->block_number);
	ethash_h256_t seedhash = ethash_get_seedhash(light->block_number);
	return ethash_full_new_internal(strbuf, seedhash, full_size, light, callback);
}

void ethash_full_delete(ethash_full_t full)
{
	// could check that munmap(..) == 0 but even if it did not can't really do anything here
	munmap(full->data, (size_t)full->file_size);
	if (full->file) {
		fclose(full->file);
	}
	free(full);
}

ethash_return_value_t ethash_full_compute(
	ethash_full_t full,
	ethash_h256_t const header_hash,
	uint64_t nonce
)
{
	ethash_return_value_t ret;
	ret.success = true;
	if (!ethash_hash(
		&ret,
		(node const*)full->data,
		NULL,
		full->file_size,
		header_hash,
		nonce)) {
		ret.success = false;
	}
	return ret;
}

void const* ethash_full_dag(ethash_full_t full)
{
	return full->data;
}

uint64_t ethash_full_dag_size(ethash_full_t full)
{
	return full->file_size;
}

static uint32_t IV[8] = {
        0x6A09E667, 0xBB67AE85, 0x3C6EF372, 0xA54FF53A,
        0x510E527F, 0x9B05688C, 0x1F83D9AB, 0x5BE0CD19,
};

static size_t MSG_PERMUTATION[16] = {2, 6,  3,  10, 7, 0,  4,  13,
                                     1, 11, 12, 5,  9, 14, 15, 8};

inline static uint32_t rotate_right(uint32_t x, int n) {
return (x >> n) | (x << (32 - n));
}

// The mixing function, G, which mixes either a column or a diagonal.
inline static void g(uint32_t state[16], size_t a, size_t b, size_t c, size_t d,
                     uint32_t mx, uint32_t my) {
    state[a] = state[a] + state[b] + mx;
    state[d] = rotate_right(state[d] ^ state[a], 16);
    state[c] = state[c] + state[d];
    state[b] = rotate_right(state[b] ^ state[c], 12);
    state[a] = state[a] + state[b] + my;
    state[d] = rotate_right(state[d] ^ state[a], 8);
    state[c] = state[c] + state[d];
    state[b] = rotate_right(state[b] ^ state[c], 7);
}

inline static void round_function(uint32_t state[16], uint32_t m[16]) {
    // Mix the columns.
    g(state, 0, 4, 8, 12, m[0], m[1]);
    g(state, 1, 5, 9, 13, m[2], m[3]);
    g(state, 2, 6, 10, 14, m[4], m[5]);
    g(state, 3, 7, 11, 15, m[6], m[7]);
    // Mix the diagonals.
    g(state, 0, 5, 10, 15, m[8], m[9]);
    g(state, 1, 6, 11, 12, m[10], m[11]);
    g(state, 2, 7, 8, 13, m[12], m[13]);
    g(state, 3, 4, 9, 14, m[14], m[15]);
}

inline static void permute(uint32_t m[16]) {
    uint32_t permuted[16];
    for (size_t i = 0; i < 16; i++) {
        permuted[i] = m[MSG_PERMUTATION[i]];
    }
    memcpy(m, permuted, sizeof(permuted));
}

inline static void compress(const uint32_t chaining_value[8],
                            const uint32_t block_words[16], uint64_t counter,
                            uint32_t block_len, uint32_t flags,
                            uint32_t out[16]) {
    uint32_t state[16] = {
            chaining_value[0],
            chaining_value[1],
            chaining_value[2],
            chaining_value[3],
            chaining_value[4],
            chaining_value[5],
            chaining_value[6],
            chaining_value[7],
            IV[0],
            IV[1],
            IV[2],
            IV[3],
            (uint32_t)counter,
            (uint32_t)(counter >> 32),
            block_len,
            flags,
    };
    uint32_t block[16];
    memcpy(block, block_words, sizeof(block));

    round_function(state, block); // round 1
    permute(block);
    round_function(state, block); // round 2
    permute(block);
    round_function(state, block); // round 3
    permute(block);
    round_function(state, block); // round 4
    permute(block);
    round_function(state, block); // round 5
    permute(block);
    round_function(state, block); // round 6
    permute(block);
    round_function(state, block); // round 7

    for (size_t i = 0; i < 8; i++) {
        state[i] ^= state[i + 8];
        state[i + 8] ^= chaining_value[i];
    }

    memcpy(out, state, sizeof(state));
}

inline static void words_from_little_endian_bytes(const void *bytes,
                                                  size_t bytes_len,
                                                  uint32_t *out) {
    assert(bytes_len % 4 == 0);
    const uint8_t *u8_ptr = (const uint8_t *)bytes;
    for (size_t i = 0; i < (bytes_len / 4); i++) {
        out[i] = ((uint32_t)(*u8_ptr++));
        out[i] += ((uint32_t)(*u8_ptr++)) << 8;
        out[i] += ((uint32_t)(*u8_ptr++)) << 16;
        out[i] += ((uint32_t)(*u8_ptr++)) << 24;
    }
}

// Each chunk or parent node can produce either an 8-word chaining value or, by
// setting the ROOT flag, any number of final output bytes. The Output struct
// captures the state just prior to choosing between those two possibilities.
typedef struct output {
    uint32_t input_chaining_value[8];
    uint32_t block_words[16];
    uint64_t counter;
    uint32_t block_len;
    uint32_t flags;
} output;

inline static void output_chaining_value(const output *self, uint32_t out[8]) {
    uint32_t out16[16];
    compress(self->input_chaining_value, self->block_words, self->counter,
             self->block_len, self->flags, out16);
    memcpy(out, out16, 8 * 4);
}

inline static void output_root_bytes(const output *self, void *out,
                                     size_t out_len) {
    uint8_t *out_u8 = (uint8_t *)out;
    uint64_t output_block_counter = 0;
    while (out_len > 0) {
        uint32_t words[16];
        compress(self->input_chaining_value, self->block_words,
                 output_block_counter, self->block_len, self->flags | ROOT, words);
        for (size_t word = 0; word < 16; word++) {
            for (int byte = 0; byte < 4; byte++) {
                if (out_len == 0) {
                    return;
                }
                *out_u8 = (uint8_t)(words[word] >> (8 * byte));
                out_u8++;
                out_len--;
            }
        }
        output_block_counter++;
    }
}

inline static void chunk_state_init(_blake3_chunk_state *self,
                                    const uint32_t key_words[8],
                                    uint64_t chunk_counter, uint32_t flags) {
    memcpy(self->chaining_value, key_words, sizeof(self->chaining_value));
    self->chunk_counter = chunk_counter;
    memset(self->block, 0, sizeof(self->block));
    self->block_len = 0;
    self->blocks_compressed = 0;
    self->flags = flags;
}

inline static size_t chunk_state_len(const _blake3_chunk_state *self) {
    return BLAKE3_BLOCK_LEN * (size_t)self->blocks_compressed +
                                      (size_t)self->block_len;
}

inline static uint32_t chunk_state_start_flag(const _blake3_chunk_state *self) {
    if (self->blocks_compressed == 0) {
        return CHUNK_START;
    } else {
        return 0;
    }
}

inline static void chunk_state_update(_blake3_chunk_state *self,
                                      const void *input, size_t input_len) {
    const uint8_t *input_u8 = (const uint8_t *)input;
    while (input_len > 0) {
        // If the block buffer is full, compress it and clear it. More input is
        // coming, so this compression is not CHUNK_END.
        if (self->block_len == BLAKE3_BLOCK_LEN) {
            uint32_t block_words[16];
            words_from_little_endian_bytes(self->block, BLAKE3_BLOCK_LEN,
                                           block_words);
            uint32_t out16[16];
            compress(self->chaining_value, block_words, self->chunk_counter,
                     BLAKE3_BLOCK_LEN, self->flags | chunk_state_start_flag(self),
                     out16);
            memcpy(self->chaining_value, out16, sizeof(self->chaining_value));
            self->blocks_compressed++;
            memset(self->block, 0, sizeof(self->block));
            self->block_len = 0;
        }

        // Copy input bytes into the block buffer.
        size_t want = BLAKE3_BLOCK_LEN - (size_t)self->block_len;
        size_t take = want;
        if (input_len < want) {
            take = input_len;
        }
        memcpy(&self->block[(size_t)self->block_len], input_u8, take);
        self->block_len += (uint8_t)take;
        input_u8 += take;
        input_len -= take;
    }
}

inline static output chunk_state_output(const _blake3_chunk_state *self) {
    output ret;
    memcpy(ret.input_chaining_value, self->chaining_value,
           sizeof(ret.input_chaining_value));
    words_from_little_endian_bytes(self->block, sizeof(self->block),
                                   ret.block_words);
    ret.counter = self->chunk_counter;
    ret.block_len = (uint32_t)self->block_len;
    ret.flags = self->flags | chunk_state_start_flag(self) | CHUNK_END;
    return ret;
}

inline static output parent_output(const uint32_t left_child_cv[8],
                                   const uint32_t right_child_cv[8],
                                   const uint32_t key_words[8],
                                   uint32_t flags) {
    output ret;
    memcpy(ret.input_chaining_value, key_words, sizeof(ret.input_chaining_value));
    memcpy(&ret.block_words[0], left_child_cv, 8 * 4);
    memcpy(&ret.block_words[8], right_child_cv, 8 * 4);
    ret.counter = 0; // Always 0 for parent nodes.
    ret.block_len =
            BLAKE3_BLOCK_LEN; // Always BLAKE3_BLOCK_LEN (64) for parent nodes.
    ret.flags = PARENT | flags;
    return ret;
}

inline static void parent_cv(const uint32_t left_child_cv[8],
                             const uint32_t right_child_cv[8],
                             const uint32_t key_words[8], uint32_t flags,
                             uint32_t out[8]) {
    output o = parent_output(left_child_cv, right_child_cv, key_words, flags);
    // We only write to `out` after we've read the inputs. That makes it safe for
    // `out` to alias an input, which we do below.
    output_chaining_value(&o, out);
}

inline static void hasher_init_internal(blake3_hasher *self,
                                        const uint32_t key_words[8],
                                        uint32_t flags) {
    chunk_state_init(&self->chunk_state, key_words, 0, flags);
    memcpy(self->key_words, key_words, sizeof(self->key_words));
    self->cv_stack_len = 0;
    self->flags = flags;
}

// Construct a new `Hasher` for the regular hash function.
void blake3_hasher_init(blake3_hasher *self) {
    hasher_init_internal(self, IV, 0);
}

// Construct a new `Hasher` for the keyed hash function.
void blake3_hasher_init_keyed(blake3_hasher *self,
                              const uint8_t key[BLAKE3_KEY_LEN]) {
    uint32_t key_words[8];
    words_from_little_endian_bytes(key, BLAKE3_KEY_LEN, key_words);
    hasher_init_internal(self, key_words, KEYED_HASH);
}

// Construct a new `Hasher` for the key derivation function. The context
// string should be hardcoded, globally unique, and application-specific.
void blake3_hasher_init_derive_key(blake3_hasher *self, const char *context) {
    blake3_hasher context_hasher;
    hasher_init_internal(&context_hasher, IV, DERIVE_KEY_CONTEXT);
    blake3_hasher_update(&context_hasher, context, strlen(context));
    uint8_t context_key[BLAKE3_KEY_LEN];
    blake3_hasher_finalize(&context_hasher, context_key, BLAKE3_KEY_LEN);
    uint32_t context_key_words[8];
    words_from_little_endian_bytes(context_key, BLAKE3_KEY_LEN,
                                   context_key_words);
    hasher_init_internal(self, context_key_words, DERIVE_KEY_MATERIAL);
}

inline static void hasher_push_stack(blake3_hasher *self,
                                     const uint32_t cv[8]) {
    memcpy(&self->cv_stack[(size_t)self->cv_stack_len * 8], cv, 8 * 4);
    self->cv_stack_len++;
}

// Returns a pointer to the popped CV, which is valid until the next push.
inline static const uint32_t *hasher_pop_stack(blake3_hasher *self) {
self->cv_stack_len--;
return &self->cv_stack[(size_t)self->cv_stack_len * 8];
}

// Section 5.1.2 of the BLAKE3 spec explains this algorithm in more detail.
inline static void hasher_add_chunk_cv(blake3_hasher *self, uint32_t new_cv[8],
                                       uint64_t total_chunks) {
    // This chunk might complete some subtrees. For each completed subtree, its
    // left child will be the current top entry in the CV stack, and its right
    // child will be the current value of `new_cv`. Pop each left child off the
    // stack, merge it with `new_cv`, and overwrite `new_cv` with the result.
    // After all these merges, push the final value of `new_cv` onto the stack.
    // The number of completed subtrees is given by the number of trailing 0-bits
    // in the new total number of chunks.
    while ((total_chunks & 1) == 0) {
        parent_cv(hasher_pop_stack(self), new_cv, self->key_words, self->flags,
                  new_cv);
        total_chunks >>= 1;
    }
    hasher_push_stack(self, new_cv);
}

// Add input to the hash state. This can be called any number of times.
void blake3_hasher_update(blake3_hasher *self, const void *input,
                          size_t input_len) {
    const uint8_t *input_u8 = (const uint8_t *)input;
    while (input_len > 0) {
        // If the current chunk is complete, finalize it and reset the chunk state.
        // More input is coming, so this chunk is not ROOT.
        if (chunk_state_len(&self->chunk_state) == BLAKE3_CHUNK_LEN) {
            output chunk_output = chunk_state_output(&self->chunk_state);
            uint32_t chunk_cv[8];
            output_chaining_value(&chunk_output, chunk_cv);
            uint64_t total_chunks = self->chunk_state.chunk_counter + 1;
            hasher_add_chunk_cv(self, chunk_cv, total_chunks);
            chunk_state_init(&self->chunk_state, self->key_words, total_chunks,
                             self->flags);
        }

        // Compress input bytes into the current chunk state.
        size_t want = BLAKE3_CHUNK_LEN - chunk_state_len(&self->chunk_state);
        size_t take = want;
        if (input_len < want) {
            take = input_len;
        }
        chunk_state_update(&self->chunk_state, input_u8, take);
        input_u8 += take;
        input_len -= take;
    }
}

// Finalize the hash and write any number of output bytes.
void blake3_hasher_finalize(const blake3_hasher *self, void *out,
                            size_t out_len) {
    // Starting with the output from the current chunk, compute all the parent
    // chaining values along the right edge of the tree, until we have the root
    // output.
    output current_output = chunk_state_output(&self->chunk_state);
    size_t parent_nodes_remaining = (size_t)self->cv_stack_len;
    while (parent_nodes_remaining > 0) {
        parent_nodes_remaining--;
        uint32_t current_cv[8];
        output_chaining_value(&current_output, current_cv);
        current_output = parent_output(&self->cv_stack[parent_nodes_remaining * 8],
                                       current_cv, self->key_words, self->flags);
    }
    output_root_bytes(&current_output, out, out_len);
}

void blake3_hash_256(const uint8_t *input, uint8_t *out, size_t len) {
    blake3_hasher hasher;
    blake3_hasher_init(&hasher);
    blake3_hasher_update(&hasher, input, len);
    blake3_hasher_finalize(&hasher, out, 32);
}

void blake3_hash_512(const uint8_t *input, uint8_t *out, size_t len) {
    blake3_hasher hasher;
    blake3_hasher_init(&hasher);
    blake3_hasher_update(&hasher, input, len);
    blake3_hasher_finalize(&hasher, out, 64);
}
#include "heavyhash.h"
#include "keccak_tiny.h"

#include <inttypes.h>
#include <string.h>
#include <stdlib.h>
#include <math.h>
#include <stdbool.h>

#define EPS 1e-9

#if defined(_MSC_VER)
#define ALIGN(n) __declspec(align(16))
#elif defined(__GNUC__) || defined(__clang)
#define ALIGN(x) __attribute__((__aligned__(x)))
#else
#define ALIGN(x)
#endif

struct xoshiro_state {
    uint64_t s[4];
};

static inline uint64_t rotl64(const uint64_t x, int k) {
    return (x << k) | (x >> (64 - k));
}

static inline uint64_t xoshiro_gen(struct xoshiro_state *state) {
    const uint64_t result = rotl64(state->s[0] + state->s[3], 23) + state->s[0];

    const uint64_t t = state->s[1] << 17;

    state->s[2] ^= state->s[0];
    state->s[3] ^= state->s[1];
    state->s[1] ^= state->s[2];
    state->s[0] ^= state->s[3];

    state->s[2] ^= t;

    state->s[3] = rotl64(state->s[3], 45);

    return result;
}

static inline uint64_t le64dec(const void *pp)
{
    const uint8_t *p = (uint8_t const *)pp;
    return ((uint64_t)(p[0]) | ((uint64_t)(p[1]) << 8) |
            ((uint64_t)(p[2]) << 16) | ((uint64_t)(p[3]) << 24)) |
           ((uint64_t)(p[4]) << 32) | ((uint64_t)(p[5]) << 40) |
           ((uint64_t)(p[6]) << 48) | ((uint64_t)(p[7]) << 56);
}

static int compute_rank(const uint_fast16_t A[64][64])
{
    double B[64][64];
    for (int i = 0; i < 64; ++i){
        for(int j = 0; j < 64; ++j){
            B[i][j] = A[i][j];
        }
    }

    int rank = 0;
    bool row_selected[64];

    for (int i = 0; i < 64; ++i)
        row_selected[i] = 0;


    for (int i = 0; i < 64; ++i) {
        int j;
        for (j = 0; j < 64; ++j) {
            if (!row_selected[j] && fabs(B[j][i]) > EPS)
                break;
        }
        if (j != 64) {
            ++rank;
            row_selected[j] = true;
            for (int p = i + 1; p < 64; ++p)
                B[j][p] /= B[j][i];
            for (int k = 0; k < 64; ++k) {
                if (k != j && fabs(B[k][i]) > EPS) {
                    for (int p = i + 1; p < 64; ++p)
                        B[k][p] -= B[j][p] * B[k][i];
                }
            }
        }
    }
    return rank;
}

static inline bool is_full_rank(const uint_fast16_t matrix[64][64])
{
    return compute_rank(matrix) == 64;
}

static inline void generate_matrix(uint_fast16_t matrix[64][64], struct xoshiro_state *state) {
    do {
        for (int i = 0; i < 64; ++i) {
            for (int j = 0; j < 64; j += 16) {
                uint64_t value = xoshiro_gen(state);
                for (int shift = 0; shift < 16; ++shift) {
                    matrix[i][j + shift] = (value >> (4*shift)) & 0xF;
                }
            }
        }
    } while (!is_full_rank(matrix));
}

static void heavyhash(const uint_fast16_t matrix[64][64], void* pdata, size_t pdata_len, void* output)
{
    ALIGN(32) uint8_t hash_first[32];
    ALIGN(32) uint8_t hash_second[32];
    ALIGN(32) uint8_t hash_xored[32];

    ALIGN(32) uint_fast16_t vector[64];
    ALIGN(32) uint_fast16_t product[64];

    sha3_256((uint8_t*) hash_first, 32, pdata, pdata_len);

    for (int i = 0; i < 32; ++i) {
        vector[2*i] = (hash_first[i] >> 4);
        vector[2*i+1] = hash_first[i] & 0xF;
    }

    for (int i = 0; i < 64; ++i) {
        uint_fast16_t sum = 0;
        for (int j = 0; j < 64; ++j) {
            sum += matrix[i][j] * vector[j];
        }
        product[i] = (sum >> 10);
    }

    for (int i = 0; i < 32; ++i) {
        hash_second[i] = (product[2*i] << 4) | (product[2*i+1]);
    }

    for (int i = 0; i < 32; ++i) {
        hash_xored[i] = hash_first[i] ^ hash_second[i];
    }
    sha3_256(output, 32, hash_xored, 32);
}

void heavyhash_hash(const char* input, char* output, uint32_t len)
{
    ALIGN(64) uint_fast16_t matrix[64][64];
    ALIGN(64) uint32_t seed[8];

    sha3_256((void*)seed, 32, (void*)(input + 4), 32);

    struct xoshiro_state state;
    for (int i = 0; i < 4; ++i)
	{
        state.s[i] = le64dec(seed + 2*i);
    }

    generate_matrix(matrix, &state);

    heavyhash(matrix, (void*)input, len, output);

}

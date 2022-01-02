#include "x16rv2.h"
#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include <stdio.h>

#include "sha3/sph_blake.h"
#include "sha3/sph_bmw.h"
#include "sha3/sph_groestl.h"
#include "sha3/sph_jh.h"
#include "sha3/sph_keccak.h"
#include "sha3/sph_skein.h"
#include "sha3/sph_luffa.h"
#include "sha3/sph_cubehash.h"
#include "sha3/sph_shavite.h"
#include "sha3/sph_simd.h"
#include "sha3/sph_echo.h"
#include "sha3/sph_hamsi.h"
#include "sha3/sph_fugue.h"
#include "sha3/sph_shabal.h"
#include "sha3/sph_whirlpool.h"
#include "sha3/sph_sha2.h"
#include "sha3/sph_tiger.h"

enum Algo {
    BLAKE = 0,
    BMW,
    GROESTL,
    JH,
    KECCAK,
    SKEIN,
    LUFFA,
    CUBEHASH,
    SHAVITE,
    SIMD,
    ECHO,
    HAMSI,
    FUGUE,
    SHABAL,
    WHIRLPOOL,
    SHA512,
    HASH_FUNC_COUNT
};

static void getAlgoString(const uint8_t* prevblock, char *output)
{
    char *sptr = output;
    int j;

    for (j = 0; j < HASH_FUNC_COUNT; j++) {
        char b = (15 - j) >> 1; // 16 ascii hex chars, reversed
        uint8_t algoDigit = (j & 1) ? prevblock[b] & 0xF : prevblock[b] >> 4;
        if (algoDigit >= 10)
            sprintf(sptr, "%c", 'A' + (algoDigit - 10));
        else
            sprintf(sptr, "%u", (uint32_t) algoDigit);
        sptr++;
    }
    *sptr = '\0';
}

void x16rv2_hash(const char* input, char* output, uint32_t len)
{
    uint32_t hash[64/4];
    char hashOrder[HASH_FUNC_COUNT + 1] = { 0 };

    sph_blake512_context     ctx_blake;
    sph_bmw512_context       ctx_bmw;
    sph_groestl512_context   ctx_groestl;
    sph_skein512_context     ctx_skein;
    sph_jh512_context        ctx_jh;
    sph_keccak512_context    ctx_keccak;
    sph_luffa512_context     ctx_luffa;
    sph_cubehash512_context  ctx_cubehash;
    sph_shavite512_context   ctx_shavite;
    sph_simd512_context      ctx_simd;
    sph_echo512_context      ctx_echo;
    sph_hamsi512_context     ctx_hamsi;
    sph_fugue512_context     ctx_fugue;
    sph_shabal512_context    ctx_shabal;
    sph_whirlpool_context    ctx_whirlpool;
    sph_sha512_context       ctx_sha512;
    sph_tiger_context        ctx_tiger;

    void *in = (void*) input;
    int size = len; // should be 80
    int i;
    int j;

    getAlgoString(&input[4], hashOrder);

    for (i = 0; i < 16; i++) {
        const char elem = hashOrder[i];
        const uint8_t algo = elem >= 'A' ? elem - 'A' + 10 : elem - '0';

        switch (algo) {
            case BLAKE:
            sph_blake512_init(&ctx_blake);
            sph_blake512(&ctx_blake, in, size);
            sph_blake512_close(&ctx_blake, hash);
            break;
            case BMW:
            sph_bmw512_init(&ctx_bmw);
            sph_bmw512(&ctx_bmw, in, size);
            sph_bmw512_close(&ctx_bmw, hash);
            break;
            case GROESTL:
            sph_groestl512_init(&ctx_groestl);
            sph_groestl512(&ctx_groestl, in, size);
            sph_groestl512_close(&ctx_groestl, hash);
            break;
            case SKEIN:
            sph_skein512_init(&ctx_skein);
            sph_skein512(&ctx_skein, in, size);
            sph_skein512_close(&ctx_skein, hash);
            break;
            case JH:
            sph_jh512_init(&ctx_jh);
            sph_jh512(&ctx_jh, in, size);
            sph_jh512_close(&ctx_jh, hash);
            break;
            case KECCAK:
            sph_tiger_init(&ctx_tiger);
            sph_tiger(&ctx_tiger, (const void*) in, size);
            sph_tiger_close(&ctx_tiger, (void*) hash);
            for (j = 24; j < 64; ++j) ((uint8_t*)hash)[j] = 0; // Pad the 24 bytes to bring it to 64 bytes

            sph_keccak512_init(&ctx_keccak);
            sph_keccak512(&ctx_keccak, hash, 64);
            sph_keccak512_close(&ctx_keccak, hash);
            break;
            case LUFFA:
            sph_tiger_init(&ctx_tiger);
            sph_tiger(&ctx_tiger, (const void*) in, size);
            sph_tiger_close(&ctx_tiger, (void*) hash);
            for (j = 24; j < 64; ++j) ((uint8_t*)hash)[j] = 0; // Pad the 24 bytes to bring it to 64 bytes

            sph_luffa512_init(&ctx_luffa);
            sph_luffa512(&ctx_luffa, hash, 64);
            sph_luffa512_close(&ctx_luffa, hash);
            break;
            case CUBEHASH:
            sph_cubehash512_init(&ctx_cubehash);
            sph_cubehash512(&ctx_cubehash, in, size);
            sph_cubehash512_close(&ctx_cubehash, hash);
            break;
            case SHAVITE:
            sph_shavite512_init(&ctx_shavite);
            sph_shavite512(&ctx_shavite, in, size);
            sph_shavite512_close(&ctx_shavite, hash);
            break;
            case SIMD:
            sph_simd512_init(&ctx_simd);
            sph_simd512(&ctx_simd, in, size);
            sph_simd512_close(&ctx_simd, hash);
            break;
            case ECHO:
            sph_echo512_init(&ctx_echo);
            sph_echo512(&ctx_echo, in, size);
            sph_echo512_close(&ctx_echo, hash);
            break;
            case HAMSI:
            sph_hamsi512_init(&ctx_hamsi);
            sph_hamsi512(&ctx_hamsi, in, size);
            sph_hamsi512_close(&ctx_hamsi, hash);
            break;
            case FUGUE:
            sph_fugue512_init(&ctx_fugue);
            sph_fugue512(&ctx_fugue, in, size);
            sph_fugue512_close(&ctx_fugue, hash);
            break;
            case SHABAL:
            sph_shabal512_init(&ctx_shabal);
            sph_shabal512(&ctx_shabal, in, size);
            sph_shabal512_close(&ctx_shabal, hash);
            break;
            case WHIRLPOOL:
            sph_whirlpool_init(&ctx_whirlpool);
            sph_whirlpool(&ctx_whirlpool, in, size);
            sph_whirlpool_close(&ctx_whirlpool, hash);
            break;
            case SHA512:
            sph_tiger_init(&ctx_tiger);
            sph_tiger(&ctx_tiger, (const void*) in, size);
            sph_tiger_close(&ctx_tiger, (void*) hash);
            for (j = 24; j < 64; ++j) ((uint8_t*)hash)[j] = 0; // Pad the 24 bytes to bring it to 64 bytes

            sph_sha512_init(&ctx_sha512);
            sph_sha512(&ctx_sha512,(const void*) hash, 64);
            sph_sha512_close(&ctx_sha512,(void*) hash);
            break;
        }
        in = (void*) hash;
        size = 64;
    }
    memcpy(output, hash, 32);
}

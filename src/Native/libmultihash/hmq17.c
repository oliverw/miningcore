#include <stdio.h>
#include <stdlib.h>
#include <string.h>

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
#include "sha3/sph_haval.h"

#ifdef _MSC_VER
#include <stdint.h>
#endif

typedef struct
{
    sph_blake512_context    blake1, blake2;
    sph_bmw512_context      bmw1, bmw2, bmw3;
    sph_groestl512_context  groestl1, groestl2;
    sph_skein512_context    skein1, skein2;
    sph_jh512_context       jh1, jh2;
    sph_keccak512_context   keccak1, keccak2;
    sph_luffa512_context    luffa1, luffa2;
    sph_cubehash512_context	cubehash;
    sph_shavite512_context	shavite1, shavite2;
    sph_simd512_context     simd1, simd2;
    sph_echo512_context     echo1, echo2;
    sph_hamsi512_context    hamsi;
    sph_fugue512_context    fugue1, fugue2;
    sph_shabal512_context   shabal;
    sph_whirlpool_context   whirlpool1, whirlpool2, whirlpool3, whirlpool4;
    sph_sha512_context      sha1, sha2;
    sph_haval256_5_context  haval1, haval2;
} hmq_contexts;

#ifdef _MSC_VER
#include <stdint.h>
static __declspec(thread) hmq_contexts base_contexts;
static __declspec(thread) int hmq_context_init = 0;
#else
static __thread hmq_contexts base_contexts;
static __thread int hmq_context_init = 0;
#endif


static void init_contexts(hmq_contexts* ctx)
{
    sph_bmw512_init(&ctx->bmw1);
    sph_bmw512_init(&ctx->bmw2);
    sph_bmw512_init(&ctx->bmw2);
    sph_bmw512_init(&ctx->bmw3);
    sph_whirlpool_init(&ctx->whirlpool1);
    sph_whirlpool_init(&ctx->whirlpool2);
    sph_whirlpool_init(&ctx->whirlpool3);
    sph_whirlpool_init(&ctx->whirlpool4);
    sph_groestl512_init(&ctx->groestl1);
    sph_groestl512_init(&ctx->groestl2);
    sph_skein512_init(&ctx->skein1);
    sph_skein512_init(&ctx->skein2);
    sph_jh512_init(&ctx->jh1);
    sph_jh512_init(&ctx->jh2);
    sph_keccak512_init(&ctx->keccak1);
    sph_keccak512_init(&ctx->keccak2);
    sph_blake512_init(&ctx->blake1);
    sph_blake512_init(&ctx->blake2);
    sph_luffa512_init(&ctx->luffa1);
    sph_luffa512_init(&ctx->luffa2);
    sph_cubehash512_init(&ctx->cubehash);
    sph_shavite512_init(&ctx->shavite1);
    sph_shavite512_init(&ctx->shavite2);
    sph_simd512_init(&ctx->simd1);
    sph_simd512_init(&ctx->simd2);
    sph_echo512_init(&ctx->echo1);
    sph_echo512_init(&ctx->echo2);
    sph_hamsi512_init(&ctx->hamsi);
    sph_fugue512_init(&ctx->fugue1);
    sph_fugue512_init(&ctx->fugue2);
    sph_shabal512_init(&ctx->shabal);
    sph_sha512_init(&ctx->sha1);
    sph_sha512_init(&ctx->sha2);
    sph_haval256_5_init(&ctx->haval1);
    sph_haval256_5_init(&ctx->haval2);
}

void hmq17_hash(const char* input, char* output, uint32_t len)
{
    uint32_t hash[32];

    const uint32_t mask = 24;

    hmq_contexts ctx;

    if (!hmq_context_init) {
        init_contexts(&base_contexts);
        hmq_context_init = 1;
    }
    memcpy(&ctx, &base_contexts, sizeof(hmq_contexts));

    sph_bmw512(&ctx.bmw1, input, len);
    sph_bmw512_close(&ctx.bmw1, hash);

    sph_whirlpool(&ctx.whirlpool1, hash, 64);
    sph_whirlpool_close(&ctx.whirlpool1, hash);

    if (hash[0] & mask) {
        sph_groestl512(&ctx.groestl1, hash, 64);
        sph_groestl512_close(&ctx.groestl1, hash);
    }
    else {
        sph_skein512(&ctx.skein1, hash, 64);
        sph_skein512_close(&ctx.skein1, hash);
    }

    sph_jh512(&ctx.jh1, hash, 64);
    sph_jh512_close(&ctx.jh1, hash);

    sph_keccak512(&ctx.keccak1, hash, 64);
    sph_keccak512_close(&ctx.keccak1, hash);

    if (hash[0] & mask) {
        sph_blake512(&ctx.blake1, hash, 64);
        sph_blake512_close(&ctx.blake1, hash);
    }
    else {
        sph_bmw512(&ctx.bmw2, hash, 64);
        sph_bmw512_close(&ctx.bmw2, hash);
    }

    sph_luffa512(&ctx.luffa1, hash, 64);
    sph_luffa512_close(&ctx.luffa1, hash);

    sph_cubehash512(&ctx.cubehash, hash, 64);
    sph_cubehash512_close(&ctx.cubehash, hash);

    if (hash[0] & mask) {
        sph_keccak512(&ctx.keccak2, hash, 64);
        sph_keccak512_close(&ctx.keccak2, hash);
    }
    else {
        sph_jh512(&ctx.jh2, hash, 64);
        sph_jh512_close(&ctx.jh2, hash);
    }

    sph_shavite512(&ctx.shavite1, hash, 64);
    sph_shavite512_close(&ctx.shavite1, hash);

    sph_simd512(&ctx.simd1, hash, 64);
    sph_simd512_close(&ctx.simd1, hash);

    if (hash[0] & mask) {
        sph_whirlpool(&ctx.whirlpool2, hash, 64);
        sph_whirlpool_close(&ctx.whirlpool2, hash);
    }
    else {
        sph_haval256_5(&ctx.haval1, hash, 64);
        sph_haval256_5_close(&ctx.haval1, hash);
        memset(&hash[8], 0, 32);
    }

    sph_echo512(&ctx.echo1, hash, 64);
    sph_echo512_close(&ctx.echo1, hash);

    sph_blake512(&ctx.blake2, hash, 64);
    sph_blake512_close(&ctx.blake2, hash);

    if (hash[0] & mask) {
        sph_shavite512(&ctx.shavite2, hash, 64);
        sph_shavite512_close(&ctx.shavite2, hash);
    }
    else {
        sph_luffa512(&ctx.luffa2, hash, 64);
        sph_luffa512_close(&ctx.luffa2, hash);
    }

    sph_hamsi512(&ctx.hamsi, hash, 64);
    sph_hamsi512_close(&ctx.hamsi, hash);

    sph_fugue512(&ctx.fugue1, hash, 64);
    sph_fugue512_close(&ctx.fugue1, hash);

    if (hash[0] & mask) {
        sph_echo512(&ctx.echo2, hash, 64);
        sph_echo512_close(&ctx.echo2, hash);
    }
    else {
        sph_simd512(&ctx.simd2, hash, 64);
        sph_simd512_close(&ctx.simd2, hash);
    }

    sph_shabal512(&ctx.shabal, hash, 64);
    sph_shabal512_close(&ctx.shabal, hash);

    sph_whirlpool(&ctx.whirlpool3, hash, 64);
    sph_whirlpool_close(&ctx.whirlpool3, hash);

    if (hash[0] & mask) {
        sph_fugue512(&ctx.fugue2, hash, 64);
        sph_fugue512_close(&ctx.fugue2, hash);
    }
    else {
        sph_sha512(&ctx.sha1, hash, 64);
        sph_sha512_close(&ctx.sha1, hash);
    }

    sph_groestl512(&ctx.groestl2, hash, 64);
    sph_groestl512_close(&ctx.groestl2, hash);

    sph_sha512(&ctx.sha2, hash, 64);
    sph_sha512_close(&ctx.sha2, hash);

    if (hash[0] & mask) {
        sph_haval256_5(&ctx.haval2, hash, 64);
        sph_haval256_5_close(&ctx.haval2, hash);
        memset(&hash[8], 0, 32);
    }
    else {
        sph_whirlpool(&ctx.whirlpool4, hash, 64);
        sph_whirlpool_close(&ctx.whirlpool4, hash);
    }

    sph_bmw512(&ctx.bmw3, hash, 64);
    sph_bmw512_close(&ctx.bmw3, hash);

    memcpy(output, hash, 32);
}

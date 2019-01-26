#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#include "sha3/extra.h"
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
#include "sha3/sph_tiger.h"
#include "Lyra2.h"
#include "sha3/gost_streebog.h"
#include "sha3/SWIFFTX.h"

// barrystyle 05112018

void x22i_hash(const char* input, char* output, size_t input_len)
{
        unsigned char hash[64 * 4] = {0}, hash2[64] = {0};

        sph_blake512_context ctx_blake;
        sph_bmw512_context ctx_bmw;
        sph_groestl512_context ctx_groestl;
        sph_jh512_context ctx_jh;
        sph_keccak512_context ctx_keccak;
        sph_skein512_context ctx_skein;
        sph_luffa512_context ctx_luffa;
        sph_cubehash512_context ctx_cubehash;
        sph_shavite512_context ctx_shavite;
        sph_simd512_context ctx_simd;
        sph_echo512_context ctx_echo;
        sph_hamsi512_context ctx_hamsi;
        sph_fugue512_context ctx_fugue;
        sph_shabal512_context ctx_shabal;
        sph_whirlpool_context ctx_whirlpool;
        sph_sha512_context ctx_sha512;
        sph_haval256_5_context ctx_haval;
        sph_tiger_context ctx_tiger;
        sph_gost512_context ctx_gost;
        sph_sha256_context ctx_sha;

        sph_blake512_init(&ctx_blake);
        sph_blake512(&ctx_blake, input, input_len);
        sph_blake512_close(&ctx_blake, hash);

        sph_bmw512_init(&ctx_bmw);
        sph_bmw512(&ctx_bmw, (const void*) hash, 64);
        sph_bmw512_close(&ctx_bmw, hash);

        sph_groestl512_init(&ctx_groestl);
        sph_groestl512(&ctx_groestl, (const void*) hash, 64);
        sph_groestl512_close(&ctx_groestl, hash);

        sph_skein512_init(&ctx_skein);
        sph_skein512(&ctx_skein, (const void*) hash, 64);
        sph_skein512_close(&ctx_skein, hash);

        sph_jh512_init(&ctx_jh);
        sph_jh512(&ctx_jh, (const void*) hash, 64);
        sph_jh512_close(&ctx_jh, hash);

        sph_keccak512_init(&ctx_keccak);
        sph_keccak512(&ctx_keccak, (const void*) hash, 64);
        sph_keccak512_close(&ctx_keccak, hash);

        sph_luffa512_init(&ctx_luffa);
        sph_luffa512(&ctx_luffa, (const void*) hash, 64);
        sph_luffa512_close (&ctx_luffa, hash);

        sph_cubehash512_init(&ctx_cubehash);
        sph_cubehash512(&ctx_cubehash, (const void*) hash, 64);
        sph_cubehash512_close(&ctx_cubehash, hash);

        sph_shavite512_init(&ctx_shavite);
        sph_shavite512(&ctx_shavite, (const void*) hash, 64);
        sph_shavite512_close(&ctx_shavite, hash);

        sph_simd512_init(&ctx_simd);
        sph_simd512(&ctx_simd, (const void*) hash, 64);
        sph_simd512_close(&ctx_simd, hash);

        sph_echo512_init(&ctx_echo);
        sph_echo512(&ctx_echo, (const void*) hash, 64);
        sph_echo512_close(&ctx_echo, hash);

        sph_hamsi512_init(&ctx_hamsi);
        sph_hamsi512(&ctx_hamsi, (const void*) hash, 64);
        sph_hamsi512_close(&ctx_hamsi, hash);

        sph_fugue512_init(&ctx_fugue);
        sph_fugue512(&ctx_fugue, (const void*) hash, 64);
        sph_fugue512_close(&ctx_fugue, hash);

        sph_shabal512_init(&ctx_shabal);
        sph_shabal512(&ctx_shabal, (const void*) hash, 64);
        sph_shabal512_close(&ctx_shabal, &hash[64]);

        sph_whirlpool_init(&ctx_whirlpool);
        sph_whirlpool (&ctx_whirlpool, (const void*) &hash[64], 64);
        sph_whirlpool_close(&ctx_whirlpool, &hash[128]);

        sph_sha512_init(&ctx_sha512);
        sph_sha512(&ctx_sha512,(const void*) &hash[128], 64);
        sph_sha512_close(&ctx_sha512,(void*) &hash[192]);

        InitializeSWIFFTX();
        ComputeSingleSWIFFTX((unsigned char*)hash, (unsigned char*)hash2, false);

        memset(hash, 0, 64);
        sph_haval256_5_init(&ctx_haval);
        sph_haval256_5(&ctx_haval,(const void*) hash2, 64);
        sph_haval256_5_close(&ctx_haval,hash);

        memset(hash2, 0, 64);
        sph_tiger_init(&ctx_tiger);
        sph_tiger (&ctx_tiger, (const void*) hash, 64);
        sph_tiger_close(&ctx_tiger, (void*) hash2);

        memset(hash, 0, 64);
        LYRA2((void*) hash, 32, (const void*) hash2, 32, (const void*) hash2, 32, 1, 4, 4);

        sph_gost512_init(&ctx_gost);
        sph_gost512 (&ctx_gost, (const void*) hash, 64);
        sph_gost512_close(&ctx_gost, (void*) hash);

        sph_sha256_init(&ctx_sha);
        sph_sha256 (&ctx_sha, (const void*) hash, 64);
        sph_sha256_close(&ctx_sha, (void*) hash);

        memcpy(output, hash, 32);
}

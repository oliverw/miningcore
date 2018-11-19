#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include <stdio.h>

#include "equi/uint256.h"

extern "C"
{
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
	#include "lyra2.h"
	#include "gost_streebog.h"
}

#include "SWIFFTX/SWIFFTX.h"

#ifdef GLOBALDEFINED
#define GLOBAL
#else
#define GLOBAL extern
#endif

void x22i_hash(const char* input, char* output, uint32_t len)
{
	sph_blake512_context      ctx_blake;
	sph_bmw512_context        ctx_bmw;
	sph_groestl512_context    ctx_groestl;
	sph_jh512_context         ctx_jh;
	sph_keccak512_context     ctx_keccak;
	sph_skein512_context      ctx_skein;
	sph_luffa512_context      ctx_luffa;
	sph_cubehash512_context   ctx_cubehash;
	sph_shavite512_context    ctx_shavite;
	sph_simd512_context       ctx_simd;
	sph_echo512_context       ctx_echo;
	sph_hamsi512_context      ctx_hamsi;
	sph_fugue512_context      ctx_fugue;
	sph_shabal512_context     ctx_shabal;
	sph_whirlpool_context     ctx_whirlpool;
	sph_sha512_context        ctx_sha2;
	sph_haval256_5_context    ctx_haval;
	sph_tiger_context         ctx_tiger;
	sph_gost512_context       ctx_gost;
	sph_sha256_context        ctx_sha;
	static unsigned char pblank[1];

#ifndef QT_NO_DEBUG
	//std::string strhash;
	//strhash = "";
#endif

	uint512 hash[22];

	sph_blake512_init(&ctx_blake);
	sph_blake512(&ctx_blake, input, len);
	sph_blake512_close(&ctx_blake, static_cast<void*>(&hash[0]));

	sph_bmw512_init(&ctx_bmw);
	sph_bmw512(&ctx_bmw, static_cast<const void*>(&hash[0]), 64);
	sph_bmw512_close(&ctx_bmw, static_cast<void*>(&hash[1]));

	sph_groestl512_init(&ctx_groestl);
	sph_groestl512(&ctx_groestl, static_cast<const void*>(&hash[1]), 64);
	sph_groestl512_close(&ctx_groestl, static_cast<void*>(&hash[2]));

	sph_skein512_init(&ctx_skein);
	sph_skein512(&ctx_skein, static_cast<const void*>(&hash[2]), 64);
	sph_skein512_close(&ctx_skein, static_cast<void*>(&hash[3]));

	sph_jh512_init(&ctx_jh);
	sph_jh512(&ctx_jh, static_cast<const void*>(&hash[3]), 64);
	sph_jh512_close(&ctx_jh, static_cast<void*>(&hash[4]));

	sph_keccak512_init(&ctx_keccak);
	sph_keccak512(&ctx_keccak, static_cast<const void*>(&hash[4]), 64);
	sph_keccak512_close(&ctx_keccak, static_cast<void*>(&hash[5]));

	sph_luffa512_init(&ctx_luffa);
	sph_luffa512(&ctx_luffa, static_cast<void*>(&hash[5]), 64);
	sph_luffa512_close(&ctx_luffa, static_cast<void*>(&hash[6]));

	sph_cubehash512_init(&ctx_cubehash);
	sph_cubehash512(&ctx_cubehash, static_cast<const void*>(&hash[6]), 64);
	sph_cubehash512_close(&ctx_cubehash, static_cast<void*>(&hash[7]));

	sph_shavite512_init(&ctx_shavite);
	sph_shavite512(&ctx_shavite, static_cast<const void*>(&hash[7]), 64);
	sph_shavite512_close(&ctx_shavite, static_cast<void*>(&hash[8]));

	sph_simd512_init(&ctx_simd);
	sph_simd512(&ctx_simd, static_cast<const void*>(&hash[8]), 64);
	sph_simd512_close(&ctx_simd, static_cast<void*>(&hash[9]));

	sph_echo512_init(&ctx_echo);
	sph_echo512(&ctx_echo, static_cast<const void*>(&hash[9]), 64);
	sph_echo512_close(&ctx_echo, static_cast<void*>(&hash[10]));

	sph_hamsi512_init(&ctx_hamsi);
	sph_hamsi512(&ctx_hamsi, static_cast<const void*>(&hash[10]), 64);
	sph_hamsi512_close(&ctx_hamsi, static_cast<void*>(&hash[11]));

	sph_fugue512_init(&ctx_fugue);
	sph_fugue512(&ctx_fugue, static_cast<const void*>(&hash[11]), 64);
	sph_fugue512_close(&ctx_fugue, static_cast<void*>(&hash[12]));

	sph_shabal512_init(&ctx_shabal);
	sph_shabal512(&ctx_shabal, static_cast<const void*>(&hash[12]), 64);
	sph_shabal512_close(&ctx_shabal, static_cast<void*>(&hash[13]));

	sph_whirlpool_init(&ctx_whirlpool);
	sph_whirlpool(&ctx_whirlpool, static_cast<const void*>(&hash[13]), 64);
	sph_whirlpool_close(&ctx_whirlpool, static_cast<void*>(&hash[14]));

	sph_sha512_init(&ctx_sha2);
	sph_sha512(&ctx_sha2, static_cast<const void*>(&hash[14]), 64);
	sph_sha512_close(&ctx_sha2, static_cast<void*>(&hash[15]));

	//unsigned char temp[SWIFFTX_INPUT_BLOCK_SIZE] = {0};
	//memcpy(temp, &hash[19], 64);
	InitializeSWIFFTX();
	ComputeSingleSWIFFTX((unsigned char*)&hash[12], (unsigned char*)&hash[16], false);

	sph_haval256_5_init(&ctx_haval);
	sph_haval256_5(&ctx_haval, static_cast<const void*>(&hash[16]), 64);
	sph_haval256_5_close(&ctx_haval, static_cast<void*>(&hash[17]));

	// fill the high 256 with zeros

	sph_tiger_init(&ctx_tiger);
	sph_tiger(&ctx_tiger, static_cast<const void*>(&hash[17]), 64);
	sph_tiger_close(&ctx_tiger, static_cast<void*>(&hash[18]));

	// fill the high 256 with zeros

	LYRA2(static_cast<void*>(&hash[19]), 32, static_cast<const void*>(&hash[18]), 32, static_cast<const void*>(&hash[18]), 32, 1, 4, 4);

	// fill over 192 with zeros

	sph_gost512_init(&ctx_gost);
	sph_gost512(&ctx_gost, static_cast<const void*>(&hash[19]), 64);
	sph_gost512_close(&ctx_gost, static_cast<void*>(&hash[20]));

	sph_sha256_init(&ctx_sha);
	sph_sha256(&ctx_sha, static_cast<const void*>(&hash[20]), 64);
	sph_sha256_close(&ctx_sha, static_cast<void*>(&hash[21]));

	memcpy(output, hash[21].trim256().begin(), 32);
}

#include "bcrypt.h"
#include "keccak.h"
#include "quark.h"
#include "scryptjane.h"
#include "scryptn.h"
#include "neoscrypt.h"
#include "skein.h"
#include "x11.h"
#include "groestl.h"
#include "blake.h"
#include "fugue.h"
#include "qubit.h"
#include "s3.h"
#include "hefty1.h"
#include "shavite3.h"
#include "cryptonight.h"
#include "x13.h"
#include "x14.h"
#include "nist5.h"
#include "x15.h"
#include "fresh.h"
#include "dcrypt.h"
#include "jh.h"
#include "c11.h"

#ifdef _WIN32
#define MODULE_API __declspec(dllexport)
#else
#define MODULE_API
#endif

extern "C" MODULE_API void scrypt_export(const char* input, char* output, uint32_t N, uint32_t R, uint32_t input_len)
{
	scrypt_N_R_1_256(input, output, N, R, input_len);
}

extern "C" MODULE_API void quark_export(const char* input, char* output, uint32_t input_len)
{
	quark_hash(input, output, input_len);
}

extern "C" MODULE_API void x11_export(const char* input, char* output, uint32_t input_len)
{
	x11_hash(input, output, input_len);
}

extern "C" MODULE_API void x15_export(const char* input, char* output, uint32_t input_len)
{
	x15_hash(input, output, input_len);
}

extern "C" MODULE_API void neoscrypt_export(const char* input, char* output, uint32_t profile)
{
	neoscrypt(input, output, profile);
}

extern "C" MODULE_API void scryptn_export(const char* input, char* output, uint32_t nFactor, uint32_t input_len)
{
	unsigned int N = 1 << nFactor;

	scrypt_N_R_1_256(input, output, N, 1, input_len); //hardcode for now to R=1 for now
}

extern "C" MODULE_API void kezzak_export(const char* input, char* output, uint32_t input_len)
{
	keccak_hash(input, output, input_len);
}

extern "C" MODULE_API void bcrypt_export(const char* input, char* output, uint32_t input_len)
{
	bcrypt_hash(input, output);
}

extern "C" MODULE_API void skein_export(const char* input, char* output, uint32_t input_len)
{
	skein_hash(input, output, input_len);
}

extern "C" MODULE_API void groestl_export(const char* input, char* output, uint32_t input_len)
{
	groestl_hash(input, output, input_len);
}

extern "C" MODULE_API void groestl_myriad_export(const char* input, char* output, uint32_t input_len)
{
	groestlmyriad_hash(input, output, input_len);
}

extern "C" MODULE_API void blake_export(const char* input, char* output, uint32_t input_len)
{
	blake_hash(input, output, input_len);
}

extern "C" MODULE_API void dcrypt_export(const char* input, char* output, uint32_t input_len)
{
	dcrypt_hash(input, output, input_len);
}

extern "C" MODULE_API void fugue_export(const char* input, char* output, uint32_t input_len)
{
	fugue_hash(input, output, input_len);
}

extern "C" MODULE_API void qubit_export(const char* input, char* output, uint32_t input_len)
{
	qubit_hash(input, output, input_len);
}

extern "C" MODULE_API void s3_export(const char* input, char* output, uint32_t input_len)
{
	s3_hash(input, output, input_len);
}

extern "C" MODULE_API void hefty1_export(const char* input, char* output, uint32_t input_len)
{
	hefty1_hash(input, output, input_len);
}

extern "C" MODULE_API void shavite3_export(const char* input, char* output, uint32_t input_len)
{
	shavite3_hash(input, output, input_len);
}

extern "C" MODULE_API void cryptonight_export(const char* input, char* output, uint32_t input_len, uint32_t fast)
{
	if (fast)
		cryptonight_fast_hash(input, output, input_len);
	else
		cryptonight_hash(input, output, input_len);
}

extern "C" MODULE_API void nist5_export(const char* input, char* output, uint32_t input_len)
{
	nist5_hash(input, output, input_len);
}

extern "C" MODULE_API void fresh_export(const char* input, char* output, uint32_t input_len)
{
	fresh_hash(input, output, input_len);
}

extern "C" MODULE_API void jh_export(const char* input, char* output, uint32_t input_len)
{
	jh_hash(input, output, input_len);
}

extern "C" MODULE_API void c11_export(const char* input, char* output)
{
	c11_hash(input, output);
}

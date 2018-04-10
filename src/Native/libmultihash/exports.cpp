/*
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)
Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:
The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

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
#include "blake2s.h"
#include "fugue.h"
#include "qubit.h"
#include "s3.h"
#include "hefty1.h"
#include "shavite3.h"
#include "x13.h"
#include "x14.h"
#include "nist5.h"
#include "x15.h"
#include "x17.h"
#include "fresh.h"
#include "dcrypt.h"
#include "jh.h"
#include "c11.h"
#include "Lyra2RE.h"
#include "Lyra2.h"
#include "x16r.h"
#include "x16s.h"
#include "equi/equi.h"
#include "libethash/sha3.h"
#include "libethash/internal.h"
#include "libethash/ethash.h"

extern "C" bool ethash_get_default_dirname(char* strbuf, size_t buffsize);

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

extern "C" MODULE_API void x17_export(const char* input, char* output, uint32_t input_len)
{
    x17_hash(input, output, input_len);
}

extern "C" MODULE_API void x15_export(const char* input, char* output, uint32_t input_len)
{
	x15_hash(input, output, input_len);
}

extern "C" MODULE_API void neoscrypt_export(const unsigned char* input, unsigned char* output, uint32_t profile)
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

extern "C" MODULE_API void blake2s_export(const char* input, char* output, uint32_t input_len)
{
    blake2s_hash(input, output, input_len);
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

extern "C" MODULE_API void lyra2re_export(const char* input, char* output)
{
	lyra2re_hash(input, output);
}

extern "C" MODULE_API void lyra2rev2_export(const char* input, char* output)
{
	lyra2re2_hash(input, output);
}

extern "C" MODULE_API void x16r_export(const char* input, char* output, uint32_t input_len)
{
    x16r_hash(input, output, input_len);
}

extern "C" MODULE_API void x16s_export(const char* input, char* output, uint32_t input_len)
{
    x16s_hash(input, output, input_len);
}

extern "C" MODULE_API bool equihash_verify_export(const char* header, const char* solution)
{
	return verifyEH(header, solution);
}

extern "C" MODULE_API void sha3_256_export(const char* input, char* output, uint32_t input_len)
{
	SHA3_256((ethash_h256 const*) output, (uint8_t const*) input, input_len);
}

extern "C" MODULE_API void sha3_512_export(const char* input, char* output, uint32_t input_len)
{
	SHA3_512((uint8_t*) output, (uint8_t const*)input, input_len);
}

extern "C" MODULE_API uint64_t ethash_get_datasize_export(uint64_t const block_number)
{
	return ethash_get_datasize(block_number);
}

extern "C" MODULE_API uint64_t ethash_get_cachesize_export(uint64_t const block_number)
{
	return ethash_get_cachesize(block_number);
}

extern "C" MODULE_API ethash_light_t ethash_light_new_export(uint64_t block_number)
{
	return ethash_light_new(block_number);
}

extern "C" MODULE_API void ethash_light_delete_export(ethash_light_t light)
{
	ethash_light_delete(light);
}

extern "C" MODULE_API void ethash_light_compute_export(
	ethash_light_t light,
	ethash_h256_t const *header_hash,
	uint64_t nonce,
	ethash_return_value_t *result)
{
	*result = ethash_light_compute(light, *header_hash, nonce);
}

extern "C" MODULE_API ethash_full_t ethash_full_new_export(const char *dirname, ethash_light_t light, ethash_callback_t callback)
{
	uint64_t full_size = ethash_get_datasize(light->block_number);
	ethash_h256_t seedhash = ethash_get_seedhash(light->block_number);
	return ethash_full_new_internal(dirname, seedhash, full_size, light, callback);
}

extern "C" MODULE_API void ethash_full_delete_export(ethash_full_t full)
{
	ethash_full_delete(full);
}

extern "C" MODULE_API void ethash_full_compute_export(
	ethash_full_t full,
	ethash_h256_t const *header_hash,
	uint64_t nonce,
	ethash_return_value_t *result)
{
	*result = ethash_full_compute(full, *header_hash, nonce);
}

extern "C" MODULE_API void const* ethash_full_dag_export(ethash_full_t full)
{
	return ethash_full_dag(full);
}

extern "C" MODULE_API uint64_t ethash_full_dag_size_export(ethash_full_t full)
{
	return ethash_full_dag_size(full);
}

extern "C" MODULE_API ethash_h256_t ethash_get_seedhash_export(uint64_t block_number)
{
	return ethash_get_seedhash(block_number);
}

extern "C" MODULE_API bool ethash_get_default_dirname_export(char *buf, size_t buf_size)
{
	return ethash_get_default_dirname(buf, buf_size);
}

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

#include "sha3.h"
#include "internal.h"
#include "ethash.h"

extern "C" bool ethash_get_default_dirname(char* strbuf, size_t buffsize);

#ifdef _WIN32
#define MODULE_API __declspec(dllexport)
#else
#define MODULE_API
#endif

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

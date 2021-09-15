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

#include <stdint.h>
#include <string>
#include <algorithm>
#include "cryptonote_basic/cryptonote_basic.h"
#include "cryptonote_basic/cryptonote_format_utils.h"
#include "cryptonote_protocol/blobdatatype.h"
#include "crypto/crypto.h"
#include "common/base58.h"
#include "crypto/hash-ops.h"
#include "serialization/binary_utils.h"

using namespace cryptonote;

#ifdef _WIN32
#define MODULE_API __declspec(dllexport)
#else
#define MODULE_API
#endif

// adapted from https://github.com/Snipa22/node-cryptonote-util/blob/master/src/main.cc

static blobdata uint64be_to_blob(uint64_t num) {
	blobdata res = "        ";
	res[0] = num >> 56 & 0xff;
	res[1] = num >> 48 & 0xff;
	res[2] = num >> 40 & 0xff;
	res[3] = num >> 32 & 0xff;
	res[4] = num >> 24 & 0xff;
	res[5] = num >> 16 & 0xff;
	res[6] = num >> 8 & 0xff;
	res[7] = num & 0xff;
	return res;
}

extern "C" MODULE_API bool convert_blob_export(const char* input, unsigned int inputSize, unsigned char *output, unsigned int *outputSize)
{
	unsigned int originalOutputSize = *outputSize;

	blobdata input_blob = std::string(input, inputSize);
	blobdata result = "";

	block block = AUTO_VAL_INIT(block);
	if (!parse_and_validate_block_from_blob(input_blob, block))
	{
		*outputSize = 0;
		return false;
	}

	// now hash it
	result = get_block_hashing_blob(block);
	*outputSize = (int) result.length();

	// output buffer big enough?
	if (result.length() > originalOutputSize)
		return false;

	// success
	memcpy(output, result.data(), result.length());
	return true;
}

extern "C" MODULE_API uint64_t decode_address_export(const char* input, unsigned int inputSize)
{
	blobdata input_blob = std::string(input, inputSize);
	blobdata data = "";

	uint64_t prefix;
	bool decodeResult = tools::base58::decode_addr(input_blob, prefix, data);

	if (!decodeResult || data.length() == 0)
		return 0L;	// error

	account_public_address adr;
	if (!::serialization::parse_binary(data, adr))
		return 0L;

	if (!crypto::check_key(adr.m_spend_public_key) || !crypto::check_key(adr.m_view_public_key))
		return 0L;

	return prefix;
}

extern "C" MODULE_API uint64_t decode_integrated_address_export(const char* input, unsigned int inputSize)
{
    blobdata input_blob = std::string(input, inputSize);
    blobdata data = "";

    uint64_t prefix;
    bool decodeResult = tools::base58::decode_addr(input_blob, prefix, data);

    if (!decodeResult || data.length() == 0)
        return 0L;	// error

    integrated_address iadr;
    if (!::serialization::parse_binary(data, iadr) || !crypto::check_key(iadr.adr.m_spend_public_key) || !crypto::check_key(iadr.adr.m_view_public_key))
        return 0L;	// error

    return prefix;
}

extern "C" MODULE_API void cn_fast_hash_export(const char* input, unsigned char *output, uint32_t inputSize)
{
	cn_fast_hash_old_sig((const void *)input, (const size_t) inputSize, (char *) output);
}

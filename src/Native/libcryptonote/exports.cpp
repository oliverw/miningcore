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
	*outputSize = result.length();

	// output buffer big enough?
	if (result.length() > originalOutputSize)
		return false;

	// success
	memcpy(output, result.data(), result.length());
	return true;
}

extern "C" MODULE_API uint32_t decode_address_export(const char* input, unsigned int inputSize)
{
	blobdata input_blob = std::string(input, inputSize);
	blobdata data = "";

	uint64_t prefix;
	bool decodeResult = tools::base58::decode_addr(input_blob, prefix, data);

	if (!decodeResult || data.length() == 0)
		return 0;	// error

	account_public_address adr;
	if (!::serialization::parse_binary(data, adr))
		return 0;

	if (!crypto::check_key(adr.m_spend_public_key) || !crypto::check_key(adr.m_view_public_key))
		return 0;

	return static_cast<uint32_t>(prefix);
}

extern "C" MODULE_API void cn_slow_hash_export(const char* input, unsigned char *output, uint32_t inputSize)
{
	cn_slow_hash((const void *) input, (const size_t) inputSize, (char *) output);
}

extern "C" MODULE_API void cn_fast_hash_export(const char* input, unsigned char *output, uint32_t inputSize)
{
	cn_fast_hash((const void *)input, (const size_t) inputSize, (char *) output);
}

#include <stdint.h>
#include <string>
#include <algorithm>
#include "cryptonote_basic/cryptonote_basic.h"
#include "cryptonote_basic/cryptonote_format_utils.h"
#include "cryptonote_protocol/blobdatatype.h"
#include "crypto/crypto.h"
#include "crypto/hash.h"
#include "common/base58.h"

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
	if (result.length() > *outputSize)
		return false;

	// success
	memcpy(output, result.data(), result.length());
	return true;
}

extern "C" MODULE_API bool decode_address_export(const char* input, unsigned int inputSize, unsigned char *output, unsigned int *outputSize)
{
	blobdata input_blob = std::string(input, inputSize);
	blobdata result = "";

	uint64_t prefix;
	tools::base58::decode_addr(input_blob, prefix, result);

	if (result.length() == 0)
	{
		*outputSize = 0;
		return false;
	}

	result = uint64be_to_blob(prefix) + result;
	*outputSize = result.length();

	// output buffer big enough?
	if (result.length() > *outputSize)
		return false;

	// success
	memcpy(output, result.data(), result.length());
	return true;
}

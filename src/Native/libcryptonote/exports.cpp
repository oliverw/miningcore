#include <cmath>
#include <stdint.h>
#include <string>
#include <algorithm>
#include "cryptonote_core/cryptonote_basic.h"
#include "cryptonote_core/cryptonote_format_utils.h"
#include "common/base58.h"
#include "serialization/binary_utils.h"

#include "crypto/hash-ops.h"

using namespace cryptonote;

#ifdef _WIN32
#define MODULE_API __declspec(dllexport)
#else
#define MODULE_API
#endif

extern "C" void cn_fast_hash(const void* data, size_t length, char* hash);

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
	get_block_hashing_blob(block, result);
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
    cn_fast_hash((const void *)input, (const size_t) inputSize, (char *) output);
}

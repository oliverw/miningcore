#include <stdint.h>
#include <string>
#include <algorithm>
#include "cryptonote_basic/cryptonote_basic.h"
#include "cryptonote_basic/cryptonote_format_utils.h"
#include "cryptonote_protocol/blobdatatype.h"
#include "crypto/crypto.h"
#include "crypto/hash.h"

using namespace cryptonote;

#ifdef _WIN32
#define MODULE_API __declspec(dllexport)
#else
#define MODULE_API
#endif

extern "C" MODULE_API bool convert_blob_export(const char* input, unsigned int inputSize, unsigned char *output, unsigned int *outputSize)
{
	blobdata input_blob = std::string(input, inputSize);

	block block = AUTO_VAL_INIT(block);
	if (!parse_and_validate_block_from_blob(input_blob, block))
	{
		*outputSize = 0;
		return false;
	}
	
	// now hash it
	blobdata result = get_block_hashing_blob(block);

	// output buffer big enough?
	if (result.length() > *outputSize)
	{
		// it's not, communicate required size
		*outputSize = result.length();
		return false;
	}

	// success
	memcpy(output, result.data(), result.length());
	return true;
}

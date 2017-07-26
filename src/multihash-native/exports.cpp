#include "bcrypt.h"
#include "keccak.h"
#include "quark.h"
#include "scryptjane.h"
#include "scryptn.h"
#include "yescrypt/yescrypt.h"
#include "yescrypt/sha256_Y.h"
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

extern "C" MODULE_API void scrypt(const char* input, char* output, uint32_t N, uint32_t R, uint32_t len)
{
	scrypt_N_R_1_256(input, output, N, R, len);
}

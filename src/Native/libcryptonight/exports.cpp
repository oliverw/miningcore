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

#if defined(__ARM_ARCH)
#include "xmrig/crypto/CryptoNight_arm.h"
#else
#include "xmrig/extra.h"
#include "xmrig/crypto/CryptoNight_x86.h"
#endif

#include "xmrig/Mem.h"

#if (defined(__AES__) && (__AES__ == 1)) || (defined(__ARM_FEATURE_CRYPTO) && (__ARM_FEATURE_CRYPTO == 1))
#define SOFT_AES false
#else
#warning Using software AES
#define SOFT_AES true
#endif

#ifdef _WIN32
#define MODULE_API __declspec(dllexport)
#else
#define MODULE_API
#endif

extern "C" MODULE_API cryptonight_ctx *cryptonight_alloc_context_export() {
    cryptonight_ctx *ctx = NULL;
	Mem::create(&ctx, xmrig::CRYPTONIGHT, 1);

    return ctx;
}

extern "C" MODULE_API cryptonight_ctx *cryptonight_alloc_lite_context_export() {
	cryptonight_ctx *ctx = NULL;
	Mem::create(&ctx, xmrig::CRYPTONIGHT_LITE, 1);

    return ctx;
}

extern "C" MODULE_API cryptonight_ctx *cryptonight_alloc_heavy_context_export() {
	cryptonight_ctx *ctx = NULL;
	Mem::create(&ctx, xmrig::CRYPTONIGHT_HEAVY, 1);

    return ctx;
}

extern "C" MODULE_API cryptonight_ctx *cryptonight_alloc_pico_context_export() {
	cryptonight_ctx *ctx = NULL;
	Mem::create(&ctx, xmrig::CRYPTONIGHT_PICO, 1);

	return ctx;
}

extern "C" MODULE_API void cryptonight_free_ctx_export(cryptonight_ctx *ctx) {
	MemInfo mi;
	Mem::release(&ctx, 1, mi);
}

extern "C" MODULE_API void cryptonight_export(cryptonight_ctx* ctx, const char* input, unsigned char *output, size_t inputSize, uint32_t variant, uint64_t height)
{
    switch (variant) {
	case 0:  
		cryptonight_single_hash<xmrig::CRYPTONIGHT, SOFT_AES, xmrig::VARIANT_0>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
		break;
	case 1:  
		cryptonight_single_hash<xmrig::CRYPTONIGHT, SOFT_AES, xmrig::VARIANT_1>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
		break;
	case 3:  
		cryptonight_single_hash<xmrig::CRYPTONIGHT, SOFT_AES, xmrig::VARIANT_XTL>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
		break;
	case 4:  
		cryptonight_single_hash<xmrig::CRYPTONIGHT, SOFT_AES, xmrig::VARIANT_MSR>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
		break;
	case 6:  
		cryptonight_single_hash<xmrig::CRYPTONIGHT, SOFT_AES, xmrig::VARIANT_XAO>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
		break;
	case 7:  
		cryptonight_single_hash<xmrig::CRYPTONIGHT, SOFT_AES, xmrig::VARIANT_RTO>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
		break;

	case 8:
#if !SOFT_AES && defined(CPU_INTEL)
		//#warning Using IvyBridge assembler implementation
			cryptonight_single_hash_asm<xmrig::CRYPTONIGHT, xmrig::VARIANT_2, xmrig::ASM_INTEL>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
#elif !SOFT_AES && defined(CPU_AMD)
		#warning Using Ryzen assembler implementation
			cryptonight_single_hash_asm<xmrig::CRYPTONIGHT, xmrig::VARIANT_2, xmrig::ASM_RYZEN>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
#elif !SOFT_AES && defined(CPU_AMD_OLD)
		#warning Using Bulldozer assembler implementation
			cryptonight_single_hash_asm<xmrig::CRYPTONIGHT, xmrig::VARIANT_2, xmrig::ASM_BULLDOZER>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
#else
		cryptonight_single_hash    <xmrig::CRYPTONIGHT, SOFT_AES, xmrig::VARIANT_2>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
#endif
		break;

	case 9:
#if !SOFT_AES && defined(CPU_INTEL)
		//#warning Using IvyBridge assembler implementation
			cryptonight_single_hash_asm<xmrig::CRYPTONIGHT, xmrig::VARIANT_HALF, xmrig::ASM_INTEL>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
#elif !SOFT_AES && defined(CPU_AMD)
		#warning Using Ryzen assembler implementation
			cryptonight_single_hash_asm<xmrig::CRYPTONIGHT, xmrig::VARIANT_HALF, xmrig::ASM_RYZEN>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
#elif !SOFT_AES && defined(CPU_AMD_OLD)
		#warning Using Bulldozer assembler implementation
			cryptonight_single_hash_asm<xmrig::CRYPTONIGHT, xmrig::VARIANT_HALF, xmrig::ASM_BULLDOZER>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
#else
		cryptonight_single_hash    <xmrig::CRYPTONIGHT, SOFT_AES, xmrig::VARIANT_HALF>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
#endif
		break;
	case 11: 
		cryptonight_single_hash_gpu<xmrig::CRYPTONIGHT, SOFT_AES, xmrig::VARIANT_GPU>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
		break;
	case 12:
		//if (!height_set) return THROW_ERROR_EXCEPTION("CryptonightR requires block template height as Argument 3");

#if !SOFT_AES && (defined(CPU_INTEL) || defined(CPU_AMD))
		cryptonight_single_hash_asm<xmrig::CRYPTONIGHT, xmrig::VARIANT_WOW, xmrig::ASM_AUTO>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
#else
		cryptonight_single_hash    <xmrig::CRYPTONIGHT, SOFT_AES, xmrig::VARIANT_WOW>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
#endif
		break;
	case 13:
		//if (!height_set) return THROW_ERROR_EXCEPTION("Cryptonight4 requires block template height as Argument 3");

#if !SOFT_AES && defined(CPU_INTEL)
		//#warning Using IvyBridge assembler implementation
			cryptonight_single_hash_asm<xmrig::CRYPTONIGHT, xmrig::VARIANT_4, xmrig::ASM_INTEL>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
#elif !SOFT_AES && defined(CPU_AMD)
		#warning Using Ryzen assembler implementation
			cryptonight_single_hash_asm<xmrig::CRYPTONIGHT, xmrig::VARIANT_4, xmrig::ASM_RYZEN>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
#elif !SOFT_AES && defined(CPU_AMD_OLD)
		#warning Using Bulldozer assembler implementation
			cryptonight_single_hash_asm<xmrig::CRYPTONIGHT, xmrig::VARIANT_4, xmrig::ASM_BULLDOZER>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
#else
		cryptonight_single_hash    <xmrig::CRYPTONIGHT, SOFT_AES, xmrig::VARIANT_4>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
#endif
		break;

	default: 
		cryptonight_single_hash<xmrig::CRYPTONIGHT, SOFT_AES, xmrig::VARIANT_1>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
	}
}

extern "C" MODULE_API void cryptonight_light_export(cryptonight_ctx* ctx, const char* input, unsigned char *output, size_t inputSize, uint32_t variant, uint64_t height)
{
    switch (variant) {
	case 0:  
		cryptonight_single_hash<xmrig::CRYPTONIGHT_LITE, SOFT_AES, xmrig::VARIANT_0>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
		break;
	case 1:  
		cryptonight_single_hash<xmrig::CRYPTONIGHT_LITE, SOFT_AES, xmrig::VARIANT_1>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
		break;
	default: 
		cryptonight_single_hash<xmrig::CRYPTONIGHT_LITE, SOFT_AES, xmrig::VARIANT_1>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
	}
}

extern "C" MODULE_API void cryptonight_heavy_export(cryptonight_ctx* ctx, const char* input, unsigned char *output, size_t inputSize, uint32_t variant, uint64_t height)
{
    switch (variant) {
	case 0:  
		cryptonight_single_hash<xmrig::CRYPTONIGHT_HEAVY, SOFT_AES, xmrig::VARIANT_0   >(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
		break;
	case 1:  
		cryptonight_single_hash<xmrig::CRYPTONIGHT_HEAVY, SOFT_AES, xmrig::VARIANT_XHV >(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
		break;
	case 2:  
		cryptonight_single_hash<xmrig::CRYPTONIGHT_HEAVY, SOFT_AES, xmrig::VARIANT_TUBE>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
		break;
	default: 
		cryptonight_single_hash<xmrig::CRYPTONIGHT_HEAVY, SOFT_AES, xmrig::VARIANT_0   >(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
	}
}

extern "C" MODULE_API void cryptonight_pico_export(cryptonight_ctx* ctx, const char* input, unsigned char *output, size_t inputSize, uint32_t variant, uint64_t height)
{
	switch (variant) {
	case 0:
#if !SOFT_AES && defined(CPU_INTEL)
		cryptonight_single_hash_asm<xmrig::CRYPTONIGHT_PICO, xmrig::VARIANT_TRTL, xmrig::ASM_INTEL>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, 0);
#elif !SOFT_AES && defined(CPU_AMD)
		cryptonight_single_hash_asm<xmrig::CRYPTONIGHT_PICO, xmrig::VARIANT_TRTL, xmrig::ASM_RYZEN>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, 0);
#elif !SOFT_AES && defined(CPU_AMD_OLD)
		cryptonight_single_hash_asm<xmrig::CRYPTONIGHT_PICO, xmrig::VARIANT_TRTL, xmrig::ASM_BULLDOZER>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, 0);
#else
		cryptonight_single_hash    <xmrig::CRYPTONIGHT_PICO, SOFT_AES, xmrig::VARIANT_TRTL>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, 0);
#endif
		break;
	default:
#if !SOFT_AES && defined(CPU_INTEL)
		cryptonight_single_hash_asm<xmrig::CRYPTONIGHT_PICO, xmrig::VARIANT_TRTL, xmrig::ASM_INTEL>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, 0);
#elif !SOFT_AES && defined(CPU_AMD)
		cryptonight_single_hash_asm<xmrig::CRYPTONIGHT_PICO, xmrig::VARIANT_TRTL, xmrig::ASM_RYZEN>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, 0);
#elif !SOFT_AES && defined(CPU_AMD_OLD)
		cryptonight_single_hash_asm<xmrig::CRYPTONIGHT_PICO, xmrig::VARIANT_TRTL, xmrig::ASM_BULLDOZER>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, 0);
#else
		cryptonight_single_hash    <xmrig::CRYPTONIGHT_PICO, SOFT_AES, xmrig::VARIANT_TRTL>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, 0);
#endif
	}
}

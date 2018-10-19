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
#include "crypto/Asm.h"
#include "crypto/CryptoNight_x86.h"
#include "common/crypto/keccak.h"

#ifdef _WIN32
#define MODULE_API __declspec(dllexport)
#else
#define MODULE_API
#endif

#if (defined(__AES__) && (__AES__ == 1)) || (defined(__ARM_FEATURE_CRYPTO) && (__ARM_FEATURE_CRYPTO == 1))
#define SOFT_AES false
#else
//#warning Using software AES
#define SOFT_AES true
#endif


extern "C" MODULE_API int cryptonight_get_context_size_export() {
    return sizeof(cryptonight_ctx) + xmrig::CRYPTONIGHT_MEMORY;
}

extern "C" MODULE_API cryptonight_ctx *cryptonight_alloc_context_export() {
    cryptonight_ctx *ctx = static_cast<cryptonight_ctx *>(_mm_malloc(sizeof(cryptonight_ctx), 16));
    ctx->memory = static_cast<uint8_t *>(_mm_malloc(xmrig::CRYPTONIGHT_MEMORY, 4096));

    return ctx;
}

extern "C" MODULE_API int cryptonight_get_context_lite_size_export() {
    return sizeof(cryptonight_ctx) + xmrig::CRYPTONIGHT_LITE_MEMORY;
}

extern "C" MODULE_API cryptonight_ctx *cryptonight_alloc_lite_context_export() {
    cryptonight_ctx *ctx = static_cast<cryptonight_ctx *>(_mm_malloc(sizeof(cryptonight_ctx), 16));
    ctx->memory = static_cast<uint8_t *>(_mm_malloc(xmrig::CRYPTONIGHT_LITE_MEMORY, 4096));

    return ctx;
}

extern "C" MODULE_API int cryptonight_get_context_heavy_size_export() {
    return sizeof(cryptonight_ctx) + xmrig::CRYPTONIGHT_HEAVY_MEMORY;
}

extern "C" MODULE_API cryptonight_ctx *cryptonight_alloc_heavy_context_export() {
    cryptonight_ctx *ctx = static_cast<cryptonight_ctx *>(_mm_malloc(sizeof(cryptonight_ctx), 16));
    ctx->memory = static_cast<uint8_t *>(_mm_malloc(xmrig::CRYPTONIGHT_HEAVY_MEMORY, 4096));

    return ctx;
}

extern "C" MODULE_API void cryptonight_free_ctx_export(cryptonight_ctx *ctx) {
    _mm_free(ctx->memory);
    _mm_free(ctx);
}

extern "C" MODULE_API void cryptonight_export(cryptonight_ctx* ctx, const char* input, unsigned char *output, uint32_t inputSize, uint32_t variant)
{
    switch (variant) {
    case 0:  cryptonight_single_hash<xmrig::CRYPTONIGHT, SOFT_AES, xmrig::VARIANT_0>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx);
        break;
    case 1:  cryptonight_single_hash<xmrig::CRYPTONIGHT, SOFT_AES, xmrig::VARIANT_1>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx);
        break;
    case 2:  cryptonight_single_hash<xmrig::CRYPTONIGHT, SOFT_AES, xmrig::VARIANT_2>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx);
        break;
    case 3:  cryptonight_single_hash<xmrig::CRYPTONIGHT, SOFT_AES, xmrig::VARIANT_XTL>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx);
        break;
    case 4:  cryptonight_single_hash<xmrig::CRYPTONIGHT, SOFT_AES, xmrig::VARIANT_MSR>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx);
        break;
    case 6:  cryptonight_single_hash<xmrig::CRYPTONIGHT, SOFT_AES, xmrig::VARIANT_XAO>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx);
        break;
    case 7:  cryptonight_single_hash<xmrig::CRYPTONIGHT, SOFT_AES, xmrig::VARIANT_RTO>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx);
        break;
    case 8:  cryptonight_single_hash<xmrig::CRYPTONIGHT, SOFT_AES, xmrig::VARIANT_2>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx);
        break;
    default: cryptonight_single_hash<xmrig::CRYPTONIGHT, SOFT_AES, xmrig::VARIANT_1>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx);
    }
}

extern "C" MODULE_API void cryptonight_light_export(cryptonight_ctx* ctx, const char* input, unsigned char *output, uint32_t inputSize, uint32_t variant)
{
    switch (variant) {
    case 0:
        cryptonight_single_hash<xmrig::CRYPTONIGHT_LITE, SOFT_AES, xmrig::VARIANT_0>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx);
        break;
    case 1:
        cryptonight_single_hash<xmrig::CRYPTONIGHT_LITE, SOFT_AES, xmrig::VARIANT_1>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx);
        break;
    default:
        cryptonight_single_hash<xmrig::CRYPTONIGHT_LITE, SOFT_AES, xmrig::VARIANT_1>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx);
    }
}

extern "C" MODULE_API void cryptonight_heavy_export(cryptonight_ctx* ctx, const char* input, unsigned char *output, uint32_t inputSize, uint32_t variant)
{
    switch (variant) {
    case 0:
        cryptonight_single_hash<xmrig::CRYPTONIGHT_HEAVY, SOFT_AES, xmrig::VARIANT_AUTO>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx);
        break;
    case 1:
        cryptonight_single_hash<xmrig::CRYPTONIGHT_HEAVY, SOFT_AES, xmrig::VARIANT_XHV >(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx);
        break;
    case 2:
        cryptonight_single_hash<xmrig::CRYPTONIGHT_HEAVY, SOFT_AES, xmrig::VARIANT_TUBE>(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx);
        break;
    default:
        cryptonight_single_hash<xmrig::CRYPTONIGHT_HEAVY, SOFT_AES, xmrig::VARIANT_0   >(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx);
    }
}

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


#ifdef _WIN32
#define MODULE_API __declspec(dllexport)
#else
#define MODULE_API
#endif

#include "crypto/common/VirtualMemory.h"
#include "crypto/cn/CnCtx.h"
#include "crypto/cn/CnHash.h"
#include "crypto/astrobwt/AstroBWT.h"
#include "crypto/kawpow/KPHash.h"
#include "3rdparty/libethash/ethash.h"
#include "crypto/ghostrider/ghostrider.h"
#include "crypto/common/portable/mm_malloc.h"

extern "C" {
    #include "c29/portable_endian.h" // for htole32/64
    #include "c29/int-util.h"
}

#include "c29.h"


#if (defined(__AES__) && (__AES__ == 1)) || (defined(__ARM_FEATURE_CRYPTO) && (__ARM_FEATURE_CRYPTO == 1))
#define SOFT_AES false
#if defined(CPU_INTEL)
//#warning Using IvyBridge assembler implementation
#define ASM_TYPE xmrig::Assembly::INTEL
#elif defined(CPU_AMD)
#warning Using Ryzen assembler implementation
#define ASM_TYPE xmrig::Assembly::RYZEN
#elif defined(CPU_AMD_OLD)
#warning Using Bulldozer assembler implementation
#define ASM_TYPE xmrig::Assembly::BULLDOZER
#elif !defined(__ARM_ARCH)
#error Unknown ASM implementation!
#endif
#else
//#warning Using software AES
#define SOFT_AES true
#endif

#define FN(algo)  xmrig::CnHash::fn(xmrig::Algorithm::algo, SOFT_AES ? xmrig::CnHash::AV_SINGLE_SOFT : xmrig::CnHash::AV_SINGLE, xmrig::Assembly::NONE)
#if defined(ASM_TYPE)
#define FNA(algo) xmrig::CnHash::fn(xmrig::Algorithm::algo, SOFT_AES ? xmrig::CnHash::AV_SINGLE_SOFT : xmrig::CnHash::AV_SINGLE, ASM_TYPE)
#else
#define FNA(algo) xmrig::CnHash::fn(xmrig::Algorithm::algo, SOFT_AES ? xmrig::CnHash::AV_SINGLE_SOFT : xmrig::CnHash::AV_SINGLE, xmrig::Assembly::NONE)
#endif

constexpr size_t max_mem_size = 20 * 1024 * 1024;

void ghostrider(const uint8_t* data, size_t size, uint8_t * output, cryptonight_ctx** ctx, uint64_t) {
    xmrig::ghostrider::hash(data, size, output, ctx, nullptr);
}

static xmrig::cn_hash_fun get_cn_fn(const int algo) {
    switch (algo) {
    case xmrig::Algorithm::CN_0:  return FN(CN_0);
    case xmrig::Algorithm::CN_1:  return FN(CN_1);
    case xmrig::Algorithm::CN_FAST:  return FN(CN_FAST);
    case xmrig::Algorithm::CN_XAO:  return FN(CN_XAO);
    case xmrig::Algorithm::CN_RTO:  return FN(CN_RTO);
    case xmrig::Algorithm::CN_2:  return FNA(CN_2);
    case xmrig::Algorithm::CN_HALF:  return FNA(CN_HALF);
    case xmrig::Algorithm::CN_GPU: return FN(CN_GPU);
    case xmrig::Algorithm::CN_R: return FNA(CN_R);
    case xmrig::Algorithm::CN_RWZ: return FNA(CN_RWZ);
    case xmrig::Algorithm::CN_ZLS: return FNA(CN_ZLS);
    case xmrig::Algorithm::CN_DOUBLE: return FNA(CN_DOUBLE);
    case xmrig::Algorithm::CN_CCX: return FNA(CN_CCX);
    case xmrig::Algorithm::GHOSTRIDER_RTM: return ghostrider;
    default: return FN(CN_R);
    }
}

static xmrig::cn_hash_fun get_cn_lite_fn(const int algo) {
    switch (algo) {
    case xmrig::Algorithm::CN_LITE_0:  return FN(CN_LITE_0);
    case xmrig::Algorithm::CN_LITE_1:  return FN(CN_LITE_1);
    default: return FN(CN_LITE_1);
    }
}

static xmrig::cn_hash_fun get_cn_heavy_fn(const int algo) {
    switch (algo) {
    case xmrig::Algorithm::CN_HEAVY_0:  return FN(CN_HEAVY_0);
    case xmrig::Algorithm::CN_HEAVY_XHV:  return FN(CN_HEAVY_XHV);
    case xmrig::Algorithm::CN_HEAVY_TUBE:  return FN(CN_HEAVY_TUBE);
    default: return FN(CN_HEAVY_0);
    }
}

static xmrig::cn_hash_fun get_cn_pico_fn(const int algo) {
    switch (algo) {
    case xmrig::Algorithm::CN_HEAVY_TUBE:  return FNA(CN_PICO_0);
    default: return FNA(CN_PICO_0);
    }
}
static xmrig::cn_hash_fun get_argon2_fn(const int algo) {
    switch (algo) {
    case xmrig::Algorithm::AR2_CHUKWA:  return FN(AR2_CHUKWA);
    case xmrig::Algorithm::AR2_WRKZ:  return FN(AR2_WRKZ);
    case xmrig::Algorithm::AR2_CHUKWA_V2:  return FN(AR2_CHUKWA_V2);
    default: return FN(AR2_CHUKWA);
    }
}

extern "C" MODULE_API void *alloc_context_export()
{
    cryptonight_ctx* ctx = nullptr;
    xmrig::CnCtx::create(&ctx, static_cast<uint8_t*>(_mm_malloc(max_mem_size, 4096)), max_mem_size, 1);
    return ctx;
}

extern "C" MODULE_API void free_context_export(cryptonight_ctx* ctx)
{
    if(ctx != nullptr)
        xmrig::CnCtx::release(&ctx, 1);
}

extern "C" MODULE_API bool cryptonight_export(const uint8_t * input, size_t input_length,
    char* output, const int algo, const uint64_t height, cryptonight_ctx* ctx)
{
    if (input_length == 0)
        return false;

    if (ctx == nullptr || input == nullptr)
        return false;

    const xmrig::cn_hash_fun fn = get_cn_fn(algo);

    fn(input, input_length, reinterpret_cast<uint8_t*>(output), &ctx, height);

    return true;
}

extern "C" MODULE_API bool cryptonight_lite_export(const uint8_t * input, size_t input_length,
    char* output, const int algo, const uint64_t height, cryptonight_ctx* ctx)
{
    if (input_length == 0)
        return false;

    if (ctx == nullptr || input == nullptr)
        return false;

    const xmrig::cn_hash_fun fn = get_cn_lite_fn(algo);

    fn(input, input_length, reinterpret_cast<uint8_t*>(output), &ctx, height);

    return true;
}

extern "C" MODULE_API bool cryptonight_heavy_export(const uint8_t * input, size_t input_length,
    char* output, const int algo, const uint64_t height, cryptonight_ctx* ctx)
{
    if (input_length == 0)
        return false;

    if (ctx == nullptr || input == nullptr)
        return false;

    const xmrig::cn_hash_fun fn = get_cn_heavy_fn(algo);

    fn(input, input_length, reinterpret_cast<uint8_t*>(output), &ctx, height);

    return true;
}

extern "C" MODULE_API bool cryptonight_pico_export(const uint8_t * input, size_t input_length,
    char* output, const int algo, const uint64_t height, cryptonight_ctx* ctx)
{
    if (input_length == 0)
        return false;

    if (ctx == nullptr || input == nullptr)
        return false;

    const xmrig::cn_hash_fun fn = get_cn_pico_fn(algo);

    fn(input, input_length, reinterpret_cast<uint8_t*>(output), &ctx, height);

    return true;
}

extern "C" MODULE_API bool argon_export(const uint8_t * input, size_t input_length,
    char* output, const int algo, const uint64_t height, cryptonight_ctx* ctx)
{
    if (input_length == 0)
        return false;

    if (ctx == nullptr || input == nullptr)
        return false;

    const xmrig::cn_hash_fun fn = get_argon2_fn(algo);

    fn(input, input_length, reinterpret_cast<uint8_t*>(output), &ctx, height);

    return true;
}

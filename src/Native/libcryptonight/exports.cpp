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

#include "crypto/cn/CryptoNight.h"

#include "crypto/common/VirtualMemory.h"
#include "crypto/cn/CnCtx.h" 
#include "crypto/cn/CnHash.h"
#include "crypto/randomx/randomx.h"
#include "crypto/defyx/defyx.h"
#include <stdexcept>

extern "C" {
#include "crypto/defyx/KangarooTwelve.h"
} 

#if (defined(__AES__)) || (defined(__ARM_FEATURE_CRYPTO) && (__ARM_FEATURE_CRYPTO == 1))
  #define SOFT_AES false
  #if defined(CPU_INTEL)
    //#warning Using IvyBridge assembler implementation
    #define ASM_TYPE xmrig::Assembly::INTEL
  #elif defined(CPU_AMD)
    //#warning Using Ryzen assembler implementation
    #define ASM_TYPE xmrig::Assembly::RYZEN
  #elif defined(CPU_AMD_OLD)
    //#warning Using Bulldozer assembler implementation
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

#ifdef _WIN32
#define MODULE_API __declspec(dllexport)
#else
#define MODULE_API
#endif

const size_t max_mem_size = 4 * 1024 * 1024;

static xmrig::cn_hash_fun get_cn_fn(const int algo) {
  switch (algo) {
    case 0:  return FN(CN_0);
    case 1:  return FN(CN_1);
    case 4:  return FN(CN_FAST);
    case 6:  return FN(CN_XAO);
    case 7:  return FN(CN_RTO);
    case 8:  return FNA(CN_2);
    case 9:  return FNA(CN_HALF);
    case 11: return FN(CN_GPU);
    case 13: return FNA(CN_R);
    case 14: return FNA(CN_RWZ);
    case 15: return FNA(CN_ZLS);
    case 16: return FNA(CN_DOUBLE);
    default: return FN(CN_1);
  }
}

class CryptonightContextWrapper
{
public:
    CryptonightContextWrapper(xmrig::VirtualMemory *mem, cryptonight_ctx *ctx)
    {
        this->ctx = ctx;
        this->mem = mem;
    }

    ~CryptonightContextWrapper()
    {
        if(ctx)
            xmrig::CnCtx::release(&ctx, 1);

        delete mem;
    }

    xmrig::VirtualMemory *mem;
    cryptonight_ctx *ctx;
};

static xmrig::cn_hash_fun get_cn_lite_fn(const int algo) {
  switch (algo) {
    case 0:  return FN(CN_LITE_0);
    case 1:  return FN(CN_LITE_1);
    default: return FN(CN_LITE_1);
  }
}

static xmrig::cn_hash_fun get_cn_heavy_fn(const int algo) {
  switch (algo) {
    case 0:  return FN(CN_HEAVY_0);
    case 1:  return FN(CN_HEAVY_XHV);
    case 2:  return FN(CN_HEAVY_TUBE);
    default: return FN(CN_HEAVY_0);
  }
}

static xmrig::cn_hash_fun get_cn_pico_fn(const int algo) {
  switch (algo) {
    case 0:  return FNA(CN_PICO_0);
    default: return FNA(CN_PICO_0);
  }
}
static xmrig::cn_hash_fun get_argon2_fn(const int algo) {
  switch (algo) {
    case 0:  return FN(AR2_CHUKWA);
    case 1:  return FN(AR2_WRKZ);
    default: return FN(AR2_CHUKWA);
  }
}

extern "C" MODULE_API CryptonightContextWrapper *cryptonight_alloc_context_export() {
    cryptonight_ctx *ctx = NULL;
    auto mem = new xmrig::VirtualMemory(max_mem_size, true, false, 0, 4096);
    xmrig::CnCtx::create(&ctx, mem->scratchpad(), max_mem_size, 1);

    auto wrapper = new CryptonightContextWrapper(mem, ctx);
    return wrapper;
}

extern "C" MODULE_API CryptonightContextWrapper *cryptonight_alloc_lite_context_export() {
    return cryptonight_alloc_context_export();
}

extern "C" MODULE_API CryptonightContextWrapper *cryptonight_alloc_heavy_context_export() {
    return cryptonight_alloc_context_export();
}

extern "C" MODULE_API CryptonightContextWrapper *cryptonight_alloc_pico_context_export() {
    return cryptonight_alloc_context_export();
}

extern "C" MODULE_API void cryptonight_free_ctx_export(CryptonightContextWrapper *wrapper) {
	delete wrapper;
}

extern "C" MODULE_API void cryptonight_export(CryptonightContextWrapper* wrapper, const char* input, unsigned char *output, size_t inputSize, uint32_t variant, uint64_t height)
{
    auto ctx = wrapper->ctx;
    const xmrig::cn_hash_fun fn = get_cn_fn(variant);

    fn(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
}

extern "C" MODULE_API void cryptonight_light_export(CryptonightContextWrapper* wrapper, const char* input, unsigned char *output, size_t inputSize, uint32_t variant, uint64_t height)
{
    auto ctx = wrapper->ctx;
    const xmrig::cn_hash_fun fn = get_cn_lite_fn(variant);

    fn(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
}

extern "C" MODULE_API void cryptonight_heavy_export(CryptonightContextWrapper* wrapper, const char* input, unsigned char *output, size_t inputSize, uint32_t variant, uint64_t height)
{
    auto ctx = wrapper->ctx;
    const xmrig::cn_hash_fun fn = get_cn_heavy_fn(variant);

    fn(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
}

extern "C" MODULE_API void cryptonight_pico_export(CryptonightContextWrapper* wrapper, const char* input, unsigned char *output, size_t inputSize, uint32_t variant, uint64_t height)
{
    auto ctx = wrapper->ctx;
    const xmrig::cn_hash_fun fn = get_cn_pico_fn(variant);

    fn(reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output), &ctx, height);
}

class RandomXVmWrapper
{
public:
    RandomXVmWrapper(xmrig::VirtualMemory *mem, randomx_vm *vm)
    {
        this->vm = vm;
        this->mem = mem;
    }

    ~RandomXVmWrapper()
    {
        if(vm)
            randomx_destroy_vm(vm);

        delete mem;
    }

    xmrig::VirtualMemory *mem;
    randomx_vm *vm;
};

class RandomXCacheWrapper
{
public:
    RandomXCacheWrapper(randomx_cache *cache, void *seedHash)
    {
        this->cache = cache;
        this->seedHash = seedHash;
    }

    ~RandomXCacheWrapper()
    {
        if(cache)
            randomx_release_cache(cache);

        if(seedHash)
            free(seedHash);
    }

    void *seedHash;
    randomx_cache *cache;
};

extern "C" MODULE_API RandomXCacheWrapper *randomx_create_cache_export(int variant, const char* seedHash, size_t seedHashSize)
{
    // Copy seed
    auto seedHashCopy = malloc(seedHashSize);
    memcpy(seedHashCopy, seedHash, seedHashSize);

    // Alloc cache
    auto cache = randomx_alloc_cache(static_cast<randomx_flags>(RANDOMX_FLAG_JIT | RANDOMX_FLAG_LARGE_PAGES));

    if (!cache)
        cache = randomx_alloc_cache(static_cast<randomx_flags>(RANDOMX_FLAG_JIT));

    switch (variant) {
        case 0:
            randomx_apply_config(RandomX_MoneroConfig);
            break;
        case 1:
            randomx_apply_config(RandomX_ScalaConfig);
            break;
        case 2:
            randomx_apply_config(RandomX_ArqmaConfig);
            break;
        case 17:
            randomx_apply_config(RandomX_WowneroConfig);
            break;
        case 18:
            randomx_apply_config(RandomX_LokiConfig);
            break;
        default:
            throw std::domain_error("Unknown RandomX algo");
    }

    // Init cache
    randomx_init_cache(cache, seedHashCopy, seedHashSize);

    // Wrap it
    auto wrapper = new RandomXCacheWrapper(cache, seedHashCopy);
    return wrapper;
}

extern "C" MODULE_API void randomx_free_cache_export(RandomXCacheWrapper *wrapper)
{
    delete wrapper;
}

extern "C" MODULE_API RandomXVmWrapper *randomx_create_vm_export(randomx_cache *cache)
{
    int flags = RANDOMX_FLAG_LARGE_PAGES | RANDOMX_FLAG_JIT;

    auto mem = new xmrig::VirtualMemory(max_mem_size, false, false, 0, 4096);
    auto vm = randomx_create_vm(static_cast<randomx_flags>(flags), cache, nullptr, mem->scratchpad());

    if (!vm)
        vm = randomx_create_vm(static_cast<randomx_flags>(flags - RANDOMX_FLAG_LARGE_PAGES), cache, nullptr, mem->scratchpad());

    if (!vm)
        return nullptr;

    auto wrapper = new RandomXVmWrapper(mem, vm);
    return wrapper;
}

extern "C" MODULE_API void randomx_free_vm_export(RandomXVmWrapper *wrapper)
{
    delete wrapper;
}

extern "C" MODULE_API void randomx_set_vm_cache_export(RandomXVmWrapper *wrapper, RandomXCacheWrapper *cacheWrapper)
{
    randomx_vm_set_cache(wrapper->vm, cacheWrapper->cache);
}

extern "C" MODULE_API void randomx_export(RandomXVmWrapper* wrapper, const char* input, unsigned char *output, size_t inputSize, uint32_t variant, uint64_t height)
{
    auto vm = wrapper->vm;

    switch (variant) {
      case 1:  defyx_calculate_hash  (vm, reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output));
               break;
      default: randomx_calculate_hash(vm, reinterpret_cast<const uint8_t*>(input), inputSize, reinterpret_cast<uint8_t*>(output));
    }
}

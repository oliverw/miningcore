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

namespace rx
{
  #include "randomx.h"
};

#ifdef _WIN32
#define MODULE_API __declspec(dllexport)
#else
#define MODULE_API __attribute__((visibility("default")))
#endif

extern "C" MODULE_API rx::randomx_flags randomx_get_flags()
{
	return rx::randomx_get_flags();
}

extern "C" MODULE_API rx::randomx_cache *randomx_alloc_cache(rx::randomx_flags flags)
{
  return rx::randomx_alloc_cache(flags);
}

extern "C" MODULE_API void randomx_init_cache(rx::randomx_cache *cache, const void *key, size_t keySize)
{
  rx::randomx_init_cache(cache, key, keySize);
}

extern "C" MODULE_API void randomx_release_cache(rx::randomx_cache* cache)
{
  rx::randomx_release_cache(cache);
}

extern "C" MODULE_API rx::randomx_dataset *randomx_alloc_dataset(rx::randomx_flags flags)
{
  return rx::randomx_alloc_dataset(flags);
}

extern "C" MODULE_API unsigned long randomx_dataset_item_count(void)
{
  return rx::randomx_dataset_item_count();
}

extern "C" MODULE_API void randomx_init_dataset(rx::randomx_dataset *dataset, rx::randomx_cache *cache, unsigned long startItem, unsigned long itemCount)
{
  rx::randomx_init_dataset(dataset, cache, startItem, itemCount);
}

extern "C" MODULE_API void *randomx_get_dataset_memory(rx::randomx_dataset *dataset)
{
   return rx::randomx_get_dataset_memory(dataset);
}

extern "C" MODULE_API void randomx_release_dataset(rx::randomx_dataset *dataset)
{
  rx::randomx_release_dataset(dataset);
}

extern "C" MODULE_API rx::randomx_vm *randomx_create_vm(rx::randomx_flags flags, rx::randomx_cache *cache, rx::randomx_dataset *dataset)
{
  return rx::randomx_create_vm(flags, cache, dataset);
}

extern "C" MODULE_API void randomx_vm_set_cache(rx::randomx_vm *machine, rx::randomx_cache* cache)
{
  rx::randomx_vm_set_cache(machine, cache);
}

extern "C" MODULE_API void randomx_vm_set_dataset(rx::randomx_vm *machine, rx::randomx_dataset *dataset)
{
  rx::randomx_vm_set_dataset(machine, dataset);
}

extern "C" MODULE_API void randomx_destroy_vm(rx::randomx_vm *machine)
{
  rx::randomx_destroy_vm(machine);
}

extern "C" MODULE_API void randomx_calculate_hash(rx::randomx_vm *machine, const void *input, size_t inputSize, void *output)
{
  rx::randomx_calculate_hash(machine, input, inputSize, output);
}

extern "C" MODULE_API void randomx_calculate_hash_first(rx::randomx_vm* machine, const void* input, size_t inputSize)
{
  rx::randomx_calculate_hash_first(machine, input, inputSize);
}

extern "C" MODULE_API void randomx_calculate_hash_next(rx::randomx_vm* machine, const void* nextInput, size_t nextInputSize, void* output)
{
  rx::randomx_calculate_hash_next(machine, nextInput, nextInputSize, output);
}

extern "C" MODULE_API void randomx_calculate_hash_last(rx::randomx_vm* machine, void* output)
{
  rx::randomx_calculate_hash_last(machine, output);
}

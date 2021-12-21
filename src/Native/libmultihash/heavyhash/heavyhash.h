#ifndef OPOWPOOL_HEAVYHASH_H
#define OPOWPOOL_HEAVYHASH_H



//void compute_blockheader_heavyhash(uint32_t* block_header, void* output);

// yiimp format
#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>
void heavyhash_hash(const char* input, char* output, uint32_t len);


#ifdef __cplusplus
}
#endif

#endif //OPOWPOOL_HEAVYHASH_H
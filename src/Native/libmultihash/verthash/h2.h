#ifndef VERTHASH_H2_INCLUDED
#define VERTHASH_H2_INCLUDED
#include <stdlib.h>
#include <stdint.h>
#ifdef __cplusplus
extern "C" {
#endif

int verthash(const unsigned char* input, const size_t input_size, unsigned char* output);
int verthash_init(const char* dat_file_name, int createIfMissing);

#ifdef __cplusplus
}
#endif

#endif

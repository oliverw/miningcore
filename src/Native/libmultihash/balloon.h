#ifndef BALLOON_H
#define BALLOON_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

int alx_init_balloon_buffer();
void balloon(const char* input, char* output, unsigned int len);
void alx_free_balloon_buffer();

#ifdef __cplusplus
}
#endif

#endif


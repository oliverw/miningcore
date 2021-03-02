#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include <stdio.h>

#include "Lyra2-z.h"

#define _ALIGN(x) __attribute__ ((aligned(x)))

void lyra2z330_hash(const char* input, char* output, uint32_t len)
{
  uint32_t _ALIGN(64) hash[8];

  LYRA2z((void*)hash, 32, (void*)input, len, (void*)input, len, 2, 330, 256);

  memcpy(output, hash, 32);
}

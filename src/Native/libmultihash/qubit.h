#ifndef QUBIT_H
#define QUBIT_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

void qubit_hash(const char* input, char* output, uint32_t len);

#ifdef __cplusplus
}
#endif

#endif

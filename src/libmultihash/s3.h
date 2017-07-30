#ifndef S3_H
#define S3_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

void s3_hash(const char* input, char* output, uint32_t len);

#ifdef __cplusplus
}
#endif

#endif

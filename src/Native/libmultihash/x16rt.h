#ifndef X16RT_H
#define X16RT_H

#ifdef __cplusplus
extern "C"
{
#endif

#include <stdint.h>

    void x16rt_hash(const char *input, char *output, uint32_t len);

#ifdef __cplusplus
}
#endif

#endif

#ifndef EQUIHASHVERIFY_H
#define EQUIHASHVERIFY_H

#include <vector>

#ifdef __cplusplus
extern "C" {
#endif

bool verifyEH(const char*, const std::vector<unsigned char>&);

#ifdef __cplusplus
}
#endif

#endif

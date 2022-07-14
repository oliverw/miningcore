#ifndef EQUIHASHVERIFY_H
#define EQUIHASHVERIFY_H

#include <vector>

#ifdef __cplusplus
extern "C" {
#endif

bool verifyEH_200_9(const char*, const std::vector<unsigned char>&, const char *personalization);
bool verifyEH_144_5(const char*, const std::vector<unsigned char>&, const char *personalization);
bool verifyEH_96_5(const char*, const std::vector<unsigned char>&, const char *personalization);

#ifdef __cplusplus
}
#endif

#endif

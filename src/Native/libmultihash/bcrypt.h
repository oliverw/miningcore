#ifndef BCRYPT_H
#define BCRYPT_H

#ifdef __cplusplus
extern "C" {
#endif

void bcrypt_hash(const char *input, char *output);

#ifdef __cplusplus
}
#endif

#endif
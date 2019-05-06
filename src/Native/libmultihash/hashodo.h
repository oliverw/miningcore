// Copyright (c) 2009-2010 Satoshi Nakamoto
// Copyright (c) 2009-2018 The DigiByte developers
// Distributed under the MIT/X11 software license, see the accompanying
// file COPYING or http://www.opensource.org/licenses/mit-license.php.

#ifndef HASH_ODO
#define HASH_ODO

#include <assert.h>
#include <string.h>

#include "odocrypt.h"
extern "C" {
#include "KeccakP-800-SnP.h"
}

inline void odocrypt_hash(const char* input, char* output, uint32_t len, uint32_t key)
{
    char cipher[KeccakP800_stateSizeInBytes] = {};

    assert(len <= OdoCrypt::DIGEST_SIZE);
    assert(OdoCrypt::DIGEST_SIZE < KeccakP800_stateSizeInBytes);
    memcpy(cipher, static_cast<const void*>(input), len);
    cipher[len] = 1;

    OdoCrypt(key).Encrypt(cipher, cipher);
    KeccakP800_Permute_12rounds(cipher);
    memcpy(output, cipher, 32);
}

#endif

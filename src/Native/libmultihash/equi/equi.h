/*
 * Copyright (c) 2016 abc at openwall dot com
 * Copyright (c) 2016 Jack Grigg
 * Copyright (c) 2016 The Zcash developers
 *
 * Distributed under the MIT software license, see the accompanying
 * file COPYING or http://www.opensource.org/licenses/mit-license.php.
 *
 * Port to C of C++ implementation of the Equihash Proof-of-Work
 * algorithm from zcashd.
 */

#ifndef EQUI_H
#define EQUI_H

#ifdef __cplusplus
extern "C" {
#endif


#include <string.h>
#include <stdint.h>
#include <stdbool.h>
#include <assert.h>

#include <sodium.h>

#include "endian.h"

static void digestInit(crypto_generichash_blake2b_state *S, const int n, const int k);

static void expandArray(const unsigned char *in, const size_t in_len,
    unsigned char *out, const size_t out_len,
    const size_t bit_len, const size_t byte_pad);

static int isZero(const uint8_t *hash, size_t len);

static void generateHash(crypto_generichash_blake2b_state *S, const uint32_t g, uint8_t *hash, const size_t hashLen);

// hdr -> header including nonce (140 bytes)
// soln -> equihash solution (excluding 3 bytes with size, so 1344 bytes length)
bool verifyEH(const char *hdr, const char *soln);

#ifdef __cplusplus
}
#endif

#endif

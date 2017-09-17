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

#include <string.h>
#include <stdint.h>
#include <stdbool.h>
#include <assert.h>

#ifdef _WIN32
#include <stdio.h>
#define alloca(x) _alloca(x)
#endif

#include <sodium.h>

#include "endian.h"

static void digestInit(crypto_generichash_blake2b_state *S, const int n, const int k) {
  uint32_t le_N = htole32(n);
  uint32_t le_K = htole32(k);

  // No VLAs with VC ;-(
#ifdef _MSC_VER
  unsigned char *personalization = (unsigned char *) alloca(sizeof(unsigned char) * crypto_generichash_blake2b_PERSONALBYTES);
#else
  unsigned char personalization[crypto_generichash_blake2b_PERSONALBYTES] = {};
#endif

  memcpy(personalization, "ZcashPoW", 9);
  memcpy(personalization + 8,  &le_N, 4);
  memcpy(personalization + 12, &le_K, 4);
  crypto_generichash_blake2b_init_salt_personal(S,
    NULL, 0, (512 / n) * n / 8, NULL, personalization);
}

static void expandArray(const unsigned char *in, const size_t in_len,
    unsigned char *out, const size_t out_len,
    const size_t bit_len, const size_t byte_pad)
{
    assert(bit_len >= 8);
    assert(8 * sizeof(uint32_t) >= 7 + bit_len);

    const size_t out_width = (bit_len + 7) / 8 + byte_pad;
    assert(out_len == 8 * out_width * in_len / bit_len);

    const uint32_t bit_len_mask = ((uint32_t)1 << bit_len) - 1;

    // The acc_bits least-significant bits of acc_value represent a bit sequence
    // in big-endian order.
    size_t acc_bits = 0;
    uint32_t acc_value = 0;

    size_t j = 0;
    for (size_t i = 0; i < in_len; i++) {
      acc_value = (acc_value << 8) | in[i];
      acc_bits += 8;

      // When we have bit_len or more bits in the accumulator, write the next
      // output element.
      if (acc_bits >= bit_len) {
        acc_bits -= bit_len;
        for (size_t x = 0; x < byte_pad; x++) {
          out[j + x] = 0;
        }
        for (size_t x = byte_pad; x < out_width; x++) {
          out[j + x] = (
            // Big-endian
            acc_value >> (acc_bits + (8 * (out_width - x - 1)))
          ) & (
            // Apply bit_len_mask across byte boundaries
            (bit_len_mask >> (8 * (out_width - x - 1))) & 0xFF
          );
        }
        j += out_width;
      }
    }
}

static int isZero(const uint8_t *hash, size_t len) {
  // This doesn't need to be constant time.
  for (int i = 0; i < len; i++) {
    if (hash[i] != 0)
      return 0;
  }
  return 1;
}

static void generateHash(crypto_generichash_blake2b_state *S, const uint32_t g, uint8_t *hash, const size_t hashLen) {
  const uint32_t le_g = htole32(g);
  crypto_generichash_blake2b_state digest = *S; /* copy */

  crypto_generichash_blake2b_update(&digest, (uint8_t *)&le_g, sizeof(le_g));
  crypto_generichash_blake2b_final(&digest, hash, hashLen);
}

// hdr -> header including nonce (140 bytes)
// soln -> equihash solution (excluding 3 bytes with size, so 1344 bytes length)
bool verifyEH(const char *hdr, const char *soln) {
  const int n = 200;
  const int k = 9;
  const int collisionBitLength  = n / (k + 1);
  const int collisionByteLength = (collisionBitLength + 7) / 8;
  const int hashLength = (k + 1) * collisionByteLength;
  const int indicesPerHashOutput = 512 / n;
  const int hashOutput = indicesPerHashOutput * n / 8;
  const int equihashSolutionSize = (1 << k) * (n / (k + 1) + 1) / 8;
  const int solnr = 1 << k;
  uint32_t indices[512];

  crypto_generichash_blake2b_state state;
  digestInit(&state, n, k);
  crypto_generichash_blake2b_update(&state, hdr, 140);

  expandArray(soln, equihashSolutionSize, (char *)&indices, sizeof(indices), collisionBitLength + 1, 1);

  // No VLAs with VC ;-(
#ifdef _MSC_VER
  uint8_t *vHash = (uint8_t*)alloca(sizeof(uint8_t) * hashLength);
  memset(vHash, 0, sizeof(vHash));

  uint8_t *tmpHash = (uint8_t*)alloca(sizeof(uint8_t) * hashOutput);
  memset(tmpHash, 0, sizeof(uint8_t) * hashOutput);

  uint8_t *hash = (uint8_t*)alloca(sizeof(uint8_t) * hashLength);
  memset(hash, 0, sizeof(uint8_t) * hashLength);

  for (int j = 0; j < solnr; j++) {
	  int i = be32toh(indices[j]);
	  generateHash(&state, i / indicesPerHashOutput, tmpHash, hashOutput);
	  expandArray(tmpHash + (i % indicesPerHashOutput * n / 8), n / 8, hash, hashLength, collisionBitLength, 0);
	  for (int k = 0; k < hashLength; ++k)
		  vHash[k] ^= hash[k];
  }
#else
  uint8_t vHash[hashLength];
  memset(vHash, 0 , sizeof(vHash));
  for (int j = 0; j < solnr; j++) {
  	uint8_t tmpHash[hashOutput];
  	uint8_t hash[hashLength];
  	int i = be32toh(indices[j]);
  	generateHash(&state, i / indicesPerHashOutput, tmpHash, hashOutput);
  	expandArray(tmpHash + (i % indicesPerHashOutput * n / 8), n / 8, hash, hashLength, collisionBitLength, 0);
  	for (int k = 0; k < hashLength; ++k)
  	    vHash[k] ^= hash[k];
  }
#endif

  return isZero(vHash, sizeof(vHash));
}

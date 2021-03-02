/*
 * Copyright (c) 2015-2016, Henry Corrigan-Gibbs (https://github.com/henrycg/balloon)
 * Copyright (c) 2018-2019, barrystyle (https://github.com/barrystyle/balloon)
 *
 * balloon² - improving on the original balloon hashing pow algorithm
 *
 * Permission to use, copy, modify, and/or distribute this software for any
 * purpose with or without fee is hereby granted, provided that the above
 * copyright notice and this permission notice appear in all copies.
 *
 * THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH
 * REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND
 * FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT,
 * INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM
 * LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR
 * OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR
 * PERFORMANCE OF THIS SOFTWARE.
 */
#include "balloon.h"
#include <openssl/evp.h>
#include <openssl/sha.h>
#include <inttypes.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <stdio.h>


#define BUFLEN (1 << 18)
#define EXPROUNDS (BUFLEN / 32)
#define BLOCKSIZE (8 * sizeof(uint32_t))
#define nullptr ((void*)0)

#ifdef __AES__

#ifdef __SSE4_2__
#include <smmintrin.h>
#endif
#include <tmmintrin.h> //SSE3
#include <wmmintrin.h> //AES-NI

#define bswap32(x) ((((x) << 24) & 0xff000000u) | (((x) << 8) & 0x00ff0000u) | (((x) >> 8) & 0x0000ff00u) | (((x) >> 24) & 0x000000ffu))

static __m128i aes_128_key_exp(__m128i key, const int rcon)
{
    __m128i key2, keygn;

    key2 = _mm_aeskeygenassist_si128(key, rcon);
    keygn = _mm_shuffle_epi32(key2, _MM_SHUFFLE(3, 3, 3, 3));
    key2 = _mm_xor_si128(key, _mm_slli_si128(key, 4));
    key2 = _mm_xor_si128(key2, _mm_slli_si128(key2, 4));
    key2 = _mm_xor_si128(key2, _mm_slli_si128(key2, 4));
    key2 = _mm_xor_si128(key2, keygn);

    return key2;
}

static void alx_key_expansion(__m128i* key, const uint32_t* key_bytes)
{
    key[0] = _mm_loadu_si128((const __m128i*)key_bytes);
    key[1] = aes_128_key_exp(key[0], 0x01);
    key[2] = aes_128_key_exp(key[1], 0x02);
    key[3] = aes_128_key_exp(key[2], 0x04);
    key[4] = aes_128_key_exp(key[3], 0x08);
    key[5] = aes_128_key_exp(key[4], 0x10);
    key[6] = aes_128_key_exp(key[5], 0x20);
    key[7] = aes_128_key_exp(key[6], 0x40);
    key[8] = aes_128_key_exp(key[7], 0x80);
    key[9] = aes_128_key_exp(key[8], 0x1B);
    key[10] = aes_128_key_exp(key[9], 0x36);
}

static void alx_aes_encrypt(uint32_t* const iv, uint64_t* out, const __m128i* key)
{
    const __m128i andval = _mm_set_epi64x(EXPROUNDS - 1, EXPROUNDS - 1);

    __m128i m[3];

    m[0] = _mm_set_epi32(iv[3], 0, 0, 0);
    iv[3] = bswap32(bswap32(iv[3]) + 1);

    m[1] = _mm_set_epi32(iv[3], 0, 0, 0);
    iv[3] = bswap32(bswap32(iv[3]) + 1);

    m[2] = _mm_set_epi32(iv[3], 0, 0, 0);
    iv[3] = bswap32(bswap32(iv[3]) + 1);

    m[0] = _mm_xor_si128(m[0], key[0]);
    m[1] = _mm_xor_si128(m[1], key[0]);
    m[2] = _mm_xor_si128(m[2], key[0]);

    m[0] = _mm_aesenc_si128(m[0], key[1]);
    m[1] = _mm_aesenc_si128(m[1], key[1]);
    m[2] = _mm_aesenc_si128(m[2], key[1]);

    m[0] = _mm_aesenc_si128(m[0], key[2]);
    m[1] = _mm_aesenc_si128(m[1], key[2]);
    m[2] = _mm_aesenc_si128(m[2], key[2]);

    m[0] = _mm_aesenc_si128(m[0], key[3]);
    m[1] = _mm_aesenc_si128(m[1], key[3]);
    m[2] = _mm_aesenc_si128(m[2], key[3]);

    m[0] = _mm_aesenc_si128(m[0], key[4]);
    m[1] = _mm_aesenc_si128(m[1], key[4]);
    m[2] = _mm_aesenc_si128(m[2], key[4]);

    m[0] = _mm_aesenc_si128(m[0], key[5]);
    m[1] = _mm_aesenc_si128(m[1], key[5]);
    m[2] = _mm_aesenc_si128(m[2], key[5]);

    m[0] = _mm_aesenc_si128(m[0], key[6]);
    m[1] = _mm_aesenc_si128(m[1], key[6]);
    m[2] = _mm_aesenc_si128(m[2], key[6]);

    m[0] = _mm_aesenc_si128(m[0], key[7]);
    m[1] = _mm_aesenc_si128(m[1], key[7]);
    m[2] = _mm_aesenc_si128(m[2], key[7]);

    m[0] = _mm_aesenc_si128(m[0], key[8]);
    m[1] = _mm_aesenc_si128(m[1], key[8]);
    m[2] = _mm_aesenc_si128(m[2], key[8]);

    m[0] = _mm_aesenc_si128(m[0], key[9]);
    m[1] = _mm_aesenc_si128(m[1], key[9]);
    m[2] = _mm_aesenc_si128(m[2], key[9]);

    m[0] = _mm_aesenclast_si128(m[0], key[10]);
    m[1] = _mm_aesenclast_si128(m[1], key[10]);
    m[2] = _mm_aesenclast_si128(m[2], key[10]);

    m[0] = _mm_and_si128(m[0], andval);
    m[1] = _mm_and_si128(m[1], andval);
    m[2] = _mm_and_si128(m[2], andval);

    m[0] = _mm_slli_epi64(m[0], 3);
    m[1] = _mm_slli_epi64(m[1], 3);
    m[2] = _mm_slli_epi64(m[2], 3);

    _mm_storeu_si128((__m128i*)out + 0, m[0]);
    _mm_storeu_si128((__m128i*)out + 1, m[1]);
    _mm_storeu_si128((__m128i*)out + 2, m[2]);
}
#endif

static void sha256(const void* input, void* output, int len)
{
    SHA256_CTX ctx;
    SHA256_Init(&ctx);
    SHA256_Update(&ctx, input, len);
    SHA256_Final((unsigned char*)output, &ctx);
}

static __thread uint32_t* buffer = nullptr;

#ifndef __AES__
static __thread EVP_CIPHER_CTX* aes_ctx;
#endif

static __thread uint8_t init = 0;

int alx_init_balloon_buffer()
{
    buffer = (uint32_t*)malloc(BUFLEN);
    if (buffer == NULL) {
        return -1;
    }
#ifndef __AES__
    aes_ctx = EVP_CIPHER_CTX_new();
#endif

    return 0;
}

void balloon_hash(const void* input, void* output)
{
    if (!init) {
        alx_init_balloon_buffer();
        init = 1;
    }

    uint32_t iv[4] = { 0 };
    uint32_t* in32 = (uint32_t*)input;
    uint32_t* prev_block = buffer;
    uint32_t* cur_block = buffer + 8;
    uint32_t* nbr_block;
    uint32_t counter = 0;
    uint32_t hashmix[42] = { 0 };
    uint32_t key_bytes[8] = { 0 };

    /* compute AES key */
    for (int i = 0; i < 8; i++) {
        hashmix[i] = in32[12 + i];
    }

    hashmix[8] = 0x00000080;
    hashmix[9] = 0;
    hashmix[10] = 0x00000004;

    sha256(hashmix, key_bytes, 11 * sizeof(uint32_t));

    /* Initialize AES cipher */
#if __AES__
    __m128i key[11];
    alx_key_expansion(key, key_bytes);
#else
    EVP_EncryptInit(aes_ctx, EVP_aes_128_ctr(), (const unsigned char*)key_bytes, (const unsigned char*)iv);
#endif

    /* Append first block from input */
    hashmix[0] = 0;
    hashmix[1] = 0;
    for (int i = 0; i < 28; i++) {
        hashmix[2 + i] = in32[(12 + i) % 20];
    }
    hashmix[30] = 0x00000080;
    hashmix[31] = 0;
    hashmix[32] = 0x00000004;
    sha256(hashmix, buffer, 33 * sizeof(uint32_t));
    counter++;

    /* Append rest of the blocks from previous blocks */
    for (int i = 1; i < EXPROUNDS; i++) {
        hashmix[0] = counter;
        hashmix[1] = 0;
        memcpy(&hashmix[2], prev_block, BLOCKSIZE);
        sha256(hashmix, cur_block, 40);
        counter++;
        prev_block = cur_block;
        cur_block += 8;
    }

    /* Mixing rounds */
    uint64_t buf[6] = { 0 };
    for (int offset = 0; offset < 2; offset++) {
        for (int i = offset; i < EXPROUNDS; i += 4) {
            cur_block = buffer + (8 * i);
            prev_block = i ? cur_block - 8 : buffer + ((BUFLEN / 4) - 8);
            hashmix[0] = counter;
            memcpy(&hashmix[2], prev_block, BLOCKSIZE);
            memcpy(&hashmix[10], cur_block, BLOCKSIZE);
#if __AES__
            alx_aes_encrypt(iv, buf, key);
#else
            int templ;
            EVP_EncryptUpdate(aes_ctx, (unsigned char*)&buf[0], &templ, (const unsigned char*)&iv, 16);
            EVP_EncryptUpdate(aes_ctx, (unsigned char*)&buf[2], &templ, (const unsigned char*)&iv, 16);
            EVP_EncryptUpdate(aes_ctx, (unsigned char*)&buf[4], &templ, (const unsigned char*)&iv, 16);
            for (int l = 0; l < 6; l++) {
                buf[l] = 8 * (buf[l] & (EXPROUNDS - 1));
            }
#endif
            nbr_block = buffer + buf[0];
            memcpy(&hashmix[18], nbr_block, BLOCKSIZE);
            nbr_block = buffer + buf[1];
            memcpy(&hashmix[26], nbr_block, BLOCKSIZE);
            nbr_block = buffer + buf[2];
            memcpy(&hashmix[34], nbr_block, BLOCKSIZE);
            sha256(hashmix, cur_block, 42 * sizeof(uint32_t));
            counter += 1;
            cur_block = buffer + (8 * (i + 2));
            prev_block = (i + 2) ? cur_block - 8 : buffer + ((BUFLEN / 4) - 8);
            hashmix[0] = counter;
            memcpy(&hashmix[2], prev_block, BLOCKSIZE);
            memcpy(&hashmix[10], cur_block, BLOCKSIZE);
            nbr_block = buffer + buf[3];
            memcpy(&hashmix[18], nbr_block, BLOCKSIZE);
            nbr_block = buffer + buf[4];
            memcpy(&hashmix[26], nbr_block, BLOCKSIZE);
            nbr_block = buffer + buf[5];
            memcpy(&hashmix[34], nbr_block, BLOCKSIZE);
            sha256(hashmix, cur_block, 42 * sizeof(uint32_t));
            counter += 1;
        }
    }

    /* Append last block on output */
    memcpy((char*)output, buffer + ((BUFLEN / 4) - 8), BLOCKSIZE);
}

void alx_free_balloon_buffer()
{
    free(buffer);
#ifndef __AES__
    EVP_CIPHER_CTX_free(aes_ctx);
#endif
}
void balloon(const char* input, char* output, unsigned int len) {
	balloon_hash((void*)input, (void*)output);
}

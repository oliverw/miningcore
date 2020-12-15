/*
 * This uses veriations of the clhash algorithm for Verus Coin, licensed
 * with the Apache-2.0 open source license.
 * 
 * Copyright (c) 2018 Michael Toutonghi
 * Distributed under the Apache 2.0 software license, available in the original form for clhash
 * here: https://github.com/lemire/clhash/commit/934da700a2a54d8202929a826e2763831bd43cf7#diff-9879d6db96fd29134fc802214163b95a
 * 
 * Original CLHash code and any portions herein, (C) 2017, 2018 Daniel Lemire and Owen Kaser
 * Faster 64-bit universal hashing
 * using carry-less multiplications, Journal of Cryptographic Engineering (to appear)
 *
 * Best used on recent x64 processors (Haswell or better).
 * 
 * This implements an intermediate step in the last part of a Verus block hash. The intent of this step
 * is to more effectively equalize FPGAs over GPUs and CPUs.
 *
 **/

#include "verus_hash.h"

#include <assert.h>
#include <string.h>

#ifdef _WIN32
#pragma warning (disable : 4146)
#include <intrin.h>
#endif
int __cpuverusoptimized = 0x80;

#if defined(__arm__)  || defined(__aarch64__)
#include "crypto/SSE2NEON.h"
#else
#include <x86intrin.h>
#endif

#ifdef _WIN32
#define posix_memalign(p, a, s) (((*(p)) = _aligned_malloc((s), (a))), *(p) ?0 :errno)
#endif

thread_local thread_specific_ptr verusclhasher_key;
thread_local thread_specific_ptr verusclhasher_descr;

#if defined(__APPLE__) || defined(_WIN32)
// attempt to workaround horrible mingw/gcc destructor bug on Windows and Mac, which passes garbage in the this pointer
// we use the opportunity of control here to clean up all of our tls variables. we could keep a list, but this is a safe,
// functional hack
thread_specific_ptr::~thread_specific_ptr() {
    if (verusclhasher_key.ptr)
    {
        verusclhasher_key.reset();
    }
    if (verusclhasher_descr.ptr)
    {
        verusclhasher_descr.reset();
    }
}
#endif // defined(__APPLE__) || defined(_WIN32)
#if defined(__arm__)  || defined(__aarch64__) //intrinsics not defined in SSE2NEON.h

static inline __attribute__((always_inline)) __m128i _mm_set_epi64x(uint64_t hi, uint64_t lo)
	{
	__m128i result;
	((uint64_t *)&result)[0] = lo;
	((uint64_t *)&result)[1] = hi;
	return result;
	}

static inline __attribute__((always_inline))  __m128i _mm_mulhrs_epi16(__m128i _a, __m128i _b)
{
	int16_t result[8];
	int16_t *a = (int16_t*)&_a, *b = (int16_t*)&_b;
	for (int i = 0; i < 8; i++)
	{
		result[i] = (int16_t)((((int32_t)(a[i]) * (int32_t)(b[i])) + 0x4000) >> 15);
	}

	return *(__m128i *)result;
}

__m128i _mm_cvtsi64_si128(uint64_t lo)
{
	__m128i result;
	((uint64_t *)&result)[0] = lo;
	((uint64_t *)&result)[1] = 0;
	return result;
}

 static inline __attribute__((always_inline)) uint8x16_t _mm_aesenc_si128 (uint8x16_t a, uint8x16_t RoundKey)
{
    return vaesmcq_u8(vaeseq_u8(a, (uint8x16_t){})) ^ RoundKey;
}


 static inline __attribute__((always_inline))  __m128i _mm_clmulepi64_si128(const __m128i a, const __m128i &b, int imm)
{
 return  (__m128i)vmull_p64(vgetq_lane_u64(a, 1), vgetq_lane_u64(b,0)); 

}

__m128i _mm_setr_epi8(u_char c0, u_char c1, u_char c2, u_char c3, u_char c4, u_char c5, u_char c6, u_char c7, u_char c8, u_char c9, u_char c10, u_char c11, u_char c12, u_char c13, u_char c14, u_char c15)
{
	__m128i result;
	((uint8_t *)&result)[0] = c0;
	((uint8_t *)&result)[1] = c1;
	((uint8_t *)&result)[2] = c2;
	((uint8_t *)&result)[3] = c3;
	((uint8_t *)&result)[4] = c4;
	((uint8_t *)&result)[5] = c5;
	((uint8_t *)&result)[6] = c6;
	((uint8_t *)&result)[7] = c7;
	((uint8_t *)&result)[8] = c8;
	((uint8_t *)&result)[9] = c9;
	((uint8_t *)&result)[10] = c10;
	((uint8_t *)&result)[11] = c11;
	((uint8_t *)&result)[12] = c12;
	((uint8_t *)&result)[13] = c13;
	((uint8_t *)&result)[14] = c14;
	((uint8_t *)&result)[15] = c15;
		return result;
}
__m128i _mm_shuffle_epi8(__m128i a, __m128i b)
{
	__m128i result;
	for (int i = 0; i < 16; i++)
	{
		if (((uint8_t *)&b)[i] & 0x80)
		{
			((uint8_t *)&result)[i] = 0;
		}
		else
		{
			((uint8_t *)&result)[i] = ((uint8_t *)&a)[((uint8_t *)&b)[i] & 0xf];
		}
	}
	return result;
}
 int64_t _mm_cvtsi128_si64(__m128i a)
{
	return ((int64_t *)&a)[0];
}
__m128i _mm_loadl_epi64(__m128i *a)
{
	__m128i b = {0}; ((uint64_t*)&b)[0] = ((uint64_t*)a)[0];
	return b;
}
#endif 

// multiply the length and the some key, no modulo
    static inline __attribute__((always_inline)) __m128i lazyLengthHash(uint64_t keylength, uint64_t length) {

    const __m128i lengthvector = _mm_set_epi64x(keylength,length);
    const __m128i clprod1 = _mm_clmulepi64_si128( lengthvector, lengthvector, 0x10);
    return clprod1;
}

// modulo reduction to 64-bit value. The high 64 bits contain garbage, see precompReduction64
  static inline __attribute__((always_inline)) __m128i precompReduction64_si128( __m128i A) {
    //const __m128i C = _mm_set_epi64x(1U,(1U<<4)+(1U<<3)+(1U<<1)+(1U<<0)); // C is the irreducible poly. (64,4,3,1,0)
    const __m128i C = _mm_cvtsi64_si128((1U<<4)+(1U<<3)+(1U<<1)+(1U<<0));
    __m128i Q2 = _mm_clmulepi64_si128( A, C, 0x01);
    __m128i Q3 = _mm_shuffle_epi8(_mm_setr_epi8(0, 27, 54, 45, 108, 119, 90, 65, (char)216, (char)195, (char)238, (char)245, (char)180, (char)175, (char)130, (char)153),
                                  _mm_srli_si128(Q2,8));
    __m128i Q4 = _mm_xor_si128(Q2,A);
    const __m128i final = _mm_xor_si128(Q3,Q4);
    return final;/// WARNING: HIGH 64 BITS CONTAIN GARBAGE
}

    static inline __attribute__((always_inline)) uint64_t precompReduction64( __m128i A) {
    return _mm_cvtsi128_si64(precompReduction64_si128(A));
}

    static inline __attribute__((always_inline)) void fixupkey(__m128i **pMoveScratch, verusclhash_descr *pdesc) {
    uint32_t ofs = pdesc->keySizeInBytes >> 4;
    for (__m128i *pfixup = *pMoveScratch; pfixup; pfixup = *++pMoveScratch)
    {
        const __m128i fixup = _mm_load_si128((__m128i *)(pfixup + ofs));
        _mm_store_si128((__m128i *)pfixup, fixup);
    }
}

    static inline __attribute__((always_inline)) void haraka512_keyed_local(unsigned char *out, const unsigned char *in, const u128 *rc) {
  u128 s[4], tmp;

  s[0] = LOAD(in);
  s[1] = LOAD(in + 16);
  s[2] = LOAD(in + 32);
  s[3] = LOAD(in + 48);

  AES4(s[0], s[1], s[2], s[3], 0);
  MIX4(s[0], s[1], s[2], s[3]);

  AES4(s[0], s[1], s[2], s[3], 8);
  MIX4(s[0], s[1], s[2], s[3]);

  AES4(s[0], s[1], s[2], s[3], 16);
  MIX4(s[0], s[1], s[2], s[3]);

  AES4(s[0], s[1], s[2], s[3], 24);
  MIX4(s[0], s[1], s[2], s[3]);

  AES4(s[0], s[1], s[2], s[3], 32);
  MIX4(s[0], s[1], s[2], s[3]);

  s[0] = _mm_xor_si128(s[0], LOAD(in));
  s[1] = _mm_xor_si128(s[1], LOAD(in + 16));
  s[2] = _mm_xor_si128(s[2], LOAD(in + 32));
  s[3] = _mm_xor_si128(s[3], LOAD(in + 48));

  TRUNCSTORE(out, s[0], s[1], s[2], s[3]);
}

// verus intermediate hash extra
__m128i __verusclmulwithoutreduction64alignedrepeat(__m128i *randomsource, const __m128i buf[4], uint64_t keyMask, __m128i **pMoveScratch)
{
    __m128i const *pbuf;

    // divide key mask by 16 from bytes to __m128i
    keyMask >>= 4;

    // the random buffer must have at least 32 16 byte dwords after the keymask to work with this
    // algorithm. we take the value from the last element inside the keyMask + 2, as that will never
    // be used to xor into the accumulator before it is hashed with other values first
    __m128i acc = _mm_load_si128(randomsource + (keyMask + 2));

    for (int64_t i = 0; i < 32; i++)
    {
        const uint64_t selector = _mm_cvtsi128_si64(acc);

        // get two random locations in the key, which will be mutated and swapped
        __m128i *prand = randomsource + ((selector >> 5) & keyMask);
        __m128i *prandex = randomsource + ((selector >> 32) & keyMask);

        *(pMoveScratch++) = prand;
        *(pMoveScratch++) = prandex;        

        // select random start and order of pbuf processing
        pbuf = buf + (selector & 3);

        switch (selector & 0x1c)
        {
            case 0:
            {
                const __m128i temp1 = _mm_load_si128(prandex);
                const __m128i temp2 = _mm_load_si128(pbuf - (((selector & 1) << 1) - 1));
                const __m128i add1 = _mm_xor_si128(temp1, temp2);
                const __m128i clprod1 = _mm_clmulepi64_si128(add1, add1, 0x10);
                acc = _mm_xor_si128(clprod1, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16(acc, temp1);
                const __m128i tempa2 = _mm_xor_si128(tempa1, temp1);

                const __m128i temp12 = _mm_load_si128(prand);
                _mm_store_si128(prand, tempa2);

                const __m128i temp22 = _mm_load_si128(pbuf);
                const __m128i add12 = _mm_xor_si128(temp12, temp22);
                const __m128i clprod12 = _mm_clmulepi64_si128(add12, add12, 0x10);
                acc = _mm_xor_si128(clprod12, acc);

                const __m128i tempb1 = _mm_mulhrs_epi16(acc, temp12);
                const __m128i tempb2 = _mm_xor_si128(tempb1, temp12);
                _mm_store_si128(prandex, tempb2);
                break;
            }
            case 4:
            {
                const __m128i temp1 = _mm_load_si128(prand);
                const __m128i temp2 = _mm_load_si128(pbuf);
                const __m128i add1 = _mm_xor_si128(temp1, temp2);
                const __m128i clprod1 = _mm_clmulepi64_si128(add1, add1, 0x10);
                acc = _mm_xor_si128(clprod1, acc);
                const __m128i clprod2 = _mm_clmulepi64_si128(temp2, temp2, 0x10);
                acc = _mm_xor_si128(clprod2, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16(acc, temp1);
                const __m128i tempa2 = _mm_xor_si128(tempa1, temp1);

                const __m128i temp12 = _mm_load_si128(prandex);
                _mm_store_si128(prandex, tempa2);

                const __m128i temp22 = _mm_load_si128(pbuf - (((selector & 1) << 1) - 1));
                const __m128i add12 = _mm_xor_si128(temp12, temp22);
                acc = _mm_xor_si128(add12, acc);

                const __m128i tempb1 = _mm_mulhrs_epi16(acc, temp12);
                const __m128i tempb2 = _mm_xor_si128(tempb1, temp12);
                _mm_store_si128(prand, tempb2);
                break;
            }
            case 8:
            {
                const __m128i temp1 = _mm_load_si128(prandex);
                const __m128i temp2 = _mm_load_si128(pbuf);
                const __m128i add1 = _mm_xor_si128(temp1, temp2);
                acc = _mm_xor_si128(add1, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16(acc, temp1);
                const __m128i tempa2 = _mm_xor_si128(tempa1, temp1);

                const __m128i temp12 = _mm_load_si128(prand);
                _mm_store_si128(prand, tempa2);

                const __m128i temp22 = _mm_load_si128(pbuf - (((selector & 1) << 1) - 1));
                const __m128i add12 = _mm_xor_si128(temp12, temp22);
                const __m128i clprod12 = _mm_clmulepi64_si128(add12, add12, 0x10);
                acc = _mm_xor_si128(clprod12, acc);
                const __m128i clprod22 = _mm_clmulepi64_si128(temp22, temp22, 0x10);
                acc = _mm_xor_si128(clprod22, acc);

                const __m128i tempb1 = _mm_mulhrs_epi16(acc, temp12);
                const __m128i tempb2 = _mm_xor_si128(tempb1, temp12);
                _mm_store_si128(prandex, tempb2);
                break;
            }
            case 0xc:
            {
                const __m128i temp1 = _mm_load_si128(prand);
                const __m128i temp2 = _mm_load_si128(pbuf - (((selector & 1) << 1) - 1));
                const __m128i add1 = _mm_xor_si128(temp1, temp2);

                // cannot be zero here
                const int32_t divisor = (uint32_t)selector;

                acc = _mm_xor_si128(add1, acc);

                const int64_t dividend = _mm_cvtsi128_si64(acc);
                const __m128i modulo = _mm_cvtsi32_si128(dividend % divisor);
                acc = _mm_xor_si128(modulo, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16(acc, temp1);
                const __m128i tempa2 = _mm_xor_si128(tempa1, temp1);

                if (dividend & 1)
                {
                    const __m128i temp12 = _mm_load_si128(prandex);
                    _mm_store_si128(prandex, tempa2);

                    const __m128i temp22 = _mm_load_si128(pbuf);
                    const __m128i add12 = _mm_xor_si128(temp12, temp22);
                    const __m128i clprod12 = _mm_clmulepi64_si128(add12, add12, 0x10);
                    acc = _mm_xor_si128(clprod12, acc);
                    const __m128i clprod22 = _mm_clmulepi64_si128(temp22, temp22, 0x10);
                    acc = _mm_xor_si128(clprod22, acc);

                    const __m128i tempb1 = _mm_mulhrs_epi16(acc, temp12);
                    const __m128i tempb2 = _mm_xor_si128(tempb1, temp12);
                    _mm_store_si128(prand, tempb2);
                }
                else
                {
                    const __m128i tempb3 = _mm_load_si128(prandex);
                    _mm_store_si128(prandex, tempa2);
                    _mm_store_si128(prand, tempb3);
                }
                break;
            }
            case 0x10:
            {
                // a few AES operations
                const __m128i *rc = prand;
                __m128i tmp;

                __m128i temp1 = _mm_load_si128(pbuf - (((selector & 1) << 1) - 1));
                __m128i temp2 = _mm_load_si128(pbuf);

                AES2(temp1, temp2, 0);
                MIX2(temp1, temp2);

                AES2(temp1, temp2, 4);
                MIX2(temp1, temp2);

                AES2(temp1, temp2, 8);
                MIX2(temp1, temp2);

                acc = _mm_xor_si128(temp2, _mm_xor_si128(temp1, acc));

                const __m128i tempa1 = _mm_load_si128(prand);
                const __m128i tempa2 = _mm_mulhrs_epi16(acc, tempa1);
                const __m128i tempa3 = _mm_xor_si128(tempa1, tempa2);

                const __m128i tempa4 = _mm_load_si128(prandex);
                _mm_store_si128(prandex, tempa3);
                _mm_store_si128(prand, tempa4);
                break;
            }
            case 0x14:
            {
                // we'll just call this one the monkins loop, inspired by Chris
                const __m128i *buftmp = pbuf - (((selector & 1) << 1) - 1);
                __m128i tmp; // used by MIX2

                uint64_t rounds = selector >> 61; // loop randomly between 1 and 8 times
                __m128i *rc = prand;
                uint64_t aesroundoffset = 0;
                __m128i onekey;

                do
                {
                    if (selector & (0x10000000 << rounds))
                    {
                        onekey = _mm_load_si128(rc++);
                        const __m128i temp2 = _mm_load_si128(rounds & 1 ? pbuf : buftmp);
                        const __m128i add1 = _mm_xor_si128(onekey, temp2);
                        const __m128i clprod1 = _mm_clmulepi64_si128(add1, add1, 0x10);
                        acc = _mm_xor_si128(clprod1, acc);
                    }
                    else
                    {
                        onekey = _mm_load_si128(rc++);
                        __m128i temp2 = _mm_load_si128(rounds & 1 ? buftmp : pbuf);
                        AES2(onekey, temp2, aesroundoffset);
                        aesroundoffset += 4;
                        MIX2(onekey, temp2);
                        acc = _mm_xor_si128(onekey, acc);
                        acc = _mm_xor_si128(temp2, acc);
                    }
                } while (rounds--);

                const __m128i tempa1 = _mm_load_si128(prand);
                const __m128i tempa2 = _mm_mulhrs_epi16(acc, tempa1);
                const __m128i tempa3 = _mm_xor_si128(tempa1, tempa2);

                const __m128i tempa4 = _mm_load_si128(prandex);
                _mm_store_si128(prandex, tempa3);
                _mm_store_si128(prand, tempa4);
                break;
            }
            case 0x18:
            {
                const __m128i temp1 = _mm_load_si128(pbuf - (((selector & 1) << 1) - 1));
                const __m128i temp2 = _mm_load_si128(prand);
                const __m128i add1 = _mm_xor_si128(temp1, temp2);
                const __m128i clprod1 = _mm_clmulepi64_si128(add1, add1, 0x10);
                acc = _mm_xor_si128(clprod1, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16(acc, temp2);
                const __m128i tempa2 = _mm_xor_si128(tempa1, temp2);

                const __m128i tempb3 = _mm_load_si128(prandex);
                _mm_store_si128(prandex, tempa2);
                _mm_store_si128(prand, tempb3);
                break;
            }
            case 0x1c:
            {
                const __m128i temp1 = _mm_load_si128(pbuf);
                const __m128i temp2 = _mm_load_si128(prandex);
                const __m128i add1 = _mm_xor_si128(temp1, temp2);
                const __m128i clprod1 = _mm_clmulepi64_si128(add1, add1, 0x10);
                acc = _mm_xor_si128(clprod1, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16(acc, temp2);
                const __m128i tempa2 = _mm_xor_si128(tempa1, temp2);

                const __m128i tempa3 = _mm_load_si128(prand);
                _mm_store_si128(prand, tempa2);

                acc = _mm_xor_si128(tempa3, acc);

                const __m128i tempb1 = _mm_mulhrs_epi16(acc, tempa3);
                const __m128i tempb2 = _mm_xor_si128(tempb1, tempa3);
                _mm_store_si128(prandex, tempb2);
                break;
            }
        }
    }
    return acc;
}

// hashes 64 bytes only by doing a carryless multiplication and reduction of the repeated 64 byte sequence 16 times, 
// returning a 64 bit hash value
uint64_t verusclhash(void * random, const unsigned char buf[64], uint64_t keyMask, __m128i **pMoveScratch) {
    __m128i  acc = __verusclmulwithoutreduction64alignedrepeat((__m128i *)random, (const __m128i *)buf, keyMask, pMoveScratch);
    acc = _mm_xor_si128(acc, lazyLengthHash(1024, 64));
    return precompReduction64(acc);
}

// hashes 64 bytes only by doing a carryless multiplication and reduction of the repeated 64 byte sequence 16 times, 
// returning a 64 bit hash value
uint64_t verusclhash_sv2_1(void * random, const unsigned char buf[64], uint64_t keyMask, __m128i **pMoveScratch) {
    __m128i acc = __verusclmulwithoutreduction64alignedrepeat_sv2_1((__m128i *)random, (const __m128i *)buf, keyMask, pMoveScratch);
    acc = _mm_xor_si128(acc, lazyLengthHash(1024, 64));
    return precompReduction64(acc);
}

uint64_t verusclhash_sv2_2(void * random, const unsigned char buf[64], uint64_t keyMask, __m128i **pMoveScratch) {
    __m128i acc = __verusclmulwithoutreduction64alignedrepeat_sv2_2((__m128i *)random, (const __m128i *)buf, keyMask, pMoveScratch);
    acc = _mm_xor_si128(acc, lazyLengthHash(1024, 64));
    return precompReduction64(acc);
}




__m128i __verusclmulwithoutreduction64alignedrepeat_sv2_1(__m128i *randomsource, const __m128i buf[4], uint64_t keyMask, __m128i **pMoveScratch)
{
    const __m128i pbuf_copy[4] = {_mm_xor_si128(buf[0], buf[2]), _mm_xor_si128(buf[1], buf[3]), buf[2], buf[3]};
    const __m128i *pbuf; 

    // divide key mask by 16 from bytes to __m128i
    keyMask >>= 4;

    // the random buffer must have at least 32 16 byte dwords after the keymask to work with this
    // algorithm. we take the value from the last element inside the keyMask + 2, as that will never
    // be used to xor into the accumulator before it is hashed with other values first
    __m128i acc = _mm_load_si128(randomsource + (keyMask + 2));

    for (int64_t i = 0; i < 32; i++)
    {
        const uint64_t selector = _mm_cvtsi128_si64(acc);

        // get two random locations in the key, which will be mutated and swapped
        __m128i *prand = randomsource + ((selector >> 5) & keyMask);
        __m128i *prandex = randomsource + ((selector >> 32) & keyMask);

        *(pMoveScratch++) = prand;
        *(pMoveScratch++) = prandex;        

        // select random start and order of pbuf processing
        pbuf = pbuf_copy + (selector & 3);

        switch (selector & 0x1c)
        {
            case 0:
            {
                const __m128i temp1 = _mm_load_si128(prandex);
                const __m128i temp2 = _mm_load_si128(pbuf - (((selector & 1) << 1) - 1));
                const __m128i add1 = _mm_xor_si128(temp1, temp2);
                const __m128i clprod1 = _mm_clmulepi64_si128(add1, add1, 0x10);
                acc = _mm_xor_si128(clprod1, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16(acc, temp1);
                const __m128i tempa2 = _mm_xor_si128(tempa1, temp1);

                const __m128i temp12 = _mm_load_si128(prand);
                _mm_store_si128(prand, tempa2);

                const __m128i temp22 = _mm_load_si128(pbuf);
                const __m128i add12 = _mm_xor_si128(temp12, temp22);
                const __m128i clprod12 = _mm_clmulepi64_si128(add12, add12, 0x10);
                acc = _mm_xor_si128(clprod12, acc);

                const __m128i tempb1 = _mm_mulhrs_epi16(acc, temp12);
                const __m128i tempb2 = _mm_xor_si128(tempb1, temp12);
                _mm_store_si128(prandex, tempb2);
                break;
            }
            case 4:
            {
                const __m128i temp1 = _mm_load_si128(prand);
                const __m128i temp2 = _mm_load_si128(pbuf);
                const __m128i add1 = _mm_xor_si128(temp1, temp2);
                const __m128i clprod1 = _mm_clmulepi64_si128(add1, add1, 0x10);
                acc = _mm_xor_si128(clprod1, acc);
                const __m128i clprod2 = _mm_clmulepi64_si128(temp2, temp2, 0x10);
                acc = _mm_xor_si128(clprod2, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16(acc, temp1);
                const __m128i tempa2 = _mm_xor_si128(tempa1, temp1);

                const __m128i temp12 = _mm_load_si128(prandex);
                _mm_store_si128(prandex, tempa2);

                const __m128i temp22 = _mm_load_si128(pbuf - (((selector & 1) << 1) - 1));
                const __m128i add12 = _mm_xor_si128(temp12, temp22);
                acc = _mm_xor_si128(add12, acc);

                const __m128i tempb1 = _mm_mulhrs_epi16(acc, temp12);
                const __m128i tempb2 = _mm_xor_si128(tempb1, temp12);
                _mm_store_si128(prand, tempb2);
                break;
            }
            case 8:
            {
                const __m128i temp1 = _mm_load_si128(prandex);
                const __m128i temp2 = _mm_load_si128(pbuf);
                const __m128i add1 = _mm_xor_si128(temp1, temp2);
                acc = _mm_xor_si128(add1, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16(acc, temp1);
                const __m128i tempa2 = _mm_xor_si128(tempa1, temp1);

                const __m128i temp12 = _mm_load_si128(prand);
                _mm_store_si128(prand, tempa2);

                const __m128i temp22 = _mm_load_si128(pbuf - (((selector & 1) << 1) - 1));
                const __m128i add12 = _mm_xor_si128(temp12, temp22);
                const __m128i clprod12 = _mm_clmulepi64_si128(add12, add12, 0x10);
                acc = _mm_xor_si128(clprod12, acc);
                const __m128i clprod22 = _mm_clmulepi64_si128(temp22, temp22, 0x10);
                acc = _mm_xor_si128(clprod22, acc);

                const __m128i tempb1 = _mm_mulhrs_epi16(acc, temp12);
                const __m128i tempb2 = _mm_xor_si128(tempb1, temp12);
                _mm_store_si128(prandex, tempb2);
                break;
            }
            case 0xc:
            {
                const __m128i temp1 = _mm_load_si128(prand);
                const __m128i temp2 = _mm_load_si128(pbuf - (((selector & 1) << 1) - 1));
                const __m128i add1 = _mm_xor_si128(temp1, temp2);

                // cannot be zero here
                const int32_t divisor = (uint32_t)selector;

                acc = _mm_xor_si128(add1, acc);

                const int64_t dividend = _mm_cvtsi128_si64(acc);
                const __m128i modulo = _mm_cvtsi32_si128(dividend % divisor);
                acc = _mm_xor_si128(modulo, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16(acc, temp1);
                const __m128i tempa2 = _mm_xor_si128(tempa1, temp1);

                if (dividend & 1)
                {
                    const __m128i temp12 = _mm_load_si128(prandex);
                    _mm_store_si128(prandex, tempa2);

                    const __m128i temp22 = _mm_load_si128(pbuf);
                    const __m128i add12 = _mm_xor_si128(temp12, temp22);
                    const __m128i clprod12 = _mm_clmulepi64_si128(add12, add12, 0x10);
                    acc = _mm_xor_si128(clprod12, acc);
                    const __m128i clprod22 = _mm_clmulepi64_si128(temp22, temp22, 0x10);
                    acc = _mm_xor_si128(clprod22, acc);

                    const __m128i tempb1 = _mm_mulhrs_epi16(acc, temp12);
                    const __m128i tempb2 = _mm_xor_si128(tempb1, temp12);
                    _mm_store_si128(prand, tempb2);
                }
                else
                {
                    const __m128i tempb3 = _mm_load_si128(prandex);
                    _mm_store_si128(prandex, tempa2);
                    _mm_store_si128(prand, tempb3);
                }
                break;
            }
            case 0x10:
            {
                // a few AES operations
                const __m128i *rc = prand;
                __m128i tmp;

                __m128i temp1 = _mm_load_si128(pbuf - (((selector & 1) << 1) - 1));
                __m128i temp2 = _mm_load_si128(pbuf);

                AES2(temp1, temp2, 0);
                MIX2(temp1, temp2);

                AES2(temp1, temp2, 4);
                MIX2(temp1, temp2);

                AES2(temp1, temp2, 8);
                MIX2(temp1, temp2);

                acc = _mm_xor_si128(temp2, _mm_xor_si128(temp1, acc));

                const __m128i tempa1 = _mm_load_si128(prand);
                const __m128i tempa2 = _mm_mulhrs_epi16(acc, tempa1);
                const __m128i tempa3 = _mm_xor_si128(tempa1, tempa2);

                const __m128i tempa4 = _mm_load_si128(prandex);
                _mm_store_si128(prandex, tempa3);
                _mm_store_si128(prand, tempa4);
                break;
            }
            case 0x14:
            {
                // we'll just call this one the monkins loop, inspired by Chris - modified to cast to uint64_t on shift for more variability in the loop
                const __m128i *buftmp = pbuf - (((selector & 1) << 1) - 1);
                __m128i tmp; // used by MIX2

                uint64_t rounds = selector >> 61; // loop randomly between 1 and 8 times
                __m128i *rc = prand;
                uint64_t aesroundoffset = 0;
                __m128i onekey;

                do
                {
                    if (selector & (((uint64_t)0x10000000) << rounds))
                    {
                        onekey = _mm_load_si128(rc++);
                        const __m128i temp2 = _mm_load_si128(rounds & 1 ? pbuf : buftmp);
                        const __m128i add1 = _mm_xor_si128(onekey, temp2);
                        const __m128i clprod1 = _mm_clmulepi64_si128(add1, add1, 0x10);
                        acc = _mm_xor_si128(clprod1, acc);
                    }
                    else
                    {
                        onekey = _mm_load_si128(rc++);
                        __m128i temp2 = _mm_load_si128(rounds & 1 ? buftmp : pbuf);
                        AES2(onekey, temp2, aesroundoffset);
                        aesroundoffset += 4;
                        MIX2(onekey, temp2);
                        acc = _mm_xor_si128(onekey, acc);
                        acc = _mm_xor_si128(temp2, acc);
                    }
                } while (rounds--);

                const __m128i tempa1 = _mm_load_si128(prand);
                const __m128i tempa2 = _mm_mulhrs_epi16(acc, tempa1);
                const __m128i tempa3 = _mm_xor_si128(tempa1, tempa2);

                const __m128i tempa4 = _mm_load_si128(prandex);
                _mm_store_si128(prandex, tempa3);
                _mm_store_si128(prand, tempa4);
                break;
            }
            case 0x18:
            {
                const __m128i *buftmp = pbuf - (((selector & 1) << 1) - 1);
                __m128i tmp; // used by MIX2

                uint64_t rounds = selector >> 61; // loop randomly between 1 and 8 times
                __m128i *rc = prand;
                uint64_t aesroundoffset = 0;
                __m128i onekey;

                do
                {
                    if (selector & (((uint64_t)0x10000000) << rounds))
                    {
                        onekey = _mm_load_si128(rc++);
                        const __m128i temp2 = _mm_load_si128(rounds & 1 ? pbuf : buftmp);
                        const __m128i add1 = _mm_xor_si128(onekey, temp2);
                        // cannot be zero here, may be negative
                        const int32_t divisor = (uint32_t)selector;
                        const int64_t dividend = _mm_cvtsi128_si64(add1);
                        const __m128i modulo = _mm_cvtsi32_si128(dividend % divisor);
                        acc = _mm_xor_si128(modulo, acc);
                    }
                    else
                    {
                        onekey = _mm_load_si128(rc++);
                        __m128i temp2 = _mm_load_si128(rounds & 1 ? buftmp : pbuf);
                        const __m128i add1 = _mm_xor_si128(onekey, temp2);
                        const __m128i clprod1 = _mm_clmulepi64_si128(add1, add1, 0x10);
                        const __m128i clprod2 = _mm_mulhrs_epi16(acc, clprod1);
                        acc = _mm_xor_si128(clprod2, acc);
                    }
                } while (rounds--);

                const __m128i tempa3 = _mm_load_si128(prandex);
                const __m128i tempa4 = _mm_xor_si128(tempa3, acc);
                _mm_store_si128(prandex, tempa4);
                _mm_store_si128(prand, onekey);
                break;
            }
            case 0x1c:
            {
                const __m128i temp1 = _mm_load_si128(pbuf);
                const __m128i temp2 = _mm_load_si128(prandex);
                const __m128i add1 = _mm_xor_si128(temp1, temp2);
                const __m128i clprod1 = _mm_clmulepi64_si128(add1, add1, 0x10);
                acc = _mm_xor_si128(clprod1, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16(acc, temp2);
                const __m128i tempa2 = _mm_xor_si128(tempa1, temp2);

                const __m128i tempa3 = _mm_load_si128(prand);
                _mm_store_si128(prand, tempa2);

                acc = _mm_xor_si128(tempa3, acc);

                const __m128i tempb1 = _mm_mulhrs_epi16(acc, tempa3);
                const __m128i tempb2 = _mm_xor_si128(tempb1, tempa3);
                _mm_store_si128(prandex, tempb2);
                break;
            }
        }
    }
    return acc;
}

__m128i __verusclmulwithoutreduction64alignedrepeat_sv2_2(__m128i *randomsource, const __m128i buf[4], uint64_t keyMask, __m128i **pMoveScratch)
{
    const __m128i pbuf_copy[4] = {_mm_xor_si128(buf[0], buf[2]), _mm_xor_si128(buf[1], buf[3]), buf[2], buf[3]};
    const __m128i *pbuf;

    // divide key mask by 16 from bytes to __m128i
    keyMask >>= 4;

    // the random buffer must have at least 32 16 byte dwords after the keymask to work with this
    // algorithm. we take the value from the last element inside the keyMask + 2, as that will never
    // be used to xor into the accumulator before it is hashed with other values first
    __m128i acc = _mm_load_si128(randomsource + (keyMask + 2));

    for (int64_t i = 0; i < 32; i++)
    {
        const uint64_t selector = _mm_cvtsi128_si64(acc);

        // get two random locations in the key, which will be mutated and swapped
        __m128i *prand = randomsource + ((selector >> 5) & keyMask);
        __m128i *prandex = randomsource + ((selector >> 32) & keyMask);

        *(pMoveScratch++) = prand;
        *(pMoveScratch++) = prandex;        

        // select random start and order of pbuf processing
        pbuf = pbuf_copy + (selector & 3);

        switch (selector & 0x1c)
        {
            case 0:
            {
                const __m128i temp1 = _mm_load_si128(prandex);
                const __m128i temp2 = _mm_load_si128(pbuf - (((selector & 1) << 1) - 1));
                const __m128i add1 = _mm_xor_si128(temp1, temp2);
                const __m128i clprod1 = _mm_clmulepi64_si128(add1, add1, 0x10);
                acc = _mm_xor_si128(clprod1, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16(acc, temp1);
                const __m128i tempa2 = _mm_xor_si128(tempa1, temp1);

                const __m128i temp12 = _mm_load_si128(prand);
                _mm_store_si128(prand, tempa2);

                const __m128i temp22 = _mm_load_si128(pbuf);
                const __m128i add12 = _mm_xor_si128(temp12, temp22);
                const __m128i clprod12 = _mm_clmulepi64_si128(add12, add12, 0x10);
                acc = _mm_xor_si128(clprod12, acc);

                const __m128i tempb1 = _mm_mulhrs_epi16(acc, temp12);
                const __m128i tempb2 = _mm_xor_si128(tempb1, temp12);
                _mm_store_si128(prandex, tempb2);
                break;
            }
            case 4:
            {
                const __m128i temp1 = _mm_load_si128(prand);
                const __m128i temp2 = _mm_load_si128(pbuf);
                const __m128i add1 = _mm_xor_si128(temp1, temp2);
                const __m128i clprod1 = _mm_clmulepi64_si128(add1, add1, 0x10);
                acc = _mm_xor_si128(clprod1, acc);
                const __m128i clprod2 = _mm_clmulepi64_si128(temp2, temp2, 0x10);
                acc = _mm_xor_si128(clprod2, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16(acc, temp1);
                const __m128i tempa2 = _mm_xor_si128(tempa1, temp1);

                const __m128i temp12 = _mm_load_si128(prandex);
                _mm_store_si128(prandex, tempa2);

                const __m128i temp22 = _mm_load_si128(pbuf - (((selector & 1) << 1) - 1));
                const __m128i add12 = _mm_xor_si128(temp12, temp22);
                acc = _mm_xor_si128(add12, acc);

                const __m128i tempb1 = _mm_mulhrs_epi16(acc, temp12);
                const __m128i tempb2 = _mm_xor_si128(tempb1, temp12);
                _mm_store_si128(prand, tempb2);
                break;
            }
            case 8:
            {
                const __m128i temp1 = _mm_load_si128(prandex);
                const __m128i temp2 = _mm_load_si128(pbuf);
                const __m128i add1 = _mm_xor_si128(temp1, temp2);
                acc = _mm_xor_si128(add1, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16(acc, temp1);
                const __m128i tempa2 = _mm_xor_si128(tempa1, temp1);

                const __m128i temp12 = _mm_load_si128(prand);
                _mm_store_si128(prand, tempa2);

                const __m128i temp22 = _mm_load_si128(pbuf - (((selector & 1) << 1) - 1));
                const __m128i add12 = _mm_xor_si128(temp12, temp22);
                const __m128i clprod12 = _mm_clmulepi64_si128(add12, add12, 0x10);
                acc = _mm_xor_si128(clprod12, acc);
                const __m128i clprod22 = _mm_clmulepi64_si128(temp22, temp22, 0x10);
                acc = _mm_xor_si128(clprod22, acc);

                const __m128i tempb1 = _mm_mulhrs_epi16(acc, temp12);
                const __m128i tempb2 = _mm_xor_si128(tempb1, temp12);
                _mm_store_si128(prandex, tempb2);
                break;
            }
            case 0xc:
            {
                const __m128i temp1 = _mm_load_si128(prand);
                const __m128i temp2 = _mm_load_si128(pbuf - (((selector & 1) << 1) - 1));
                const __m128i add1 = _mm_xor_si128(temp1, temp2);

                // cannot be zero here
                const int32_t divisor = (uint32_t)selector;

                acc = _mm_xor_si128(add1, acc);

                const int64_t dividend = _mm_cvtsi128_si64(acc);
                const __m128i modulo = _mm_cvtsi32_si128(dividend % divisor);
                acc = _mm_xor_si128(modulo, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16(acc, temp1);
                const __m128i tempa2 = _mm_xor_si128(tempa1, temp1);

                if (dividend & 1)
                {
                    const __m128i temp12 = _mm_load_si128(prandex);
                    _mm_store_si128(prandex, tempa2);

                    const __m128i temp22 = _mm_load_si128(pbuf);
                    const __m128i add12 = _mm_xor_si128(temp12, temp22);
                    const __m128i clprod12 = _mm_clmulepi64_si128(add12, add12, 0x10);
                    acc = _mm_xor_si128(clprod12, acc);
                    const __m128i clprod22 = _mm_clmulepi64_si128(temp22, temp22, 0x10);
                    acc = _mm_xor_si128(clprod22, acc);

                    const __m128i tempb1 = _mm_mulhrs_epi16(acc, temp12);
                    const __m128i tempb2 = _mm_xor_si128(tempb1, temp12);
                    _mm_store_si128(prand, tempb2);
                }
                else
                {
                    const __m128i tempb3 = _mm_load_si128(prandex);
                    _mm_store_si128(prandex, tempa2);
                    _mm_store_si128(prand, tempb3);
                    const __m128i tempb4 = _mm_load_si128(pbuf);
                    acc = _mm_xor_si128(tempb4, acc);
                }
                break;
            }
            case 0x10:
            {
                // a few AES operations
                const __m128i *rc = prand;
                __m128i tmp;

                __m128i temp1 = _mm_load_si128(pbuf - (((selector & 1) << 1) - 1));
                __m128i temp2 = _mm_load_si128(pbuf);

                AES2(temp1, temp2, 0);
                MIX2(temp1, temp2);

                AES2(temp1, temp2, 4);
                MIX2(temp1, temp2);

                AES2(temp1, temp2, 8);
                MIX2(temp1, temp2);

                acc = _mm_xor_si128(temp2, _mm_xor_si128(temp1, acc));

                const __m128i tempa1 = _mm_load_si128(prand);
                const __m128i tempa2 = _mm_mulhrs_epi16(acc, tempa1);
                const __m128i tempa3 = _mm_xor_si128(tempa1, tempa2);

                const __m128i tempa4 = _mm_load_si128(prandex);
                _mm_store_si128(prandex, tempa3);
                _mm_store_si128(prand, tempa4);
                break;
            }
            case 0x14:
            {
                // we'll just call this one the monkins loop, inspired by Chris - modified to cast to uint64_t on shift for more variability in the loop
                const __m128i *buftmp = pbuf - (((selector & 1) << 1) - 1);
                __m128i tmp; // used by MIX2

                uint64_t rounds = selector >> 61; // loop randomly between 1 and 8 times
                __m128i *rc = prand;
                uint64_t aesroundoffset = 0;
                __m128i onekey;

                do
                {
                    if (selector & (((uint64_t)0x10000000) << rounds))
                    {
                        onekey = _mm_load_si128(rc++);
                        const __m128i temp2 = _mm_load_si128(rounds & 1 ? pbuf : buftmp);
                        const __m128i add1 = _mm_xor_si128(onekey, temp2);
                        const __m128i clprod1 = _mm_clmulepi64_si128(add1, add1, 0x10);
                        acc = _mm_xor_si128(clprod1, acc);
                    }
                    else
                    {
                        onekey = _mm_load_si128(rc++);
                        __m128i temp2 = _mm_load_si128(rounds & 1 ? buftmp : pbuf);
                        AES2(onekey, temp2, aesroundoffset);
                        aesroundoffset += 4;
                        MIX2(onekey, temp2);
                        acc = _mm_xor_si128(onekey, acc);
                        acc = _mm_xor_si128(temp2, acc);
                    }
                } while (rounds--);

                const __m128i tempa1 = _mm_load_si128(prand);
                const __m128i tempa2 = _mm_mulhrs_epi16(acc, tempa1);
                const __m128i tempa3 = _mm_xor_si128(tempa1, tempa2);

                const __m128i tempa4 = _mm_load_si128(prandex);
                _mm_store_si128(prandex, tempa3);
                _mm_store_si128(prand, tempa4);
                break;
            }
            case 0x18:
            {
                const __m128i *buftmp = pbuf - (((selector & 1) << 1) - 1);
                __m128i tmp; // used by MIX2

                uint64_t rounds = selector >> 61; // loop randomly between 1 and 8 times
                __m128i *rc = prand;
                __m128i onekey;

                do
                {
                    if (selector & (((uint64_t)0x10000000) << rounds))
                    {
                        onekey = _mm_load_si128(rc++);
                        const __m128i temp2 = _mm_load_si128(rounds & 1 ? pbuf : buftmp);
                        onekey = _mm_xor_si128(onekey, temp2);
                        // cannot be zero here, may be negative
                        const int32_t divisor = (uint32_t)selector;
                        const int64_t dividend = _mm_cvtsi128_si64(onekey);
                        const __m128i modulo = _mm_cvtsi32_si128(dividend % divisor);
                        acc = _mm_xor_si128(modulo, acc);
                    }
                    else
                    {
                        onekey = _mm_load_si128(rc++);
                        __m128i temp2 = _mm_load_si128(rounds & 1 ? buftmp : pbuf);
                        const __m128i add1 = _mm_xor_si128(onekey, temp2);
                        onekey = _mm_clmulepi64_si128(add1, add1, 0x10);
                        const __m128i clprod2 = _mm_mulhrs_epi16(acc, onekey);
                        acc = _mm_xor_si128(clprod2, acc);
                    }
                } while (rounds--);

                const __m128i tempa3 = _mm_load_si128(prandex);
                const __m128i tempa4 = _mm_xor_si128(tempa3, acc);

                _mm_store_si128(prandex, onekey);
                _mm_store_si128(prand, tempa4);
                break;
            }
            case 0x1c:
            {
                const __m128i temp1 = _mm_load_si128(pbuf);
                const __m128i temp2 = _mm_load_si128(prandex);
                const __m128i add1 = _mm_xor_si128(temp1, temp2);
                const __m128i clprod1 = _mm_clmulepi64_si128(add1, add1, 0x10);
                acc = _mm_xor_si128(clprod1, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16(acc, temp2);
                const __m128i tempa2 = _mm_xor_si128(tempa1, temp2);

                const __m128i tempa3 = _mm_load_si128(prand);
                _mm_store_si128(prand, tempa2);

                acc = _mm_xor_si128(tempa3, acc);
                const __m128i temp4 = _mm_load_si128(pbuf - (((selector & 1) << 1) - 1)); 
                acc = _mm_xor_si128(temp4,acc);  
                const __m128i tempb1 = _mm_mulhrs_epi16(acc, tempa3);
                const __m128i tempb2 = _mm_xor_si128(tempb1, tempa3);
                _mm_store_si128(prandex, tempb2);
                break;
            }
        }
    }
    return acc;
}

void *alloc_aligned_buffer(uint64_t bufSize)
{
    void *answer = NULL;
    if (posix_memalign(&answer, sizeof(__m128i)*2, bufSize))
    {
        return NULL;
    }
    else
    {
        return answer;
    }
}

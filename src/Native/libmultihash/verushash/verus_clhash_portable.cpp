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

#ifdef __APPLE__
#include <sys/types.h>
#endif// APPLE

#ifdef __linux__ 

#if defined(__i386__) || defined(__X86_64__)
#include <x86intrin.h>
#elif defined(__arm__) || defined(__aarch64__)
#include "crypto/SSE2NEON.h"
#endif 

#elif _WIN32
#pragma warning (disable : 4146)
#include <intrin.h>
#endif

void clmul64(uint64_t a, uint64_t b, uint64_t* r)
{
    uint8_t s = 4,i; //window size
    uint64_t two_s = 1 << s; //2^s
    uint64_t smask = two_s-1; //s 1 bits
    uint64_t u[16];
    uint64_t tmp;
    uint64_t ifmask;
    //Precomputation
    u[0] = 0;
    u[1] = b;
    for(i = 2 ; i < two_s; i += 2){
        u[i] = u[i >> 1] << 1; //even indices: left shift
        u[i + 1] = u[i] ^ b; //odd indices: xor b
    }
    //Multiply
    r[0] = u[a & smask]; //first window only affects lower word
    r[1] = 0;
    for(i = s ; i < 64 ; i += s){
        tmp = u[a >> i & smask];     
        r[0] ^= tmp << i;
        r[1] ^= tmp >> (64 - i);
    }
    //Repair
    uint64_t m = 0xEEEEEEEEEEEEEEEE; //s=4 => 16 times 1110
    for(i = 1 ; i < s ; i++){
        tmp = ((a & m) >> i);
        m &= m << 1; //shift mask to exclude all bit j': j' mod s = i
        ifmask = -((b >> (64-i)) & 1); //if the (64-i)th bit of b is 1
        r[1] ^= (tmp & ifmask);
    }
}

u128 _mm_clmulepi64_si128_emu(const __m128i &a, const __m128i &b, int imm)
{
    uint64_t result[2];
    clmul64(*((uint64_t*)&a + (imm & 1)), *((uint64_t*)&b + ((imm & 0x10) >> 4)), result);

    /*
    // TEST
    const __m128i tmp1 = _mm_load_si128(&a);
    const __m128i tmp2 = _mm_load_si128(&b);
    imm = imm & 0x11;
    const __m128i testresult = (imm == 0x10) ? _mm_clmulepi64_si128(tmp1, tmp2, 0x10) : ((imm == 0x01) ? _mm_clmulepi64_si128(tmp1, tmp2, 0x01) : ((imm == 0x00) ? _mm_clmulepi64_si128(tmp1, tmp2, 0x00) : _mm_clmulepi64_si128(tmp1, tmp2, 0x11)));
    if (!memcmp(&testresult, &result, 16))
    {
        printf("_mm_clmulepi64_si128_emu: Portable version passed!\n");
    }
    else
    {
        printf("_mm_clmulepi64_si128_emu: Portable version failed! a: %lxh %lxl, b: %lxh %lxl, imm: %x, emu: %lxh %lxl, intrin: %lxh %lxl\n", 
               *((uint64_t *)&a + 1), *(uint64_t *)&a,
               *((uint64_t *)&b + 1), *(uint64_t *)&b,
               imm,
               *((uint64_t *)result + 1), *(uint64_t *)result,
               *((uint64_t *)&testresult + 1), *(uint64_t *)&testresult);
        return testresult;
    }
    */

    return *(__m128i *)result;
}

u128 _mm_mulhrs_epi16_emu(__m128i _a, __m128i _b)
{
    int16_t result[8];
    int16_t *a = (int16_t*)&_a, *b = (int16_t*)&_b;
    for (int i = 0; i < 8; i ++)
    {
        result[i] = (int16_t)((((int32_t)(a[i]) * (int32_t)(b[i])) + 0x4000) >> 15);
    }

    /*
    const __m128i testresult = _mm_mulhrs_epi16(_a, _b);
    if (!memcmp(&testresult, &result, 16))
    {
        printf("_mm_mulhrs_epi16_emu: Portable version passed!\n");
    }
    else
    {
        printf("_mm_mulhrs_epi16_emu: Portable version failed! a: %lxh %lxl, b: %lxh %lxl, emu: %lxh %lxl, intrin: %lxh %lxl\n", 
               *((uint64_t *)&a + 1), *(uint64_t *)&a,
               *((uint64_t *)&b + 1), *(uint64_t *)&b,
               *((uint64_t *)result + 1), *(uint64_t *)result,
               *((uint64_t *)&testresult + 1), *(uint64_t *)&testresult);
    }
    */

    return *(__m128i *)result;
}

inline u128 _mm_set_epi64x_emu(uint64_t hi, uint64_t lo)
{
    __m128i result;
    ((uint64_t *)&result)[0] = lo;
    ((uint64_t *)&result)[1] = hi;
    return result;
}

inline u128 _mm_cvtsi64_si128_emu(uint64_t lo)
{
    __m128i result;
    ((uint64_t *)&result)[0] = lo;
    ((uint64_t *)&result)[1] = 0;
    return result;
}

inline int64_t _mm_cvtsi128_si64_emu(const __m128i &a)
{
    return *(int64_t *)&a;
}

inline int32_t _mm_cvtsi128_si32_emu(const __m128i &a)
{
    return *(int32_t *)&a;
}

inline u128 _mm_cvtsi32_si128_emu(uint32_t lo)
{
    __m128i result;
    ((uint32_t *)&result)[0] = lo;
    ((uint32_t *)&result)[1] = 0;
    ((uint64_t *)&result)[1] = 0;

    /*
    const __m128i testresult = _mm_cvtsi32_si128(lo);
    if (!memcmp(&testresult, &result, 16))
    {
        printf("_mm_cvtsi32_si128_emu: Portable version passed!\n");
    }
    else
    {
        printf("_mm_cvtsi32_si128_emu: Portable version failed!\n");
    }
    */

    return result;
}

u128 _mm_setr_epi8_emu(u_char c0, u_char c1, u_char c2, u_char c3, u_char c4, u_char c5, u_char c6, u_char c7, u_char c8, u_char c9, u_char c10, u_char c11, u_char c12, u_char c13, u_char c14, u_char c15)
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

    /*
    const __m128i testresult = _mm_setr_epi8(c0,c1,c2,c3,c4,c5,c6,c7,c8,c9,c10,c11,c12,c13,c14,c15);
    if (!memcmp(&testresult, &result, 16))
    {
        printf("_mm_setr_epi8_emu: Portable version passed!\n");
    }
    else
    {
        printf("_mm_setr_epi8_emu: Portable version failed!\n");
    }
    */

    return result;
}

inline __m128i _mm_srli_si128_emu(__m128i a, int imm8)
{
    unsigned char result[16];
    uint8_t shift = imm8 & 0xff;
    if (shift > 15) shift = 16;

    int i;
    for (i = 0; i < (16 - shift); i++)
    {
        result[i] = ((unsigned char *)&a)[shift + i];
    }
    for ( ; i < 16; i++)
    {
        result[i] = 0;
    }

    /*
    const __m128i tmp1 = _mm_load_si128(&a);
    __m128i testresult = _mm_srli_si128(tmp1, imm8);
    if (!memcmp(&testresult, result, 16))
    {
        printf("_mm_srli_si128_emu: Portable version passed!\n");
    }
    else
    {
        printf("_mm_srli_si128_emu: Portable version failed! val: %lx%lx imm: %x emu: %lx%lx, intrin: %lx%lx\n", 
               *((uint64_t *)&a + 1), *(uint64_t *)&a,
               imm8,
               *((uint64_t *)result + 1), *(uint64_t *)result,
               *((uint64_t *)&testresult + 1), *(uint64_t *)&testresult);
    }
    */

    return *(__m128i *)result;
}

inline __m128i _mm_xor_si128_emu(__m128i a, __m128i b)
{
#ifdef _WIN32
    uint64_t result[2];
    result[0] = *(uint64_t *)&a ^ *(uint64_t *)&b;
    result[1] = *((uint64_t *)&a + 1) ^ *((uint64_t *)&b + 1);
    return *(__m128i *)result;
#else
    return a ^ b;
#endif
}

inline __m128i _mm_load_si128_emu(const void *p)
{
    return *(__m128i *)p;
}

inline void _mm_store_si128_emu(void *p, __m128i val)
{
    *(__m128i *)p = val;
}

__m128i _mm_shuffle_epi8_emu(__m128i a, __m128i b)
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

    /*
    const __m128i tmp1 = _mm_load_si128(&a);
    const __m128i tmp2 = _mm_load_si128(&b);
    __m128i testresult = _mm_shuffle_epi8(tmp1, tmp2);
    if (!memcmp(&testresult, &result, 16))
    {
        printf("_mm_shuffle_epi8_emu: Portable version passed!\n");
    }
    else
    {
        printf("_mm_shuffle_epi8_emu: Portable version failed!\n");
    }
    */

    return result;
}

// portable
static inline __m128i lazyLengthHash_port(uint64_t keylength, uint64_t length) {
    const __m128i lengthvector = _mm_set_epi64x_emu(keylength,length);
    const __m128i clprod1 = _mm_clmulepi64_si128_emu( lengthvector, lengthvector, 0x10);
    return clprod1;
}

// modulo reduction to 64-bit value. The high 64 bits contain garbage, see precompReduction64
static inline __m128i precompReduction64_si128_port( __m128i A) {

    //const __m128i C = _mm_set_epi64x(1U,(1U<<4)+(1U<<3)+(1U<<1)+(1U<<0)); // C is the irreducible poly. (64,4,3,1,0)
    const __m128i C = _mm_cvtsi64_si128_emu((1U<<4)+(1U<<3)+(1U<<1)+(1U<<0));
    __m128i Q2 = _mm_clmulepi64_si128_emu( A, C, 0x01);
    __m128i Q3 = _mm_shuffle_epi8_emu(_mm_setr_epi8_emu(0, 27, 54, 45, 108, 119, 90, 65, (char)216, (char)195, (char)238, (char)245, (char)180, (char)175, (char)130, (char)153),
                                  _mm_srli_si128_emu(Q2,8));
    __m128i Q4 = _mm_xor_si128_emu(Q2,A);
    const __m128i final = _mm_xor_si128_emu(Q3,Q4);
    return final;/// WARNING: HIGH 64 BITS SHOULD BE ASSUMED TO CONTAIN GARBAGE
}

static inline uint64_t precompReduction64_port( __m128i A) {
    __m128i tmp = precompReduction64_si128_port(A);
    return _mm_cvtsi128_si64_emu(tmp);
}

// verus intermediate hash extra
__m128i __verusclmulwithoutreduction64alignedrepeat_port(__m128i *randomsource, const __m128i buf[4], uint64_t keyMask, __m128i **pMoveScratch)
{
    __m128i const *pbuf;

    // divide key mask by 16 from bytes to __m128i
    keyMask >>= 4;

    // the random buffer must have at least 32 16 byte dwords after the keymask to work with this
    // algorithm. we take the value from the last element inside the keyMask + 2, as that will never
    // be used to xor into the accumulator before it is hashed with other values first
    __m128i acc = _mm_load_si128_emu(randomsource + (keyMask + 2));

    for (int64_t i = 0; i < 32; i++)
    {
        const uint64_t selector = _mm_cvtsi128_si64_emu(acc);

        // get two random locations in the key, which will be mutated and swapped
        __m128i *prand = randomsource + ((selector >> 5) & keyMask);
        __m128i *prandex = randomsource + ((selector >> 32) & keyMask);

        *pMoveScratch++ = prand;
        *pMoveScratch++ = prandex;

        // select random start and order of pbuf processing
        pbuf = buf + (selector & 3);

        switch (selector & 0x1c)
        {
            case 0:
            {
                const __m128i temp1 = _mm_load_si128_emu(prandex);
                const __m128i temp2 = _mm_load_si128_emu(pbuf - (((selector & 1) << 1) - 1));
                const __m128i add1 = _mm_xor_si128_emu(temp1, temp2);
                const __m128i clprod1 = _mm_clmulepi64_si128_emu(add1, add1, 0x10);
                acc = _mm_xor_si128_emu(clprod1, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16_emu(acc, temp1);
                const __m128i tempa2 = _mm_xor_si128_emu(tempa1, temp1);

                const __m128i temp12 = _mm_load_si128_emu(prand);
                _mm_store_si128_emu(prand, tempa2);

                const __m128i temp22 = _mm_load_si128_emu(pbuf);
                const __m128i add12 = _mm_xor_si128_emu(temp12, temp22);
                const __m128i clprod12 = _mm_clmulepi64_si128_emu(add12, add12, 0x10);
                acc = _mm_xor_si128_emu(clprod12, acc);

                const __m128i tempb1 = _mm_mulhrs_epi16_emu(acc, temp12);
                const __m128i tempb2 = _mm_xor_si128_emu(tempb1, temp12);
                _mm_store_si128_emu(prandex, tempb2);
                break;
            }
            case 4:
            {
                const __m128i temp1 = _mm_load_si128_emu(prand);
                const __m128i temp2 = _mm_load_si128_emu(pbuf);
                const __m128i add1 = _mm_xor_si128_emu(temp1, temp2);
                const __m128i clprod1 = _mm_clmulepi64_si128_emu(add1, add1, 0x10);
                acc = _mm_xor_si128_emu(clprod1, acc);
                const __m128i clprod2 = _mm_clmulepi64_si128_emu(temp2, temp2, 0x10);
                acc = _mm_xor_si128_emu(clprod2, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16_emu(acc, temp1);
                const __m128i tempa2 = _mm_xor_si128_emu(tempa1, temp1);

                const __m128i temp12 = _mm_load_si128_emu(prandex);
                _mm_store_si128_emu(prandex, tempa2);

                const __m128i temp22 = _mm_load_si128_emu(pbuf - (((selector & 1) << 1) - 1));
                const __m128i add12 = _mm_xor_si128_emu(temp12, temp22);
                acc = _mm_xor_si128_emu(add12, acc);

                const __m128i tempb1 = _mm_mulhrs_epi16_emu(acc, temp12);
                const __m128i tempb2 = _mm_xor_si128_emu(tempb1, temp12);
                _mm_store_si128_emu(prand, tempb2);
                break;
            }
            case 8:
            {
                const __m128i temp1 = _mm_load_si128_emu(prandex);
                const __m128i temp2 = _mm_load_si128_emu(pbuf);
                const __m128i add1 = _mm_xor_si128_emu(temp1, temp2);
                acc = _mm_xor_si128_emu(add1, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16_emu(acc, temp1);
                const __m128i tempa2 = _mm_xor_si128_emu(tempa1, temp1);

                const __m128i temp12 = _mm_load_si128_emu(prand);
                _mm_store_si128_emu(prand, tempa2);

                const __m128i temp22 = _mm_load_si128_emu(pbuf - (((selector & 1) << 1) - 1));
                const __m128i add12 = _mm_xor_si128_emu(temp12, temp22);
                const __m128i clprod12 = _mm_clmulepi64_si128_emu(add12, add12, 0x10);
                acc = _mm_xor_si128_emu(clprod12, acc);
                const __m128i clprod22 = _mm_clmulepi64_si128_emu(temp22, temp22, 0x10);
                acc = _mm_xor_si128_emu(clprod22, acc);

                const __m128i tempb1 = _mm_mulhrs_epi16_emu(acc, temp12);
                const __m128i tempb2 = _mm_xor_si128_emu(tempb1, temp12);
                _mm_store_si128_emu(prandex, tempb2);
                break;
            }
            case 0xc:
            {
                const __m128i temp1 = _mm_load_si128_emu(prand);
                const __m128i temp2 = _mm_load_si128_emu(pbuf - (((selector & 1) << 1) - 1));
                const __m128i add1 = _mm_xor_si128_emu(temp1, temp2);

                // cannot be zero here
                const int32_t divisor = (uint32_t)selector;

                acc = _mm_xor_si128_emu(add1, acc);

                const int64_t dividend = _mm_cvtsi128_si64_emu(acc);
                const __m128i modulo = _mm_cvtsi32_si128_emu(dividend % divisor);
                acc = _mm_xor_si128_emu(modulo, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16_emu(acc, temp1);
                const __m128i tempa2 = _mm_xor_si128_emu(tempa1, temp1);

                if (dividend & 1)
                {
                    const __m128i temp12 = _mm_load_si128_emu(prandex);
                    _mm_store_si128_emu(prandex, tempa2);

                    const __m128i temp22 = _mm_load_si128_emu(pbuf);
                    const __m128i add12 = _mm_xor_si128_emu(temp12, temp22);
                    const __m128i clprod12 = _mm_clmulepi64_si128_emu(add12, add12, 0x10);
                    acc = _mm_xor_si128_emu(clprod12, acc);
                    const __m128i clprod22 = _mm_clmulepi64_si128_emu(temp22, temp22, 0x10);
                    acc = _mm_xor_si128_emu(clprod22, acc);

                    const __m128i tempb1 = _mm_mulhrs_epi16_emu(acc, temp12);
                    const __m128i tempb2 = _mm_xor_si128_emu(tempb1, temp12);
                    _mm_store_si128_emu(prand, tempb2);
                }
                else
                {
                    const __m128i tempb3 = _mm_load_si128_emu(prandex);
                    _mm_store_si128_emu(prandex, tempa2);
                    _mm_store_si128_emu(prand, tempb3);
                }
                break;
            }
            case 0x10:
            {
                // a few AES operations
                const __m128i *rc = prand;
                __m128i tmp;

                __m128i temp1 = _mm_load_si128_emu(pbuf - (((selector & 1) << 1) - 1));
                __m128i temp2 = _mm_load_si128_emu(pbuf);

                AES2_EMU(temp1, temp2, 0);
                MIX2_EMU(temp1, temp2);

                AES2_EMU(temp1, temp2, 4);
                MIX2_EMU(temp1, temp2);

                AES2_EMU(temp1, temp2, 8);
                MIX2_EMU(temp1, temp2);

                acc = _mm_xor_si128_emu(temp1, acc);
                acc = _mm_xor_si128_emu(temp2, acc);

                const __m128i tempa1 = _mm_load_si128_emu(prand);
                const __m128i tempa2 = _mm_mulhrs_epi16_emu(acc, tempa1);
                const __m128i tempa3 = _mm_xor_si128_emu(tempa1, tempa2);

                const __m128i tempa4 = _mm_load_si128_emu(prandex);
                _mm_store_si128_emu(prandex, tempa3);
                _mm_store_si128_emu(prand, tempa4);
                break;
            }
            case 0x14:
            {
                // we'll just call this one the monkins loop, inspired by Chris
                const __m128i *buftmp = pbuf - (((selector & 1) << 1) - 1);
                __m128i tmp; // used by MIX2

                uint64_t rounds = selector >> 61; // loop randomly between 1 and 8 times
                __m128i *rc = prand;
                uint64_t aesround = 0;
                __m128i onekey;

                do
                {
                    // note that due to compiler and CPUs, we expect this to do:
                    // if (selector & ((0x10000000 << rounds) & 0xffffffff) if rounds != 3 else selector & 0xffffffff80000000):
                    if (selector & (0x10000000 << rounds))
                    {
                        onekey = _mm_load_si128_emu(rc++);
                        const __m128i temp2 = _mm_load_si128_emu(rounds & 1 ? pbuf : buftmp);
                        const __m128i add1 = _mm_xor_si128_emu(onekey, temp2);
                        const __m128i clprod1 = _mm_clmulepi64_si128_emu(add1, add1, 0x10);
                        acc = _mm_xor_si128_emu(clprod1, acc);
                    }
                    else
                    {
                        onekey = _mm_load_si128_emu(rc++);
                        __m128i temp2 = _mm_load_si128_emu(rounds & 1 ? buftmp : pbuf);
                        const uint64_t roundidx = aesround++ << 2;
                        AES2_EMU(onekey, temp2, roundidx);

                        MIX2_EMU(onekey, temp2);

                        acc = _mm_xor_si128_emu(onekey, acc);
                        acc = _mm_xor_si128_emu(temp2, acc);
                    }
                } while (rounds--);

                const __m128i tempa1 = _mm_load_si128_emu(prand);
                const __m128i tempa2 = _mm_mulhrs_epi16_emu(acc, tempa1);
                const __m128i tempa3 = _mm_xor_si128_emu(tempa1, tempa2);

                const __m128i tempa4 = _mm_load_si128_emu(prandex);
                _mm_store_si128_emu(prandex, tempa3);
                _mm_store_si128_emu(prand, tempa4);
                break;
            }
            case 0x18:
            {
                const __m128i temp1 = _mm_load_si128_emu(pbuf - (((selector & 1) << 1) - 1));
                const __m128i temp2 = _mm_load_si128_emu(prand);
                const __m128i add1 = _mm_xor_si128_emu(temp1, temp2);
                const __m128i clprod1 = _mm_clmulepi64_si128_emu(add1, add1, 0x10);
                acc = _mm_xor_si128_emu(clprod1, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16_emu(acc, temp2);
                const __m128i tempa2 = _mm_xor_si128_emu(tempa1, temp2);

                const __m128i tempb3 = _mm_load_si128_emu(prandex);
                _mm_store_si128_emu(prandex, tempa2);
                _mm_store_si128_emu(prand, tempb3);
                break;
            }
            case 0x1c:
            {
                const __m128i temp1 = _mm_load_si128_emu(pbuf);
                const __m128i temp2 = _mm_load_si128_emu(prandex);
                const __m128i add1 = _mm_xor_si128_emu(temp1, temp2);
                const __m128i clprod1 = _mm_clmulepi64_si128_emu(add1, add1, 0x10);
                acc = _mm_xor_si128_emu(clprod1, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16_emu(acc, temp2);
                const __m128i tempa2 = _mm_xor_si128_emu(tempa1, temp2);

                const __m128i tempa3 = _mm_load_si128_emu(prand);
                _mm_store_si128_emu(prand, tempa2);

                acc = _mm_xor_si128_emu(tempa3, acc);

                const __m128i tempb1 = _mm_mulhrs_epi16_emu(acc, tempa3);
                const __m128i tempb2 = _mm_xor_si128_emu(tempb1, tempa3);
                _mm_store_si128_emu(prandex, tempb2);
                break;
            }
        }
    }
    return acc;
}

// verus intermediate hash extra
__m128i __verusclmulwithoutreduction64alignedrepeat_sv2_1_port(__m128i *randomsource, const __m128i buf[4], uint64_t keyMask, __m128i **pMoveScratch)
{
    const __m128i pbuf_copy[4] = {_mm_xor_si128(buf[0],buf[2]), _mm_xor_si128(buf[1],buf[3]), buf[2], buf[3]}; 
    const  __m128i *pbuf; 

    // divide key mask by 16 from bytes to __m128i
    keyMask >>= 4;

    // the random buffer must have at least 32 16 byte dwords after the keymask to work with this
    // algorithm. we take the value from the last element inside the keyMask + 2, as that will never
    // be used to xor into the accumulator before it is hashed with other values first
    __m128i acc = _mm_load_si128_emu(randomsource + (keyMask + 2));

    for (int64_t i = 0; i < 32; i++)
    {
        const uint64_t selector = _mm_cvtsi128_si64_emu(acc);

        // get two random locations in the key, which will be mutated and swapped
        __m128i *prand = randomsource + ((selector >> 5) & keyMask);
        __m128i *prandex = randomsource + ((selector >> 32) & keyMask);

        *pMoveScratch++ = prand;
        *pMoveScratch++ = prandex;

        // select random start and order of pbuf processing
        pbuf = pbuf_copy + (selector & 3);

        switch (selector & 0x1c)
        {
            case 0:
            {
                const __m128i temp1 = _mm_load_si128_emu(prandex);
                const __m128i temp2 = _mm_load_si128_emu(pbuf - (((selector & 1) << 1) - 1));
                const __m128i add1 = _mm_xor_si128_emu(temp1, temp2);
                const __m128i clprod1 = _mm_clmulepi64_si128_emu(add1, add1, 0x10);
                acc = _mm_xor_si128_emu(clprod1, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16_emu(acc, temp1);
                const __m128i tempa2 = _mm_xor_si128_emu(tempa1, temp1);

                const __m128i temp12 = _mm_load_si128_emu(prand);
                _mm_store_si128_emu(prand, tempa2);

                const __m128i temp22 = _mm_load_si128_emu(pbuf);
                const __m128i add12 = _mm_xor_si128_emu(temp12, temp22);
                const __m128i clprod12 = _mm_clmulepi64_si128_emu(add12, add12, 0x10);
                acc = _mm_xor_si128_emu(clprod12, acc);

                const __m128i tempb1 = _mm_mulhrs_epi16_emu(acc, temp12);
                const __m128i tempb2 = _mm_xor_si128_emu(tempb1, temp12);
                _mm_store_si128_emu(prandex, tempb2);
                break;
            }
            case 4:
            {
                const __m128i temp1 = _mm_load_si128_emu(prand);
                const __m128i temp2 = _mm_load_si128_emu(pbuf);
                const __m128i add1 = _mm_xor_si128_emu(temp1, temp2);
                const __m128i clprod1 = _mm_clmulepi64_si128_emu(add1, add1, 0x10);
                acc = _mm_xor_si128_emu(clprod1, acc);
                const __m128i clprod2 = _mm_clmulepi64_si128_emu(temp2, temp2, 0x10);
                acc = _mm_xor_si128_emu(clprod2, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16_emu(acc, temp1);
                const __m128i tempa2 = _mm_xor_si128_emu(tempa1, temp1);

                const __m128i temp12 = _mm_load_si128_emu(prandex);
                _mm_store_si128_emu(prandex, tempa2);

                const __m128i temp22 = _mm_load_si128_emu(pbuf - (((selector & 1) << 1) - 1));
                const __m128i add12 = _mm_xor_si128_emu(temp12, temp22);
                acc = _mm_xor_si128_emu(add12, acc);

                const __m128i tempb1 = _mm_mulhrs_epi16_emu(acc, temp12);
                const __m128i tempb2 = _mm_xor_si128_emu(tempb1, temp12);
                _mm_store_si128_emu(prand, tempb2);
                break;
            }
            case 8:
            {
                const __m128i temp1 = _mm_load_si128_emu(prandex);
                const __m128i temp2 = _mm_load_si128_emu(pbuf);
                const __m128i add1 = _mm_xor_si128_emu(temp1, temp2);
                acc = _mm_xor_si128_emu(add1, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16_emu(acc, temp1);
                const __m128i tempa2 = _mm_xor_si128_emu(tempa1, temp1);

                const __m128i temp12 = _mm_load_si128_emu(prand);
                _mm_store_si128_emu(prand, tempa2);

                const __m128i temp22 = _mm_load_si128_emu(pbuf - (((selector & 1) << 1) - 1));
                const __m128i add12 = _mm_xor_si128_emu(temp12, temp22);
                const __m128i clprod12 = _mm_clmulepi64_si128_emu(add12, add12, 0x10);
                acc = _mm_xor_si128_emu(clprod12, acc);
                const __m128i clprod22 = _mm_clmulepi64_si128_emu(temp22, temp22, 0x10);
                acc = _mm_xor_si128_emu(clprod22, acc);

                const __m128i tempb1 = _mm_mulhrs_epi16_emu(acc, temp12);
                const __m128i tempb2 = _mm_xor_si128_emu(tempb1, temp12);
                _mm_store_si128_emu(prandex, tempb2);
                break;
            }
            case 0xc:
            {
                const __m128i temp1 = _mm_load_si128_emu(prand);
                const __m128i temp2 = _mm_load_si128_emu(pbuf - (((selector & 1) << 1) - 1));
                const __m128i add1 = _mm_xor_si128_emu(temp1, temp2);

                // cannot be zero here
                const int32_t divisor = (uint32_t)selector;

                acc = _mm_xor_si128_emu(add1, acc);

                const int64_t dividend = _mm_cvtsi128_si64_emu(acc);
                const __m128i modulo = _mm_cvtsi32_si128_emu(dividend % divisor);
                acc = _mm_xor_si128_emu(modulo, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16_emu(acc, temp1);
                const __m128i tempa2 = _mm_xor_si128_emu(tempa1, temp1);

                if (dividend & 1)
                {
                    const __m128i temp12 = _mm_load_si128_emu(prandex);
                    _mm_store_si128_emu(prandex, tempa2);

                    const __m128i temp22 = _mm_load_si128_emu(pbuf);
                    const __m128i add12 = _mm_xor_si128_emu(temp12, temp22);
                    const __m128i clprod12 = _mm_clmulepi64_si128_emu(add12, add12, 0x10);
                    acc = _mm_xor_si128_emu(clprod12, acc);
                    const __m128i clprod22 = _mm_clmulepi64_si128_emu(temp22, temp22, 0x10);
                    acc = _mm_xor_si128_emu(clprod22, acc);

                    const __m128i tempb1 = _mm_mulhrs_epi16_emu(acc, temp12);
                    const __m128i tempb2 = _mm_xor_si128_emu(tempb1, temp12);
                    _mm_store_si128_emu(prand, tempb2);
                }
                else
                {
                    const __m128i tempb3 = _mm_load_si128_emu(prandex);
                    _mm_store_si128_emu(prandex, tempa2);
                    _mm_store_si128_emu(prand, tempb3);
                }
                break;
            }
            case 0x10:
            {
                // a few AES operations
                const __m128i *rc = prand;
                __m128i tmp;

                __m128i temp1 = _mm_load_si128_emu(pbuf - (((selector & 1) << 1) - 1));
                __m128i temp2 = _mm_load_si128_emu(pbuf);

                AES2_EMU(temp1, temp2, 0);
                MIX2_EMU(temp1, temp2);

                AES2_EMU(temp1, temp2, 4);
                MIX2_EMU(temp1, temp2);

                AES2_EMU(temp1, temp2, 8);
                MIX2_EMU(temp1, temp2);

                acc = _mm_xor_si128_emu(temp1, acc);
                acc = _mm_xor_si128_emu(temp2, acc);

                const __m128i tempa1 = _mm_load_si128_emu(prand);
                const __m128i tempa2 = _mm_mulhrs_epi16_emu(acc, tempa1);
                const __m128i tempa3 = _mm_xor_si128_emu(tempa1, tempa2);

                const __m128i tempa4 = _mm_load_si128_emu(prandex);
                _mm_store_si128_emu(prandex, tempa3);
                _mm_store_si128_emu(prand, tempa4);
                break;
            }
            case 0x14:
            {
                // we'll just call this one the monkins loop, inspired by Chris
                const __m128i *buftmp = pbuf - (((selector & 1) << 1) - 1);
                __m128i tmp; // used by MIX2

                uint64_t rounds = selector >> 61; // loop randomly between 1 and 8 times
                __m128i *rc = prand;
                uint64_t aesround = 0;
                __m128i onekey;

                do
                {
                    // this is simplified over the original verus_clhash
                    if (selector & (((uint64_t)0x10000000) << rounds))
                    {
                        onekey = _mm_load_si128_emu(rc++);
                        const __m128i temp2 = _mm_load_si128_emu(rounds & 1 ? pbuf : buftmp);
                        const __m128i add1 = _mm_xor_si128_emu(onekey, temp2);
                        const __m128i clprod1 = _mm_clmulepi64_si128_emu(add1, add1, 0x10);
                        acc = _mm_xor_si128_emu(clprod1, acc);
                    }
                    else
                    {
                        onekey = _mm_load_si128_emu(rc++);
                        __m128i temp2 = _mm_load_si128_emu(rounds & 1 ? buftmp : pbuf);
                        const uint64_t roundidx = aesround++ << 2;
                        AES2_EMU(onekey, temp2, roundidx);

                        MIX2_EMU(onekey, temp2);

                        acc = _mm_xor_si128_emu(onekey, acc);
                        acc = _mm_xor_si128_emu(temp2, acc);
                    }
                } while (rounds--);

                const __m128i tempa1 = _mm_load_si128_emu(prand);
                const __m128i tempa2 = _mm_mulhrs_epi16_emu(acc, tempa1);
                const __m128i tempa3 = _mm_xor_si128_emu(tempa1, tempa2);

                const __m128i tempa4 = _mm_load_si128_emu(prandex);
                _mm_store_si128_emu(prandex, tempa3);
                _mm_store_si128_emu(prand, tempa4);
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
                        onekey = _mm_load_si128_emu(rc++);
                        const __m128i temp2 = _mm_load_si128_emu(rounds & 1 ? pbuf : buftmp);
                        const __m128i add1 = _mm_xor_si128_emu(onekey, temp2);
                        // cannot be zero here, may be negative
                        const int32_t divisor = (uint32_t)selector;
                        const int64_t dividend = _mm_cvtsi128_si64_emu(add1);
                        const __m128i modulo = _mm_cvtsi32_si128_emu(dividend % divisor);
                        acc = _mm_xor_si128_emu(modulo, acc);
                    }
                    else
                    {
                        onekey = _mm_load_si128_emu(rc++);
                        __m128i temp2 = _mm_load_si128_emu(rounds & 1 ? buftmp : pbuf);
                        const __m128i add1 = _mm_xor_si128_emu(onekey, temp2);
                        const __m128i clprod1 = _mm_clmulepi64_si128_emu(add1, add1, 0x10);
                        const __m128i clprod2 = _mm_mulhrs_epi16_emu(acc, clprod1);
                        acc = _mm_xor_si128_emu(clprod2, acc);
                    }
                } while (rounds--);

                const __m128i tempa3 = _mm_load_si128_emu(prandex);
                const __m128i tempa4 = _mm_xor_si128_emu(tempa3, acc);
                _mm_store_si128_emu(prandex, tempa4);
                _mm_store_si128_emu(prand, onekey);
                break;
            }
            case 0x1c:
            {
                const __m128i temp1 = _mm_load_si128_emu(pbuf);
                const __m128i temp2 = _mm_load_si128_emu(prandex);
                const __m128i add1 = _mm_xor_si128_emu(temp1, temp2);
                const __m128i clprod1 = _mm_clmulepi64_si128_emu(add1, add1, 0x10);
                acc = _mm_xor_si128_emu(clprod1, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16_emu(acc, temp2);
                const __m128i tempa2 = _mm_xor_si128_emu(tempa1, temp2);

                const __m128i tempa3 = _mm_load_si128_emu(prand);
                _mm_store_si128_emu(prand, tempa2);

                acc = _mm_xor_si128_emu(tempa3, acc);

                const __m128i tempb1 = _mm_mulhrs_epi16_emu(acc, tempa3);
                const __m128i tempb2 = _mm_xor_si128_emu(tempb1, tempa3);
                _mm_store_si128_emu(prandex, tempb2);
                break;
            }
        }
    }
    return acc;
}
__m128i __verusclmulwithoutreduction64alignedrepeat_sv2_2_port(__m128i *randomsource, const __m128i buf[4], uint64_t keyMask, __m128i **pMoveScratch)
{
    const __m128i pbuf_copy[4] = {_mm_xor_si128(buf[0],buf[2]), _mm_xor_si128(buf[1],buf[3]), buf[2], buf[3]}; 
    const  __m128i *pbuf; 

    // divide key mask by 16 from bytes to __m128i
    keyMask >>= 4;

    // the random buffer must have at least 32 16 byte dwords after the keymask to work with this
    // algorithm. we take the value from the last element inside the keyMask + 2, as that will never
    // be used to xor into the accumulator before it is hashed with other values first
    __m128i acc = _mm_load_si128_emu(randomsource + (keyMask + 2));

    for (int64_t i = 0; i < 32; i++)
    {
        //std::cout << "LOOP " << i << " acc: " << LEToHex(acc) << std::endl;
        
        const uint64_t selector = _mm_cvtsi128_si64_emu(acc);

        // get two random locations in the key, which will be mutated and swapped
        __m128i *prand = randomsource + ((selector >> 5) & keyMask);
        __m128i *prandex = randomsource + ((selector >> 32) & keyMask);

        *pMoveScratch++ = prand;
        *pMoveScratch++ = prandex;

        // select random start and order of pbuf processing
        pbuf = pbuf_copy + (selector & 3);

        switch (selector & 0x1c)
        {
            case 0:
            {
                const __m128i temp1 = _mm_load_si128_emu(prandex);
                const __m128i temp2 = _mm_load_si128_emu(pbuf - (((selector & 1) << 1) - 1));
                const __m128i add1 = _mm_xor_si128_emu(temp1, temp2);
                const __m128i clprod1 = _mm_clmulepi64_si128_emu(add1, add1, 0x10);
                acc = _mm_xor_si128_emu(clprod1, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16_emu(acc, temp1);
                const __m128i tempa2 = _mm_xor_si128_emu(tempa1, temp1);

                const __m128i temp12 = _mm_load_si128_emu(prand);
                _mm_store_si128_emu(prand, tempa2);

                const __m128i temp22 = _mm_load_si128_emu(pbuf);
                const __m128i add12 = _mm_xor_si128_emu(temp12, temp22);
                const __m128i clprod12 = _mm_clmulepi64_si128_emu(add12, add12, 0x10);
                acc = _mm_xor_si128_emu(clprod12, acc);

                const __m128i tempb1 = _mm_mulhrs_epi16_emu(acc, temp12);
                const __m128i tempb2 = _mm_xor_si128_emu(tempb1, temp12);
                _mm_store_si128_emu(prandex, tempb2);
                break;
            }
            case 4:
            {
                const __m128i temp1 = _mm_load_si128_emu(prand);
                const __m128i temp2 = _mm_load_si128_emu(pbuf);
                const __m128i add1 = _mm_xor_si128_emu(temp1, temp2);
                const __m128i clprod1 = _mm_clmulepi64_si128_emu(add1, add1, 0x10);
                acc = _mm_xor_si128_emu(clprod1, acc);
                const __m128i clprod2 = _mm_clmulepi64_si128_emu(temp2, temp2, 0x10);
                acc = _mm_xor_si128_emu(clprod2, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16_emu(acc, temp1);
                const __m128i tempa2 = _mm_xor_si128_emu(tempa1, temp1);

                const __m128i temp12 = _mm_load_si128_emu(prandex);
                _mm_store_si128_emu(prandex, tempa2);

                const __m128i temp22 = _mm_load_si128_emu(pbuf - (((selector & 1) << 1) - 1));
                const __m128i add12 = _mm_xor_si128_emu(temp12, temp22);
                acc = _mm_xor_si128_emu(add12, acc);

                const __m128i tempb1 = _mm_mulhrs_epi16_emu(acc, temp12);
                const __m128i tempb2 = _mm_xor_si128_emu(tempb1, temp12);
                _mm_store_si128_emu(prand, tempb2);
                break;
            }
            case 8:
            {
                const __m128i temp1 = _mm_load_si128_emu(prandex);
                const __m128i temp2 = _mm_load_si128_emu(pbuf);
                const __m128i add1 = _mm_xor_si128_emu(temp1, temp2);
                acc = _mm_xor_si128_emu(add1, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16_emu(acc, temp1);
                const __m128i tempa2 = _mm_xor_si128_emu(tempa1, temp1);

                const __m128i temp12 = _mm_load_si128_emu(prand);
                _mm_store_si128_emu(prand, tempa2);

                const __m128i temp22 = _mm_load_si128_emu(pbuf - (((selector & 1) << 1) - 1));
                const __m128i add12 = _mm_xor_si128_emu(temp12, temp22);
                const __m128i clprod12 = _mm_clmulepi64_si128_emu(add12, add12, 0x10);
                acc = _mm_xor_si128_emu(clprod12, acc);
                const __m128i clprod22 = _mm_clmulepi64_si128_emu(temp22, temp22, 0x10);
                acc = _mm_xor_si128_emu(clprod22, acc);

                const __m128i tempb1 = _mm_mulhrs_epi16_emu(acc, temp12);
                const __m128i tempb2 = _mm_xor_si128_emu(tempb1, temp12);
                _mm_store_si128_emu(prandex, tempb2);
                break;
            }
            case 0xc:
            {
                const __m128i temp1 = _mm_load_si128_emu(prand);
                const __m128i temp2 = _mm_load_si128_emu(pbuf - (((selector & 1) << 1) - 1));
                const __m128i add1 = _mm_xor_si128_emu(temp1, temp2);

                // cannot be zero here
                const int32_t divisor = (uint32_t)selector;

                acc = _mm_xor_si128_emu(add1, acc);

                const int64_t dividend = _mm_cvtsi128_si64_emu(acc);
                const __m128i modulo = _mm_cvtsi32_si128_emu(dividend % divisor);
                acc = _mm_xor_si128_emu(modulo, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16_emu(acc, temp1);
                const __m128i tempa2 = _mm_xor_si128_emu(tempa1, temp1);

                if (dividend & 1)
                {
                    const __m128i temp12 = _mm_load_si128_emu(prandex);
                    _mm_store_si128_emu(prandex, tempa2);

                    const __m128i temp22 = _mm_load_si128_emu(pbuf);
                    const __m128i add12 = _mm_xor_si128_emu(temp12, temp22);
                    const __m128i clprod12 = _mm_clmulepi64_si128_emu(add12, add12, 0x10);
                    acc = _mm_xor_si128_emu(clprod12, acc);
                    const __m128i clprod22 = _mm_clmulepi64_si128_emu(temp22, temp22, 0x10);
                    acc = _mm_xor_si128_emu(clprod22, acc);

                    const __m128i tempb1 = _mm_mulhrs_epi16_emu(acc, temp12);
                    const __m128i tempb2 = _mm_xor_si128_emu(tempb1, temp12);
                    _mm_store_si128_emu(prand, tempb2);
                }
                else
                {
                    const __m128i tempb3 = _mm_load_si128_emu(prandex);
                    _mm_store_si128_emu(prandex, tempa2);
                    _mm_store_si128_emu(prand, tempb3);
                    const __m128i tempb4 = _mm_load_si128_emu(pbuf);
                    acc = _mm_xor_si128_emu(tempb4, acc);
                }
                break;
            }
            case 0x10:
            {
                // a few AES operations
                const __m128i *rc = prand;
                __m128i tmp;

                __m128i temp1 = _mm_load_si128_emu(pbuf - (((selector & 1) << 1) - 1));
                __m128i temp2 = _mm_load_si128_emu(pbuf);

                AES2_EMU(temp1, temp2, 0);
                MIX2_EMU(temp1, temp2);

                AES2_EMU(temp1, temp2, 4);
                MIX2_EMU(temp1, temp2);

                AES2_EMU(temp1, temp2, 8);
                MIX2_EMU(temp1, temp2);

                acc = _mm_xor_si128_emu(temp1, acc);
                acc = _mm_xor_si128_emu(temp2, acc);

                const __m128i tempa1 = _mm_load_si128_emu(prand);
                const __m128i tempa2 = _mm_mulhrs_epi16_emu(acc, tempa1);
                const __m128i tempa3 = _mm_xor_si128_emu(tempa1, tempa2);

                const __m128i tempa4 = _mm_load_si128_emu(prandex);
                _mm_store_si128_emu(prandex, tempa3);
                _mm_store_si128_emu(prand, tempa4);
                break;
            }
            case 0x14:
            {
                // we'll just call this one the monkins loop, inspired by Chris
                const __m128i *buftmp = pbuf - (((selector & 1) << 1) - 1);
                __m128i tmp; // used by MIX2

                uint64_t rounds = selector >> 61; // loop randomly between 1 and 8 times
                __m128i *rc = prand;
                uint64_t aesround = 0;
                __m128i onekey;

                do
                {
                    // this is simplified over the original verus_clhash
                    if (selector & (((uint64_t)0x10000000) << rounds))
                    {
                        onekey = _mm_load_si128_emu(rc++);
                        const __m128i temp2 = _mm_load_si128_emu(rounds & 1 ? pbuf : buftmp);
                        const __m128i add1 = _mm_xor_si128_emu(onekey, temp2);
                        const __m128i clprod1 = _mm_clmulepi64_si128_emu(add1, add1, 0x10);
                        acc = _mm_xor_si128_emu(clprod1, acc);
                    }
                    else
                    {
                        onekey = _mm_load_si128_emu(rc++);
                        __m128i temp2 = _mm_load_si128_emu(rounds & 1 ? buftmp : pbuf);
                        const uint64_t roundidx = aesround++ << 2;
                        AES2_EMU(onekey, temp2, roundidx);

                        MIX2_EMU(onekey, temp2);

                        acc = _mm_xor_si128_emu(onekey, acc);
                        acc = _mm_xor_si128_emu(temp2, acc);
                    }
                } while (rounds--);

                const __m128i tempa1 = _mm_load_si128_emu(prand);
                const __m128i tempa2 = _mm_mulhrs_epi16_emu(acc, tempa1);
                const __m128i tempa3 = _mm_xor_si128_emu(tempa1, tempa2);

                const __m128i tempa4 = _mm_load_si128_emu(prandex);
                _mm_store_si128_emu(prandex, tempa3);
                _mm_store_si128_emu(prand, tempa4);
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
                        onekey = _mm_load_si128_emu(rc++);
                        const __m128i temp2 = _mm_load_si128_emu(rounds & 1 ? pbuf : buftmp);
                        onekey = _mm_xor_si128_emu(onekey, temp2);
                        // cannot be zero here, may be negative
                        const int32_t divisor = (uint32_t)selector;
                        const int64_t dividend = _mm_cvtsi128_si64_emu(onekey);
                        const __m128i modulo = _mm_cvtsi32_si128_emu(dividend % divisor);
                        acc = _mm_xor_si128_emu(modulo, acc);
                    }
                    else
                    {
                        onekey = _mm_load_si128_emu(rc++);
                        __m128i temp2 = _mm_load_si128_emu(rounds & 1 ? buftmp : pbuf);
                        const __m128i add1 = _mm_xor_si128_emu(onekey, temp2);
                        onekey = _mm_clmulepi64_si128_emu(add1, add1, 0x10);
                        const __m128i clprod2 = _mm_mulhrs_epi16_emu(acc, onekey);
                        acc = _mm_xor_si128_emu(clprod2, acc);
                    }
                } while (rounds--);

                const __m128i tempa3 = _mm_load_si128_emu(prandex);
                const __m128i tempa4 = _mm_xor_si128_emu(tempa3, acc);
                _mm_store_si128_emu(prandex, onekey);
                _mm_store_si128_emu(prand, tempa4);
                break;
            }
            case 0x1c:
            {
                const __m128i temp1 = _mm_load_si128_emu(pbuf);
                const __m128i temp2 = _mm_load_si128_emu(prandex);
                const __m128i add1 = _mm_xor_si128_emu(temp1, temp2);
                const __m128i clprod1 = _mm_clmulepi64_si128_emu(add1, add1, 0x10);
                acc = _mm_xor_si128_emu(clprod1, acc);

                const __m128i tempa1 = _mm_mulhrs_epi16_emu(acc, temp2);
                const __m128i tempa2 = _mm_xor_si128_emu(tempa1, temp2);

                const __m128i tempa3 = _mm_load_si128_emu(prand);
                _mm_store_si128_emu(prand, tempa2);

                acc = _mm_xor_si128_emu(tempa3, acc);
                const __m128i temp4 = _mm_load_si128_emu(pbuf - (((selector & 1) << 1) - 1));
                acc = _mm_xor_si128_emu(temp4,acc);  
                const __m128i tempb1 = _mm_mulhrs_epi16_emu(acc, tempa3);
                const __m128i tempb2 = _mm_xor_si128_emu(tempb1, tempa3);
                _mm_store_si128_emu(prandex, tempb2);
                break;
            }
        }
    }
    return acc;
}
// hashes 64 bytes only by doing a carryless multiplication and reduction of the repeated 64 byte sequence 16 times, 
// returning a 64 bit hash value
uint64_t verusclhash_port(void * random, const unsigned char buf[64], uint64_t keyMask, __m128i **pMoveScratch) {
    __m128i * rs64 = (__m128i *)random;
    const __m128i * string = (const __m128i *) buf;

    __m128i acc = __verusclmulwithoutreduction64alignedrepeat_port(rs64, string, keyMask, pMoveScratch);
    acc = _mm_xor_si128_emu(acc, lazyLengthHash_port(1024, 64));
    return precompReduction64_port(acc);
}

// hashes 64 bytes only by doing a carryless multiplication and reduction of the repeated 64 byte sequence 16 times, 
// returning a 64 bit hash value
uint64_t verusclhash_sv2_1_port(void * random, const unsigned char buf[64], uint64_t keyMask, __m128i **pMoveScratch) {
    __m128i * rs64 = (__m128i *)random;
    const __m128i * string = (const __m128i *) buf;

    __m128i  acc = __verusclmulwithoutreduction64alignedrepeat_sv2_1_port(rs64, string, keyMask, pMoveScratch);
    acc = _mm_xor_si128_emu(acc, lazyLengthHash_port(1024, 64));
    return precompReduction64_port(acc);
}

uint64_t verusclhash_sv2_2_port(void * random, const unsigned char buf[64], uint64_t keyMask, __m128i **pMoveScratch) {
    __m128i * rs64 = (__m128i *)random;
    const __m128i * string = (const __m128i *) buf;

    __m128i  acc = __verusclmulwithoutreduction64alignedrepeat_sv2_2_port(rs64, string, keyMask, pMoveScratch);
    acc = _mm_xor_si128_emu(acc, lazyLengthHash_port(1024, 64));
    return precompReduction64_port(acc);
}

#ifndef __SHA2_H__
#define __SHA2_H__

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>
#include <sha3/extra.h>

#define bswap_32(x) ((((x) << 24) & 0xff000000u) | (((x) << 8) & 0x00ff0000u) \
	| (((x) >> 8) & 0x0000ff00u) | (((x) >> 24) & 0x000000ffu))

#define bswap_64(x) (((uint64_t) bswap_32((uint32_t)((x) & 0xffffffffu)) << 32) \
	| (uint64_t) bswap_32((uint32_t)((x) >> 32)))

    /*static inline uint32_t be32dec(const void* pp)
    {
        const uint8_t* p = (uint8_t const*)pp;
        return ((uint32_t)(p[3]) + ((uint32_t)(p[2]) << 8) +
            ((uint32_t)(p[1]) << 16) + ((uint32_t)(p[0]) << 24));
    }

    static inline void be32enc(void* pp, uint32_t x)
    {
        uint8_t* p = (uint8_t*)pp;
        p[3] = x & 0xff;
        p[2] = (x >> 8) & 0xff;
        p[1] = (x >> 16) & 0xff;
        p[0] = (x >> 24) & 0xff;
    }*/

    void sha256d(unsigned char* hash, const unsigned char* data, int len);

    static inline uint32_t swab32(uint32_t v)
    {
#ifdef WANT_BUILTIN_BSWAP
        return __builtin_bswap32(v);
#else
        return bswap_32(v);
#endif
    }

    static inline uint64_t swab64(uint64_t v)
    {
#ifdef WANT_BUILTIN_BSWAP
        return __builtin_bswap64(v);
#else
        return bswap_64(v);
#endif
    }

    static inline void swab256(void* dest_p, const void* src_p)
    {
        uint32_t* dest = (uint32_t*)dest_p;
        const uint32_t* src = (const uint32_t*)src_p;

        dest[0] = swab32(src[7]);
        dest[1] = swab32(src[6]);
        dest[2] = swab32(src[5]);
        dest[3] = swab32(src[4]);
        dest[4] = swab32(src[3]);
        dest[5] = swab32(src[2]);
        dest[6] = swab32(src[1]);
        dest[7] = swab32(src[0]);
    }

#ifdef __cplusplus
}
#endif

#endif

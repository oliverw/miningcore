#include <assert.h>
#include <errno.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#include "sha256.h"
#include "sysendian.h"
#include "yespower.h"
#include "insecure_memzero.h"

#ifdef __ICC
/* Miscompile with icc 14.0.0 (at least), so don't use restrict there */
#define restrict
#elif __STDC_VERSION__ >= 199901L
/* Have restrict */
#elif defined(__GNUC__)
#define restrict __restrict
#else
#define restrict
#endif

/*
 * Encode a length len*2 vector of (uint32_t) into a length len*8 vector of
 * (uint8_t) in big-endian form.
 */
static void
be32enc_vect(uint8_t * dst, const uint32_t * src, size_t len)
{

	/* Encode vector, two words at a time. */
	do {
		be32enc(&dst[0], src[0]);
		be32enc(&dst[4], src[1]);
		src += 2;
		dst += 8;
	} while (--len);
}

/*
 * Decode a big-endian length len*8 vector of (uint8_t) into a length
 * len*2 vector of (uint32_t).
 */
static void
be32dec_vect(uint32_t * dst, const uint8_t * src, size_t len)
{

	/* Decode vector, two words at a time. */
	do {
		dst[0] = be32dec(&src[0]);
		dst[1] = be32dec(&src[4]);
		src += 8;
		dst += 2;
	} while (--len);
}

/* SHA256 round constants. */
static const uint32_t Krnd[64] = {
	0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5,
	0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
	0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3,
	0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
	0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc,
	0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
	0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7,
	0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
	0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13,
	0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
	0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3,
	0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
	0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5,
	0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
	0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208,
	0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
};

/* Elementary functions used by SHA256 */
#define Ch(x, y, z)	((x & (y ^ z)) ^ z)
#define Maj(x, y, z)	((x & (y | z)) | (y & z))
#define SHR(x, n)	(x >> n)
#define ROTR(x, n)	((x >> n) | (x << (32 - n)))
#define S0(x)		(ROTR(x, 2) ^ ROTR(x, 13) ^ ROTR(x, 22))
#define S1(x)		(ROTR(x, 6) ^ ROTR(x, 11) ^ ROTR(x, 25))
#define s0(x)		(ROTR(x, 7) ^ ROTR(x, 18) ^ SHR(x, 3))
#define s1(x)		(ROTR(x, 17) ^ ROTR(x, 19) ^ SHR(x, 10))

/* SHA256 round function */
#define RND(a, b, c, d, e, f, g, h, k)			\
	h += S1(e) + Ch(e, f, g) + k;			\
	d += h;						\
	h += S0(a) + Maj(a, b, c);

/* Adjusted round function for rotating state */
#define RNDr(S, W, i, ii)			\
	RND(S[(64 - i) % 8], S[(65 - i) % 8],	\
	    S[(66 - i) % 8], S[(67 - i) % 8],	\
	    S[(68 - i) % 8], S[(69 - i) % 8],	\
	    S[(70 - i) % 8], S[(71 - i) % 8],	\
	    W[i + ii] + Krnd[i + ii])

/* Message schedule computation */
#define MSCH(W, ii, i)				\
	W[i + ii + 16] = s1(W[i + ii + 14]) + W[i + ii + 9] + s0(W[i + ii + 1]) + W[i + ii]

/*
 * SHA256 block compression function.  The 256-bit state is transformed via
 * the 512-bit input block to produce a new state.
 */
static void
SHA256_Transform(uint32_t state[static restrict 8],
    const uint8_t block[static restrict 64],
    uint32_t W[static restrict 64], uint32_t S[static restrict 8])
{
	int i;

	/* 1. Prepare the first part of the message schedule W. */
	be32dec_vect(W, block, 8);

	/* 2. Initialize working variables. */
	memcpy(S, state, 32);

	/* 3. Mix. */
	for (i = 0; i < 64; i += 16) {
		RNDr(S, W, 0, i);
		RNDr(S, W, 1, i);
		RNDr(S, W, 2, i);
		RNDr(S, W, 3, i);
		RNDr(S, W, 4, i);
		RNDr(S, W, 5, i);
		RNDr(S, W, 6, i);
		RNDr(S, W, 7, i);
		RNDr(S, W, 8, i);
		RNDr(S, W, 9, i);
		RNDr(S, W, 10, i);
		RNDr(S, W, 11, i);
		RNDr(S, W, 12, i);
		RNDr(S, W, 13, i);
		RNDr(S, W, 14, i);
		RNDr(S, W, 15, i);

		if (i == 48)
			break;
		MSCH(W, 0, i);
		MSCH(W, 1, i);
		MSCH(W, 2, i);
		MSCH(W, 3, i);
		MSCH(W, 4, i);
		MSCH(W, 5, i);
		MSCH(W, 6, i);
		MSCH(W, 7, i);
		MSCH(W, 8, i);
		MSCH(W, 9, i);
		MSCH(W, 10, i);
		MSCH(W, 11, i);
		MSCH(W, 12, i);
		MSCH(W, 13, i);
		MSCH(W, 14, i);
		MSCH(W, 15, i);
	}

	/* 4. Mix local working variables into global state. */
	state[0] += S[0];
	state[1] += S[1];
	state[2] += S[2];
	state[3] += S[3];
	state[4] += S[4];
	state[5] += S[5];
	state[6] += S[6];
	state[7] += S[7];
}

static const uint8_t PAD[64] = {
	0x80, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
	0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
	0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
	0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
};

/* Add padding and terminating bit-count. */
static void
SHA256_Pad(SHA256_CTX * ctx, uint32_t tmp32[static restrict 72])
{
	size_t r;

	/* Figure out how many bytes we have buffered. */
	r = (ctx->count >> 3) & 0x3f;

	/* Pad to 56 mod 64, transforming if we finish a block en route. */
	if (r < 56) {
		/* Pad to 56 mod 64. */
		memcpy(&ctx->buf[r], PAD, 56 - r);
	} else {
		/* Finish the current block and mix. */
		memcpy(&ctx->buf[r], PAD, 64 - r);
		SHA256_Transform(ctx->state, ctx->buf, &tmp32[0], &tmp32[64]);

		/* The start of the final block is all zeroes. */
		memset(&ctx->buf[0], 0, 56);
	}

	/* Add the terminating bit-count. */
	be64enc(&ctx->buf[56], ctx->count);

	/* Mix in the final block. */
	SHA256_Transform(ctx->state, ctx->buf, &tmp32[0], &tmp32[64]);
}

/* Magic initialization constants. */
static const uint32_t initial_state[8] = {
	0x6A09E667, 0xBB67AE85, 0x3C6EF372, 0xA54FF53A,
	0x510E527F, 0x9B05688C, 0x1F83D9AB, 0x5BE0CD19
};

/**
 * SHA256_Init(ctx):
 * Initialize the SHA256 context ${ctx}.
 */
void
SHA256_Init(SHA256_CTX * ctx)
{

	/* Zero bits processed so far. */
	ctx->count = 0;

	/* Initialize state. */
	memcpy(ctx->state, initial_state, sizeof(initial_state));
}

/**
 * SHA256_Update(ctx, in, len):
 * Input ${len} bytes from ${in} into the SHA256 context ${ctx}.
 */
static void
_SHA256_Update(SHA256_CTX * ctx, const void * in, size_t len,
    uint32_t tmp32[static restrict 72])
{
	uint32_t r;
	const uint8_t * src = in;

	/* Return immediately if we have nothing to do. */
	if (len == 0)
		return;

	/* Number of bytes left in the buffer from previous updates. */
	r = (ctx->count >> 3) & 0x3f;

	/* Update number of bits. */
	ctx->count += (uint64_t)(len) << 3;

	/* Handle the case where we don't need to perform any transforms. */
	if (len < 64 - r) {
		memcpy(&ctx->buf[r], src, len);
		return;
	}

	/* Finish the current block. */
	memcpy(&ctx->buf[r], src, 64 - r);
	SHA256_Transform(ctx->state, ctx->buf, &tmp32[0], &tmp32[64]);
	src += 64 - r;
	len -= 64 - r;

	/* Perform complete blocks. */
	while (len >= 64) {
		SHA256_Transform(ctx->state, src, &tmp32[0], &tmp32[64]);
		src += 64;
		len -= 64;
	}

	/* Copy left over data into buffer. */
	memcpy(ctx->buf, src, len);
}

/* Wrapper function for intermediate-values sanitization. */
void
SHA256_Update(SHA256_CTX * ctx, const void * in, size_t len)
{
	uint32_t tmp32[72];

	/* Call the real function. */
	_SHA256_Update(ctx, in, len, tmp32);

	/* Clean the stack. */
	insecure_memzero(tmp32, 288);
}

/**
 * SHA256_Final(digest, ctx):
 * Output the SHA256 hash of the data input to the context ${ctx} into the
 * buffer ${digest}.
 */
static void
_SHA256_Final(uint8_t digest[32], SHA256_CTX * ctx,
    uint32_t tmp32[static restrict 72])
{

	/* Add padding. */
	SHA256_Pad(ctx, tmp32);

	/* Write the hash. */
	be32enc_vect(digest, ctx->state, 4);
}

/* Wrapper function for intermediate-values sanitization. */
void
SHA256_Final(uint8_t digest[32], SHA256_CTX * ctx)
{
	uint32_t tmp32[72];

	/* Call the real function. */
	_SHA256_Final(digest, ctx, tmp32);

	/* Clear the context state. */
	insecure_memzero(ctx, sizeof(SHA256_CTX));

	/* Clean the stack. */
	insecure_memzero(tmp32, 288);
}

/**
 * SHA256_Buf(in, len, digest):
 * Compute the SHA256 hash of ${len} bytes from ${in} and write it to ${digest}.
 */
void
SHA256_Buf(const void * in, size_t len, uint8_t digest[32])
{
	SHA256_CTX ctx;
	uint32_t tmp32[72];

	SHA256_Init(&ctx);
	_SHA256_Update(&ctx, in, len, tmp32);
	_SHA256_Final(digest, &ctx, tmp32);

	/* Clean the stack. */
	insecure_memzero(&ctx, sizeof(SHA256_CTX));
	insecure_memzero(tmp32, 288);
}

/**
 * HMAC_SHA256_Init(ctx, K, Klen):
 * Initialize the HMAC-SHA256 context ${ctx} with ${Klen} bytes of key from
 * ${K}.
 */
static void
_HMAC_SHA256_Init(HMAC_SHA256_CTX * ctx, const void * _K, size_t Klen,
    uint32_t tmp32[static restrict 72], uint8_t pad[static restrict 64],
    uint8_t khash[static restrict 32])
{
	const uint8_t * K = _K;
	size_t i;

	/* If Klen > 64, the key is really SHA256(K). */
	if (Klen > 64) {
		SHA256_Init(&ctx->ictx);
		_SHA256_Update(&ctx->ictx, K, Klen, tmp32);
		_SHA256_Final(khash, &ctx->ictx, tmp32);
		K = khash;
		Klen = 32;
	}

	/* Inner SHA256 operation is SHA256(K xor [block of 0x36] || data). */
	SHA256_Init(&ctx->ictx);
	memset(pad, 0x36, 64);
	for (i = 0; i < Klen; i++)
		pad[i] ^= K[i];
	_SHA256_Update(&ctx->ictx, pad, 64, tmp32);

	/* Outer SHA256 operation is SHA256(K xor [block of 0x5c] || hash). */
	SHA256_Init(&ctx->octx);
	memset(pad, 0x5c, 64);
	for (i = 0; i < Klen; i++)
		pad[i] ^= K[i];
	_SHA256_Update(&ctx->octx, pad, 64, tmp32);
}

/* Wrapper function for intermediate-values sanitization. */
void
HMAC_SHA256_Init(HMAC_SHA256_CTX * ctx, const void * _K, size_t Klen)
{
	uint32_t tmp32[72];
	uint8_t pad[64];
	uint8_t khash[32];

	/* Call the real function. */
	_HMAC_SHA256_Init(ctx, _K, Klen, tmp32, pad, khash);

	/* Clean the stack. */
	insecure_memzero(tmp32, 288);
	insecure_memzero(khash, 32);
	insecure_memzero(pad, 64);
}

/**
 * HMAC_SHA256_Update(ctx, in, len):
 * Input ${len} bytes from ${in} into the HMAC-SHA256 context ${ctx}.
 */
static void
_HMAC_SHA256_Update(HMAC_SHA256_CTX * ctx, const void * in, size_t len,
    uint32_t tmp32[static restrict 72])
{

	/* Feed data to the inner SHA256 operation. */
	_SHA256_Update(&ctx->ictx, in, len, tmp32);
}

/* Wrapper function for intermediate-values sanitization. */
void
HMAC_SHA256_Update(HMAC_SHA256_CTX * ctx, const void * in, size_t len)
{
	uint32_t tmp32[72];

	/* Call the real function. */
	_HMAC_SHA256_Update(ctx, in, len, tmp32);

	/* Clean the stack. */
	insecure_memzero(tmp32, 288);
}

/**
 * HMAC_SHA256_Final(digest, ctx):
 * Output the HMAC-SHA256 of the data input to the context ${ctx} into the
 * buffer ${digest}.
 */
static void
_HMAC_SHA256_Final(uint8_t digest[32], HMAC_SHA256_CTX * ctx,
    uint32_t tmp32[static restrict 72], uint8_t ihash[static restrict 32])
{

	/* Finish the inner SHA256 operation. */
	_SHA256_Final(ihash, &ctx->ictx, tmp32);

	/* Feed the inner hash to the outer SHA256 operation. */
	_SHA256_Update(&ctx->octx, ihash, 32, tmp32);

	/* Finish the outer SHA256 operation. */
	_SHA256_Final(digest, &ctx->octx, tmp32);
}

/* Wrapper function for intermediate-values sanitization. */
void
HMAC_SHA256_Final(uint8_t digest[32], HMAC_SHA256_CTX * ctx)
{
	uint32_t tmp32[72];
	uint8_t ihash[32];

	/* Call the real function. */
	_HMAC_SHA256_Final(digest, ctx, tmp32, ihash);

	/* Clean the stack. */
	insecure_memzero(tmp32, 288);
	insecure_memzero(ihash, 32);
}

/**
 * HMAC_SHA256_Buf(K, Klen, in, len, digest):
 * Compute the HMAC-SHA256 of ${len} bytes from ${in} using the key ${K} of
 * length ${Klen}, and write the result to ${digest}.
 */
void
HMAC_SHA256_Buf(const void * K, size_t Klen, const void * in, size_t len,
    uint8_t digest[32])
{
	HMAC_SHA256_CTX ctx;
	uint32_t tmp32[72];
	uint8_t tmp8[96];

	_HMAC_SHA256_Init(&ctx, K, Klen, tmp32, &tmp8[0], &tmp8[64]);
	_HMAC_SHA256_Update(&ctx, in, len, tmp32);
	_HMAC_SHA256_Final(digest, &ctx, tmp32, &tmp8[0]);

	/* Clean the stack. */
	insecure_memzero(&ctx, sizeof(HMAC_SHA256_CTX));
	insecure_memzero(tmp32, 288);
	insecure_memzero(tmp8, 96);
}

/* Add padding and terminating bit-count, but don't invoke Transform yet. */
static int
SHA256_Pad_Almost(SHA256_CTX * ctx, uint8_t len[static restrict 8],
    uint32_t tmp32[static restrict 72])
{
	uint32_t r;

	r = (ctx->count >> 3) & 0x3f;
	if (r >= 56)
		return -1;

	/*
	 * Convert length to a vector of bytes -- we do this now rather
	 * than later because the length will change after we pad.
	 */
	be64enc(len, ctx->count);

	/* Add 1--56 bytes so that the resulting length is 56 mod 64. */
	_SHA256_Update(ctx, PAD, 56 - r, tmp32);

	/* Add the terminating bit-count. */
	ctx->buf[63] = len[7];
	_SHA256_Update(ctx, len, 7, tmp32);

	return 0;
}

/**
 * PBKDF2_SHA256(passwd, passwdlen, salt, saltlen, c, buf, dkLen):
 * Compute PBKDF2(passwd, salt, c, dkLen) using HMAC-SHA256 as the PRF, and
 * write the output to buf.  The value dkLen must be at most 32 * (2^32 - 1).
 */
void
PBKDF2_SHA256(const uint8_t * passwd, size_t passwdlen, const uint8_t * salt,
    size_t saltlen, uint64_t c, uint8_t * buf, size_t dkLen)
{
	HMAC_SHA256_CTX Phctx, PShctx, hctx;
	uint32_t tmp32[72];
	union {
		uint8_t tmp8[96];
		uint32_t state[8];
	} u;
	size_t i;
	uint8_t ivec[4];
	uint8_t U[32];
	uint8_t T[32];
	uint64_t j;
	int k;
	size_t clen;

	/* Sanity-check. */
	assert(dkLen <= 32 * (size_t)(UINT32_MAX));

	if (c == 1 && (dkLen & 31) == 0 && (saltlen & 63) <= 51) {
		uint32_t oldcount;
		uint8_t * ivecp;

		/* Compute HMAC state after processing P and S. */
		_HMAC_SHA256_Init(&hctx, passwd, passwdlen,
		    tmp32, &u.tmp8[0], &u.tmp8[64]);
		_HMAC_SHA256_Update(&hctx, salt, saltlen, tmp32);

		/* Prepare ictx padding. */
		oldcount = hctx.ictx.count & (0x3f << 3);
		_HMAC_SHA256_Update(&hctx, "\0\0\0", 4, tmp32);
		if ((hctx.ictx.count & (0x3f << 3)) < oldcount ||
		    SHA256_Pad_Almost(&hctx.ictx, u.tmp8, tmp32))
			goto generic; /* Can't happen due to saltlen check */
		ivecp = hctx.ictx.buf + (oldcount >> 3);

		/* Prepare octx padding. */
		hctx.octx.count += 32 << 3;
		SHA256_Pad_Almost(&hctx.octx, u.tmp8, tmp32);

		/* Iterate through the blocks. */
		for (i = 0; i * 32 < dkLen; i++) {
			/* Generate INT(i + 1). */
			be32enc(ivecp, (uint32_t)(i + 1));

			/* Compute U_1 = PRF(P, S || INT(i)). */
			memcpy(u.state, hctx.ictx.state, sizeof(u.state));
			SHA256_Transform(u.state, hctx.ictx.buf,
			    &tmp32[0], &tmp32[64]);
			be32enc_vect(hctx.octx.buf, u.state, 4);
			memcpy(u.state, hctx.octx.state, sizeof(u.state));
			SHA256_Transform(u.state, hctx.octx.buf,
			    &tmp32[0], &tmp32[64]);
			be32enc_vect(&buf[i * 32], u.state, 4);
		}

		goto cleanup;
	}

generic:
	/* Compute HMAC state after processing P. */
	_HMAC_SHA256_Init(&Phctx, passwd, passwdlen,
	    tmp32, &u.tmp8[0], &u.tmp8[64]);

	/* Compute HMAC state after processing P and S. */
	memcpy(&PShctx, &Phctx, sizeof(HMAC_SHA256_CTX));
	_HMAC_SHA256_Update(&PShctx, salt, saltlen, tmp32);

	/* Iterate through the blocks. */
	for (i = 0; i * 32 < dkLen; i++) {
		/* Generate INT(i + 1). */
		be32enc(ivec, (uint32_t)(i + 1));

		/* Compute U_1 = PRF(P, S || INT(i)). */
		memcpy(&hctx, &PShctx, sizeof(HMAC_SHA256_CTX));
		_HMAC_SHA256_Update(&hctx, ivec, 4, tmp32);
		_HMAC_SHA256_Final(T, &hctx, tmp32, u.tmp8);

		if (c > 1) {
			/* T_i = U_1 ... */
			memcpy(U, T, 32);

			for (j = 2; j <= c; j++) {
				/* Compute U_j. */
				memcpy(&hctx, &Phctx, sizeof(HMAC_SHA256_CTX));
				_HMAC_SHA256_Update(&hctx, U, 32, tmp32);
				_HMAC_SHA256_Final(U, &hctx, tmp32, u.tmp8);

				/* ... xor U_j ... */
				for (k = 0; k < 32; k++)
					T[k] ^= U[k];
			}
		}

		/* Copy as many bytes as necessary into buf. */
		clen = dkLen - i * 32;
		if (clen > 32)
			clen = 32;
		memcpy(&buf[i * 32], T, clen);
	}

	/* Clean the stack. */
	insecure_memzero(&Phctx, sizeof(HMAC_SHA256_CTX));
	insecure_memzero(&PShctx, sizeof(HMAC_SHA256_CTX));
	insecure_memzero(U, 32);
	insecure_memzero(T, 32);

cleanup:
	insecure_memzero(&hctx, sizeof(HMAC_SHA256_CTX));
	insecure_memzero(tmp32, 288);
	insecure_memzero(&u, sizeof(u));
}

static void blkcpy(uint32_t *dst, const uint32_t *src, size_t count)
{
	do {
		*dst++ = *src++;
	} while (--count);
}

static void blkxor(uint32_t *dst, const uint32_t *src, size_t count)
{
	do {
		*dst++ ^= *src++;
	} while (--count);
}

/**
 * salsa20(B):
 * Apply the Salsa20 core to the provided block.
 */
static void salsa20(uint32_t B[16], uint32_t rounds)
{
	uint32_t x[16];
	size_t i;

	/* SIMD unshuffle */
	for (i = 0; i < 16; i++)
		x[i * 5 % 16] = B[i];

	for (i = 0; i < rounds; i += 2) {
#define R(a,b) (((a) << (b)) | ((a) >> (32 - (b))))
		/* Operate on columns */
		x[ 4] ^= R(x[ 0]+x[12], 7);  x[ 8] ^= R(x[ 4]+x[ 0], 9);
		x[12] ^= R(x[ 8]+x[ 4],13);  x[ 0] ^= R(x[12]+x[ 8],18);

		x[ 9] ^= R(x[ 5]+x[ 1], 7);  x[13] ^= R(x[ 9]+x[ 5], 9);
		x[ 1] ^= R(x[13]+x[ 9],13);  x[ 5] ^= R(x[ 1]+x[13],18);

		x[14] ^= R(x[10]+x[ 6], 7);  x[ 2] ^= R(x[14]+x[10], 9);
		x[ 6] ^= R(x[ 2]+x[14],13);  x[10] ^= R(x[ 6]+x[ 2],18);

		x[ 3] ^= R(x[15]+x[11], 7);  x[ 7] ^= R(x[ 3]+x[15], 9);
		x[11] ^= R(x[ 7]+x[ 3],13);  x[15] ^= R(x[11]+x[ 7],18);

		/* Operate on rows */
		x[ 1] ^= R(x[ 0]+x[ 3], 7);  x[ 2] ^= R(x[ 1]+x[ 0], 9);
		x[ 3] ^= R(x[ 2]+x[ 1],13);  x[ 0] ^= R(x[ 3]+x[ 2],18);

		x[ 6] ^= R(x[ 5]+x[ 4], 7);  x[ 7] ^= R(x[ 6]+x[ 5], 9);
		x[ 4] ^= R(x[ 7]+x[ 6],13);  x[ 5] ^= R(x[ 4]+x[ 7],18);

		x[11] ^= R(x[10]+x[ 9], 7);  x[ 8] ^= R(x[11]+x[10], 9);
		x[ 9] ^= R(x[ 8]+x[11],13);  x[10] ^= R(x[ 9]+x[ 8],18);

		x[12] ^= R(x[15]+x[14], 7);  x[13] ^= R(x[12]+x[15], 9);
		x[14] ^= R(x[13]+x[12],13);  x[15] ^= R(x[14]+x[13],18);
#undef R
	}

	/* SIMD shuffle */
	for (i = 0; i < 16; i++)
		B[i] += x[i * 5 % 16];
}

/**
 * blockmix_salsa(B):
 * Compute B = BlockMix_{salsa20, 1}(B).  The input B must be 128 bytes in
 * length.
 */
static void blockmix_salsa(uint32_t *B, uint32_t rounds)
{
	uint32_t X[16];
	size_t i;

	/* 1: X <-- B_{2r - 1} */
	blkcpy(X, &B[16], 16);

	/* 2: for i = 0 to 2r - 1 do */
	for (i = 0; i < 2; i++) {
		/* 3: X <-- H(X xor B_i) */
		blkxor(X, &B[i * 16], 16);
		salsa20(X, rounds);

		/* 4: Y_i <-- X */
		/* 6: B' <-- (Y_0, Y_2 ... Y_{2r-2}, Y_1, Y_3 ... Y_{2r-1}) */
		blkcpy(&B[i * 16], X, 16);
	}
}

/*
 * These are tunable, but they must meet certain constraints and are part of
 * what defines a yespower version.
 */
#define PWXsimple 2
#define PWXgather 4
/* Version 0.5 */
#define PWXrounds_0_5 6
#define Swidth_0_5 8
/* Version 1.0 */
#define PWXrounds_1_0 3
#define Swidth_1_0 11

/* Derived values.  Not tunable on their own. */
#define PWXbytes (PWXgather * PWXsimple * 8)
#define PWXwords (PWXbytes / sizeof(uint32_t))
#define rmin ((PWXbytes + 127) / 128)

/* Runtime derived values.  Not tunable on their own. */
#define Swidth_to_Sbytes1(Swidth) ((1 << Swidth) * PWXsimple * 8)
#define Swidth_to_Smask(Swidth) (((1 << Swidth) - 1) * PWXsimple * 8)

typedef struct {
	yespower_version_t version;
	uint32_t salsa20_rounds;
	uint32_t PWXrounds, Swidth, Sbytes, Smask;
	uint32_t *S;
	uint32_t (*S0)[2], (*S1)[2], (*S2)[2];
	size_t w;
} pwxform_ctx_t;

/**
 * pwxform(B):
 * Transform the provided block using the provided S-boxes.
 */
static void pwxform(uint32_t *B, pwxform_ctx_t *ctx)
{
	uint32_t (*X)[PWXsimple][2] = (uint32_t (*)[PWXsimple][2])B;
	uint32_t (*S0)[2] = ctx->S0, (*S1)[2] = ctx->S1, (*S2)[2] = ctx->S2;
	uint32_t Smask = ctx->Smask;
	size_t w = ctx->w;
	size_t i, j, k;

	/* 1: for i = 0 to PWXrounds - 1 do */
	for (i = 0; i < ctx->PWXrounds; i++) {
		/* 2: for j = 0 to PWXgather - 1 do */
		for (j = 0; j < PWXgather; j++) {
			uint32_t xl = X[j][0][0];
			uint32_t xh = X[j][0][1];
			uint32_t (*p0)[2], (*p1)[2];

			/* 3: p0 <-- (lo(B_{j,0}) & Smask) / (PWXsimple * 8) */
			p0 = S0 + (xl & Smask) / sizeof(*S0);
			/* 4: p1 <-- (hi(B_{j,0}) & Smask) / (PWXsimple * 8) */
			p1 = S1 + (xh & Smask) / sizeof(*S1);

			/* 5: for k = 0 to PWXsimple - 1 do */
			for (k = 0; k < PWXsimple; k++) {
				uint64_t x, s0, s1;

				/* 6: B_{j,k} <-- (hi(B_{j,k}) * lo(B_{j,k}) + S0_{p0,k}) xor S1_{p1,k} */
				s0 = ((uint64_t)p0[k][1] << 32) + p0[k][0];
				s1 = ((uint64_t)p1[k][1] << 32) + p1[k][0];

				xl = X[j][k][0];
				xh = X[j][k][1];

				x = (uint64_t)xh * xl;
				x += s0;
				x ^= s1;

				X[j][k][0] = x;
				X[j][k][1] = x >> 32;
			}

			if (ctx->version != YESPOWER_0_5 &&
			    (i == 0 || j < PWXgather / 2)) {
				if (j & 1) {
					for (k = 0; k < PWXsimple; k++) {
						S1[w][0] = X[j][k][0];
						S1[w][1] = X[j][k][1];
						w++;
					}
				} else {
					for (k = 0; k < PWXsimple; k++) {
						S0[w + k][0] = X[j][k][0];
						S0[w + k][1] = X[j][k][1];
					}
				}
			}
		}
	}

	if (ctx->version != YESPOWER_0_5) {
		/* 14: (S0, S1, S2) <-- (S2, S0, S1) */
		ctx->S0 = S2;
		ctx->S1 = S0;
		ctx->S2 = S1;
		/* 15: w <-- w mod 2^Swidth */
		ctx->w = w & ((1 << ctx->Swidth) * PWXsimple - 1);
	}
}

/**
 * blockmix_pwxform(B, ctx, r):
 * Compute B = BlockMix_pwxform{salsa20, ctx, r}(B).  The input B must be
 * 128r bytes in length.
 */
static void blockmix_pwxform(uint32_t *B, pwxform_ctx_t *ctx, size_t r)
{
	uint32_t X[PWXwords];
	size_t r1, i;

	/* Convert 128-byte blocks to PWXbytes blocks */
	/* 1: r_1 <-- 128r / PWXbytes */
	r1 = 128 * r / PWXbytes;

	/* 2: X <-- B'_{r_1 - 1} */
	blkcpy(X, &B[(r1 - 1) * PWXwords], PWXwords);

	/* 3: for i = 0 to r_1 - 1 do */
	for (i = 0; i < r1; i++) {
		/* 4: if r_1 > 1 */
		if (r1 > 1) {
			/* 5: X <-- X xor B'_i */
			blkxor(X, &B[i * PWXwords], PWXwords);
		}

		/* 7: X <-- pwxform(X) */
		pwxform(X, ctx);

		/* 8: B'_i <-- X */
		blkcpy(&B[i * PWXwords], X, PWXwords);
	}

	/* 10: i <-- floor((r_1 - 1) * PWXbytes / 64) */
	i = (r1 - 1) * PWXbytes / 64;

	/* 11: B_i <-- H(B_i) */
	salsa20(&B[i * 16], ctx->salsa20_rounds);

#if 1 /* No-op with our current pwxform settings, but do it to make sure */
	/* 12: for i = i + 1 to 2r - 1 do */
	for (i++; i < 2 * r; i++) {
		/* 13: B_i <-- H(B_i xor B_{i-1}) */
		blkxor(&B[i * 16], &B[(i - 1) * 16], 16);
		salsa20(&B[i * 16], ctx->salsa20_rounds);
	}
#endif
}

/**
 * integerify(B, r):
 * Return the result of parsing B_{2r-1} as a little-endian integer.
 */
static uint32_t integerify(const uint32_t *B, size_t r)
{
/*
 * Our 32-bit words are in host byte order.  Also, they are SIMD-shuffled, but
 * we only care about the least significant 32 bits anyway.
 */
	const uint32_t *X = &B[(2 * r - 1) * 16];
	return X[0];
}

/**
 * p2floor(x):
 * Largest power of 2 not greater than argument.
 */
static uint32_t p2floor(uint32_t x)
{
	uint32_t y;
	while ((y = x & (x - 1)))
		x = y;
	return x;
}

/**
 * wrap(x, i):
 * Wrap x to the range 0 to i-1.
 */
static uint32_t wrap(uint32_t x, uint32_t i)
{
	uint32_t n = p2floor(i);
	return (x & (n - 1)) + (i - n);
}

/**
 * smix1(B, r, N, V, X, ctx):
 * Compute first loop of B = SMix_r(B, N).  The input B must be 128r bytes in
 * length; the temporary storage V must be 128rN bytes in length; the temporary
 * storage X must be 128r bytes in length.
 */
static void smix1(uint32_t *B, size_t r, uint32_t N,
    uint32_t *V, uint32_t *X, pwxform_ctx_t *ctx)
{
	size_t s = 32 * r;
	uint32_t i, j;
	size_t k;

	/* 1: X <-- B */
	for (k = 0; k < 2 * r; k++)
		for (i = 0; i < 16; i++)
			X[k * 16 + i] = le32dec(&B[k * 16 + (i * 5 % 16)]);

	if (ctx->version != YESPOWER_0_5) {
		for (k = 1; k < r; k++) {
			blkcpy(&X[k * 32], &X[(k - 1) * 32], 32);
			blockmix_pwxform(&X[k * 32], ctx, 1);
		}
	}

	/* 2: for i = 0 to N - 1 do */
	for (i = 0; i < N; i++) {
		/* 3: V_i <-- X */
		blkcpy(&V[i * s], X, s);

		if (i > 1) {
			/* j <-- Wrap(Integerify(X), i) */
			j = wrap(integerify(X, r), i);

			/* X <-- X xor V_j */
			blkxor(X, &V[j * s], s);
		}

		/* 4: X <-- H(X) */
		if (V != ctx->S)
			blockmix_pwxform(X, ctx, r);
		else
			blockmix_salsa(X, ctx->salsa20_rounds);
	}

	/* B' <-- X */
	for (k = 0; k < 2 * r; k++)
		for (i = 0; i < 16; i++)
			le32enc(&B[k * 16 + (i * 5 % 16)], X[k * 16 + i]);
}

/**
 * smix2(B, r, N, Nloop, V, X, ctx):
 * Compute second loop of B = SMix_r(B, N).  The input B must be 128r bytes in
 * length; the temporary storage V must be 128rN bytes in length; the temporary
 * storage X must be 128r bytes in length.  The value N must be a power of 2
 * greater than 1.
 */
static void smix2(uint32_t *B, size_t r, uint32_t N, uint32_t Nloop,
    uint32_t *V, uint32_t *X, pwxform_ctx_t *ctx)
{
	size_t s = 32 * r;
	uint32_t i, j;
	size_t k;

	/* X <-- B */
	for (k = 0; k < 2 * r; k++)
		for (i = 0; i < 16; i++)
			X[k * 16 + i] = le32dec(&B[k * 16 + (i * 5 % 16)]);

	/* 6: for i = 0 to N - 1 do */
	for (i = 0; i < Nloop; i++) {
		/* 7: j <-- Integerify(X) mod N */
		j = integerify(X, r) & (N - 1);

		/* 8.1: X <-- X xor V_j */
		blkxor(X, &V[j * s], s);
		/* V_j <-- X */
		if (Nloop != 2)
			blkcpy(&V[j * s], X, s);

		/* 8.2: X <-- H(X) */
		blockmix_pwxform(X, ctx, r);
	}

	/* 10: B' <-- X */
	for (k = 0; k < 2 * r; k++)
		for (i = 0; i < 16; i++)
			le32enc(&B[k * 16 + (i * 5 % 16)], X[k * 16 + i]);
}

/**
 * smix(B, r, N, p, t, V, X, ctx):
 * Compute B = SMix_r(B, N).  The input B must be 128rp bytes in length; the
 * temporary storage V must be 128rN bytes in length; the temporary storage
 * X must be 128r bytes in length.  The value N must be a power of 2 and at
 * least 16.
 */
static void smix(uint32_t *B, size_t r, uint32_t N,
    uint32_t *V, uint32_t *X, pwxform_ctx_t *ctx)
{
	uint32_t Nloop_all = (N + 2) / 3; /* 1/3, round up */
	uint32_t Nloop_rw = Nloop_all;

	Nloop_all++; Nloop_all &= ~(uint32_t)1; /* round up to even */
	if (ctx->version == YESPOWER_0_5) {
		Nloop_rw &= ~(uint32_t)1; /* round down to even */
	} else {
		Nloop_rw++; Nloop_rw &= ~(uint32_t)1; /* round up to even */
	}

	smix1(B, 1, ctx->Sbytes / 128, ctx->S, X, ctx);
	smix1(B, r, N, V, X, ctx);
	smix2(B, r, N, Nloop_rw /* must be > 2 */, V, X, ctx);
	smix2(B, r, N, Nloop_all - Nloop_rw /* 0 or 2 */, V, X, ctx);
}

/**
 * yespower(local, src, srclen, params, dst):
 * Compute yespower(src[0 .. srclen - 1], N, r), to be checked for "< target".
 *
 * Return 0 on success; or -1 on error.
 */
int yespower(yespower_local_t *local,
    const uint8_t *src, size_t srclen,
    const yespower_params_t *params, yespower_binary_t *dst)
{
	yespower_version_t version = params->version;
	uint32_t N = params->N;
	uint32_t r = params->r;
	const uint8_t *pers = params->pers;
	size_t perslen = params->perslen;
	int retval = -1;
	size_t B_size, V_size;
	uint32_t *B, *V, *X, *S;
	pwxform_ctx_t ctx;
	uint32_t sha256[8];

	/* Sanity-check parameters */
	if ((version != YESPOWER_0_5 && version != YESPOWER_1_0) ||
	    N < 1024 || N > 512 * 1024 || r < 8 || r > 32 ||
	    (N & (N - 1)) != 0 || r < rmin ||
	    (!pers && perslen)) {
		errno = EINVAL;
		return -1;
	}

	/* Allocate memory */
	B_size = (size_t)128 * r;
	V_size = B_size * N;
	if ((V = malloc(V_size)) == NULL)
		return -1;
	if ((B = malloc(B_size)) == NULL)
		goto free_V;
	if ((X = malloc(B_size)) == NULL)
		goto free_B;
	ctx.version = version;
	if (version == YESPOWER_0_5) {
		ctx.salsa20_rounds = 8;
		ctx.PWXrounds = PWXrounds_0_5;
		ctx.Swidth = Swidth_0_5;
		ctx.Sbytes = 2 * Swidth_to_Sbytes1(ctx.Swidth);
	} else {
		ctx.salsa20_rounds = 2;
		ctx.PWXrounds = PWXrounds_1_0;
		ctx.Swidth = Swidth_1_0;
		ctx.Sbytes = 3 * Swidth_to_Sbytes1(ctx.Swidth);
	}
	if ((S = malloc(ctx.Sbytes)) == NULL)
		goto free_X;
	ctx.S = S;
	ctx.S0 = (uint32_t (*)[2])S;
	ctx.S1 = ctx.S0 + (1 << ctx.Swidth) * PWXsimple;
	ctx.S2 = ctx.S1 + (1 << ctx.Swidth) * PWXsimple;
	ctx.Smask = Swidth_to_Smask(ctx.Swidth);
	ctx.w = 0;

	SHA256_Buf(src, srclen, (uint8_t *)sha256);

	if (version != YESPOWER_0_5) {
		if (pers) {
			src = pers;
			srclen = perslen;
		} else {
			srclen = 0;
		}
	}

	/* 1: (B_0 ... B_{p-1}) <-- PBKDF2(P, S, 1, p * MFLen) */
	PBKDF2_SHA256((uint8_t *)sha256, sizeof(sha256),
	    src, srclen, 1, (uint8_t *)B, B_size);

	blkcpy(sha256, B, sizeof(sha256) / sizeof(sha256[0]));

	/* 3: B_i <-- MF(B_i, N) */
	smix(B, r, N, V, X, &ctx);

	if (version == YESPOWER_0_5) {
		/* 5: DK <-- PBKDF2(P, B, 1, dkLen) */
		PBKDF2_SHA256((uint8_t *)sha256, sizeof(sha256),
		    (uint8_t *)B, B_size, 1, (uint8_t *)dst, sizeof(*dst));

		if (pers) {
			HMAC_SHA256_Buf(dst, sizeof(*dst), pers, perslen,
			    (uint8_t *)sha256);
			SHA256_Buf(sha256, sizeof(sha256), (uint8_t *)dst);
		}
	} else {
		HMAC_SHA256_Buf((uint8_t *)B + B_size - 64, 64,
		    sha256, sizeof(sha256), (uint8_t *)dst);
	}

	/* Success! */
	retval = 0;

	/* Free memory */
	free(S);
free_X:
	free(X);
free_B:
	free(B);
free_V:
	free(V);

	return retval;
}

int yespower_tls(const uint8_t *src, size_t srclen,
    const yespower_params_t *params, yespower_binary_t *dst)
{
/* The reference implementation doesn't use thread-local storage */
	return yespower(NULL, src, srclen, params, dst);
}

int yespower_init_local(yespower_local_t *local)
{
/* The reference implementation doesn't use the local structure */
	local->base = local->aligned = NULL;
	local->base_size = local->aligned_size = 0;
	return 0;
}

int yespower_free_local(yespower_local_t *local)
{
/* The reference implementation frees its memory in yespower() */
	(void)local; /* unused */
	return 0;
}

void yespower_hash(const char* input, char* output, uint32_t len)
{
    yespower_params_t yespower_1_0 = {
        .version = YESPOWER_1_0,
        .N = 2048,
        .r = 32,
        .pers = NULL,
        .perslen = 0
    };
    yespower_tls(input, 80, &yespower_1_0, (yespower_binary_t *)output);
}

void yespowerIC_hash(const char* input, char* output, uint32_t len)
{
    yespower_params_t yespower_1_0_isotopec = {
        .version = YESPOWER_1_0,
        .N = 2048,
        .r = 32,
        .pers = (const uint8_t *)"IsotopeC",
        .perslen = 8
    };
    yespower_tls(input, 80, &yespower_1_0_isotopec, (yespower_binary_t *)output);
}

void yespowerIOTS_hash(const char* input, char* output, uint32_t len)
{
    yespower_params_t yespower_1_0_iots = {
        .version = YESPOWER_1_0,
        .N = 2048,
        .r = 32,
        .pers = (const uint8_t *)"Iots is committed to the development of IOT",
        .perslen = 43
    };
    yespower_tls(input, 80, &yespower_1_0_iots, (yespower_binary_t *)output);
}

void yespowerR16_hash(const char* input, char* output, uint32_t len)
{
    yespower_params_t yespower_1_0_r16 = {
        .version = YESPOWER_1_0,
        .N = 4096,
        .r = 16,
        .pers = NULL,
        .perslen = 0
    };
    yespower_tls(input, 80, &yespower_1_0_r16, (yespower_binary_t *)output);
}

void yespowerRES_hash(const char* input, char* output, uint32_t len)
{
    yespower_params_t yespower_1_0_resistance = {
        .version = YESPOWER_1_0,
        .N = 4096,
        .r = 32,
        .pers = NULL,
        .perslen = 0
    };
    yespower_tls(input, 140, &yespower_1_0_resistance, (yespower_binary_t *)output);
}

void yespowerSUGAR_hash(const char* input, char* output, uint32_t len)
{
    yespower_params_t yespower_1_0_sugarchain = {
        .version = YESPOWER_1_0,
        .N = 2048,
        .r = 32,
        .pers = (const uint8_t *)"Satoshi Nakamoto 31/Oct/2008 Proof-of-work is essentially one-CPU-one-vote",
        .perslen = 74
    };
    yespower_tls(input, 80, &yespower_1_0_sugarchain, (yespower_binary_t *)output);
}

void yespowerURX_hash(const char* input, char* output, uint32_t len)
{
    yespower_params_t yespower_1_0_uraniumx = {
        .version = YESPOWER_1_0,
        .N = 2048,
        .r = 32,
        .pers = (const uint8_t *)"UraniumX",
        .perslen = 8 
    };
    yespower_tls( input, len, &yespower_1_0_uraniumx, (yespower_binary_t *)output);
}

void yespowerLTNCG_hash(const char* input, char* output, uint32_t len)
{
    yespower_params_t yespower_1_0_ltncg = {
        .version = YESPOWER_1_0,
        .N = 2048,
        .r = 32,
        .pers = (const uint8_t *)"LTNCGYES",
        .perslen = 8 
    };
    yespower_tls( input, len, &yespower_1_0_ltncg, (yespower_binary_t *)output);
}

void yespowerLITB_hash(const char* input, char* output, uint32_t len)
{
    yespower_params_t yespower_1_0_litb = {
        .version = YESPOWER_1_0,
	.N = 2048,
	.r = 32,
	.pers = "LITBpower: The number of LITB working or available for proof-of-work mining",
	.perslen = 73 
    };
    yespower_tls( input, len, &yespower_1_0_litb, (yespower_binary_t *)output);
}

void yespowerTIDE_hash(const char* input, char* output, uint32_t len)
{
    yespower_params_t yespower_1_0_tide = 
	{
		.version = YESPOWER_1_0,
		.N = 2048,
		.r = 8,
		.pers = NULL,
		.perslen = 0 
    };
    yespower_tls( input, len, &yespower_1_0_tide, (yespower_binary_t *)output);
}

void cpupower_hash(const char* input, char* output, uint32_t len)
{
    yespower_params_t yespower_1_0_cpupower = 
	{
		.version = YESPOWER_1_0,
		.N = 2048,
		.r = 32,
		.pers = "CPUpower: The number of CPU working or available for proof-of-work mining",
		.perslen = 73 
    };
    yespower_tls( input, len, &yespower_1_0_cpupower, (yespower_binary_t *)output);
}

/* $Id: sph_gost.h 216 2010-06-08 09:46:57Z tp $ */
/**
 * GOST interface. This is the interface for GOST R 12 with the
 * recommended parameters for SHA-3, with output lengths 256
 * and 512 bits.
 *
 * ==========================(LICENSE BEGIN)============================
 *
 * Copyright (c) 2007-2010  Projet RNRT SAPHIR
 *
 * Permission is hereby granted, free of charge, to any person obtaining
 * a copy of this software and associated documentation files (the
 * "Software"), to deal in the Software without restriction, including
 * without limitation the rights to use, copy, modify, merge, publish,
 * distribute, sublicense, and/or sell copies of the Software, and to
 * permit persons to whom the Software is furnished to do so, subject to
 * the following conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
 * IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
 * CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
 * TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
 * SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 *
 * ===========================(LICENSE END)=============================
 *
 * @file     sph_gost.h
 * @author   Mish <mish@btchouse.com>
 */

#ifndef SPH_GOST_H__
#define SPH_GOST_H__

#ifdef __cplusplus
extern "C"{
#endif

#include <stddef.h>
#include "sph_types.h"

/**
 * Output size (in bits) for GOST-256.
 */
#define SPH_SIZE_gost256   256

/**
 * Output size (in bits) for GOST-512.
 */
#define SPH_SIZE_gost512   512

/**
 * This structure is a context for Keccak computations: it contains the
 * intermediate values and some data from the last entered block. Once a
 * GOST computation has been performed, the context can be reused for
 * another computation.
 *
 * The contents of this structure are private. A running GOST computation
 * can be cloned by copying the context (e.g. with a simple
 * <code>memcpy()</code>).
 */

/**
 * This structure is a context for Gost-256 computations.
 */

typedef struct {
#ifndef DOXYGEN_IGNORE
	unsigned char buf[32];    /* first field, for alignment */
	size_t ptr;
	sph_u32 V[3][8];
#endif
} sph_gost256_context;

/**
 * This structure is a context for Gost-512 computations.
 */
typedef struct {
#ifndef DOXYGEN_IGNORE
	unsigned char buf[64];    /* first field, for alignment */
	size_t ptr;
	sph_u32 V[5][8];
#endif
} sph_gost512_context;


/**
 * Initialize a GOST-256 context. This process performs no memory allocation.
 *
 * @param cc   the GOST-256 context (pointer to a
 *             <code>sph_gost256_context</code>)
 */
void sph_gost256_init(void *cc);

/**
 * Process some data bytes. It is acceptable that <code>len</code> is zero
 * (in which case this function does nothing).
 *
 * @param cc     the Gost-256 context
 * @param data   the input data
 * @param len    the input data length (in bytes)
 */
void sph_gost256(void *cc, const void *data, size_t len);

/**
 * Terminate the current GOST-256 computation and output the result into
 * the provided buffer. The destination buffer must be wide enough to
 * accomodate the result (32 bytes). The context is automatically
 * reinitialized.
 *
 * @param cc    the GOST-256 context
 * @param dst   the destination buffer
 */
void sph_gost256_close(void *cc, void *dst);

/**
 * Add a few additional bits (0 to 7) to the current computation, then
 * terminate it and output the result in the provided buffer, which must
 * be wide enough to accomodate the result (32 bytes). If bit number i
 * in <code>ub</code> has value 2^i, then the extra bits are those
 * numbered 7 downto 8-n (this is the big-endian convention at the byte
 * level). The context is automatically reinitialized.
 *
 * @param cc    the GOST-256 context
 * @param ub    the extra bits
 * @param n     the number of extra bits (0 to 7)
 * @param dst   the destination buffer
 */
void sph_gost256_addbits_and_close(
	void *cc, unsigned ub, unsigned n, void *dst);

/**
 * Initialize a Gost-512 context. This process performs no memory allocation.
 *
 * @param cc   the GOST-512 context (pointer to a
 *             <code>sph_gost512_context</code>)
 */
void sph_gost512_init(void *cc);

/**
 * Process some data bytes. It is acceptable that <code>len</code> is zero
 * (in which case this function does nothing).
 *
 * @param cc     the GOST-512 context
 * @param data   the input data
 * @param len    the input data length (in bytes)
 */
void sph_gost512(void *cc, const void *data, size_t len);

/**
 * Terminate the current GOST-512 computation and output the result into
 * the provided buffer. The destination buffer must be wide enough to
 * accomodate the result (64 bytes). The context is automatically
 * reinitialized.
 *
 * @param cc    the GOST-512 context
 * @param dst   the destination buffer
 */
void sph_gost512_close(void *cc, void *dst);

/**
 * Add a few additional bits (0 to 7) to the current computation, then
 * terminate it and output the result in the provided buffer, which must
 * be wide enough to accomodate the result (64 bytes). If bit number i
 * in <code>ub</code> has value 2^i, then the extra bits are those
 * numbered 7 downto 8-n (this is the big-endian convention at the byte
 * level). The context is automatically reinitialized.
 *
 * @param cc    the GOST-512 context
 * @param ub    the extra bits
 * @param n     the number of extra bits (0 to 7)
 * @param dst   the destination buffer
 */
void sph_gost512_addbits_and_close(
	void *cc, unsigned ub, unsigned n, void *dst);

#ifdef __cplusplus
}
#endif

#endif

/*-
 * Copyright 2009 Colin Percival, 2011 ArtForz, 2013 Neisklar, 2014 James Lovejoy
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions
 * are met:
 * 1. Redistributions of source code must retain the above copyright
 *    notice, this list of conditions and the following disclaimer.
 * 2. Redistributions in binary form must reproduce the above copyright
 *    notice, this list of conditions and the following disclaimer in the
 *    documentation and/or other materials provided with the distribution.
 *
 * THIS SOFTWARE IS PROVIDED BY THE AUTHOR AND CONTRIBUTORS ``AS IS'' AND
 * ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
 * IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
 * ARE DISCLAIMED.  IN NO EVENT SHALL THE AUTHOR OR CONTRIBUTORS BE LIABLE
 * FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
 * DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS
 * OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)
 * HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT
 * LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY
 * OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF
 * SUCH DAMAGE.
 *
 * This file was originally written by Colin Percival as part of the Tarsnap
 * online backup system.
 */

#include "Lyra2RE.h"
#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include <stdio.h>
#include "sha3/sph_blake.h"
#include "sha3/sph_groestl.h"
#include "sha3/sph_keccak.h"
#include "sha3/sph_skein.h"
#include "Lyra2.h"

void lyra2re_hash(const char* input, char* output)
{
    sph_blake256_context     ctx_blake;
    sph_groestl256_context   ctx_groestl;
    sph_keccak256_context    ctx_keccak;
    sph_skein256_context     ctx_skein;

    uint32_t hashA[8], hashB[8];

    sph_blake256_init(&ctx_blake);
    sph_blake256 (&ctx_blake, input, 80);
    sph_blake256_close (&ctx_blake, hashA);

    sph_keccak256_init(&ctx_keccak);
    sph_keccak256 (&ctx_keccak,hashA, 32);
    sph_keccak256_close(&ctx_keccak, hashB);

	LYRA2((void*)hashA, 32, (const void*)hashB, 32, (const void*)hashB, 32, 1, 8, 8);

	sph_skein256_init(&ctx_skein);
    sph_skein256 (&ctx_skein, hashA, 32);
    sph_skein256_close(&ctx_skein, hashB);

    sph_groestl256_init(&ctx_groestl);
    sph_groestl256 (&ctx_groestl, hashB, 32);
    sph_groestl256_close(&ctx_groestl, hashA);

	memcpy(output, hashA, 32);
}



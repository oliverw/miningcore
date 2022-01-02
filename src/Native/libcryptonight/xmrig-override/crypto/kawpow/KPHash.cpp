/* XMRig
 * Copyright 2010      Jeff Garzik <jgarzik@pobox.com>
 * Copyright 2012-2014 pooler      <pooler@litecoinpool.org>
 * Copyright 2014      Lucas Jones <https://github.com/lucasjones>
 * Copyright 2014-2016 Wolf9466    <https://github.com/OhGodAPet>
 * Copyright 2016      Jay D Dee   <jayddee246@gmail.com>
 * Copyright 2017-2019 XMR-Stak    <https://github.com/fireice-uk>, <https://github.com/psychocrypt>
 * Copyright 2018      Lee Clagett <https://github.com/vtnerd>
 * Copyright 2018-2019 tevador     <tevador@gmail.com>
 * Copyright 2018-2019 SChernykh   <https://github.com/SChernykh>
 * Copyright 2016-2019 XMRig       <https://github.com/xmrig>, <support@xmrig.com>
 *
 *   This program is free software: you can redistribute it and/or modify
 *   it under the terms of the GNU General Public License as published by
 *   the Free Software Foundation, either version 3 of the License, or
 *   (at your option) any later version.
 *
 *   This program is distributed in the hope that it will be useful,
 *   but WITHOUT ANY WARRANTY; without even the implied warranty of
 *   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 *   GNU General Public License for more details.
 *
 *   You should have received a copy of the GNU General Public License
 *   along with this program. If not, see <http://www.gnu.org/licenses/>.
 */


#include "crypto/kawpow/KPHash.h"
#include "3rdparty/libethash/ethash.h"

namespace xmrig {


static const uint32_t ravencoin_kawpow[15] = {
        0x00000072, //R
        0x00000041, //A
        0x00000056, //V
        0x00000045, //E
        0x0000004E, //N
        0x00000043, //C
        0x0000004F, //O
        0x00000049, //I
        0x0000004E, //N
        0x0000004B, //K
        0x00000041, //A
        0x00000057, //W
        0x00000050, //P
        0x0000004F, //O
        0x00000057, //W
};


void KPHash::verify(const uint32_t (&header_hash)[8], uint64_t nonce, const uint32_t (&mix_hash)[8], uint32_t (&output)[8])
{
    uint32_t state2[8];

    {
        // Absorb phase for initial round of keccak

        uint32_t state[25] = {0x0};     // Keccak's state

        // 1st fill with header data (8 words)
        for (int i = 0; i < 8; i++)
            state[i] = header_hash[i];

        // 2nd fill with nonce (2 words)
        state[8] = nonce;
        state[9] = nonce >> 32;

        // 3rd apply ravencoin input constraints
        for (int i = 10; i < 25; i++)
            state[i] = ravencoin_kawpow[i-10];

        ethash_keccakf800(state);

        for (int i = 0; i < 8; i++)
            state2[i] = state[i];
    }

    // Absorb phase for last round of keccak (256 bits)

    uint32_t state[25] = {0x0};     // Keccak's state

    // 1st initial 8 words of state are kept as carry-over from initial keccak
    for (int i = 0; i < 8; i++)
        state[i] = state2[i];

    // 2nd subsequent 8 words are carried from digest/mix
    for (int i = 8; i < 16; i++)
        state[i] = mix_hash[i-8];

    // 3rd apply ravencoin input constraints
    for (int i = 16; i < 25; i++)
        state[i] = ravencoin_kawpow[i - 16];

    // Run keccak loop
    ethash_keccakf800(state);

    for (int i = 0; i < 8; ++i)
        output[i] = state[i];
}


} // namespace xmrig

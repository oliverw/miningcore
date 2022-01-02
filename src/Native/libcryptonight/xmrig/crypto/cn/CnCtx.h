/* XMRig
 * Copyright (c) 2018      Lee Clagett <https://github.com/vtnerd>
 * Copyright (c) 2018-2020 SChernykh   <https://github.com/SChernykh>
 * Copyright (c) 2016-2020 XMRig       <https://github.com/xmrig>, <support@xmrig.com>
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

#ifndef XMRIG_CN_CTX_H
#define XMRIG_CN_CTX_H


#include <cstddef>
#include <cstdint>


struct cryptonight_ctx;


namespace xmrig
{


class CnCtx
{
public:
    static void create(cryptonight_ctx **ctx, uint8_t *memory, size_t size, size_t count);
    static void release(cryptonight_ctx **ctx, size_t count);
};


} /* namespace xmrig */


#endif /* XMRIG_CN_CTX_H */

/* XMRig
 * Copyright 2010      Jeff Garzik <jgarzik@pobox.com>
 * Copyright 2012-2014 pooler      <pooler@litecoinpool.org>
 * Copyright 2014      Lucas Jones <https://github.com/lucasjones>
 * Copyright 2014-2016 Wolf9466    <https://github.com/OhGodAPet>
 * Copyright 2016      Jay D Dee   <jayddee246@gmail.com>
 * Copyright 2017-2018 XMR-Stak    <https://github.com/fireice-uk>, <https://github.com/psychocrypt>
 * Copyright 2018      Lee Clagett <https://github.com/vtnerd>
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


#include "crypto/common/Algorithm.h"


xmrig::Algorithm::Family xmrig::Algorithm::family(Id id)
{
    switch (id) {
    case CN_0:
    case CN_1:
    case CN_2:
    case CN_R:
    case CN_FAST:
    case CN_HALF:
    case CN_XAO:
    case CN_RTO:
    case CN_RWZ:
    case CN_ZLS:
    case CN_DOUBLE:
#   ifdef XMRIG_ALGO_CN_GPU
    case CN_GPU:
#   endif
        return CN;

#   ifdef XMRIG_ALGO_CN_LITE
    case CN_LITE_0:
    case CN_LITE_1:
        return CN_LITE;
#   endif

#   ifdef XMRIG_ALGO_CN_HEAVY
    case CN_HEAVY_0:
    case CN_HEAVY_TUBE:
    case CN_HEAVY_XHV:
        return CN_HEAVY;
#   endif

#   ifdef XMRIG_ALGO_CN_PICO
    case CN_PICO_0:
        return CN_PICO;
#   endif

#   ifdef XMRIG_ALGO_RANDOMX
    case RX_0:
    case RX_WOW:
    case RX_LOKI:
    case DEFYX:
    case RX_ARQ:
        return RANDOM_X;
#   endif

#   ifdef XMRIG_ALGO_ARGON2
    case AR2_CHUKWA:
    case AR2_WRKZ:
        return ARGON2;
#   endif

    default:
        break;
    }

    return UNKNOWN;
}

/* XMRig
 * Copyright 2010      Jeff Garzik <jgarzik@pobox.com>
 * Copyright 2012-2014 pooler      <pooler@litecoinpool.org>
 * Copyright 2014      Lucas Jones <https://github.com/lucasjones>
 * Copyright 2014-2016 Wolf9466    <https://github.com/OhGodAPet>
 * Copyright 2016      Jay D Dee   <jayddee246@gmail.com>
 * Copyright 2017-2018 XMR-Stak    <https://github.com/fireice-uk>, <https://github.com/psychocrypt>
 * Copyright 2018-2020 SChernykh   <https://github.com/SChernykh>
 * Copyright 2016-2020 XMRig       <https://github.com/xmrig>, <support@xmrig.com>
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


#include <cassert>


#include "backend/cpu/Cpu.h"


#if defined(XMRIG_FEATURE_HWLOC)
#   include "backend/cpu/platform/HwlocCpuInfo.h"
#elif defined(XMRIG_FEATURE_LIBCPUID)
#   include "backend/cpu/platform/AdvancedCpuInfo.h"
#else
#   include "backend/cpu/platform/BasicCpuInfo.h"
#endif


static xmrig::ICpuInfo *cpuInfo = nullptr;


xmrig::ICpuInfo *xmrig::Cpu::info()
{
    if (cpuInfo == nullptr) {
#       if defined(XMRIG_FEATURE_HWLOC)
        cpuInfo = new HwlocCpuInfo();
#       elif defined(XMRIG_FEATURE_LIBCPUID)
        cpuInfo = new AdvancedCpuInfo();
#       else
        cpuInfo = new BasicCpuInfo();
#       endif
    }

    return cpuInfo;
}


void xmrig::Cpu::release()
{
    delete cpuInfo;
    cpuInfo = nullptr;
}

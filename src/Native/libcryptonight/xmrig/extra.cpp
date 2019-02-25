/* XMRig
 * Copyright 2010      Jeff Garzik <jgarzik@pobox.com>
 * Copyright 2012-2014 pooler      <pooler@litecoinpool.org>
 * Copyright 2014      Lucas Jones <https://github.com/lucasjones>
 * Copyright 2014-2016 Wolf9466    <https://github.com/OhGodAPet>
 * Copyright 2016      Jay D Dee   <jayddee246@gmail.com>
 * Copyright 2017-2018 XMR-Stak    <https://github.com/fireice-uk>, <https://github.com/psychocrypt>
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

#include <string.h> // for memcpy
#include "extra.h"
#include "crypto/CryptoNight_constants.h"
#include "common/cpu/Cpu.h"
#include "Mem.h"

#if !defined(__ARM_ARCH) && !defined(XMRIG_NO_ASM)
template<typename T, typename U>
static void patchCode(T dst, U src, const uint32_t iterations, const uint32_t mask)
{
    const uint8_t* p = reinterpret_cast<const uint8_t*>(src);

    // Workaround for Visual Studio placing trampoline in debug builds.
#   if defined(_MSC_VER)
    if (p[0] == 0xE9) {
        p += *(int32_t*)(p + 1) + 5;
    }
#   endif

    size_t size = 0;
    while (*(uint32_t*)(p + size) != 0xDEADC0DE) {
        ++size;
    }
    size += sizeof(uint32_t);

    memcpy((void*) dst, (const void*) src, size);

    uint8_t* patched_data = reinterpret_cast<uint8_t*>(dst);
    for (size_t i = 0; i + sizeof(uint32_t) <= size; ++i) {
        switch (*(uint32_t*)(patched_data + i)) {
        case xmrig::CRYPTONIGHT_ITER:
            *(uint32_t*)(patched_data + i) = iterations;
            break;

        case xmrig::CRYPTONIGHT_MASK:
            *(uint32_t*)(patched_data + i) = mask;
            break;
        }
    }
}


extern "C" void cnv2_mainloop_ivybridge_asm(cryptonight_ctx *ctx);
extern "C" void cnv2_mainloop_ryzen_asm(cryptonight_ctx *ctx);
extern "C" void cnv2_mainloop_bulldozer_asm(cryptonight_ctx *ctx);
extern "C" void cnv2_double_mainloop_sandybridge_asm(cryptonight_ctx *ctx0, cryptonight_ctx *ctx1);


xmrig::CpuThread::cn_mainloop_fun        cn_half_mainloop_ivybridge_asm          = nullptr;
xmrig::CpuThread::cn_mainloop_fun        cn_half_mainloop_ryzen_asm              = nullptr;
xmrig::CpuThread::cn_mainloop_fun        cn_half_mainloop_bulldozer_asm          = nullptr;
xmrig::CpuThread::cn_mainloop_double_fun cn_half_double_mainloop_sandybridge_asm = nullptr;

xmrig::CpuThread::cn_mainloop_fun        cn_trtl_mainloop_ivybridge_asm          = nullptr;
xmrig::CpuThread::cn_mainloop_fun        cn_trtl_mainloop_ryzen_asm              = nullptr;
xmrig::CpuThread::cn_mainloop_fun        cn_trtl_mainloop_bulldozer_asm          = nullptr;
xmrig::CpuThread::cn_mainloop_double_fun cn_trtl_double_mainloop_sandybridge_asm = nullptr;

void xmrig::CpuThread::patchAsmVariants()
{
    const int allocation_size = 65536;
    uint8_t *base = static_cast<uint8_t *>(Mem::allocateExecutableMemory(allocation_size));

    cn_half_mainloop_ivybridge_asm              = reinterpret_cast<cn_mainloop_fun>         (base + 0x0000);
    cn_half_mainloop_ryzen_asm                  = reinterpret_cast<cn_mainloop_fun>         (base + 0x1000);
    cn_half_mainloop_bulldozer_asm              = reinterpret_cast<cn_mainloop_fun>         (base + 0x2000);
    cn_half_double_mainloop_sandybridge_asm     = reinterpret_cast<cn_mainloop_double_fun>  (base + 0x3000);

    cn_trtl_mainloop_ivybridge_asm              = reinterpret_cast<cn_mainloop_fun>         (base + 0x4000);
    cn_trtl_mainloop_ryzen_asm                  = reinterpret_cast<cn_mainloop_fun>         (base + 0x5000);
    cn_trtl_mainloop_bulldozer_asm              = reinterpret_cast<cn_mainloop_fun>         (base + 0x6000);
    cn_trtl_double_mainloop_sandybridge_asm     = reinterpret_cast<cn_mainloop_double_fun>  (base + 0x7000);

    patchCode(cn_half_mainloop_ivybridge_asm,           cnv2_mainloop_ivybridge_asm,            xmrig::CRYPTONIGHT_HALF_ITER,   xmrig::CRYPTONIGHT_MASK);
    patchCode(cn_half_mainloop_ryzen_asm,               cnv2_mainloop_ryzen_asm,                xmrig::CRYPTONIGHT_HALF_ITER,   xmrig::CRYPTONIGHT_MASK);
    patchCode(cn_half_mainloop_bulldozer_asm,           cnv2_mainloop_bulldozer_asm,            xmrig::CRYPTONIGHT_HALF_ITER,   xmrig::CRYPTONIGHT_MASK);
    patchCode(cn_half_double_mainloop_sandybridge_asm,  cnv2_double_mainloop_sandybridge_asm,   xmrig::CRYPTONIGHT_HALF_ITER,   xmrig::CRYPTONIGHT_MASK);

    patchCode(cn_trtl_mainloop_ivybridge_asm,           cnv2_mainloop_ivybridge_asm,            xmrig::CRYPTONIGHT_TRTL_ITER,   xmrig::CRYPTONIGHT_PICO_MASK);
    patchCode(cn_trtl_mainloop_ryzen_asm,               cnv2_mainloop_ryzen_asm,                xmrig::CRYPTONIGHT_TRTL_ITER,   xmrig::CRYPTONIGHT_PICO_MASK);
    patchCode(cn_trtl_mainloop_bulldozer_asm,           cnv2_mainloop_bulldozer_asm,            xmrig::CRYPTONIGHT_TRTL_ITER,   xmrig::CRYPTONIGHT_PICO_MASK);
    patchCode(cn_trtl_double_mainloop_sandybridge_asm,  cnv2_double_mainloop_sandybridge_asm,   xmrig::CRYPTONIGHT_TRTL_ITER,   xmrig::CRYPTONIGHT_PICO_MASK);

    Mem::protectExecutableMemory(base, allocation_size);
    Mem::flushInstructionCache(base, allocation_size);
}

struct Static {
    Static() {
        xmrig::Cpu::init();
        xmrig::CpuThread::patchAsmVariants();
    }
} s;

#endif

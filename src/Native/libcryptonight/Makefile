CC = gcc
INC_DIRS = -Ixmrig-override -Ixmrig -Ixmrig/3rdparty/argon2/include -Ixmrig/3rdparty/argon2/lib

# Temporarily disable optimizations to debug crashes
CFLAGS = -std=gnu11 -fPIC -DNDEBUG -Ofast -fno-fast-math -w -DHAVE_SSE2 -DHAVE_SSSE3 -DHAVE_AVX2 -msse4.2 -maes -mavx2 -DCPU_INTEL $(INC_DIRS)
CXXFLAGS = -std=gnu++11 -s -fPIC -DNDEBUG -Ofast -fno-fast-math -fexceptions -fno-rtti -Wno-class-memaccess -w -msse4.2 -maes -mavx2 -DCPU_INTEL $(INC_DIRS)

LDFLAGS = -shared
TARGET  = libcryptonight.so

OBJECTS =	xmrig/crypto/cn/asm/cn_main_loop.o \
			xmrig/crypto/cn/asm/CryptonightR_template.o \
			xmrig/crypto/cn/r/CryptonightR_gen.o \
			xmrig/crypto/cn/gpu/cn_gpu_ssse3.o \
			xmrig/crypto/cn/c_blake256.o \
			xmrig/crypto/cn/c_groestl.o \
			xmrig/crypto/cn/c_jh.o \
			xmrig/crypto/cn/c_skein.o \
			xmrig/crypto/common/keccak.o \
			xmrig-override/crypto/common/Algorithm.o \
			xmrig/crypto/cn/CnCtx.o \
			xmrig/crypto/cn/CnHash.o \
			xmrig/crypto/common/MemoryPool.o \
			xmrig/crypto/common/VirtualMemory.o \
			xmrig/crypto/common/VirtualMemory_unix.o \
			 \
			xmrig/crypto/randomx/aes_hash.o \
			xmrig/crypto/randomx/argon2_ref.o \
			xmrig/crypto/randomx/bytecode_machine.o \
			xmrig/crypto/randomx/dataset.o \
			xmrig/crypto/randomx/soft_aes.o \
			xmrig/crypto/randomx/virtual_memory.o \
			xmrig/crypto/randomx/vm_interpreted.o \
			xmrig/crypto/randomx/allocator.o \
			xmrig/crypto/randomx/randomx.o \
			xmrig/crypto/randomx/superscalar.o \
			xmrig/crypto/randomx/vm_compiled.o \
			xmrig/crypto/randomx/vm_interpreted_light.o \
			xmrig/crypto/randomx/argon2_core.o \
			xmrig/crypto/randomx/blake2_generator.o \
			xmrig/crypto/randomx/instructions_portable.o \
			xmrig/crypto/randomx/reciprocal.o \
			xmrig/crypto/randomx/virtual_machine.o \
			xmrig/crypto/randomx/vm_compiled_light.o \
			xmrig/crypto/randomx/blake2/blake2b.o \
			xmrig/crypto/randomx/jit_compiler_x86_static.o \
			xmrig/crypto/randomx/jit_compiler_x86.o \
			 \
			xmrig/3rdparty/argon2/lib/argon2.o \
			xmrig/3rdparty/argon2/lib/core.o \
			xmrig/3rdparty/argon2/lib/encoding.o \
			xmrig/3rdparty/argon2/lib/genkat.o \
			xmrig/3rdparty/argon2/lib/impl-select.o \
			xmrig/3rdparty/argon2/lib/blake2/blake2.o \
			xmrig/3rdparty/argon2/arch/x86_64/lib/argon2-arch.o \
			xmrig/3rdparty/argon2/arch/x86_64/lib/argon2-avx2.o \
			xmrig/3rdparty/argon2/arch/x86_64/lib/argon2-avx512f.o \
			xmrig/3rdparty/argon2/arch/x86_64/lib/argon2-sse2.o \
			xmrig/3rdparty/argon2/arch/x86_64/lib/argon2-ssse3.o \
			xmrig/3rdparty/argon2/arch/x86_64/lib/argon2-xop.o \
			xmrig/3rdparty/argon2/arch/x86_64/lib/cpu-flags.o \
			 \
			xmrig/crypto/defyx/defyx.o \
			xmrig/crypto/defyx/KangarooTwelve.o \
			xmrig/crypto/defyx/KeccakP-1600-reference.o \
			xmrig/crypto/defyx/KeccakSpongeWidth1600.o \
			xmrig/crypto/defyx/yescrypt-best.o \
			xmrig/crypto/defyx/sha256.o \
			exports.o

all: $(TARGET)

$(TARGET): $(OBJECTS)
	g++ $(CXXFLAGS) $(CPPFLAGS) $(LDFLAGS) -o $@ $^ $(LDLIBS)

.PHONY: clean

clean:
	find . -name '*.o' -exec rm -r {} \;
	rm -f libcryptonight.so

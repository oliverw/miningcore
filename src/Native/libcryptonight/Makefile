INC_DIRS = -I. -Ixmrig/3rdparty/argon2/include -Ixmrig/3rdparty/argon2/lib -Ixmrig -Ixmrig-override
CFLAGS = -DCPU_INTEL -g -std=gnu11 -fPIC -DNDEBUG -Ofast -fno-fast-math -w $(CPU_FLAGS) $(HAVE_FEATURE) $(INC_DIRS)
CXXFLAGS = -DCPU_INTEL -g -std=gnu++11 -s -fPIC -DNDEBUG -Ofast -fno-fast-math -fexceptions -fno-rtti -Wno-class-memaccess -w $(CPU_FLAGS) $(HAVE_FEATURE) $(INC_DIRS)
LDFLAGS = -shared
TARGET  = libcryptonight.so

OBJECTS = exports.o \
	xmrig/crypto/cn/asm/cn_main_loop.o \
	xmrig/crypto/cn/asm/CryptonightR_template.o \
	xmrig/crypto/cn/r/CryptonightR_gen.o \
	xmrig/crypto/cn/gpu/cn_gpu_ssse3.o \
	xmrig-override/backend/cpu/platform/BasicCpuInfo.o \
	\
	xmrig-override/backend/cpu/Cpu.o \
	c29b.o \
	c29i.o \
	c29s.o \
	c29v.o \
	xmrig/crypto/cn/c_blake256.o \
	xmrig/crypto/cn/c_groestl.o \
	xmrig/crypto/cn/c_jh.o \
	xmrig/crypto/cn/c_skein.o \
	xmrig/base/crypto/keccak.o \
	xmrig/base/crypto/sha3.o \
	xmrig/crypto/cn/CnCtx.o \
	xmrig/crypto/cn/CnHash.o \
	xmrig/crypto/common/MemoryPool.o \
	xmrig/crypto/common/VirtualMemory.o \
	xmrig/crypto/common/VirtualMemory_unix.o \
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
	\
	xmrig/crypto/astrobwt/AstroBWT.o \
	xmrig/crypto/astrobwt/Salsa20.o \
	xmrig/crypto/astrobwt/salsa20_ref/salsa20.o \
	\
	xmrig/crypto/ghostrider/sph_blake.o \
	xmrig/crypto/ghostrider/sph_bmw.o \
	xmrig/crypto/ghostrider/sph_cubehash.o \
	xmrig/crypto/ghostrider/sph_echo.o \
	xmrig/crypto/ghostrider/sph_fugue.o \
	xmrig/crypto/ghostrider/sph_groestl.o \
	xmrig/crypto/ghostrider/sph_hamsi.o \
	xmrig/crypto/ghostrider/sph_hamsi_helper.o \
	xmrig/crypto/ghostrider/sph_jh.o \
	xmrig/crypto/ghostrider/sph_keccak.o \
	xmrig/crypto/ghostrider/sph_luffa.o \
	xmrig/crypto/ghostrider/sph_sha2.o \
	xmrig/crypto/ghostrider/sph_shabal.o \
	xmrig/crypto/ghostrider/sph_shavite.o \
	xmrig/crypto/ghostrider/sph_simd.o \
	xmrig/crypto/ghostrider/sph_skein.o \
	xmrig/crypto/ghostrider/sph_whirlpool.o \
	xmrig-override/crypto/ghostrider/ghostrider.o \
	\
	xmrig/3rdparty/libethash/keccakf800.o


all: $(TARGET)

$(TARGET): $(OBJECTS)
	$(CXX) $(LDFLAGS) -o $@ $^ $(LDLIBS)

.PHONY: clean

clean:
	$(RM) $(TARGET) $(OBJECTS)

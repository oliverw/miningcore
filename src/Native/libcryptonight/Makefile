CC = gcc
INC_DIRS = -Ixmrig -IIxmrig/3rdparty

# Temporarily disable optimizations to debug crashes
CFLAGS = -g -Wall -c -fPIC -maes -O2 -fpermissive -Wno-fpermissive -Wno-strict-aliasing -Wno-sign-compare -DCPU_INTEL $(INC_DIRS)
CXXFLAGS = -g -Wall -maes -O2 -Wno-unused-function -fPIC -fpermissive -Wno-strict-aliasing -Wno-sign-compare -std=c++0x -fexceptions -frtti -DCPU_INTEL $(INC_DIRS)

LDFLAGS = -shared
TARGET  = libcryptonight.so

OBJECTS =	xmrig/crypto/asm/cn_main_loop.o xmrig/crypto/asm/CryptonightR_template.o  \
			xmrig/crypto/cn_gpu_ssse3.o xmrig/common/cpu/Cpu.o xmrig/common/cpu/BasicCpuInfo.o  \
			xmrig/extra.o xmrig/Mem.o xmrig/Mem_unix.o  \
			xmrig/crypto/c_blake256.o xmrig/crypto/c_groestl.o xmrig/crypto/c_jh.o  \
			xmrig/crypto/c_skein.o xmrig/common/crypto/keccak.o  \
			xmrig/crypto/CryptonightR_gen.o  \
			exports.o

all: $(TARGET)

$(TARGET): $(OBJECTS)
	g++ $(CXXFLAGS) $(CPPFLAGS) $(LDFLAGS) -o $@ $^ $(LDLIBS)

.PHONY: clean

clean:
	find . -name '*.o' -exec rm -r {} \;
	rm -f libcryptonight.so

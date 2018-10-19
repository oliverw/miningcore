CC = gcc
INC_DIRS = -I. -I./3rdparty

# Temporarily disable optimizations to debug crashes
CFLAGS = -g -Wall -c -fPIC -maes -O2 -fpermissive -Wno-fpermissive -Wno-strict-aliasing -Wno-sign-compare -DCPU_INTEL $(INC_DIRS)
CXXFLAGS = -g -Wall -maes -O2 -Wno-unused-function -fPIC -fpermissive -Wno-strict-aliasing -Wno-sign-compare -std=c++0x -fexceptions -frtti -DCPU_INTEL $(INC_DIRS)

LDFLAGS = -shared
TARGET  = libcryptonight.so

OBJECTS = crypto/c_blake256.c crypto/c_groestl.c crypto/c_jh.c crypto/c_skein.c  common/crypto/keccak.cpp exports.o \
          crypto/Asm.cpp crypto/asm/cnv2_main_loop.S

all: $(TARGET)

$(TARGET): $(OBJECTS)
	g++ $(CXXFLAGS) $(CPPFLAGS) $(LDFLAGS) -o $@ $^ $(LDLIBS)

.PHONY: clean

clean:
	find . -name '*.o' -exec rm -r {} \;
	rm -f libcryptonight.so

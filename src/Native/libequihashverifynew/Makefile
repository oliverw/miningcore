CC = g++
INC_DIRS = -I. -I/usr/include -I/usr/include/boost
CXXFLAGS = -I. -I/usr/include -I/usr/include/boost -fPIC -pthread -Wall -Wextra -Wno-unused-parameter -m64 -O3 -fno-omit-frame-pointer -fno-rtti -fno-exceptions -std=gnu++0x -std=c++11 -fPIC -fexceptions
LDFLAGS = -shared -pthread -rdynamic -m64
LDLIBS = -lboost_system -lsodium
TARGET  = libequihashverifynew.so

OBJECTS = utilstrencodings.o \
	util.o \
	support/cleanse.o \
	random.o \
	uint256.o \
	arith_uint256.o \
	crypto/hmac_sha512.o \
	crypto/sha1.o \
	crypto/sha512.o \
	crypto/sha256.o \
	crypto/hmac_sha256.o \
	crypto/equihash.o \
	crypto/ripemd160.o \
	equihashverify.o \
	exports.o

all: $(TARGET)

$(TARGET): $(OBJECTS)
	g++ $(LDFLAGS) -o $@ $^ $(LDLIBS)

.PHONY: clean

clean:
	find . -name '*.o' -exec rm -r {} \;
	rm -f libequihashverifynew.so

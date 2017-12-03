CC = gcc
INC_DIRS = -I. -Icontrib/epee/include
CFLAGS = -Wall -c -fPIC -maes -O2 -g $(INC_DIRS) -Wno-strict-aliasing
CXXFLAGS = -Wall -Wno-unused-function -fPIC -fpermissive -std=c++0x -fexceptions -frtti -O2 -g $(INC_DIRS)
LDFLAGS = -shared
LDLIBS = -lboost_system -lboost_date_time
TARGET  = libcryptonote.so

OBJECTS = contrib/epee/src/hex.o \
		common/base58.o crypto/aesb.o crypto/blake256.o crypto/chacha8.o \
		crypto/crypto-ops-data.o crypto/crypto-ops.o crypto/crypto.o crypto/groestl.o crypto/hash-extra-blake.o \
		crypto/hash-extra-groestl.o crypto/hash-extra-jh.o crypto/hash-extra-skein.o crypto/hash.o crypto/jh.o \
		crypto/keccak.o crypto/oaes_lib.o crypto/random.o crypto/skein.o crypto/slow-hash.o crypto/tree-hash.o \
		crypto/slow-hash-lite.o cryptonote_basic/cryptonote_format_utils.o exports.o

all: $(TARGET)

$(TARGET): $(OBJECTS)
	g++ $(LDFLAGS) -o $@ $^ $(LDLIBS)

.PHONY: clean

clean:
	find . -name '*.o' -exec rm -r {} \;
	rm libcryptonote.so

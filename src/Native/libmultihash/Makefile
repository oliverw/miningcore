CC = gcc
CFLAGS = -g -Wall -c -fPIC -O2 -Wno-pointer-sign -Wno-char-subscripts -Wno-unused-variable -Wno-unused-function -Wno-strict-aliasing -Wno-discarded-qualifiers -Wno-unused-const-variable
CXXFLAGS = -g -Wall -fPIC -fpermissive -O2 -Wno-char-subscripts -Wno-unused-variable -Wno-unused-function -Wno-strict-aliasing -Wno-sign-compare -std=c++11
LDFLAGS = -shared
LDLIBS = -lsodium
TARGET  = libmultihash.so

OBJECTS = bcrypt.o blake.o blake2s.o c11.o dcrypt.o fresh.o \
    fugue.o groestl.o hefty1.o jh.o keccak.o neoscrypt.o exports.o nist5.o quark.o qubit.o s3.o scryptn.o \
    sha3/aes_helper.o sha3/hamsi.o sha3/hamsi_helper.o sha3/sph_blake.o sha3/sph_bmw.o sha3/sph_cubehash.o \
    sha3/sph_echo.o sha3/sph_fugue.o sha3/sph_groestl.o sha3/sph_hefty1.o sha3/sph_jh.o sha3/sph_keccak.o \
    sha3/sph_luffa.o sha3/sph_shabal.o sha3/sph_shavite.o sha3/sph_simd.o sha3/sph_skein.o sha3/sph_whirlpool.o \
    sha3/sph_haval.o sha3/sph_sha2.o sha3/sph_sha2big.o sha3/sph_blake2s.o sha3/sm3.o \
    shavite3.o skein.o x11.o x13.o x15.o x17.o x16r.o x16s.o \
    Lyra2.o Lyra2RE.o Sponge.o  \
    equi/util.o equi/support/cleanse.o equi/random.o \
    equi/uint256.o equi/arith_uint256.o equi/crypto/hmac_sha512.o \
    equi/crypto/sha1.o equi/crypto/sha512.o equi/crypto/sha256.o \
    equi/crypto/hmac_sha256.o equi/crypto/equihash.o equi/crypto/ripemd160.o \
    equi/equihashverify.o \
    libethash/internal.o libethash/io.o libethash/io_posix.o libethash/sha3.o

all: $(TARGET)

$(TARGET): $(OBJECTS)
	g++ $(LDFLAGS) -o $@ $^ $(LDLIBS)

.PHONY: clean

clean:
	find . -name '*.o' -exec rm -r {} \;
	rm -f libmultihash.so

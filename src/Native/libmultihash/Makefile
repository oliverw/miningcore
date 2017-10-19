CC = gcc
CFLAGS = -Wall -c -fPIC -O2 -g -Wno-pointer-sign -Wno-unused-function -Wno-strict-aliasing -Wno-discarded-qualifiers
CXXFLAGS = -Wall -fPIC -fpermissive -O2 -g -Wno-unused-function -Wno-strict-aliasing
LDFLAGS = -shared
LDLIBS = -lsodium
TARGET  = libmultihash.so

OBJECTS = bcrypt.o blake.o c11.o dcrypt.o fresh.o \
	fugue.o groestl.o hefty1.o jh.o keccak.o neoscrypt.o exports.o nist5.o quark.o qubit.o s3.o scryptn.o \
	sha3/aes_helper.o sha3/hamsi.o sha3/hamsi_helper.o sha3/sph_blake.o sha3/sph_bmw.o sha3/sph_cubehash.o \
	sha3/sph_echo.o sha3/sph_fugue.o sha3/sph_groestl.o sha3/sph_hefty1.o sha3/sph_jh.o sha3/sph_keccak.o \
	sha3/sph_luffa.o sha3/sph_shabal.o sha3/sph_shavite.o sha3/sph_simd.o sha3/sph_skein.o sha3/sph_whirlpool.o \
	shavite3.o skein.o x11.o x15.o \
	Lyra2.o Lyra2RE.o Sponge.o  \
	equi/endian.o equi/equi.o \
	libethash/internal.o libethash/io.o libethash/io_posix.o libethash/sha3.o

all: $(TARGET)

$(TARGET): $(OBJECTS)
	g++ $(LDFLAGS) -o $@ $^ $(LDLIBS)

.PHONY: clean

clean:
	find . -name '*.o' -exec rm -r {} \;
	rm libmultihash.so

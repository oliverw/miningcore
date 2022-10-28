CFLAGS = -g -Wall -c -fPIC -O2 -Wno-pointer-sign -Wno-char-subscripts -Wno-unused-variable -Wno-unused-function -Wno-strict-aliasing -Wno-discarded-qualifiers -Wno-unused-const-variable $(CPU_FLAGS) $(HAVE_FEATURE)
CXXFLAGS = -g -Wall -fPIC -fpermissive -O2 -Wno-char-subscripts -Wno-unused-variable -Wno-unused-function -Wno-strict-aliasing -Wno-sign-compare -std=c++11 $(CPU_FLAGS) $(HAVE_FEATURE)
LDFLAGS = -shared
LDLIBS = -lsodium
TARGET  = libmultihash.so

OBJECTS = bcrypt.o blake.o c11.o dcrypt.o fresh.o lane.o \
	fugue.o groestl.o hefty1.o jh.o keccak.o neoscrypt.o exports.o nist5.o quark.o qubit.o s3.o scryptn.o \
	sha256csm.o hmq17.o phi.o \
	sha3/aes_helper.o sha3/hamsi.o sha3/hamsi_helper.o sha3/sph_blake.o sha3/sph_bmw.o sha3/sph_cubehash.o \
	sha3/sph_echo.o sha3/sph_fugue.o sha3/sph_groestl.o sha3/sph_hefty1.o sha3/sph_jh.o sha3/sph_keccak.o \
	sha3/sph_luffa.o sha3/sph_shabal.o sha3/sph_shavite.o sha3/sph_simd.o sha3/sph_skein.o sha3/sph_whirlpool.o \
	sha3/sph_haval.o sha3/sph_sha2.o sha3/sph_sha2big.o sha3/sm3.o sha3/panama.o \
	sha3/extra.o sha3/gost_streebog.o sha3/sph_tiger.o sha3/SWIFFTX.o KeccakP-800-reference.o \
	shavite3.o skein.o x11.o x13.o x15.o x17.o x16r.o x16rv2.o x16s.o x21s.o x22i.o \
	blake2/sse/blake2s.o blake2/sse/blake2b.o \
	Lyra2.o Lyra2RE.o Sponge.o geek.o  \
	heavyhash/heavyhash.o heavyhash/keccak_tiny.o \
	verthash/tiny_sha3/sha3.o verthash/h2.o \
	equi/util.o equi/support/cleanse.o equi/random.o \
	equi/uint256.o equi/arith_uint256.o equi/crypto/hmac_sha512.o \
	equi/crypto/sha1.o equi/crypto/sha512.o equi/crypto/sha256.o \
	equi/crypto/hmac_sha256.o equi/crypto/equihash.o equi/crypto/ripemd160.o \
	equi/equihashverify.o sha512_256.o sha256dt.o

all: $(TARGET)

$(TARGET): $(OBJECTS)
	$(CXX) $(LDFLAGS) -o $@ $^ $(LDLIBS)

.PHONY: clean

clean:
	$(RM) $(TARGET) $(OBJECTS)

CC		= g++
FLAGS   = -std=gnu99 -Iinclude
CFLAGS  = -fPIC -fpermissive -g #-pedantic -Wall -Wextra -ggdb3
LDFLAGS = -shared

TARGET  = multihash.so

SOURCES = bcrypt.c blake.c c11.c cryptonight.c crypto\aesb.c crypto\c_blake256.c crypto\c_groestl.c crypto\c_jh.c \
	crypto\c_keccak.c crypto\c_skein.c crypto\hash.c crypto\oaes_lib.c crypto\wild_keccak.cpp dcrypt.c fresh.c \
	fugue.c groestl.c hefty1.c jh.c keccak.c neoscrypt.c exports.cpp nist5.c quark.c qubit.c s3.c scryptn.c \
	sha3\aes_helper.c sha3\hamsi.c sha3\hamsi_helper.c sha3\sph_blake.c sha3\sph_bmw.c sha3\sph_cubehash.c \
	sha3\sph_echo.c sha3\sph_fugue.c sha3\sph_groestl.c sha3\sph_hefty1.c sha3\sph_jh.c sha3\sph_keccak.c \
	sha3\sph_luffa.c sha3\sph_shabal.c sha3\sph_shavite.c sha3\sph_simd.c sha3\sph_skein.c sha3\sph_whirlpool.c \
	shavite3.c skein.c x11.c x15.c 

HEADERS = bcrypt.h blake.h boolberry.h c11.h cryptonight.h crypto\crypto.h crypto\c_blake256.h crypto\c_groestl.h \
	crypto\c_jh.h crypto\c_keccak.h crypto\c_skein.h crypto\groestl_tables.h crypto\hash-ops.h crypto\hash.h \
	crypto\int-util.h crypto\oaes_config.h crypto\oaes_lib.h crypto\skein_port.h crypto\wild_keccak.h dcrypt.h \
	fresh.h fugue.h groestl.h hefty1.h jh.h keccak.h neoscrypt.h nist5.h quark.h qubit.h s3.h \
	scryptn.h sha256.h sha3\sph_blake.h sha3\sph_bmw.h sha3\sph_cubehash.h sha3\sph_echo.h sha3\sph_fugue.h \
	sha3\sph_groestl.h sha3\sph_hamsi.h sha3\sph_hefty1.h sha3\sph_jh.h sha3\sph_keccak.h sha3\sph_luffa.h \
	sha3\sph_shabal.h sha3\sph_shavite.h sha3\sph_simd.h sha3\sph_skein.h sha3\sph_types.h sha3\sph_whirlpool.h \
	shavite3.h skein.h stdafx.h stdint.h targetver.h x11.h x15.h 

# OBJECTS = $(patsubst %.cpp,%.o,$(SOURCES:.c=.o))
OBJECTS = bcrypt.o blake.o c11.o cryptonight.o crypto\aesb.o crypto\c_blake256.o crypto\c_groestl.o crypto\c_jh.o \
	crypto\c_keccak.o crypto\c_skein.o crypto\hash.o crypto\oaes_lib.o crypto\wild_keccak.o dcrypt.o fresh.o \
	fugue.o groestl.o hefty1.o jh.o keccak.o neoscrypt.o exports.o nist5.o quark.o qubit.o s3.o scryptn.o \
	sha3\aes_helper.o sha3\hamsi.o sha3\hamsi_helper.o sha3\sph_blake.o sha3\sph_bmw.o sha3\sph_cubehash.o \
	sha3\sph_echo.o sha3\sph_fugue.o sha3\sph_groestl.o sha3\sph_hefty1.o sha3\sph_jh.o sha3\sph_keccak.o \
	sha3\sph_luffa.o sha3\sph_shabal.o sha3\sph_shavite.o sha3\sph_simd.o sha3\sph_skein.o sha3\sph_whirlpool.o \
	shavite3.o skein.o x11.o x15.o

BINDIR = bin

all: $(TARGET)

$(TARGET): $(OBJECTS)
	$(CC) $(FLAGS) $(CFLAGS) $(DEBUGFLAGS) -o $(TARGET) $(OBJECTS)

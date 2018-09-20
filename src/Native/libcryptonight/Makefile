CC = gcc
INC_DIRS = -I.
CFLAGS = -Wall -c -fPIC -maes -O2 -fpermissive -Wno-strict-aliasing -g $(INC_DIRS)
CXXFLAGS = -Wall -maes -Wno-unused-function -fPIC -fpermissive -std=c++0x -fexceptions -frtti -O2 -g $(INC_DIRS)
LDFLAGS = -shared
TARGET  = libcryptonight.so

OBJECTS = crypto/c_blake256.c crypto/c_groestl.c crypto/c_jh.c crypto/c_skein.c common/crypto/keccak.cpp exports.o

all: $(TARGET)

$(TARGET): $(OBJECTS)
	g++ $(CXXFLAGS) $(CPPFLAGS) $(LDFLAGS) -o $@ $^ $(LDLIBS)

.PHONY: clean

clean:
	find . -name '*.o' -exec rm -r {} \;
	rm libcryptonight.so

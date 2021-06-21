CC = gcc
CFLAGS = -g -Wall -c -fPIC -O2 -Wno-pointer-sign -Wno-char-subscripts -Wno-unused-variable -Wno-unused-function -Wno-strict-aliasing -Wno-discarded-qualifiers -Wno-unused-const-variable
CXXFLAGS = -g -Wall -fPIC -fpermissive -O2 -Wno-char-subscripts -Wno-unused-variable -Wno-unused-function -Wno-strict-aliasing -Wno-sign-compare -std=c++11
LDFLAGS = -shared
LDLIBS =
TARGET  = libethhash.so

OBJECTS = internal.o io.o io_posix.o sha3.o

all: $(TARGET)

$(TARGET): $(OBJECTS)
	g++ $(LDFLAGS) -o $@ $^ $(LDLIBS)

.PHONY: clean

clean:
	find . -name '*.o' -exec rm -r {} \;
	rm -f libethhash.so

CC = g++
INC_DIRS = -I/tmp/RandomX/src

CFLAGS = -std=gnu11 -fPIC -O2 $(INC_DIRS)
CXXFLAGS = -std=gnu++11 -s -fPIC -O2 $(INC_DIRS)

LDFLAGS = -shared -L.
LDLIBS = -lrandomx

TARGET  = librandomx.so
OBJECTS =	exports.o

all: $(TARGET)

$(TARGET): $(OBJECTS)
	$(CC) $(CXXFLAGS) $(CPPFLAGS) $(LDFLAGS) -o $@ $^ $(LDLIBS)

.PHONY: clean

clean:
	find . -name '*.o' -exec rm -r {} \;
	rm -f librandomx.so

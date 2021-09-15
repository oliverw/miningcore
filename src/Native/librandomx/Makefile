# Create shared library librandomx.so from static library librandomx.a using --whole-archive

CC = g++
LDFLAGS = -shared -pthread -L. -Wl,-whole-archive librandomx.a -Wl,-no-whole-archive
LDLIBS = -lstdc++ -lgcc -lc
TARGET  = librandomx.so

all: $(TARGET)

$(TARGET): $(OBJECTS)
	$(CC) $(CXXFLAGS) $(CPPFLAGS) $(LDFLAGS) -o $@ $^ $(LDLIBS)

.PHONY: clean

clean:
	find . -name '*.o' -exec rm -r {} \;
	rm -f librandomx.so

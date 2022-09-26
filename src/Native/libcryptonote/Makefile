INC_DIRS = -I. -Icontrib/epee/include
CFLAGS = $(CPU_FLAGS) $(INC_DIRS) -fno-exceptions -std=gnu11 -march=native -fPIC -DNDEBUG -Ofast -funroll-loops -fvariable-expansion-in-unroller -ftree-loop-if-convert-stores -fmerge-all-constants -fbranch-target-load-optimize2
CXXFLAGS = $(CPU_FLAGS) $(INC_DIRS) -fexceptions -frtti -std=gnu++11 -march=native -fPIC -DNDEBUG -Ofast -s -funroll-loops -fvariable-expansion-in-unroller -ftree-loop-if-convert-stores -fmerge-all-constants -fbranch-target-load-optimize2
LDFLAGS = -shared
LDLIBS = -lboost_system -lboost_date_time
TARGET  = libcryptonote.so

OBJECTS = exports.o \
	cryptonote_core/cryptonote_format_utils.o \
	offshore/pricing_record.o \
	crypto/tree-hash.o \
	crypto/crypto.o \
	crypto/crypto-ops.o \
	crypto/crypto-ops-data.o \
	crypto/hash.o \
	crypto/keccak.o \
	common/base58.o

all: $(TARGET)

$(TARGET): $(OBJECTS)
	$(CXX) $(CXXFLAGS) $(CPPFLAGS) $(LDFLAGS) -o $@ $^ $(LDLIBS)

.PHONY: clean

clean:
	$(RM) $(TARGET) $(OBJECTS)

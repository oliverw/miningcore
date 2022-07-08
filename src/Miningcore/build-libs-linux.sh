#!/bin/bash

OutDir=$1

AES=$(../Native/check_cpu.sh aes && echo -maes || echo)
SSE2=$(../Native/check_cpu.sh sse2 && echo -msse2 || echo)
SSE3=$(../Native/check_cpu.sh sse3 && echo -msse3 || echo)
SSSE3=$(../Native/check_cpu.sh ssse3 && echo -mssse3 || echo)
AVX=$(../Native/check_cpu.sh avx && echo -mavx || echo)
AVX2=$(../Native/check_cpu.sh avx2 && echo -mavx2 || echo)
AVX512F=$(../Native/check_cpu.sh avx512f && echo -mavx512f || echo)

export CPU_FLAGS="$AES $SSE2 $SSE3 $SSSE3 $AVX $AVX2 $AVX512F"

HAVE_AES=$(../Native/check_cpu.sh aes && echo -D__AES__ || echo)
HAVE_SSE2=$(../Native/check_cpu.sh sse2 && echo -DHAVE_SSE2 || echo)
HAVE_SSE3=$(../Native/check_cpu.sh sse3 && echo -DHAVE_SSE3 || echo)
HAVE_SSSE3=$(../Native/check_cpu.sh ssse3 && echo -DHAVE_SSSE3 || echo)
HAVE_AVX=$(../Native/check_cpu.sh avx && echo -DHAVE_AVX || echo)
HAVE_AVX2=$(../Native/check_cpu.sh avx2 && echo -DHAVE_AVX2 || echo)
HAVE_AVX512F=$(../Native/check_cpu.sh avx512f && echo -DHAVE_AVX512F || echo)

export HAVE_FEATURE="$HAVE_AES $HAVE_SSE2 $HAVE_SSE3 $HAVE_SSSE3 $HAVE_AVX $HAVE_AVX2 $HAVE_AVX512F"

(cd ../Native/libmultihash && make clean && make) && mv ../Native/libmultihash/libmultihash.so "$OutDir"
(cd ../Native/libethhash && make clean && make) && mv ../Native/libethhash/libethhash.so "$OutDir"
(cd ../Native/libcryptonote && make clean && make) && mv ../Native/libcryptonote/libcryptonote.so "$OutDir"
(cd ../Native/libcryptonight && make clean && make) && mv ../Native/libcryptonight/libcryptonight.so "$OutDir"

((cd /tmp && rm -rf RandomX && git clone https://github.com/tevador/RandomX && cd RandomX && git checkout tags/v1.1.10 && mkdir build && cd build && cmake -DARCH=native .. && make) && (cd ../Native/librandomx && cp /tmp/RandomX/build/librandomx.a . && make clean && make) && mv ../Native/librandomx/librandomx.so "$OutDir")
((cd /tmp && rm -rf RandomARQ && git clone https://github.com/arqma/RandomARQ && cd RandomARQ && git checkout 14850620439045b319fa6398f5a164715c4a66ce && mkdir build && cd build && cmake -DARCH=native .. && make) && (cd ../Native/librandomarq && cp /tmp/RandomARQ/build/librandomx.a . && make clean && make) && mv ../Native/librandomarq/librandomarq.so "$OutDir")

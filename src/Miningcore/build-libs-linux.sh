#!/bin/bash

OutDir=$(sed 's/\\/\//g' <<<"$1")

(cd ../Native/libmultihash && make clean && make && mv ../Native/libmultihash/libmultihash.so "$OutDir")
(cd ../Native/libethhash && make clean && make && mv ../Native/libethhash/libethhash.so "$OutDir")
(cd ../Native/libcryptonote && make clean && make && mv ../Native/libcryptonote/libcryptonote.so "$OutDir")
(cd ../Native/libcryptonight && make clean && make && mv ../Native/libcryptonight/libcryptonight.so "$OutDir")

((cd /tmp && rm -rf RandomX && git clone https://github.com/tevador/RandomX && cd RandomX && git checkout tags/v1.1.10 && mkdir build && cd build && cmake -DARCH=native .. && make) && (cd ../Native/librandomx && cp /tmp/RandomX/build/librandomx.a . && make clean && make) && mv ../Native/librandomx/librandomx.so "$OutDir")
((cd /tmp && rm -rf RandomARQ && git clone https://github.com/arqma/RandomARQ && cd RandomARQ && git checkout 14850620439045b319fa6398f5a164715c4a66ce && mkdir build && cd build && cmake -DARCH=native .. && make) && (cd ../Native/librandomarq && cp /tmp/RandomX/build/librandomx.a . && make clean && make) && mv ../Native/librandomarq/librandomarq.so "$OutDir")

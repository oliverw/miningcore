# ethash: C/C++ implementation of Ethash, the Ethereum Proof of Work algorithm.
# Copyright 2018-2019 Pawel Bylica.
# Licensed under the Apache License, Version 2.0.

include(GNUInstallDirs)

add_library(
    ethash SHARED
    bit_manipulation.h
    builtins.h
    endianness.hpp
    ${include_dir}/ethash/ethash.h
    ${include_dir}/ethash/ethash.hpp
    ethash-internal.hpp
    ethash.cpp
    ${include_dir}/ethash/hash_types.h
    managed.cpp
    kiss99.hpp
    primes.h
    primes.c
    ${include_dir}/ethash/progpow.hpp
    progpow.cpp
)
set_property(TARGET ethash PROPERTY POSITION_INDEPENDENT_CODE ON)

target_link_libraries(ethash PRIVATE keccak)
target_include_directories(ethash PUBLIC $<BUILD_INTERFACE:${include_dir}>$<INSTALL_INTERFACE:include>)
if(CABLE_COMPILER_GNULIKE AND NOT SANITIZE MATCHES undefined)
    target_compile_options(ethash PRIVATE $<$<COMPILE_LANGUAGE:CXX>:-fno-rtti>)
endif()

install(
    TARGETS ethash
    EXPORT ethashTargets
    ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR}
    LIBRARY DESTINATION ${CMAKE_INSTALL_LIBDIR}
)

{
    "targets": [
        {
            "target_name": "equihashverify",
            "dependencies": [

            ],
            "sources": [
                "support/cleanse.cpp",
                "uint256.cpp",
                "arith_uint256.cpp",
                "random.cpp",   
                "util.cpp",
                "utilstrencodings.cpp",
                "crypto/equihash.cpp",
                "crypto/hmac_sha256.cpp",
                "crypto/hmac_sha512.cpp",
                "crypto/ripemd160.cpp",
                "crypto/sha1.cpp",
                "crypto/sha256.cpp",
                "crypto/sha512.cpp",
                "equihashverify.cc"                
            ],
            "include_dirs": [
                "<!(node -e \"require('nan')\")",
                ".",
                "/usr/include",
                "/usr/include/boost",
            ],
            "libararies": [
                "/usr/lib/x86_64-linux-gnu"
            ],
            "defines": [
            "HAVE_DECL_STRNLEN=1",
            "HAVE_BYTESWAP_H=1"
            ],
            "cflags_cc": [
                "-std=c++11",
                "-Wl,--whole-archive",
                "-fPIC",
                "-fexceptions"
                
            ],
            "link_settings": {
                "libraries": [
                    "-Wl,-rpath,./build/Release/",
                    "-lboost_system",
                    "-lsodium"
                ]
            },
        }
       ]
}


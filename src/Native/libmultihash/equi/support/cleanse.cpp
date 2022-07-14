// Copyright (c) 2009-2010 Satoshi Nakamoto
// Copyright (c) 2009-2015 The Bitcoin Core developers
// Distributed under the MIT software license, see the accompanying
// file COPYING or http://www.opensource.org/licenses/mit-license.php.

#include "cleanse.h"

#ifndef _WIN32
#include <openssl/crypto.h>
#else
#include <Windows.h>
#endif

void memory_cleanse(void *ptr, size_t len)
{
#ifndef _WIN32
    OPENSSL_cleanse(ptr, len);
#else
    ZeroMemory(ptr, 0, len);
#endif
}

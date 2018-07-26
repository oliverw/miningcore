// Copyright (c) 2009-2010 Satoshi Nakamoto
// Copyright (c) 2009-2014 The Bitcoin Core developers
// Distributed under the MIT software license, see the accompanying
// file COPYING or http://www.opensource.org/licenses/mit-license.php.

/**
 * Server/client environment: argument handling, config file parsing,
 * logging, thread wrappers
 */
#ifndef BITCOIN_UTIL_H
#define BITCOIN_UTIL_H

#include "tinyformat.h"
#include <string>



/** Send a string to the log output */
int LogPrintStr(const std::string &str);

#define LogPrintf(...) LogPrint(NULL, __VA_ARGS__)

/**
 * When we switch to C++11, this can be switched to variadic templates instead
 * of this macro-based construction (see tinyformat.h).
 */
#define MAKE_ERROR_AND_LOG_FUNC(n)                                        \
    /**   Print to debug.log if -debug=category switch is given OR category is NULL. */ \
    template<TINYFORMAT_ARGTYPES(n)>                                          \
    static inline int LogPrint(const char* category, const char* format, TINYFORMAT_VARARGS(n))  \
    {                                                                         \
        return LogPrintStr(tfm::format(format, TINYFORMAT_PASSARGS(n))); \
    }                                                                         \
    /**   Log error and return false */                                        \
    template<TINYFORMAT_ARGTYPES(n)>                                          \
    static inline bool error(const char* format, TINYFORMAT_VARARGS(n))                     \
    {                                                                         \
        LogPrintStr("ERROR: " + tfm::format(format, TINYFORMAT_PASSARGS(n)) + "\n"); \
        return false;                                                         \
    }

TINYFORMAT_FOREACH_ARGNUM(MAKE_ERROR_AND_LOG_FUNC)

/**
 * Zero-arg versions of logging and error, these are not covered by
 * TINYFORMAT_FOREACH_ARGNUM
 */
static inline int LogPrint(const char* category, const char* format)
{
    return LogPrintStr(format);
}
static inline bool error(const char* format)
{
    return LogPrintStr(std::string("ERROR: ") + format + "\n");
}


#endif // BITCOIN_UTIL_H

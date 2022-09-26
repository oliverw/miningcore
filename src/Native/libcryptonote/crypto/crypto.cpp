// Copyright (c) 2012-2013 The Cryptonote developers
// Distributed under the MIT/X11 software license, see the accompanying
// file COPYING or http://www.opensource.org/licenses/mit-license.php.

#if !defined(WIN32)
#include <alloca.h>
#else
#include <malloc.h>
#endif

#include <cassert>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <memory>
#include <mutex>

#include "common/varint.h"
#include "warnings.h"
#include "crypto.h"
#include "hash.h"

namespace crypto {

  using std::abort;
  using std::int32_t;
  using std::int64_t;
  using std::lock_guard;
  using std::mutex;
  using std::size_t;
  using std::uint32_t;
  using std::uint64_t;

  extern "C" {
#include "crypto-ops.h"
#include "random.h"
  }

  mutex random_lock;

  static inline unsigned char *operator &(ec_point &point) {
    return &reinterpret_cast<unsigned char &>(point);
  }

  static inline const unsigned char *operator &(const ec_point &point) {
    return &reinterpret_cast<const unsigned char &>(point);
  }

  static inline unsigned char *operator &(ec_scalar &scalar) {
    return &reinterpret_cast<unsigned char &>(scalar);
  }

  static inline const unsigned char *operator &(const ec_scalar &scalar) {
    return &reinterpret_cast<const unsigned char &>(scalar);
  }

  bool crypto_ops::check_key(const public_key &key) {
    ge_p3 point;
    return ge_frombytes_vartime(&point, &key) == 0;
  }
}

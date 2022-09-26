// Copyright (c) 2012-2013 The Cryptonote developers
// Distributed under the MIT/X11 software license, see the accompanying
// file COPYING or http://www.opensource.org/licenses/mit-license.php.

#pragma once

#include <cstddef>
#include <mutex>
#include <vector>

#include "common/pod-class.h"
#include "generic-ops.h"
#include "hash.h"

namespace crypto {

  extern "C" {
#include "random.h"
  }

  extern std::mutex random_lock;

#pragma pack(push, 1)
  POD_CLASS ec_point {
    char data[32];
  };

  POD_CLASS ec_scalar {
    char data[32];
  };

  POD_CLASS public_key: ec_point {
    friend class crypto_ops;
  };

  POD_CLASS secret_key: ec_scalar {
    friend class crypto_ops;
  };

  POD_CLASS key_derivation: ec_point {
    friend class crypto_ops;
  };

  POD_CLASS key_image: ec_point {
    friend class crypto_ops;
  };

  POD_CLASS signature {
    ec_scalar c, r;
    friend class crypto_ops;
  };

  POD_CLASS view_tag {
    char data;
  };
#pragma pack(pop)

  static_assert(sizeof(ec_point) == 32 && sizeof(ec_scalar) == 32 &&
    sizeof(public_key) == 32 && sizeof(secret_key) == 32 &&
    sizeof(key_derivation) == 32 && sizeof(key_image) == 32 &&
    sizeof(signature) == 64 && sizeof(view_tag) == 1, "Invalid structure size");

  class crypto_ops {
    crypto_ops();
    crypto_ops(const crypto_ops &);
    void operator=(const crypto_ops &);
    ~crypto_ops();

    static bool check_key(const public_key &);
    friend bool check_key(const public_key &);
  };

  /* Generate a value filled with random bytes.
   */
  template<typename T>
  typename std::enable_if<std::is_pod<T>::value, T>::type rand() {
    typename std::remove_cv<T>::type res;
    std::lock_guard<std::mutex> lock(random_lock);
    generate_random_bytes(sizeof(T), &res);
    return res;
  }

  /* Check a public key. Returns true if it is valid, false otherwise.
   */
  inline bool check_key(const public_key &key) {
    return crypto_ops::check_key(key);
  }
}

CRYPTO_MAKE_COMPARABLE(public_key)
CRYPTO_MAKE_HASHABLE(key_image)
CRYPTO_MAKE_COMPARABLE(signature)
CRYPTO_MAKE_COMPARABLE(view_tag)

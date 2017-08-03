// Copyright (c) 2012-2013 The Cryptonote developers
// Distributed under the MIT/X11 software license, see the accompanying
// file COPYING or http://www.opensource.org/licenses/mit-license.php.

#pragma once
#include "../hash.h"
#include "../wild_keccak.h"


namespace cryptonote
{
  template<typename callback_t>
  bool get_blob_longhash_bb(const blobdata& bd, crypto::hash& res, uint64_t height, callback_t accessor)
  {
    crypto::wild_keccak_dbl<crypto::mul_f>(reinterpret_cast<const uint8_t*>(bd.data()), bd.size(), reinterpret_cast<uint8_t*>(&res), sizeof(res), [&](crypto::state_t_m& st, crypto::mixin_t& mix)
    {
      if(!height)
      {
        memset(&mix, 0, sizeof(mix));
        return;
      }
#define GET_H(index) accessor(st[index])
      for(size_t i = 0; i!=6; i++)
      {
        *(crypto::hash*)&mix[i*4]  = XOR_4(GET_H(i*4), GET_H(i*4+1), GET_H(i*4+2), GET_H(i*4+3));
      }
    });
    return true;
  }
}

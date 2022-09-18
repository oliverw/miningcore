// Copyright (c) 2019, Haven Protocol
// 
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without modification, are
// permitted provided that the following conditions are met:
// 
// 1. Redistributions of source code must retain the above copyright notice, this list of
//    conditions and the following disclaimer.
// 
// 2. Redistributions in binary form must reproduce the above copyright notice, this list
//    of conditions and the following disclaimer in the documentation and/or other
//    materials provided with the distribution.
// 
// 3. Neither the name of the copyright holder nor the names of its contributors may be
//    used to endorse or promote products derived from this software without specific
//    prior written permission.
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY
// EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF
// MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL
// THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
// SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
// PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
// INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT,
// STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF
// THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
// 
// Parts of this file are originally copyright (c) 2012-2013 The Cryptonote developers

#pragma once

#include <vector>

#include "serialization.h"
#include "offshore/pricing_record.h"
#include "cryptonote_config.h"

// read
template <template <bool> class Archive>
bool do_serialize(Archive<false> &ar, offshore::pricing_record &pr, uint8_t version)
{
  if (version < HF_VERSION_XASSET_FEES_V2)
  {
    // very basic sanity check
    if (ar.remaining_bytes() < sizeof(offshore::pricing_record_v1)) {
      ar.stream().setstate(std::ios::failbit);
      return false;
    }

    offshore::pricing_record_v1 pr_v1;
    ar.serialize_blob(&pr_v1, sizeof(offshore::pricing_record_v1), "");
    if (!ar.stream().good())
      return false;

    if (!pr_v1.write_to_pr(pr))
      return false;
  }
  else
  {
    // very basic sanity check
    if (ar.remaining_bytes() < sizeof(offshore::pricing_record)) {
      ar.stream().setstate(std::ios::failbit);
      return false;
    }

    ar.serialize_blob(&pr, sizeof(offshore::pricing_record), "");
    if (!ar.stream().good())
      return false;
  }
  return true;
}

// write
template <template <bool> class Archive>
bool do_serialize(Archive<true> &ar, offshore::pricing_record &pr, uint8_t version)
{
  ar.begin_string();
  if (version < HF_VERSION_XASSET_FEES_V2)
  {
    offshore::pricing_record_v1 pr_v1;
    if (!pr_v1.read_from_pr(pr))
      return false;
    ar.serialize_blob(&pr_v1, sizeof(offshore::pricing_record_v1), "");
  }
  else
  {
    ar.serialize_blob(&pr, sizeof(offshore::pricing_record), "");
  }
  if (!ar.stream().good())
    return false;
  ar.end_string();
  return true;
}

BLOB_SERIALIZER(offshore::pricing_record);

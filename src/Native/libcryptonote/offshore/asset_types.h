// Copyright (c) 2021, Haven Protocol
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

#pragma once
#include <string>
#include <vector>

namespace offshore {

  const std::vector<std::string> ASSET_TYPES = {"XHV", "XAG", "XAU", "XAUD", "XBTC", "XCAD", "XCHF", "XCNY", "XEUR", "XGBP", "XJPY", "XNOK", "XNZD", "XUSD"};

  class asset_type_counts
  {

    public:

      // Fields 
      uint64_t XHV;
      uint64_t XAG;
      uint64_t XAU;
      uint64_t XAUD;
      uint64_t XBTC;
      uint64_t XCAD;
      uint64_t XCHF;
      uint64_t XCNY;
      uint64_t XEUR;
      uint64_t XGBP;
      uint64_t XJPY;
      uint64_t XNOK;
      uint64_t XNZD;
      uint64_t XUSD;

      asset_type_counts() noexcept
        : XHV(0)
        , XAG(0)
        , XAU(0)
        , XAUD(0)
        , XBTC(0)
        , XCAD(0)
        , XCHF(0)
        , XCNY(0)
        , XEUR(0)
        , XGBP(0)
        , XJPY(0)
        , XNOK(0)
        , XNZD(0)
        , XUSD(0)
      {
      }

      uint64_t operator[](const std::string asset_type) const noexcept
      {
        if (asset_type == "XHV") {
          return XHV;
        } else if (asset_type == "XUSD") {
          return XUSD;
        } else if (asset_type == "XAG") {
          return XAG;
        } else if (asset_type == "XAU") {
          return XAU;
        } else if (asset_type == "XAUD") {
          return XAUD;
        } else if (asset_type == "XBTC") {
          return XBTC;
        } else if (asset_type == "XCAD") {
          return XCAD;
        } else if (asset_type == "XCHF") {
          return XCHF;
        } else if (asset_type == "XCNY") {
          return XCNY;
        } else if (asset_type == "XEUR") {
          return XEUR;
        } else if (asset_type == "XGBP") {
          return XGBP;
        } else if (asset_type == "XJPY") {
          return XJPY;
        } else if (asset_type == "XNOK") {
          return XNOK;
        } else if (asset_type == "XNZD") {
          return XNZD;
        }

        return 0;
      }

      void add(const std::string asset_type, const uint64_t val)
      {
        if (asset_type == "XHV") {
          XHV += val;
        } else if (asset_type == "XUSD") {
          XUSD += val;
        } else if (asset_type == "XAG") {
          XAG += val;
        } else if (asset_type == "XAU") {
          XAU += val;
        } else if (asset_type == "XAUD") {
          XAUD += val;
        } else if (asset_type == "XBTC") {
          XBTC += val;
        } else if (asset_type == "XCAD") {
          XCAD += val;
        } else if (asset_type == "XCHF") {
          XCHF += val;
        } else if (asset_type == "XCNY") {
          XCNY += val;
        } else if (asset_type == "XEUR") {
          XEUR += val;
        } else if (asset_type == "XGBP") {
          XGBP += val;
        } else if (asset_type == "XJPY") {
          XJPY += val;
        } else if (asset_type == "XNOK") {
          XNOK += val;
        } else if (asset_type == "XNZD") {
          XNZD += val;
        }
      }
  };
}

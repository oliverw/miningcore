// Copyright (c) 2017-2018, The Monero Project
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

#pragma once

#include <array>
#include <cstdint>
#include <iosfwd>
#include <string>

#include "span.h"

namespace epee
{
  struct to_hex
  {
    //! \return A std::string containing hex of `src`.
    static std::string string(const span<const std::uint8_t> src);

    //! \return An array containing hex of `src`.
    template<std::size_t N>
    static std::array<char, N * 2> array(const std::array<std::uint8_t, N>& src) noexcept
    {
      std::array<char, N * 2> out{{}};
      static_assert(N <= 128, "keep the stack size down");
      buffer_unchecked(out.data(), {src.data(), src.size()});
      return out;
    }

    //! Append `src` as hex to `out`.
    static void buffer(std::ostream& out, const span<const std::uint8_t> src);

    //! Append `< + src + >` as hex to `out`.
    static void formatted(std::ostream& out, const span<const std::uint8_t> src);

  private:
    //! Write `src` bytes as hex to `out`. `out` must be twice the length
    static void buffer_unchecked(char* out, const span<const std::uint8_t> src) noexcept;
  };
}

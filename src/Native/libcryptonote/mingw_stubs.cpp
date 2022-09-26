#include <string>
#include <stdexcept>
#include <vector>
#include <ios>
#include "serialization/variant.h"
#include "serialization/serialization.h"
#include "hex.h"
#include "ringct/rctTypes.h"

static const char *errorMsg = "Don't call me. I'm a stub";

std::string epee::to_hex::string(const epee::span<const std::uint8_t> src)
{
  throw std::runtime_error(errorMsg);
}

size_t rct::n_bulletproof_max_amounts(const std::vector<rct::Bulletproof> &proofs)
{
  throw std::runtime_error(errorMsg);
}

extern "C" void cn_slow_hash(const void *data, size_t length, char *hash)
{
  throw std::runtime_error(errorMsg);
}

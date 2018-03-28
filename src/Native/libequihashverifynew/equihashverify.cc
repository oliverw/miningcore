#include <vector>
#include "crypto/equihash.h"
#include "equihashverify.h"

bool verifyEH(const char *hdr, const std::vector<unsigned char> &soln){
  unsigned int n = 200;
  unsigned int k = 9;

  // Hash state
  crypto_generichash_blake2b_state state;
  EhInitialiseState(n, k, state);

  crypto_generichash_blake2b_update(&state, (const unsigned char*)hdr, 140);

  bool isValid = Eh200_9.IsValidSolution(state, soln);

  return isValid;
}

#include "yespower_sugarchain.h"
/* 
 * yespower for sugarchain
 */
int yespower_hash(const char *input, char *output)
{
	yespower_params_t params = {
		.version = YESPOWER_1_0,
		.N = 2048,
		.r = 32,
		.pers = "Satoshi Nakamoto 31/Oct/2008 Proof-of-work is essentially one-CPU-one-vote",
		.perslen = 74
	};
	return yespower_tls(input, 80, &params, (yespower_binary_t *) output);
}
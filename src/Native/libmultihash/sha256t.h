#ifndef SHA256T_H
#define SHA256T_H

#include "sha256.h"

static void
SHA256t_Init(SHA256_CTX * ctx)
{
  ctx->count[0] = 0;
  ctx->count[1] = 512;
  ctx->state[0] = 0xDFA9BF2C;
  ctx->state[1] = 0xB72074D4;
  ctx->state[2] = 0x6BB01122;
  ctx->state[3] = 0xD338E869;
  ctx->state[4] = 0xAA3FF126;
  ctx->state[5] = 0x475BBF30;
  ctx->state[6] = 0x8FD52E5B;
  ctx->state[7] = 0x9F75C9AD;
}
#endif

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define false 0
#define true 1
#define uint32_t unsigned int

#define Ch(x, y, z)     ((x & (y ^ z)) ^ z)
#define Maj(x, y, z)    ((x & (y | z)) | (y & z))
#define ROTR(x, n)      ((x >> n) | (x << (32 - n)))
#define S0(x)           (ROTR(x, 2) ^ ROTR(x, 13) ^ ROTR(x, 22))
#define S1(x)           (ROTR(x, 6) ^ ROTR(x, 11) ^ ROTR(x, 25))
#define s0(x)           (ROTR(x, 7) ^ ROTR(x, 18) ^ (x >> 3))
#define s1(x)           (ROTR(x, 17) ^ ROTR(x, 19) ^ (x >> 10))

const char hexmap[16] = {'0','1','2','3','4','5','6','7','8','9','a','b','c','d','e','f'};
const uint32_t K[64] = {
    0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5,
    0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
    0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3,
    0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
    0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc,
    0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
    0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7,
    0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
    0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13,
    0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
    0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3,
    0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
    0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5,
    0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
    0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208,
    0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
};

void sha256(unsigned char * instr, unsigned char * hash, unsigned int len)
{

 uint32_t H[8] = {
    0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a,
    0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19
 };
 uint32_t W[64] = {0};
 unsigned int T1, T2, t;
 unsigned int a, b, c, d, e, f, g, h, i;
 unsigned int curBlock = 0;

 unsigned int l = (len + 1) / 4 + 2;
 unsigned int N = (l + 15) / 16;
 instr[len] = 128; // Hash Terminator
 for (t = len + 1; t < N * 64; t++) instr[t] = 0;

 for (curBlock = 0; curBlock < N; curBlock ++) {
  for (t = 0; t < 16; t++) W[t] = (instr[curBlock * 64 + t * 4 + 0] << 24) + (instr[curBlock * 64 + t * 4 + 1] << 16) + (instr[curBlock * 64 + t * 4 + 2] << 8) + instr[curBlock * 64 + t * 4 + 3];
  if (curBlock == N - 1) W[15] = len * 8;
  for (t = 16; t < 64; t++) W[t] = s1(W[t-2]) + W[t-7] + s0(W[t-15]) + W[t-16];

  a = H[0]; b = H[1]; c = H[2]; d = H[3]; e = H[4]; f = H[5]; g = H[6]; h = H[7];
  for (t = 0; t < 64 ; t++) {
   T1 = h + S1(e) + Ch(e,f,g) + K[t] + W[t];
   T2 = S0(a) + Maj(a,b,c);
   h = g;
   g = f;
   f = e;
   e = d + T1;
   d = c;
   c = b;
   b = a;
   a = T1 + T2;
  }

  H[0] += a; H[1] += b; H[2] += c; H[3] += d; H[4] += e; H[5] += f; H[6] += g; H[7] += h;
 }


 t = 0; i = H[t]; hash[t * 8 + 7] = i & 0xf; hash[t * 8 + 6] = (i >> 4) & 0xf; hash[t * 8 + 5] = (i >> 8) & 0xf; hash[t * 8 + 4] = (i >> 12) & 0xf; hash[t * 8 + 3] = (i >> 16) & 0xf; hash[t * 8 + 2] = (i >> 20) & 0xf; hash[t * 8 + 1] = (i >> 24) & 0xf; hash[t * 8 + 0] = (i >> 28) & 0xf;
 t = 1; i = H[t]; hash[t * 8 + 7] = i & 0xf; hash[t * 8 + 6] = (i >> 4) & 0xf; hash[t * 8 + 5] = (i >> 8) & 0xf; hash[t * 8 + 4] = (i >> 12) & 0xf; hash[t * 8 + 3] = (i >> 16) & 0xf; hash[t * 8 + 2] = (i >> 20) & 0xf; hash[t * 8 + 1] = (i >> 24) & 0xf; hash[t * 8 + 0] = (i >> 28) & 0xf;
 t = 2; i = H[t]; hash[t * 8 + 7] = i & 0xf; hash[t * 8 + 6] = (i >> 4) & 0xf; hash[t * 8 + 5] = (i >> 8) & 0xf; hash[t * 8 + 4] = (i >> 12) & 0xf; hash[t * 8 + 3] = (i >> 16) & 0xf; hash[t * 8 + 2] = (i >> 20) & 0xf; hash[t * 8 + 1] = (i >> 24) & 0xf; hash[t * 8 + 0] = (i >> 28) & 0xf;
 t = 3; i = H[t]; hash[t * 8 + 7] = i & 0xf; hash[t * 8 + 6] = (i >> 4) & 0xf; hash[t * 8 + 5] = (i >> 8) & 0xf; hash[t * 8 + 4] = (i >> 12) & 0xf; hash[t * 8 + 3] = (i >> 16) & 0xf; hash[t * 8 + 2] = (i >> 20) & 0xf; hash[t * 8 + 1] = (i >> 24) & 0xf; hash[t * 8 + 0] = (i >> 28) & 0xf;
 t = 4; i = H[t]; hash[t * 8 + 7] = i & 0xf; hash[t * 8 + 6] = (i >> 4) & 0xf; hash[t * 8 + 5] = (i >> 8) & 0xf; hash[t * 8 + 4] = (i >> 12) & 0xf; hash[t * 8 + 3] = (i >> 16) & 0xf; hash[t * 8 + 2] = (i >> 20) & 0xf; hash[t * 8 + 1] = (i >> 24) & 0xf; hash[t * 8 + 0] = (i >> 28) & 0xf;
 t = 5; i = H[t]; hash[t * 8 + 7] = i & 0xf; hash[t * 8 + 6] = (i >> 4) & 0xf; hash[t * 8 + 5] = (i >> 8) & 0xf; hash[t * 8 + 4] = (i >> 12) & 0xf; hash[t * 8 + 3] = (i >> 16) & 0xf; hash[t * 8 + 2] = (i >> 20) & 0xf; hash[t * 8 + 1] = (i >> 24) & 0xf; hash[t * 8 + 0] = (i >> 28) & 0xf;
 t = 6; i = H[t]; hash[t * 8 + 7] = i & 0xf; hash[t * 8 + 6] = (i >> 4) & 0xf; hash[t * 8 + 5] = (i >> 8) & 0xf; hash[t * 8 + 4] = (i >> 12) & 0xf; hash[t * 8 + 3] = (i >> 16) & 0xf; hash[t * 8 + 2] = (i >> 20) & 0xf; hash[t * 8 + 1] = (i >> 24) & 0xf; hash[t * 8 + 0] = (i >> 28) & 0xf;
 t = 7; i = H[t]; hash[t * 8 + 7] = i & 0xf; hash[t * 8 + 6] = (i >> 4) & 0xf; hash[t * 8 + 5] = (i >> 8) & 0xf; hash[t * 8 + 4] = (i >> 12) & 0xf; hash[t * 8 + 3] = (i >> 16) & 0xf; hash[t * 8 + 2] = (i >> 20) & 0xf; hash[t * 8 + 1] = (i >> 24) & 0xf; hash[t * 8 + 0] = (i >> 28) & 0xf;
}

void hexToAsc(unsigned char * tmp_list, unsigned int len) {
  unsigned int i;
  for (i = 0; i < len; i++) tmp_list[i] = hexmap[ tmp_list[i] ];
}


unsigned char mix_hashed_num(unsigned char * hashedData, unsigned char * ret_list, unsigned int * ret_list_len) {
  unsigned char * tmp_list = malloc(128);
  unsigned int i, index = 0, tmp_val;
  for (i = 0; i < 64; i++) tmp_list[i] = 255;
  *ret_list_len = 0;
  unsigned int counter = 0;

  unsigned char hashed_end = false;
  while (hashed_end == false) {
    counter += 1;
    index += hashedData[index] + 1;

    if (index >= 64) {
      index %= 64;
      hexToAsc(hashedData, 64);
      sha256(hashedData, hashedData, 64);
    }

    tmp_val = hexmap[ hashedData[index] ];
    tmp_list[64] = tmp_val;
    sha256(tmp_list, tmp_list, 65);
    hexToAsc(tmp_list, 64);

    if (index == 63)
      if (tmp_val == tmp_list[63])
        hashed_end = true;

    for(i = 0; i < 64;i++) ret_list[*ret_list_len + i] = tmp_list[i];
    *ret_list_len += 64;

    if (*ret_list_len > 1048576) {
      free(tmp_list); 
      return false; 
    }
  }
  free(tmp_list); return true;
}


void dcrypt_hash(const char * input, char * hash, uint32_t len) {
  unsigned char * instr = malloc(len);
  memcpy( instr, input, len );
  unsigned char * hashed = malloc(128);
  unsigned char * mixedHash = malloc(1048576 + 1024); // This assumes a max length of work of 1024 bytes?;
  unsigned char * finalToHash;
  unsigned int lenMixedHash = 0;
  sha256(instr, hashed, len); 
  if (mix_hashed_num(hashed, mixedHash, &lenMixedHash) == true) {
    finalToHash = malloc( lenMixedHash + len + 64 );
    memcpy( finalToHash, mixedHash, lenMixedHash);
    memcpy( &(finalToHash[lenMixedHash]), instr, len);
    sha256(finalToHash, hash, len + lenMixedHash);
    free(finalToHash);
  } else {
    printf("Buffer limit exceeded.\n");
  }
  free(mixedHash);
}

/* :int main(int argc, char *argv[])
{
  char * hash  = malloc(64);
  int i;
  
  memset(hash,  0, 32);
  char instr[128] = "The quick brown fox jumps over the lazy dog";

  dcrypt_hash(instr, hash, 43); 
  
  printf("\nHash result\n");
  for (i = 0 ; i < 64; i++) printf("%01x", hash[i]);  printf("\n");

  free(hash);
  return 0;
} */

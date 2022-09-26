// Copyright (c) 2019, Haven Protocol
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
// Portions of this code based upon code Copyright (c) 2019, The Monero Project

#include "pricing_record.h"

#include "serialization/keyvalue_serialization.h"
#include "storages/portable_storage.h"

#include "string_tools.h"
namespace offshore
{

  namespace
  {
    struct pr_serialized
    {
      uint64_t xAG;
      uint64_t xAU;
      uint64_t xAUD;
      uint64_t xBTC;
      uint64_t xCAD;
      uint64_t xCHF;
      uint64_t xCNY;
      uint64_t xEUR;
      uint64_t xGBP;
      uint64_t xJPY;
      uint64_t xNOK;
      uint64_t xNZD;
      uint64_t xUSD;
      uint64_t unused1;
      uint64_t unused2;
      uint64_t unused3;
      uint64_t timestamp;
      std::string signature;

      BEGIN_KV_SERIALIZE_MAP()
        KV_SERIALIZE(xAG)
        KV_SERIALIZE(xAU)
        KV_SERIALIZE(xAUD)
        KV_SERIALIZE(xBTC)
        KV_SERIALIZE(xCAD)
        KV_SERIALIZE(xCHF)
        KV_SERIALIZE(xCNY)
        KV_SERIALIZE(xEUR)
        KV_SERIALIZE(xGBP)
        KV_SERIALIZE(xJPY)
        KV_SERIALIZE(xNOK)
        KV_SERIALIZE(xNZD)
        KV_SERIALIZE(xUSD)
        KV_SERIALIZE(unused1)
        KV_SERIALIZE(unused2)
        KV_SERIALIZE(unused3)
        KV_SERIALIZE(timestamp)
        KV_SERIALIZE(signature)
      END_KV_SERIALIZE_MAP()
    };
  }
  
  pricing_record::pricing_record() noexcept
    : xAG(0)
    , xAU(0)
    , xAUD(0)
    , xBTC(0)
    , xCAD(0)
    , xCHF(0)
    , xCNY(0)
    , xEUR(0)
    , xGBP(0)
    , xJPY(0)
    , xNOK(0)
    , xNZD(0)
    , xUSD(0)
    , unused1(0)
    , unused2(0)
    , unused3(0)
    , timestamp(0)
  {
    std::memset(signature, 0, sizeof(signature));
  }

  bool pricing_record::_load(epee::serialization::portable_storage& src, epee::serialization::section* hparent)
  {
    pr_serialized in{};
    if (in._load(src, hparent))
    {
      // Copy everything into the local instance
      xAG = in.xAG;
      xAU = in.xAU;
      xAUD = in.xAUD;
      xBTC = in.xBTC;
      xCAD = in.xCAD;
      xCHF = in.xCHF;
      xCNY = in.xCNY;
      xEUR = in.xEUR;
      xGBP = in.xGBP;
      xJPY = in.xJPY;
      xNOK = in.xNOK;
      xNZD = in.xNZD;
      xUSD = in.xUSD;
      unused1 = in.unused1;
      unused2 = in.unused2;
      unused3 = in.unused3;
      timestamp = in.timestamp;
      for (unsigned int i = 0; i < in.signature.length(); i += 2) {
        std::string byteString = in.signature.substr(i, 2);
        signature[i>>1] = (char) strtol(byteString.c_str(), NULL, 16);
      }
      return true;
    }

    // Report error here?
    return false;
  }

  bool pricing_record::store(epee::serialization::portable_storage& dest, epee::serialization::section* hparent) const
  {
    std::string sig_hex;
    for (unsigned int i=0; i<64; i++) {
      std::stringstream ss;
      ss << std::hex << std::setw(2) << std::setfill('0') << (0xff & signature[i]);
      sig_hex += ss.str();
    }
    const pr_serialized out{xAG,xAU,xAUD,xBTC,xCAD,xCHF,xCNY,xEUR,xGBP,xJPY,xNOK,xNZD,xUSD,unused1,unused2,unused3,timestamp,sig_hex};
    return out.store(dest, hparent);
  }

  pricing_record::pricing_record(const pricing_record& orig) noexcept
    : xAG(orig.xAG)
    , xAU(orig.xAU)
    , xAUD(orig.xAUD)
    , xBTC(orig.xBTC)
    , xCAD(orig.xCAD)
    , xCHF(orig.xCHF)
    , xCNY(orig.xCNY)
    , xEUR(orig.xEUR)
    , xGBP(orig.xGBP)
    , xJPY(orig.xJPY)
    , xNOK(orig.xNOK)
    , xNZD(orig.xNZD)
    , xUSD(orig.xUSD)
    , unused1(orig.unused1)
    , unused2(orig.unused2)
    , unused3(orig.unused3)
    , timestamp(orig.timestamp)
  {
    std::memcpy(signature, orig.signature, sizeof(signature));
  }

  pricing_record& pricing_record::operator=(const pricing_record& orig) noexcept
  {
    xAG = orig.xAG;
    xAU = orig.xAU;
    xAUD = orig.xAUD;
    xBTC = orig.xBTC;
    xCAD = orig.xCAD;
    xCHF = orig.xCHF;
    xCNY = orig.xCNY;
    xEUR = orig.xEUR;
    xGBP = orig.xGBP;
    xJPY = orig.xJPY;
    xNOK = orig.xNOK;
    xNZD = orig.xNZD;
    xUSD = orig.xUSD;
    unused1 = orig.unused1;
    unused2 = orig.unused2;
    unused3 = orig.unused3;
    timestamp = orig.timestamp;
    ::memcpy(signature, orig.signature, sizeof(signature));
    return *this;
  }

  uint64_t pricing_record::operator[](const std::string& asset_type) const
  {
    if (asset_type == "XHV") {
      return 1000000000000;
    } else if (asset_type == "XUSD") {
      return unused1;
    } else if (asset_type == "XAG") {
      return xAG;
    } else if (asset_type == "XAU") {
      return xAU;
    } else if (asset_type == "XAUD") {
      return xAUD;
    } else if (asset_type == "XBTC") {
      return xBTC;
    } else if (asset_type == "XCAD") {
      return xCAD;
    } else if (asset_type == "XCHF") {
      return xCHF;
    } else if (asset_type == "XCNY") {
      return xCNY;
    } else if (asset_type == "XEUR") {
      return xEUR;
    } else if (asset_type == "XGBP") {
      return xGBP;
    } else if (asset_type == "XJPY") {
      return xJPY;
    } else if (asset_type == "XNOK") {
      return xNOK;
    } else if (asset_type == "XNZD") {
      return xNZD;
    } else {
     CHECK_AND_ASSERT_THROW_MES(false, "Asset type doesn't exist in pricing record!");
    }
  }
  
  bool pricing_record::equal(const pricing_record& other) const noexcept
  {
    return ((xAG == other.xAG) &&
	    (xAU == other.xAU) &&
	    (xAUD == other.xAUD) &&
	    (xBTC == other.xBTC) &&
	    (xCAD == other.xCAD) &&
	    (xCHF == other.xCHF) &&
	    (xCNY == other.xCNY) &&
	    (xEUR == other.xEUR) &&
	    (xGBP == other.xGBP) &&
	    (xJPY == other.xJPY) &&
	    (xNOK == other.xNOK) &&
	    (xNZD == other.xNZD) &&
	    (xUSD == other.xUSD) &&
	    (unused1 == other.unused1) &&
	    (unused2 == other.unused2) &&
	    (unused3 == other.unused3) &&
	    (timestamp == other.timestamp) &&
	    !::memcmp(signature, other.signature, sizeof(signature)));
  }

  bool pricing_record::empty() const noexcept
  {
    const pricing_record empty_pr = offshore::pricing_record();
    return (*this).equal(empty_pr);
  }

  bool pricing_record::verifySignature(const std::string& public_key) const 
  {
    CHECK_AND_ASSERT_THROW_MES(!public_key.empty(), "Pricing record verification failed. NULL public key. PK Size: " << public_key.size()); // TODO: is this necessary or the one below already covers this case, meannin it will produce empty pubkey?
    
    // extract the key
    EVP_PKEY* pubkey;
    BIO* bio = BIO_new_mem_buf(public_key.c_str(), public_key.size());
    if (!bio) {
      return false;
    }
    pubkey = PEM_read_bio_PUBKEY(bio, NULL, NULL, NULL);
    BIO_free(bio);
    CHECK_AND_ASSERT_THROW_MES(pubkey != NULL, "Pricing record verification failed. NULL public key.");


    // Convert our internal 64-byte binary representation into 128-byte hex string
    std::string sig_hex;
    for (unsigned int i=0; i<64; i++) {
      std::stringstream ss;
      ss << std::hex << std::setw(2) << std::setfill('0') << (0xff & signature[i]);
      sig_hex += ss.str();
    }
    
    // Rebuild the OpenSSL format of the signature from the r+s values
    std::string sig_rebuilt = "30";
    std::string r_rebuilt = (signature[0] == 0) ? sig_hex.substr(2, 62) : sig_hex.substr(0,64);
    if (signature[(signature[0] == 0) ? 1 : 0] & 0x80)
      r_rebuilt = "00" + r_rebuilt;
    std::string s_rebuilt = (signature[(signature[32] == 0) ? 33 : 32] == 0) ? sig_hex.substr(66, 62) : sig_hex.substr(64,64);
    if (signature[(signature[32] == 0) ? 33 : 32] & 0x80)
      s_rebuilt = "00" + s_rebuilt;
    size_t sig_length = (r_rebuilt.length() + s_rebuilt.length() + 8) >> 1;
    std::stringstream ss;
    ss << std::hex << sig_length;
    sig_rebuilt += std::string(2-ss.str().length(), '0') + ss.str();
    ss.clear();
    sig_rebuilt += "02";
    size_t r_length = r_rebuilt.length() >> 1;
    std::stringstream ss2;
    ss2 << std::hex << r_length;
    sig_rebuilt += std::string(2-ss2.str().length(), '0') + ss2.str();
    ss2.clear();
    sig_rebuilt += r_rebuilt;
    sig_rebuilt += "02";
    size_t s_length = s_rebuilt.length() >> 1;
    std::stringstream ss3;
    ss3 << std::hex << s_length;
    sig_rebuilt += std::string(2-ss3.str().length(), '0') + ss3.str();
    ss3.clear();
    sig_rebuilt += s_rebuilt;

    // Build the JSON string, so that we can verify the signature
    std::ostringstream oss;
    oss << "{\"xAG\":" << xAG;
    oss << ",\"xAU\":" << xAU;
    oss << ",\"xAUD\":" << xAUD;
    oss << ",\"xBTC\":" << xBTC;
    oss << ",\"xCAD\":" << xCAD;
    oss << ",\"xCHF\":" << xCHF;
    oss << ",\"xCNY\":" << xCNY;
    oss << ",\"xEUR\":" << xEUR;
    oss << ",\"xGBP\":" << xGBP;
    oss << ",\"xJPY\":" << xJPY;
    oss << ",\"xNOK\":" << xNOK;
    oss << ",\"xNZD\":" << xNZD;
    oss << ",\"xUSD\":" << xUSD;
    oss << ",\"unused1\":" << unused1;
    oss << ",\"unused2\":" << unused2;
    oss << ",\"unused3\":" << unused3;
    if (timestamp > 0)
      oss << ",\"timestamp\":" << timestamp;
    oss << "}";
    std::string message = oss.str();    

    // Convert signature from hex-encoded to binary
    std::string compact;
    for (unsigned int i = 0; i < sig_rebuilt.length(); i += 2) {
      std::string byteString = sig_rebuilt.substr(i, 2);
      char byte = (char) strtol(byteString.c_str(), NULL, 16);
      compact += (byte);
    }

    // Create a verify digest from the message
    EVP_MD_CTX *ctx = EVP_MD_CTX_create();
    int ret = 0;
    if (ctx) {
      ret = EVP_DigestVerifyInit(ctx, NULL, EVP_sha256(), NULL, pubkey);
      if (ret == 1) {
        ret = EVP_DigestVerifyUpdate(ctx, message.data(), message.length());
        if (ret == 1) {
          ret = EVP_DigestVerifyFinal(ctx, (const unsigned char *)compact.data(), compact.length());
        }
      }
    }

    // Cleanup the context we created
    EVP_MD_CTX_destroy(ctx);
    // Cleanup the openssl stuff
    EVP_PKEY_free(pubkey);
    
    if (ret == 1)
      return true;

    // Get the errors from OpenSSL
    //ERR_print_errors_fp (stderr);
  
    return false;
  }

  void pricing_record::set_for_height_821428() {
    const std::string pr_821428 = "9b3f6f2f8f0000003d620e1202000000be71be2555120000b8627010000000000000000000000000ea0885b2270d00000000000000000000f797ff9be00b0000ddbdb005270a0000fc90cfe02b01060000000000000000000000000000000000d0a28224000e000000d643be960e0000002e8bb6a40e000000f8a817f80d00002f5d27d45cdbfbac3d0f6577103f68de30895967d7562fbd56c161ae90130f54301b1ea9d5fd062f37dac75c3d47178bc6f149d21da1ff0e8430065cb762b93a";
    this->xAG = 614976143259;
    this->xAU = 8892867133;
    this->xAUD = 20156914758078;
    this->xBTC = 275800760;
    this->xCAD = 0;
    this->xCHF = 14464149948650;
    this->xCNY = 0;
    this->xEUR = 13059317798903;
    this->xGBP = 11162715471325;
    this->xJPY = 1690137827184892;
    this->xNOK = 0;
    this->xNZD = 0;
    this->xUSD = 15393775330000;
    this->unused1 = 16040600000000;
    this->unused2 = 16100600000000;
    this->unused3 = 15359200000000;
    this->timestamp = 0;
    std::string sig = "2f5d27d45cdbfbac3d0f6577103f68de30895967d7562fbd56c161ae90130f54301b1ea9d5fd062f37dac75c3d47178bc6f149d21da1ff0e8430065cb762b93a";
    int j=0;
    for (unsigned int i = 0; i < sig.size(); i += 2) {
      std::string byteString = sig.substr(i, 2);
      this->signature[j++] = (char) strtol(byteString.c_str(), NULL, 16);
    }
  }

  // overload for pr validation for block
  bool pricing_record::valid(uint32_t hf_version, uint64_t bl_timestamp, uint64_t last_bl_timestamp) const 
  {
    // check for empty pr 
    if (hf_version >= HF_VERSION_XASSET_FEES_V2) {
      if (this->empty())
        return true;
    } else {
      unsigned char test_sig[64];
      std::memset(test_sig, 0, sizeof(test_sig));
      if (std::memcmp(test_sig, this->signature, sizeof(this->signature)) == 0) {
        return true;
      }
    }

    // Oracle public keys
    std::string const mainnet_public_key = "-----BEGIN PUBLIC KEY-----\n"
      "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE5YBxWx1AZCA9jTUk8Pr2uZ9jpfRt\n"
      "KWv3Vo1/Gny+1vfaxsXhBQiG1KlHkafNGarzoL0WHW4ocqaaqF5iv8i35A==\n"
      "-----END PUBLIC KEY-----\n";
    std::string const testnet_public_key = "-----BEGIN PUBLIC KEY-----\n"
      "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEtWqvQh7OdXrdgXcDeBMRVfLWTW3F\n"
      "wByeoVJFBfZymScJIJl46j66xG6ngnyj4ai4/QPFnSZ1I9jjMRlTWC4EPA==\n"
      "-----END PUBLIC KEY-----\n";
    std::string const stagenet_public_key = "-----BEGIN PUBLIC KEY-----\n"
      "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEtWqvQh7OdXrdgXcDeBMRVfLWTW3F\n"
      "wByeoVJFBfZymScJIJl46j66xG6ngnyj4ai4/QPFnSZ1I9jjMRlTWC4EPA==\n"
      "-----END PUBLIC KEY-----\n";
    
    // Comment out all but 1 of the following lines to select the correct Oracle PK
    std::string const public_key = mainnet_public_key;
    //std::string const public_key = testnet_public_key;
    //std::string const public_key = stagenet_public_key;

    // verify the signature
    if (!verifySignature(public_key)) {
      LOG_ERROR("Invalid pricing record signature.");
      return false;
    }
  
    // valiadte the timestmap
    if (hf_version >= HF_VERSION_XASSET_FEES_V2) {
      if (this->timestamp > bl_timestamp + PRICING_RECORD_VALID_TIME_DIFF_FROM_BLOCK) {
        LOG_ERROR("Pricing record timestamp is too far in the future.");
        return false;
      }
      if (this->timestamp <= last_bl_timestamp) {
        LOG_ERROR("Pricing record timestamp is too old.");
        return false;
      }
    }

    return true;
  }

}
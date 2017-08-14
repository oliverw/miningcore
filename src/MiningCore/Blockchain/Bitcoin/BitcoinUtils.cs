using System;
using System.Linq;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace MiningCore.Blockchain.Bitcoin
{
    public static class BitcoinUtils
    {
        /// <summary>
        /// Bitcoin addresses are implemented using the Base58Check encoding of the hash of either:
        /// 
        /// Pay-to-script-hash(p2sh): payload is: RIPEMD160(SHA256(redeemScript)) where redeemScript is a 
        /// script the wallet knows how to spend; version byte = 0x05 (these addresses begin with the digit '3')
        /// 
        /// Pay-to-pubkey-hash(p2pkh): payload is RIPEMD160(SHA256(ECDSA_publicKey)) where 
        /// ECDSA_publicKey is a public key the wallet knows the private key for; version byte = 0x00 
        /// (these addresses begin with the digit '1')
        /// 
        /// The resulting hash in both of these cases is always exactly 20 bytes.
        /// </summary>
        public static IDestination AddressToScript(string address)
        {
            var decoded = Encoders.Base58.DecodeData(address);
            if (decoded.Length != 25)
                throw new FormatException($"{address} is invalid");

            // skip first byte which is the version/application byte
            // see: https://en.bitcoin.it/wiki/Base58Check_encoding
            var pubKeyHash = decoded.Skip(1).Take(20).ToArray();

            // convert to IDestination
            var keyId = new KeyId(pubKeyHash);
            return keyId;
        }
    }
}
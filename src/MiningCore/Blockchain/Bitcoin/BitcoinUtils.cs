/*
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

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
        /// Pay-to-script-hash(p2sh): payload is: RIPEMD160(SHA256(redeemScript)) where redeemScript is a
        /// script the wallet knows how to spend; version byte = 0x05 (these addresses begin with the digit '3')
        /// Pay-to-pubkey-hash(p2pkh): payload is RIPEMD160(SHA256(ECDSA_publicKey)) where
        /// ECDSA_publicKey is a public key the wallet knows the private key for; version byte = 0x00
        /// (these addresses begin with the digit '1')
        /// The resulting hash in both of these cases is always exactly 20 bytes.
        /// </summary>
        public static IDestination AddressToDestination(string address)
        {
            var decoded = Encoders.Base58.DecodeData(address);
            var pubKeyHash = decoded.Skip(1).Take(20).ToArray();
            var result = new KeyId(pubKeyHash);
            return result;
        }
    }
}

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Ed25519;
using NSec.Cryptography;

namespace Miningcore.Blockchain.Pandanite
{
    public class Transaction
    {
        public string signature { get; set; }
        public string signingKey { get; set; }
        public string timestamp { get; set; }
        public string to { get; set; }
        public string from { get; set; }
        public ulong amount { get; set; }
        public ulong fee { get; set; }
        public bool isTransactionFee { get; set; }

        public byte[] CalculateHash()
        {
            using (var sha256 = SHA256.Create())
            using (var stream = new MemoryStream())
            {
                stream.Write(CalculateContentHash());
                if (!isTransactionFee) {
                    stream.Write(signature.ToByteArray());
                }
                
                stream.Flush();
                stream.Position = 0;

                return sha256.ComputeHash(stream);
            }
        }

        public byte[] CalculateContentHash()
        {
            using (var sha256 = SHA256.Create())
            using (var stream = new MemoryStream())
            {
                stream.Write(to.ToByteArray());
                if (!isTransactionFee) {
                    stream.Write(from.ToByteArray());
                }
                stream.Write(BitConverter.GetBytes(fee));
                stream.Write(BitConverter.GetBytes(amount));
                stream.Write(BitConverter.GetBytes(ulong.Parse(timestamp)));
                stream.Flush();
                stream.Position = 0;

                return sha256.ComputeHash(stream);
            }
        }

        public unsafe void Sign(byte[] pubKey, byte[] privateKey)
        {
            byte[] sign = new byte[64];
            var hash = CalculateContentHash();

            fixed(byte* sPtr = sign, hPtr = hash, pubPtr = pubKey, privPtr = privateKey) {
                Unmanaged.ed25519_sign(sPtr, hPtr, hash.Length, pubPtr, privPtr);
                signature = sign.AsString();
                signingKey = pubKey.AsString();
            }
        }
    }
}

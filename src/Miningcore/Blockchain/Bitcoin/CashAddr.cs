using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin.DataEncoders;
namespace Miningcore.Blockchain.Bitcoin{

    // https://github.com/bitcoincashjs/bchaddrjs
    public static class BchAddr
    {
        public enum CashFormat
        {
            Legacy,
            Bitpay,
            Cashaddr
        }

        public enum CashNetwork
        {
            Mainnet,
            Testnet,
            RegTest,
            DevaultMainnet
        }

        public enum CashType
        {
            P2PKH,
            P2SH
        }

        public class BchAddrData
        {
            public CashFormat Format { get; set; }
            public CashNetwork Network { get; set; }
            public CashType Type { get; set; }
            public byte[] Hash { get; set; }

            public string GetHash()
            {
                if (Hash == null) return null;
                return Encoders.Hex.EncodeData(Hash);
            }

            public string AsLegacyAddress => EncodeAsLegacy(this);
            public string AsBitpayAddress => EncodeAsBitpay(this);
            public string AsCashaddrAddress => EncodeAsCashaddr(this);
            public string AsCashaddrAddressNoPrefix => EncodeAsCashaddrNoPrefix(this);

            public static BchAddrData Create(CashFormat format, CashNetwork network, CashType type, byte[] hash)
            {
                return new BchAddrData
                {
                    Format = format,
                    Network = network,
                    Type = type,
                    Hash = hash,
                };
            }
        }

        /// <summary>
        /// Encodes the given decoded address into legacy format
        /// </summary>
        /// <param name="decoded"></param>
        /// <returns></returns>
        public static string EncodeAsLegacy(BchAddrData decoded)
        {
            var versionByte = GetVersionByte(CashFormat.Legacy, decoded.Network, decoded.Type);
            var buffer = new byte[1] { versionByte };
            buffer = buffer.Concat(decoded.Hash).ToArray();
            return Encoders.Base58Check.EncodeData(buffer);
        }

        /// <summary>
        /// Encodes the given decoded address into bitpay format
        /// </summary>
        /// <param name="decoded"></param>
        /// <returns></returns>
        public static string EncodeAsBitpay(BchAddrData decoded)
        {
            var versionByte = GetVersionByte(CashFormat.Bitpay, decoded.Network, decoded.Type);
            var buffer = new byte[1] { versionByte };
            buffer = buffer.Concat(decoded.Hash).ToArray();
            return Encoders.Base58Check.EncodeData(buffer);
        }

        /// <summary>
        /// Encodes the given decoded address into cashaddr format
        /// </summary>
        /// <param name="decoded"></param>
        /// <returns></returns>
        public static string EncodeAsCashaddr(BchAddrData decoded)
        {
            var prefix = GetCashaddrkPrefix(decoded);
            var type = decoded.Type == CashType.P2PKH ? "P2PKH" : "P2SH";
            var hash = decoded.Hash;
            return CashAddr.Encode(prefix, type, hash);
        }

        /// <summary>
        /// Encodes the given decoded address into cashaddr format without a prefix
        /// </summary>
        /// <param name="decoded"></param>
        /// <returns></returns>
        public static string EncodeAsCashaddrNoPrefix(BchAddrData decoded)
        {
            var address = EncodeAsCashaddr(decoded);
            if (address.IndexOf(":") != -1)
            {
                return address.Split(':')[1];
            }
            throw new Validation.ValidationError($"Invalid BchAddrData");
        }

        public static string GetCashaddrkPrefix(BchAddrData data)
        {
            switch (data.Network)
            {
                case CashNetwork.Mainnet:
                    return "bitcoincash";
                case CashNetwork.DevaultMainnet:
                    return "devault";
                case CashNetwork.Testnet:
                    return "bchtest";
                case CashNetwork.RegTest:
                    return "bchreg";
            }
            throw new Validation.ValidationError($"Invalid BchAddrData");
        }

        /// <summary>
        /// Detects what is the given address' format
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        public static BchAddrData DecodeAddress(string address)
        {
            try
            {
                return DecodeBase58Address(address);
            }
            catch { }
            try
            {
                return DecodeCashAddress(address);
            }
            catch { }
            throw new Validation.ValidationError($"Invalid address {address}");
        }

        /// <summary>
        /// returns the Version byte for base58 formats
        /// </summary>
        /// <param name="format"></param>
        /// <param name="network"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        private static byte GetVersionByte(CashFormat format, CashNetwork network, CashType type)
        {
            switch (format)
            {
                case CashFormat.Legacy:
                    if (network == CashNetwork.Mainnet && type == CashType.P2PKH) return 0;
                    else if (network == CashNetwork.Mainnet && type == CashType.P2SH) return 5;
                    break;
                case CashFormat.Bitpay:
                    if (network == CashNetwork.Mainnet && type == CashType.P2PKH) return 28;
                    else if (network == CashNetwork.Mainnet && type == CashType.P2SH) return 40;
                    break;
            }
            if (network == CashNetwork.Testnet && type == CashType.P2PKH) return 111;
            else if (network == CashNetwork.Testnet && type == CashType.P2SH) return 196;
            else if (network == CashNetwork.RegTest && type == CashType.P2PKH) return 111;
            else if (network == CashNetwork.RegTest && type == CashType.P2SH) return 196;
            throw new Validation.ValidationError("Invalid parameters");
        }

        /// <summary>
        /// Decodes the given address into its constituting hash, format, network and type
        /// </summary>
        /// <param name="address">A valid Bitcoin Cash address in any format</param>
        /// <returns></returns>
        private static BchAddrData DecodeBase58Address(string address)
        {
            var payload = Encoders.Base58Check.DecodeData(address);
            var versionByte = payload[0];
            var hash = payload.Skip(1).ToArray();
            switch (versionByte)
            {
                case 0:
                    return BchAddrData.Create(CashFormat.Legacy, CashNetwork.Mainnet, CashType.P2PKH, hash);
                case 5:
                    return BchAddrData.Create(CashFormat.Legacy, CashNetwork.Mainnet, CashType.P2SH, hash);
                case 111:
                    return BchAddrData.Create(CashFormat.Legacy, CashNetwork.Testnet, CashType.P2PKH, hash);
                case 196:
                    return BchAddrData.Create(CashFormat.Legacy, CashNetwork.Testnet, CashType.P2SH, hash);
                case 28:
                    return BchAddrData.Create(CashFormat.Bitpay, CashNetwork.Mainnet, CashType.P2PKH, hash);
                case 40:
                    return BchAddrData.Create(CashFormat.Bitpay, CashNetwork.Mainnet, CashType.P2SH, hash);
                default:
                    throw new Validation.ValidationError($"Invalid address type in version byte: {versionByte}");
            }
        }

        /// <summary>
        /// Attempts to decode the given address assuming it is a cashaddr address
        /// </summary>
        /// <param name="address">A valid Bitcoin Cash address in any format</param>
        /// <returns></returns>
        private static BchAddrData DecodeCashAddress(string address)
        {
            if (address.IndexOf(":") != -1)
            {
                return DecodeCashAddressWithPrefix(address);
            }
            else
            {
                var prefixes = new string[] { "bitcoincash", "bchtest", "bchreg" };
                foreach (var prefix in prefixes)
                {
                    try
                    {
                        var result = DecodeCashAddressWithPrefix(prefix + ":" + address);
                        return result;
                    }
                    catch { }
                }
            }
            throw new Validation.ValidationError($"Invalid address {address}");
        }

        /// <summary>
        /// Attempts to decode the given address assuming it is a cashaddr address with explicit prefix
        /// </summary>
        /// <param name="address">A valid Bitcoin Cash address in any format</param>
        /// <returns></returns>
        public static BchAddrData DecodeCashAddressWithPrefix(string address)
        {
            var decoded = CashAddr.Decode(address);
            var type = decoded.Type == "P2PKH" ? CashType.P2PKH : CashType.P2SH;
            switch (decoded.Prefix)
            {
                case "bitcoincash":
                    return BchAddrData.Create(CashFormat.Cashaddr, CashNetwork.Mainnet, type, decoded.Hash);
                case "devault":
                    return BchAddrData.Create(CashFormat.Cashaddr, CashNetwork.DevaultMainnet, type, decoded.Hash);
                case "bchtest":
                    return BchAddrData.Create(CashFormat.Cashaddr, CashNetwork.Testnet, type, decoded.Hash);
                case "regtest":
                    return BchAddrData.Create(CashFormat.Cashaddr, CashNetwork.RegTest, type, decoded.Hash);
            }
            throw new Validation.ValidationError($"Invalid address {address}");
        }
    }

    // https://github.com/bitcoincashjs/cashaddrjs
    public static class CashAddr
    {
        public class CashAddrData
        {
            public string Prefix { get; set; }
            public string Type { get; set; }
            public byte[] Hash { get; set; }
        }

        static string[] VALID_PREFIXES = new string[] { "devault","bitcoincash", "bchtest", "bchreg" };

        /// <summary>
        /// Encodes a hash from a given type into a Bitcoin Cash address with the given prefix
        /// </summary>
        /// <param name="prefix">prefix Network prefix. E.g.: 'bitcoincash'</param>
        /// <param name="type">type Type of address to generate. Either 'P2PKH' or 'P2SH'</param>
        /// <param name="hash">hash Hash to encode represented as an array of 8-bit integers</param>
        /// <returns></returns>
        public static string Encode(string prefix, string type, byte[] hash)
        {
            Validation.Validate(IsValidPrefix(prefix), $"Invalid prefix: {prefix}");
            var prefixData = Concat(PrefixToByte5Array(prefix), new byte[1]);
            var versionByte = GetTypeBits(type) + GetHashSizeBits(hash);
            var payloadData = ToByte5Array(Concat(new byte[1] { (byte)versionByte }, hash));
            var checksumData = Concat(Concat(prefixData, payloadData), new byte[8]);
            var payload = Concat(payloadData, ChecksumToByte5Array(Polymod(checksumData)));
            return prefix + ':' + Base32.Encode(payload);
        }

        /// <summary>
        /// Decodes the given address into its constituting prefix, type and hash
        /// </summary>
        /// <param name="address">Address to decode. E.g.: 'bitcoincash:qpm2qsznhks23z7629mms6s4cwef74vcwvy22gdx6a'</param>
        /// <returns>DecodeData</returns>
        public static CashAddrData Decode(string address)
        {
            var pieces = address.ToLower().Split(':');
            Validation.Validate(pieces.Length == 2, $"Missing prefix: {address}");
            var prefix = pieces[0];
            var payload = Base32.Decode(pieces[1]);
            Validation.Validate(ValidChecksum(prefix, payload), $"Invalid checksum: {address}");
            var data = payload.Take(payload.Length - 8).ToArray();
            var payloadData = FromByte5Array(data);
            var versionByte = payloadData[0];
            var hash = payloadData.Skip(1).ToArray();
            Validation.Validate(GetHashSize((byte)versionByte) == hash.Length * 8, $"Invalid hash size: {address}");
            var type = GetType((byte)versionByte);
            return new CashAddrData
            {
                Prefix = prefix,
                Type = type,
                Hash = hash
            };
        }

        /// <summary>
        /// Retrieves the address type from its bit representation within the version byte
        /// </summary>
        /// <param name="versionByte"></param>
        /// <returns></returns>
        public static string GetType(byte versionByte)
        {
            switch (versionByte & 120)
            {
                case 0:
                    return "P2PKH";
                case 8:
                    return "P2SH";
                default:
                    throw new Validation.ValidationError($"Invalid address type in version byte: {versionByte}");
            }
        }

        /// <summary>
        /// Verify that the payload has not been corrupted by checking that the checksum is valid
        /// </summary>
        /// <param name="prefix"></param>
        /// <param name="payload"></param>
        /// <returns></returns>
        public static bool ValidChecksum(string prefix, byte[] payload)
        {
            var prefixData = Concat(PrefixToByte5Array(prefix), new byte[1]);
            var checksumData = Concat(prefixData, payload);
            return Polymod(checksumData).Equals(0);
        }


        /// <summary>
        /// Returns the concatenation a and b
        /// </summary>
        public static byte[] Concat(byte[] a, byte[] b)
        {
            return a.Concat(b).ToArray();
        }

        /// <summary>
        /// Returns an array representation of the given checksum to be encoded within the address' payload
        /// </summary>
        /// <param name="checksum"></param>
        /// <returns></returns>
        public static byte[] ChecksumToByte5Array(long checksum)
        {
            var result = new byte[8];
            for (var i = 0; i < 8; ++i)
            {
                result[7 - i] = (byte)(checksum & 31);
                checksum = checksum >> 5;
            }
            return result;
        }

        /// <summary>
        /// Computes a checksum from the given input data as specified for the CashAddr
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static long Polymod(byte[] data)
        {
            var GENERATOR = new long[] { 0x98f2bc8e61, 0x79b76d99e2, 0xf33e5fb3c4, 0xae2eabe2a8, 0x1e4f43e470 };
            long checksum = 1;
            for (var i = 0; i < data.Length; ++i)
            {
                var value = data[i];
                var topBits = checksum >> 35;
                checksum = ((checksum & 0x07ffffffff) << 5) ^ value;
                for (var j = 0; j < GENERATOR.Length; ++j)
                {
                    if (((topBits >> j) & 1).Equals(1))
                    {
                        checksum = checksum ^ GENERATOR[j];
                    }
                }
            }
            return checksum ^ 1;
        }

        /// <summary>
        /// Checks whether a string is a valid prefix; ie., it has a single letter case
        /// and is one of 'bitcoincash', 'bchtest', or 'bchreg'.
        /// </summary>
        /// <param name="prefix"></param>
        /// <returns></returns>
        public static bool IsValidPrefix(string prefix)
        {
            return HasSingleCase(prefix) && VALID_PREFIXES.Contains(prefix.ToLower());
        }

        /// <summary>
        /// Derives an array from the given prefix to be used in the computation of the address checksum
        /// </summary>
        /// <param name="prefix">Network prefix. E.g.: 'bitcoincash'</param>
        /// <returns></returns>
        public static byte[] PrefixToByte5Array(string prefix)
        {
            var result = new byte[prefix.Length];
            int i = 0;
            foreach (char c in prefix.ToCharArray())
            {
                result[i++] = (byte)(c & 31);
            }
            return result;
        }

        /// <summary>
        /// Returns true if, and only if, the given string contains either uppercase or lowercase letters, but not both
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public static bool HasSingleCase(string str)
        {
            return str == str.ToLower() || str == str.ToUpper();
        }

        /// <summary>
        /// Returns the bit representation of the length in bits of the given hash within the version byte
        /// </summary>
        /// <param name="hash">Hash to encode represented as an array of 8-bit integers</param>
        /// <returns></returns>
        public static byte GetHashSizeBits(byte[] hash)
        {
            switch (hash.Length * 8)
            {
                case 160:
                    return 0;
                case 192:
                    return 1;
                case 224:
                    return 2;
                case 256:
                    return 3;
                case 320:
                    return 4;
                case 384:
                    return 5;
                case 448:
                    return 6;
                case 512:
                    return 7;
                default:
                    throw new Validation.ValidationError($"Invalid hash size: {hash.Length}");
            }
        }

        /// <summary>
        /// Retrieves the the length in bits of the encoded hash from its bit representation within the version byte
        /// </summary>
        /// <param name="versionByte"></param>
        /// <returns></returns>
        public static int GetHashSize(byte versionByte)
        {
            switch (versionByte & 7)
            {
                case 0:
                    return 160;
                case 1:
                    return 192;
                case 2:
                    return 224;
                case 3:
                    return 256;
                case 4:
                    return 320;
                case 5:
                    return 384;
                case 6:
                    return 448;
                case 7:
                    return 512;
                default:
                    throw new Validation.ValidationError($"Invalid versionByte: {versionByte}");
            }
        }

        /// <summary>
        /// Returns the bit representation of the given type within the version byte
        /// </summary>
        /// <param name="type">Address type. Either 'P2PKH' or 'P2SH'</param>
        /// <returns></returns>
        public static byte GetTypeBits(string type)
        {
            switch (type)
            {
                case "P2PKH":
                    return 0;
                case "P2SH":
                    return 8;
                default:
                    throw new Validation.ValidationError($"Invalid type: {type}");
            }
        }

        /// <summary>
        /// Converts an array of 8-bit integers into an array of 5-bit integers, right-padding with zeroes if necessary
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static byte[] ToByte5Array(byte[] data)
        {
            return ConvertBits.Convert(data, 8, 5);
        }

        /// <summary>
        /// Converts an array of 5-bit integers back into an array of 8-bit integers
        /// removing extra zeroes left from padding if necessary.
        /// Throws a ValidationError if input is not a zero-padded array of 8-bit integers
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static byte[] FromByte5Array(byte[] data)
        {
            return ConvertBits.Convert(data, 5, 8, true);
        }

    }

    internal static class ConvertBits
    {
        /// <summary>
        /// Converts an array of integers made up of 'from' bits into an
        /// array of integers made up of 'to' bits.The output array is
        /// zero-padded if necessary, unless strict mode is true.
        /// </summary>
        /// <param name="data">data Array of integers made up of 'from' bits</param>
        /// <param name="from">from Length in bits of elements in the input array</param>
        /// <param name="to">to Length in bits of elements in the output array</param>
        /// <param name="strictMode">strictMode Require the conversion to be completed without padding</param>
        /// <returns></returns>
        public static byte[] Convert(byte[] data, int from, int to, bool strictMode = false)
        {
            Validation.Validate(from > 0, "Invald 'from' parameter");
            Validation.Validate(to > 0, "Invald 'to' parameter");
            Validation.Validate(data.Length > 0, "Invald data");
            var d = data.Length * from / (double)to;
            var length = strictMode ? (int)Math.Floor(d) : (int)Math.Ceiling(d);
            var mask = (1 << to) - 1;
            var result = new byte[length];
            var index = 0;
            var accumulator = 0;
            var bits = 0;
            for (var i = 0; i < data.Length; ++i)
            {
                var value = data[i];
                Validation.Validate(0 <= value && (value >> from) == 0, $"Invalid value: {value}");
                accumulator = (accumulator << from) | value;
                bits += from;
                while (bits >= to)
                {
                    bits -= to;
                    result[index] = (byte)((accumulator >> bits) & mask);
                    ++index;
                }
            }
            if (!strictMode)
            {
                if (bits > 0)
                {
                    result[index] = (byte)((accumulator << (to - bits)) & mask);
                    ++index;
                }
            }
            else
            {
                Validation.Validate(
                  bits < from && ((accumulator << (to - bits)) & mask) == 0,
                  $"Input cannot be converted to {to} bits without padding, but strict mode was used"
                );
            }
            return result;
        }
    }

    internal static class Base32
    {
        private static readonly char[] DIGITS;
        private static Dictionary<char, int> CHAR_MAP = new Dictionary<char, int>();

        static Base32()
        {
            DIGITS = "qpzry9x8gf2tvdw0s3jn54khce6mua7l".ToCharArray();
            for (int i = 0; i < DIGITS.Length; i++) CHAR_MAP[DIGITS[i]] = i;
        }

        /// <summary>
        /// Decodes the given base32-encoded string into an array of 5-bit integers
        /// </summary>
        /// <param name="encoded"></param>
        /// <returns></returns>
        public static byte[] Decode(string encoded)
        {
            if (encoded.Length == 0)
            {
                throw new CashaddrBase32EncoderException("Invalid encoded string");
            }
            var result = new byte[encoded.Length];
            int next = 0;
            foreach (char c in encoded.ToCharArray())
            {
                if (!CHAR_MAP.ContainsKey(c))
                {
                    throw new CashaddrBase32EncoderException($"Invalid character: {c}");
                }
                result[next++] = (byte)CHAR_MAP[c];
            }
            return result;
        }

        /// <summary>
        /// Encodes the given array of 5-bit integers as a base32-encoded string
        /// </summary>
        /// <param name="data">data Array of integers between 0 and 31 inclusive</param>
        /// <returns></returns>
        public static string Encode(byte[] data)
        {
            if (data.Length == 0)
            {
                throw new CashaddrBase32EncoderException("Invalid data");
            }
            string base32 = String.Empty;
            for (var i = 0; i < data.Length; ++i)
            {
                var value = data[i];
                if (0 <= value && value < 32)
                    base32 += DIGITS[value];
                else
                    throw new CashaddrBase32EncoderException($"Invalid value: {value}");
            }
            return base32;
        }

        private class CashaddrBase32EncoderException : Exception
        {
            public CashaddrBase32EncoderException(string message) : base(message)
            {
            }
        }
    }

    internal static class Validation
    {
        public static void Validate(bool condition, string message)
        {
            if (!condition)
            {
                throw new ValidationError(message);
            }
        }

        internal class ValidationError : Exception
        {
            public ValidationError(string message) : base(message)
            {
            }
        }
    }

}
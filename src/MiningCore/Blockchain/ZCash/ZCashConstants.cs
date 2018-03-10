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

using System.Collections.Generic;
using System.Globalization;
using MiningCore.Blockchain.Bitcoin;
using MiningCore.Configuration;
using NBitcoin.BouncyCastle.Math;

namespace MiningCore.Blockchain.ZCash
{
    public class ZCashCoinbaseTxConfig
    {
        public BigInteger Diff1 { get; set; }
        public System.Numerics.BigInteger Diff1b { get; set; }

        public bool PayFoundersReward { get; set; }
        public decimal PercentFoundersReward { get; set; }
        public string[] FoundersRewardAddresses { get; set; }
        public ulong FoundersRewardSubsidySlowStartInterval { get; set; }
        public ulong FoundersRewardSubsidyHalvingInterval { get; set; }
        public ulong FoundersRewardSubsidySlowStartShift => FoundersRewardSubsidySlowStartInterval / 2;
        public ulong LastFoundersRewardBlockHeight => FoundersRewardSubsidyHalvingInterval + FoundersRewardSubsidySlowStartShift - 1;

        public decimal PercentTreasuryReward { get; set; }
        public ulong TreasuryRewardStartBlockHeight { get; set; }
        public string[] TreasuryRewardAddresses { get; set; }
        public double TreasuryRewardAddressChangeInterval { get; set; }
    }

    public class ZCashConstants
    {
        public const int TargetPaddingLength = 32;

        private static readonly Dictionary<BitcoinNetworkType, ZCashCoinbaseTxConfig> ZCashCoinbaseTxConfig = new Dictionary<BitcoinNetworkType, ZCashCoinbaseTxConfig>
        {
            {
                BitcoinNetworkType.Main, new ZCashCoinbaseTxConfig
                {
                    Diff1 = new BigInteger("0007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", 16),
                    Diff1b = System.Numerics.BigInteger.Parse("0007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", NumberStyles.HexNumber),

                    PayFoundersReward = true,
                    PercentFoundersReward = 20,
                    FoundersRewardSubsidyHalvingInterval = 840000,
                    FoundersRewardSubsidySlowStartInterval = 20000,

                    FoundersRewardAddresses = new[]
                    {
                        "t3Vz22vK5z2LcKEdg16Yv4FFneEL1zg9ojd", "t3cL9AucCajm3HXDhb5jBnJK2vapVoXsop3", "t3fqvkzrrNaMcamkQMwAyHRjfDdM2xQvDTR",
                        "t3TgZ9ZT2CTSK44AnUPi6qeNaHa2eC7pUyF", "t3SpkcPQPfuRYHsP5vz3Pv86PgKo5m9KVmx", "t3Xt4oQMRPagwbpQqkgAViQgtST4VoSWR6S",
                        "t3ayBkZ4w6kKXynwoHZFUSSgXRKtogTXNgb", "t3adJBQuaa21u7NxbR8YMzp3km3TbSZ4MGB", "t3K4aLYagSSBySdrfAGGeUd5H9z5Qvz88t2",
                        "t3RYnsc5nhEvKiva3ZPhfRSk7eyh1CrA6Rk", "t3Ut4KUq2ZSMTPNE67pBU5LqYCi2q36KpXQ", "t3ZnCNAvgu6CSyHm1vWtrx3aiN98dSAGpnD",
                        "t3fB9cB3eSYim64BS9xfwAHQUKLgQQroBDG", "t3cwZfKNNj2vXMAHBQeewm6pXhKFdhk18kD", "t3YcoujXfspWy7rbNUsGKxFEWZqNstGpeG4",
                        "t3bLvCLigc6rbNrUTS5NwkgyVrZcZumTRa4", "t3VvHWa7r3oy67YtU4LZKGCWa2J6eGHvShi", "t3eF9X6X2dSo7MCvTjfZEzwWrVzquxRLNeY",
                        "t3esCNwwmcyc8i9qQfyTbYhTqmYXZ9AwK3X", "t3M4jN7hYE2e27yLsuQPPjuVek81WV3VbBj", "t3gGWxdC67CYNoBbPjNvrrWLAWxPqZLxrVY",
                        "t3LTWeoxeWPbmdkUD3NWBquk4WkazhFBmvU", "t3P5KKX97gXYFSaSjJPiruQEX84yF5z3Tjq", "t3f3T3nCWsEpzmD35VK62JgQfFig74dV8C9",
                        "t3Rqonuzz7afkF7156ZA4vi4iimRSEn41hj", "t3fJZ5jYsyxDtvNrWBeoMbvJaQCj4JJgbgX", "t3Pnbg7XjP7FGPBUuz75H65aczphHgkpoJW",
                        "t3WeKQDxCijL5X7rwFem1MTL9ZwVJkUFhpF", "t3Y9FNi26J7UtAUC4moaETLbMo8KS1Be6ME", "t3aNRLLsL2y8xcjPheZZwFy3Pcv7CsTwBec",
                        "t3gQDEavk5VzAAHK8TrQu2BWDLxEiF1unBm", "t3Rbykhx1TUFrgXrmBYrAJe2STxRKFL7G9r", "t3aaW4aTdP7a8d1VTE1Bod2yhbeggHgMajR",
                        "t3YEiAa6uEjXwFL2v5ztU1fn3yKgzMQqNyo", "t3g1yUUwt2PbmDvMDevTCPWUcbDatL2iQGP", "t3dPWnep6YqGPuY1CecgbeZrY9iUwH8Yd4z",
                        "t3QRZXHDPh2hwU46iQs2776kRuuWfwFp4dV", "t3enhACRxi1ZD7e8ePomVGKn7wp7N9fFJ3r", "t3PkLgT71TnF112nSwBToXsD77yNbx2gJJY",
                        "t3LQtHUDoe7ZhhvddRv4vnaoNAhCr2f4oFN", "t3fNcdBUbycvbCtsD2n9q3LuxG7jVPvFB8L", "t3dKojUU2EMjs28nHV84TvkVEUDu1M1FaEx",
                        "t3aKH6NiWN1ofGd8c19rZiqgYpkJ3n679ME", "t3MEXDF9Wsi63KwpPuQdD6by32Mw2bNTbEa", "t3WDhPfik343yNmPTqtkZAoQZeqA83K7Y3f",
                        "t3PSn5TbMMAEw7Eu36DYctFezRzpX1hzf3M", "t3R3Y5vnBLrEn8L6wFjPjBLnxSUQsKnmFpv", "t3Pcm737EsVkGTbhsu2NekKtJeG92mvYyoN"
                    }
                }
            },
            {
                BitcoinNetworkType.Test, new ZCashCoinbaseTxConfig
                {
                    Diff1 = new BigInteger("0007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", 16),
                    Diff1b = System.Numerics.BigInteger.Parse("0007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", NumberStyles.HexNumber),

                    PayFoundersReward = true,
                    PercentFoundersReward = 20,
                    FoundersRewardSubsidyHalvingInterval = 840000,
                    FoundersRewardSubsidySlowStartInterval = 20000,

                    FoundersRewardAddresses = new[]
                    {
                        "t2UNzUUx8mWBCRYPRezvA363EYXyEpHokyi", "t2N9PH9Wk9xjqYg9iin1Ua3aekJqfAtE543", "t2NGQjYMQhFndDHguvUw4wZdNdsssA6K7x2", "t2ENg7hHVqqs9JwU5cgjvSbxnT2a9USNfhy",
                        "t2BkYdVCHzvTJJUTx4yZB8qeegD8QsPx8bo", "t2J8q1xH1EuigJ52MfExyyjYtN3VgvshKDf", "t2Crq9mydTm37kZokC68HzT6yez3t2FBnFj", "t2EaMPUiQ1kthqcP5UEkF42CAFKJqXCkXC9",
                        "t2F9dtQc63JDDyrhnfpzvVYTJcr57MkqA12", "t2LPirmnfYSZc481GgZBa6xUGcoovfytBnC", "t26xfxoSw2UV9Pe5o3C8V4YybQD4SESfxtp", "t2D3k4fNdErd66YxtvXEdft9xuLoKD7CcVo",
                        "t2DWYBkxKNivdmsMiivNJzutaQGqmoRjRnL", "t2C3kFF9iQRxfc4B9zgbWo4dQLLqzqjpuGQ", "t2MnT5tzu9HSKcppRyUNwoTp8MUueuSGNaB", "t2AREsWdoW1F8EQYsScsjkgqobmgrkKeUkK",
                        "t2Vf4wKcJ3ZFtLj4jezUUKkwYR92BLHn5UT", "t2K3fdViH6R5tRuXLphKyoYXyZhyWGghDNY", "t2VEn3KiKyHSGyzd3nDw6ESWtaCQHwuv9WC", "t2F8XouqdNMq6zzEvxQXHV1TjwZRHwRg8gC",
                        "t2BS7Mrbaef3fA4xrmkvDisFVXVrRBnZ6Qj", "t2FuSwoLCdBVPwdZuYoHrEzxAb9qy4qjbnL", "t2SX3U8NtrT6gz5Db1AtQCSGjrpptr8JC6h", "t2V51gZNSoJ5kRL74bf9YTtbZuv8Fcqx2FH",
                        "t2FyTsLjjdm4jeVwir4xzj7FAkUidbr1b4R", "t2EYbGLekmpqHyn8UBF6kqpahrYm7D6N1Le", "t2NQTrStZHtJECNFT3dUBLYA9AErxPCmkka", "t2GSWZZJzoesYxfPTWXkFn5UaxjiYxGBU2a",
                        "t2RpffkzyLRevGM3w9aWdqMX6bd8uuAK3vn", "t2JzjoQqnuXtTGSN7k7yk5keURBGvYofh1d", "t2AEefc72ieTnsXKmgK2bZNckiwvZe3oPNL", "t2NNs3ZGZFsNj2wvmVd8BSwSfvETgiLrD8J",
                        "t2ECCQPVcxUCSSQopdNquguEPE14HsVfcUn", "t2JabDUkG8TaqVKYfqDJ3rqkVdHKp6hwXvG", "t2FGzW5Zdc8Cy98ZKmRygsVGi6oKcmYir9n", "t2DUD8a21FtEFn42oVLp5NGbogY13uyjy9t",
                        "t2UjVSd3zheHPgAkuX8WQW2CiC9xHQ8EvWp", "t2TBUAhELyHUn8i6SXYsXz5Lmy7kDzA1uT5", "t2Tz3uCyhP6eizUWDc3bGH7XUC9GQsEyQNc", "t2NysJSZtLwMLWEJ6MH3BsxRh6h27mNcsSy",
                        "t2KXJVVyyrjVxxSeazbY9ksGyft4qsXUNm9", "t2J9YYtH31cveiLZzjaE4AcuwVho6qjTNzp", "t2QgvW4sP9zaGpPMH1GRzy7cpydmuRfB4AZ", "t2NDTJP9MosKpyFPHJmfjc5pGCvAU58XGa4",
                        "t29pHDBWq7qN4EjwSEHg8wEqYe9pkmVrtRP", "t2Ez9KM8VJLuArcxuEkNRAkhNvidKkzXcjJ", "t2D5y7J5fpXajLbGrMBQkFg2mFN8fo3n8cX", "t2UV2wr1PTaUiybpkV3FdSdGxUJeZdZztyt",
                    }
                }
            },
            {
                BitcoinNetworkType.RegTest, new ZCashCoinbaseTxConfig
                {
                    Diff1 = new BigInteger("0007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", 16),
                    Diff1b = System.Numerics.BigInteger.Parse("0007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", NumberStyles.HexNumber),

                    PayFoundersReward = true,
                    PercentFoundersReward = 20,
                    FoundersRewardSubsidyHalvingInterval = 150,
                    FoundersRewardSubsidySlowStartInterval = 0,

                    FoundersRewardAddresses = new[]
                    {
                        "t2FwcEhFdNXuFMv1tcYwaBJtYVtMj8b1uTg"
                    }
                }
            },
        };

        private static readonly Dictionary<BitcoinNetworkType, ZCashCoinbaseTxConfig> ZCLCoinbaseTxConfig = new Dictionary<BitcoinNetworkType, ZCashCoinbaseTxConfig>
        {
            {
                BitcoinNetworkType.Main, new ZCashCoinbaseTxConfig
                {
                    Diff1 = new BigInteger("0007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", 16),
                    Diff1b = System.Numerics.BigInteger.Parse("0007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", NumberStyles.HexNumber),

                    PayFoundersReward = false,
                    PercentFoundersReward = 0,
                    FoundersRewardSubsidyHalvingInterval = 0,
                    FoundersRewardSubsidySlowStartInterval = 0,

                    FoundersRewardAddresses = new[]
                    {
                        ""
                    }
                }
            },
            {
                BitcoinNetworkType.Test, new ZCashCoinbaseTxConfig
                {
                    Diff1 = new BigInteger("0007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", 16),
                    Diff1b = System.Numerics.BigInteger.Parse("0007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", NumberStyles.HexNumber),

                    PayFoundersReward = false,
                    PercentFoundersReward = 0,
                    FoundersRewardSubsidyHalvingInterval = 0,
                    FoundersRewardSubsidySlowStartInterval = 0,

                    FoundersRewardAddresses = new[]
                    {
                        ""
                    }
                }
            },
            {
                BitcoinNetworkType.RegTest, new ZCashCoinbaseTxConfig
                {
                    Diff1 = new BigInteger("0007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", 16),
                    Diff1b = System.Numerics.BigInteger.Parse("0007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", NumberStyles.HexNumber),

                    PayFoundersReward = false,
                    PercentFoundersReward = 0,
                    FoundersRewardSubsidyHalvingInterval = 0,
                    FoundersRewardSubsidySlowStartInterval = 0,

                    FoundersRewardAddresses = new[]
                    {
                        ""
                    }
                }
            },
        };

        private static readonly Dictionary<BitcoinNetworkType, ZCashCoinbaseTxConfig> ZencashCoinbaseTxConfig = new Dictionary<BitcoinNetworkType, ZCashCoinbaseTxConfig>
        {
            {
                BitcoinNetworkType.Main, new ZCashCoinbaseTxConfig
                {
                    Diff1 = new BigInteger("0007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", 16),
                    Diff1b = System.Numerics.BigInteger.Parse("0007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", NumberStyles.HexNumber),

                    PayFoundersReward = true,
                    PercentFoundersReward = 8.5m,
                    FoundersRewardSubsidyHalvingInterval = 840000,
                    FoundersRewardSubsidySlowStartInterval = 2,

                    FoundersRewardAddresses = new[]
                    {
                        "zssEdGnZCQ9G86LZFtbynMn1hYTVhn6eYCL", "zsrCsXXmUf8k59NLasEKfxA7us3iNvaPATz", "zsnLPsWMXW2s4w9EmFSwtSLRxL2LhPcfdby",
                        "zshdovbcPfUAfkPeEE2qLbKnoue9RsbVokU", "zsqmq97JAKCCBFRGvxgv6FiJgQLCZBDp62S", "zskyFVFA7VRYX8EGdXmYN75WaBB25FmiL3g",
                        "zsmncLmwEUdVmAGPUrUnNKmPGXyej7mbmdM", "zsfa9VVJCEdjfPbku4XrFcRR8kTDm2T64rz", "zsjdMnfWuFi46VeN2HSXVQWEGsnGHgVxayY",
                        "zseb8wRQ8rZ722oLX5B8rx7qwZiBRb9mdig", "zsjxkovhqiMVggoW7jvSRi3NTSD3a6b6qfd", "zsokCCSU3wvZrS2G6mEDpJ5cH49E7sDyNr1",
                        "zt12EsFgkABHLMRXA7JNnpMqLrxsgCLnVEV", "zt39mvuG9gDTHX8A8Qk45rbk3dSdQoJ8ZAv", "zssTQZs5YxDGijKC86dvcDxzWogWcK7n5AK",
                        "zsywuMoQK7Bved2nrXs56AEtWBhpb88rMzS", "zsxVS2w7h1fHFX2nQtGm4372pd4DSHzq9ee", "zsupGi7ro3uC8CEVwm9r7vrdVUZaXQnHF6T",
                        "zshVZvW47dA5AB3Sqk1h7ytwWJeUJUJxxaE", "zsubBCjvDx252MKFsL4Dcf5rJU9Z9Upqr1N", "zsweaST3NcU4hfgkVULfCsfEq41pjgMDgcW",
                        "zswz6Rxb1S33fUpftETZwtGiVSeYxNKq2xc", "zswnpHtiBbrvYDzbhPQshkgvLSfYhDMRJ4S", "zsjSYAWaEYj35Ht7aXrRJUGY6Dc8qCmgYqu",
                        "zsvMv8fGroWR8epbSiGDCJHmfe6ec2uFQrt", "zsujxCT56BExQDAwKwktBjtnopYnw8BiKbg", "zsxeXc2FTAzmUmeZmqVsKVdwTMSvzyns4rT",
                        "zsuLqgABNudD8bVPbVGeUjGqapuoXp68i7F", "zsoc39J1dCFK1U8kckZznvQkv8As7sajYLz", "zt21NFdu1KRPJ7VRKtrWugM2Jqe5ePNmU4T",
                        "zsp15qbVcbx9ifcjKe6XZEJTvzsFUZ2BHLT", "zso2KvqH6yxLQEYggHdmfL3Tcd5V6E9tqhp", "zsnFG2W5ZHRYh3QucNze4mp31tBkemtfxdj",
                        "zsex2CGJtxHyHbpLXm7kESBmp3vWRqUkJMy", "zsvtFv96nrgrXKUbtNe2BpCt8aQEp5oJ7F8", "zsk5KitThmhK9KBa1KDybPgEmGSFTHzhMVA",
                        "zsuy4n48c4NsJyaCZEzwdAKULM1FqbB6Y4z", "zsgtQVMpX2zNMLvHHG2NDwfqKoaebvVectJ", "zszQqXRSPGdqsWw4iaMTNN6aJz4JjEzSdCF",
                        "zst6dBLrTtaMQBX7BLMNjKLTGcP11PBmgTV", "zshD9r6Eb6dZGdzYW2HCb9CzkMokCT1NGJR", "zswUaj1TboEGmvSfF7fdoxWyH3RMx7MBHHo",
                        "zsv8s4Poi5GxCsbBrRJ97Vsvazp84nrz5AN", "zsmmxrKU6dqWFwUKow1iyovg3gxrgXpEivr", "zskh1221aRC9WEfb5a59WxffeW34McmZZsw",
                        "zssAhuj57NnVm4yNFT6o8muRctABkUaBu3L", "zsi5Yr4Z8HwBvdBqQE8gk7ahExDu95J4oqZ", "zsy6ryEaxfk8emJ8bGVB7tmwRwBL8cfSqBW",
                    },

                    PercentTreasuryReward = 12,
                    TreasuryRewardAddressChangeInterval = 50000,
                    TreasuryRewardStartBlockHeight = 139200,

                    TreasuryRewardAddresses = new[]
                    {
                        "zsyF68hcYYNLPj5i4PfQJ1kUY6nsFnZkc82", "zsfULrmbX7xbhqhAFRffVqCw9RyGv2hqNNG",
                        "zsoemTfqjicem2QVU8cgBHquKb1o9JR5p4Z", "zt339oiGL6tTgc9Q71f5g1sFTZf6QiXrRUr"
                    }
                }
            },
            {
                BitcoinNetworkType.Test, new ZCashCoinbaseTxConfig
                {
                    Diff1 = new BigInteger("0007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", 16),
                    Diff1b = System.Numerics.BigInteger.Parse("0007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", NumberStyles.HexNumber),

                    PayFoundersReward = true,
                    PercentFoundersReward = 8.5m,
                    FoundersRewardSubsidyHalvingInterval = 840000,
                    FoundersRewardSubsidySlowStartInterval = 2,

                    FoundersRewardAddresses = new[]
                    {
                        "zrH8KT8KUcpKKNBu3fjH4hA84jZBCawErqn", "zrGsMC4ou1r5Vxy7Dnxg4PfKpansx83BM8g", "zr6sB2Az36D8CqzeZNavB11WbovsGtJSAZG",
                        "zrBAG3pXCTDq14nivNK9mW8SfwMNcdmMQpb", "zrRLwpYRYky4wsvwLVrDp8fs89EBTRhNMB1", "zrLozMfptTmE3zLP5SrTLyB8TXqH84Agjrr",
                        "zrMckkaLtVTEUvxj4ouU7BPDGa8xmdTZSVE", "zrFc897wJXmF7BcEdbvi2mS1bLrcuWYK6hm", "zrHEnni486u9SNcLWacroSgcdyMA33tfM92",
                        "zrJ3ymPV3R8Xk4N3BdNb898xvZvARm5K7mq", "zrDj3P6trx73291krxU51U9QnfkbGxkyJ6E", "zrJs3vMGFJi9pQCireeSLckJonamBnwTSrY",
                        "zrKFdXQoAkkycy52EFoWARyzZWx6Kat2Som", "zrEXbSe79FXA9KRMnJTZUWZdZhNnjcnAdrq", "zr7iAwfNgJsMpSCmijK3TuVDuNvSmLr1rUz",
                        "zrDEK7K6cftqSjeyVUH1WqJtBUkXN7GidxH", "zrRennuq75hyBVU4JymtZk8UcQ1vRPKpmpj", "zr9HRTL79pKmn5R8fvkC9kucZ4u1bQruLTD",
                        "zrML8KXpJsa1NVnbJuawX86ZvAn543tWdTT", "zrLBAkQoxpEtnztSUEcdxnEvuwtgxyAMGX7", "zr6kPnVzFBYmcBDsWoTrPHRuBxLq21o4zvT",
                        "zrMY3vdvqs9KSvx9TawvcyuVurt1Jj6GPVo", "zr9WB1qBpM4nwi1mudUFfjtMNmqzaBQDsXn", "zrAHbtHDPAqmzWJMQqSYzGyFnDWN3oELZRs",
                        "zrH1f5K3z7EQ6RWWZ7StCDWHTZwFChBVA2W", "zrNTacAid9LS4kAqzM4sw1YcF7gLFrzVM7U", "zrFyZpMVKMeDqbn6A2uUiL9mZmgxuR1pUBg",
                        "zrD1cqGFGzBcPogFHJvnN4XegvvmbTjA43t", "zr5A1D7czWkB4pAWfGC5Pux5Ek7anYybdPK", "zr8yTAxCy6jAdsc6qPvmVEQHbYo25AJKhy9",
                        "zrFW2YjQw4cABim5kEDwapbSqTz3wW7cWkk", "zr9nJvNbsrvUTZD41fhqAQeUcgMfqZmAweN", "zrCx4dXZd5b2tD483Ds4diHpo1QxBMJ76Jr",
                        "zr6eVeRwU6Puob3K1RfWtva1R458oj8pzkL", "zr7B92iHtQcobZjGCXo3DAqMQjsn7ka31wE", "zr8bcemLWAjYuphXSVqtqZWEnJipCB9F5oC",
                        "zrFzsuPXb7HsFd3srBqtVqnC9GQ94DQubV2", "zr4yiBobiHjHnCYi75NmYtyoqCV4A3kpHDL", "zrGVdR4K4F8MfmWxhUiTypK7PTsvHi8uTAh",
                        "zr7WiCDqCMvUdH1xmMu8YrBMFb2x2E6BX3z", "zrEFrGWLX4hPHuHRUD3TPbMAJyeSpMSctUc", "zr5c3f8PTnW8qBFX1GvK2LhyLBBCb1WDdGG",
                        "zrGkAZkZLqC9QKJR3XomgxNizCpNuAupTeg", "zrM7muDowiun9tCHhu5K9vcDGfUptuYorfZ", "zrCsWfwKotWnQmFviqAHAPAJ2jXqZYW966P",
                        "zrLLB3JB3jozUoMGFEGhjqyVXTpngVQ8c4T", "zrAEa8YjJ2f3m2VsM1Xa9EwibZxEnRoSLUx", "zrAdJgp7Cx35xTvB7ABWP8YLTNDArMjP1s3"
                    },

                    PercentTreasuryReward = 12,
                    TreasuryRewardAddressChangeInterval = 10000,
                    TreasuryRewardStartBlockHeight = 85500,

                    TreasuryRewardAddresses = new[]
                    {
                        "zrRBQ5heytPMN5nY3ssPf3cG4jocXeD8fm1"
                    }
                }
            },
            {
                BitcoinNetworkType.RegTest, new ZCashCoinbaseTxConfig
                {
                    Diff1 = new BigInteger("0007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", 16),
                    Diff1b = System.Numerics.BigInteger.Parse("0007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", NumberStyles.HexNumber),

                    PayFoundersReward = true,
                    PercentFoundersReward = 8.5m,
                    FoundersRewardSubsidyHalvingInterval = 2000,
                    FoundersRewardSubsidySlowStartInterval = 0,

                    FoundersRewardAddresses = new[]
                    {
                        "zrKmSdqZKZjnARd5e8FfRg4v1m74X7twxGa",
                    },

                    PercentTreasuryReward = 12,
                    TreasuryRewardAddressChangeInterval = 100,
                    TreasuryRewardStartBlockHeight = 139200,

                    TreasuryRewardAddresses = new[]
                    {
                        "zrKmSdqZKZjnARd5e8FfRg4v1m74X7twxGa"
                    }
                }
            },
        };

        private static readonly Dictionary<BitcoinNetworkType, ZCashCoinbaseTxConfig> BTCPCoinbaseTxConfig = new Dictionary<BitcoinNetworkType, ZCashCoinbaseTxConfig>
        {
            {
                BitcoinNetworkType.Main, new ZCashCoinbaseTxConfig
                {
                    Diff1 = new BigInteger("007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", 16),
                    Diff1b = System.Numerics.BigInteger.Parse("007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", NumberStyles.HexNumber),

                    PayFoundersReward = false,
                    PercentFoundersReward = 0,
                    FoundersRewardSubsidyHalvingInterval = 0,
                    FoundersRewardSubsidySlowStartInterval = 0,

                    FoundersRewardAddresses = new[]
                    {
                        ""
                    }
                }
            },
            {
                BitcoinNetworkType.Test, new ZCashCoinbaseTxConfig
                {
                    Diff1 = new BigInteger("007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", 16),
                    Diff1b = System.Numerics.BigInteger.Parse("007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", NumberStyles.HexNumber),

                    PayFoundersReward = false,
                    PercentFoundersReward = 0,
                    FoundersRewardSubsidyHalvingInterval = 0,
                    FoundersRewardSubsidySlowStartInterval = 0,

                    FoundersRewardAddresses = new[]
                    {
                        ""
                    }
                }
            },
            {
                BitcoinNetworkType.RegTest, new ZCashCoinbaseTxConfig
                {
                    Diff1 = new BigInteger("007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", 16),
                    Diff1b = System.Numerics.BigInteger.Parse("007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", NumberStyles.HexNumber),

                    PayFoundersReward = false,
                    PercentFoundersReward = 0,
                    FoundersRewardSubsidyHalvingInterval = 0,
                    FoundersRewardSubsidySlowStartInterval = 0,

                    FoundersRewardAddresses = new[]
                    {
                        ""
                    }
                }
            },
        };

        public static Dictionary<CoinType, Dictionary<BitcoinNetworkType, ZCashCoinbaseTxConfig>> CoinbaseTxConfig =
            new Dictionary<CoinType, Dictionary<BitcoinNetworkType, ZCashCoinbaseTxConfig>>
            {
                { CoinType.ZEC, ZCashCoinbaseTxConfig },
                { CoinType.ZCL, ZCLCoinbaseTxConfig },
                { CoinType.ZEN, ZencashCoinbaseTxConfig },
                { CoinType.BTG, ZCLCoinbaseTxConfig },
                { CoinType.BTCP, BTCPCoinbaseTxConfig },
            };
    }

    public enum ZOperationStatus
    {
        Queued,
        Executing,
        Success,
        Cancelled,
        Failed
    }

    public static class ZCashCommands
    {
        public const string ZGetBalance = "z_getbalance";
        public const string ZGetTotalBalance = "z_gettotalbalance";
        public const string ZGetListAddresses = "z_listaddresses";
        public const string ZValidateAddress = "z_validateaddress";
        public const string ZShieldCoinbase = "z_shieldcoinbase";

        /// <summary>
        /// Returns an operationid. You use the operationid value with z_getoperationstatus and
        /// z_getoperationresult to obtain the result of sending funds, which if successful, will be a txid.
        /// </summary>
        public const string ZSendMany = "z_sendmany";

        public const string ZGetOperationStatus = "z_getoperationstatus";
        public const string ZGetOperationResult = "z_getoperationresult";
    }
}

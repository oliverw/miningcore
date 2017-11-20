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
using MiningCore.Blockchain.ZCash;
using NBitcoin.BouncyCastle.Math;

namespace MiningCore.Blockchain.Zencash
{
    public class ZencashCoinbaseTxConfig : ZCashCoinbaseTxConfig
    {
        new public decimal PercentFoundersReward { get; set; }
        new public decimal PercentTreasuryReward { get; set; }
    }

    public class ZencashConstants : ZCashConstants
    {
        private static readonly Dictionary<BitcoinNetworkType, ZencashCoinbaseTxConfig> ZencashCoinbaseTxConfig = new Dictionary<BitcoinNetworkType, ZencashCoinbaseTxConfig>
        {
            {
                BitcoinNetworkType.Main, new ZencashCoinbaseTxConfig
                {
                    PayFoundersReward = true,
                    PercentFoundersReward = 8.5m,
                    FoundersRewardSubsidyHalvingInterval = 840000,
                    FoundersRewardSubsidySlowStartInterval = 0,
                    PercentTreasuryReward = 12.0m,
                    TreasuryRewardAddressChangeInterval = 50000,
                    TreasuryRewardStartBlockHeight = 139200,
                    
                    FoundersRewardAddresses = new[]
                    {
                        "zssEdGnZCQ9G86LZFtbynMn1hYTVhn6eYCL","zsrCsXXmUf8k59NLasEKfxA7us3iNvaPATz","zsnLPsWMXW2s4w9EmFSwtSLRxL2LhPcfdby",
                        "zshdovbcPfUAfkPeEE2qLbKnoue9RsbVokU","zsqmq97JAKCCBFRGvxgv6FiJgQLCZBDp62S","zskyFVFA7VRYX8EGdXmYN75WaBB25FmiL3g",
                        "zsmncLmwEUdVmAGPUrUnNKmPGXyej7mbmdM","zsfa9VVJCEdjfPbku4XrFcRR8kTDm2T64rz","zsjdMnfWuFi46VeN2HSXVQWEGsnGHgVxayY",
                        "zseb8wRQ8rZ722oLX5B8rx7qwZiBRb9mdig","zsjxkovhqiMVggoW7jvSRi3NTSD3a6b6qfd","zsokCCSU3wvZrS2G6mEDpJ5cH49E7sDyNr1",
                        "zt12EsFgkABHLMRXA7JNnpMqLrxsgCLnVEV","zt39mvuG9gDTHX8A8Qk45rbk3dSdQoJ8ZAv","zssTQZs5YxDGijKC86dvcDxzWogWcK7n5AK",
                        "zsywuMoQK7Bved2nrXs56AEtWBhpb88rMzS","zsxVS2w7h1fHFX2nQtGm4372pd4DSHzq9ee","zsupGi7ro3uC8CEVwm9r7vrdVUZaXQnHF6T",
                        "zshVZvW47dA5AB3Sqk1h7ytwWJeUJUJxxaE","zsubBCjvDx252MKFsL4Dcf5rJU9Z9Upqr1N","zsweaST3NcU4hfgkVULfCsfEq41pjgMDgcW",
                        "zswz6Rxb1S33fUpftETZwtGiVSeYxNKq2xc","zswnpHtiBbrvYDzbhPQshkgvLSfYhDMRJ4S","zsjSYAWaEYj35Ht7aXrRJUGY6Dc8qCmgYqu",
                        "zsvMv8fGroWR8epbSiGDCJHmfe6ec2uFQrt","zsujxCT56BExQDAwKwktBjtnopYnw8BiKbg","zsxeXc2FTAzmUmeZmqVsKVdwTMSvzyns4rT",
                        "zsuLqgABNudD8bVPbVGeUjGqapuoXp68i7F","zsoc39J1dCFK1U8kckZznvQkv8As7sajYLz","zt21NFdu1KRPJ7VRKtrWugM2Jqe5ePNmU4T",
                        "zsp15qbVcbx9ifcjKe6XZEJTvzsFUZ2BHLT","zso2KvqH6yxLQEYggHdmfL3Tcd5V6E9tqhp","zsnFG2W5ZHRYh3QucNze4mp31tBkemtfxdj",
                        "zsex2CGJtxHyHbpLXm7kESBmp3vWRqUkJMy","zsvtFv96nrgrXKUbtNe2BpCt8aQEp5oJ7F8","zsk5KitThmhK9KBa1KDybPgEmGSFTHzhMVA",
                        "zsuy4n48c4NsJyaCZEzwdAKULM1FqbB6Y4z","zsgtQVMpX2zNMLvHHG2NDwfqKoaebvVectJ","zszQqXRSPGdqsWw4iaMTNN6aJz4JjEzSdCF",
                        "zst6dBLrTtaMQBX7BLMNjKLTGcP11PBmgTV","zshD9r6Eb6dZGdzYW2HCb9CzkMokCT1NGJR","zswUaj1TboEGmvSfF7fdoxWyH3RMx7MBHHo",
                        "zsv8s4Poi5GxCsbBrRJ97Vsvazp84nrz5AN","zsmmxrKU6dqWFwUKow1iyovg3gxrgXpEivr","zskh1221aRC9WEfb5a59WxffeW34McmZZsw",
                        "zssAhuj57NnVm4yNFT6o8muRctABkUaBu3L","zsi5Yr4Z8HwBvdBqQE8gk7ahExDu95J4oqZ","zsy6ryEaxfk8emJ8bGVB7tmwRwBL8cfSqBW",
                    },

                    TreasuryRewardAddresses = new[]
                    {
                        "zsyF68hcYYNLPj5i4PfQJ1kUY6nsFnZkc82","zsfULrmbX7xbhqhAFRffVqCw9RyGv2hqNNG",
                        "zsoemTfqjicem2QVU8cgBHquKb1o9JR5p4Z","zt339oiGL6tTgc9Q71f5g1sFTZf6QiXrRUr",
                    }
                }
            },
            {
                BitcoinNetworkType.Test, new ZencashCoinbaseTxConfig
                {
                    PayFoundersReward = true,
                    PercentFoundersReward = 8.5m,
                    FoundersRewardSubsidyHalvingInterval = 840000,
                    FoundersRewardSubsidySlowStartInterval = 20000,
                    PercentTreasuryReward = 12.0m,
                    TreasuryRewardAddressChangeInterval = 10000,
                    TreasuryRewardStartBlockHeight = 85500,

                    FoundersRewardAddresses = new[]
                    {
                        "zrH8KT8KUcpKKNBu3fjH4hA84jZBCawErqn","zrGsMC4ou1r5Vxy7Dnxg4PfKpansx83BM8g","zr6sB2Az36D8CqzeZNavB11WbovsGtJSAZG",
                        "zrBAG3pXCTDq14nivNK9mW8SfwMNcdmMQpb","zrRLwpYRYky4wsvwLVrDp8fs89EBTRhNMB1","zrLozMfptTmE3zLP5SrTLyB8TXqH84Agjrr",
                        "zrMckkaLtVTEUvxj4ouU7BPDGa8xmdTZSVE","zrFc897wJXmF7BcEdbvi2mS1bLrcuWYK6hm","zrHEnni486u9SNcLWacroSgcdyMA33tfM92",
                        "zrJ3ymPV3R8Xk4N3BdNb898xvZvARm5K7mq","zrDj3P6trx73291krxU51U9QnfkbGxkyJ6E","zrJs3vMGFJi9pQCireeSLckJonamBnwTSrY",
                        "zrKFdXQoAkkycy52EFoWARyzZWx6Kat2Som","zrEXbSe79FXA9KRMnJTZUWZdZhNnjcnAdrq","zr7iAwfNgJsMpSCmijK3TuVDuNvSmLr1rUz",
                        "zrDEK7K6cftqSjeyVUH1WqJtBUkXN7GidxH","zrRennuq75hyBVU4JymtZk8UcQ1vRPKpmpj","zr9HRTL79pKmn5R8fvkC9kucZ4u1bQruLTD",
                        "zrML8KXpJsa1NVnbJuawX86ZvAn543tWdTT","zrLBAkQoxpEtnztSUEcdxnEvuwtgxyAMGX7","zr6kPnVzFBYmcBDsWoTrPHRuBxLq21o4zvT",
                        "zrMY3vdvqs9KSvx9TawvcyuVurt1Jj6GPVo","zr9WB1qBpM4nwi1mudUFfjtMNmqzaBQDsXn","zrAHbtHDPAqmzWJMQqSYzGyFnDWN3oELZRs",
                        "zrH1f5K3z7EQ6RWWZ7StCDWHTZwFChBVA2W","zrNTacAid9LS4kAqzM4sw1YcF7gLFrzVM7U","zrFyZpMVKMeDqbn6A2uUiL9mZmgxuR1pUBg",
                        "zrD1cqGFGzBcPogFHJvnN4XegvvmbTjA43t","zr5A1D7czWkB4pAWfGC5Pux5Ek7anYybdPK","zr8yTAxCy6jAdsc6qPvmVEQHbYo25AJKhy9",
                        "zrFW2YjQw4cABim5kEDwapbSqTz3wW7cWkk","zr9nJvNbsrvUTZD41fhqAQeUcgMfqZmAweN","zrCx4dXZd5b2tD483Ds4diHpo1QxBMJ76Jr",
                        "zr6eVeRwU6Puob3K1RfWtva1R458oj8pzkL","zr7B92iHtQcobZjGCXo3DAqMQjsn7ka31wE","zr8bcemLWAjYuphXSVqtqZWEnJipCB9F5oC",
                        "zrFzsuPXb7HsFd3srBqtVqnC9GQ94DQubV2","zr4yiBobiHjHnCYi75NmYtyoqCV4A3kpHDL","zrGVdR4K4F8MfmWxhUiTypK7PTsvHi8uTAh",
                        "zr7WiCDqCMvUdH1xmMu8YrBMFb2x2E6BX3z","zrEFrGWLX4hPHuHRUD3TPbMAJyeSpMSctUc","zr5c3f8PTnW8qBFX1GvK2LhyLBBCb1WDdGG",
                        "zrGkAZkZLqC9QKJR3XomgxNizCpNuAupTeg","zrM7muDowiun9tCHhu5K9vcDGfUptuYorfZ","zrCsWfwKotWnQmFviqAHAPAJ2jXqZYW966P",
                        "zrLLB3JB3jozUoMGFEGhjqyVXTpngVQ8c4T","zrAEa8YjJ2f3m2VsM1Xa9EwibZxEnRoSLUx","zrAdJgp7Cx35xTvB7ABWP8YLTNDArMjP1s3",
                   },

                    TreasuryRewardAddresses = new[]
                    {
                        "zrRBQ5heytPMN5nY3ssPf3cG4jocXeD8fm1","zrRBQ5heytPMN5nY3ssPf3cG4jocXeD8fm1",
                        "zrRBQ5heytPMN5nY3ssPf3cG4jocXeD8fm1","zrRBQ5heytPMN5nY3ssPf3cG4jocXeD8fm1",
                    }
                }
            },
        };

        new public static Dictionary<CoinType, Dictionary<BitcoinNetworkType, ZencashCoinbaseTxConfig>> CoinbaseTxConfig =
            new Dictionary<CoinType, Dictionary<BitcoinNetworkType, ZencashCoinbaseTxConfig>>
            {
                { CoinType.ZEN, ZencashCoinbaseTxConfig },
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

    public static class ZencashCommands
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

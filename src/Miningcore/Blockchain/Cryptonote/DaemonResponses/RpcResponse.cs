using System;
using System.Text.Json.Serialization;

namespace Miningcore.Blockchain.Cryptonote.DaemonResponses
{
    internal class RpcResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; }
        [JsonPropertyName("error")]
        public Error Error { get; set; }
        [JsonIgnore()]
        public bool ContainsError
        {
            get
            {
                return Error != null && Error.Code != default;
            }
        }
    }

    internal class Error
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }
        [JsonPropertyName("message")]
        public string Message { get; set; }
        [JsonIgnore()]
        public JsonRpcErrorCode JsonRpcErrorCode
        {
            get
            {
                if(Code <= -32000 && Code >= -32099)
                    return JsonRpcErrorCode.ServerError;
                else
                {
                    if(Enum.IsDefined(typeof(JsonRpcErrorCode), Code))
                    {
                        return (JsonRpcErrorCode) Code;
                    }
                    else
                        return JsonRpcErrorCode.UnknownError;
                }
            }
        }
    }

    public enum JsonRpcErrorCode
    {
        //////////////////////////////
        /// JsonRpc-Related Errors ///
        //////////////////////////////

        /// <summary>
        /// Invalid JSON was received by the server.
        /// An error occurred on the server while parsing the JSON text.
        /// </summary>
        ParseError = -32700,
        /// <summary>
        /// The JSON sent is not a valid Request object.
        /// </summary>
        InvalidRequest = -32600,
        /// <summary>
        /// The method does not exist / is not available.
        /// </summary>
        MethodNotFound = -32601,
        /// <summary>
        /// Invalid method parameter(s).
        /// </summary>
        InvalidParameters = -32602,
        /// <summary>
        /// Internal JSON-RPC error.
        /// </summary>
        InternalJsonError = -32603,
        /// <summary>
        /// Reserved for implementation-defined server-errors.
        /// </summary>
        ServerError = -32000,

        /////////////////////////////
        /// Monero-Related Errors ///
        /////////////////////////////

        // Source: https://github.com/monero-project/monero/blob/8286f07b265d16a87b3fe3bb53e8d7bf37b5265a/src/wallet/wallet_rpc_server_error_codes.h
        UnknownError = -1,
        WrongAddress = -2,
        DaemonIsBusy = -3,
        GenericTransferError = -4,
        WrongPaymentID = -5,
        TransferType = -6,
        Denied = -7,
        WrongTxid = -8,
        WrongSignature = -9,
        WrongKeyImage = -10,
        WrongUri = -11,
        WrongIndex = -12,
        NotOpen = -13,
        AccountIndexOutOfBounds = -14,
        AddressIndexOutOfBounds = -15,
        TxNotPossible = -16,
        NotEnoughMoney = -17,
        TxTooLarge = -18,
        NoutEnoughOutsToMix = -19,
        ZeroDestination = -20,
        WalletAlreadyExists = -21,
        InvalidPassword = -22,
        NoWalletDirectory = -23,
        NoTxKey = -24,
        WrongKey = -25,
        BadHex = -26,
        BadTxMetadata = -27,
        AlreadyMultiSig = -28,
        WatchOnly = -29,
        BadMultiSigInfo = -30,
        NotMultiSig = -31,
        WrongLR = -32,  // MultiSig curve points that get "merged" from all signers.
        ThresholdNotReached = -33,
        BadMultiSigTxData = -34,
        MultiSigSignature = -35,
        MultiSigSubmission = -36,
        NotEnoughUnlockedMoney = -37,
        NoDaemonConnection = -38,
        BadUnsignedTxData = -39,
        BadSignedTxData = -40,
        SignedSubmission = -41,
        SignUnsigned = -42,
        NonDeterministic = -43,
        InvalidLogLevel = -44,
        AttributeNotFound = -45,
        InvalidSignatureType = -47, // Yes, -46 appears to be missing. Maybe -46 is bad luck.
    }


}

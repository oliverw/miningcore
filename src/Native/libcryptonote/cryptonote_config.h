#pragma once

#define CURRENT_TRANSACTION_VERSION  1
#define POU_TRANSACTION_VERSION      6
#define OFFSHORE_TRANSACTION_VERSION 3
#define HF_VERSION_XASSET_FEES_V2    17
#define HF_VERSION_HAVEN2            18

// UNLOCK TIMES
#define TX_V6_OFFSHORE_UNLOCK_BLOCKS                    21*720  // 21 day unlock time
#define TX_V6_ONSHORE_UNLOCK_BLOCKS                     360     // 12 hour unlock time
#define TX_V6_XASSET_UNLOCK_BLOCKS                      1440    // 2 day unlock time
#define TX_V6_OFFSHORE_UNLOCK_BLOCKS_TESTNET            60     // 2 hour unlock time - FOR TESTING ONLY
#define TX_V6_ONSHORE_UNLOCK_BLOCKS_TESTNET             30     // 1 hour unlock time - FOR TESTING ONLY
#define TX_V6_XASSET_UNLOCK_BLOCKS_TESTNET              60     // 2 hour unlock time - FOR TESTING ONLY

#define PRICING_RECORD_VALID_TIME_DIFF_FROM_BLOCK       120  // seconds

enum BLOB_TYPE {
  BLOB_TYPE_CRYPTONOTE        = 0,
  BLOB_TYPE_FORKNOTE1         = 1,
  BLOB_TYPE_FORKNOTE2         = 2,
  BLOB_TYPE_CRYPTONOTE2       = 3, // Masari
  BLOB_TYPE_CRYPTONOTE_RYO    = 4, // Ryo
  BLOB_TYPE_CRYPTONOTE_LOKI   = 5, // Loki
  BLOB_TYPE_CRYPTONOTE3       = 6, // Masari
  BLOB_TYPE_AEON              = 7, // Aeon
  BLOB_TYPE_CRYPTONOTE_CUCKOO = 8, // MoneroV / Swap
  BLOB_TYPE_CRYPTONOTE_XTNC   = 9, // XTNC
  BLOB_TYPE_CRYPTONOTE_TUBE   = 10, // TUBE
  BLOB_TYPE_CRYPTONOTE_XHV    = 11, // Haven
  BLOB_TYPE_CRYPTONOTE_XTA    = 12, // ITALO
};

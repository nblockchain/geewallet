namespace GWallet.Backend.Tests.Unit

open NUnit.Framework

open GWallet.Backend
open GWallet.Backend.UtxoCoin.Lightning

[<TestFixture>]
type SerializedChannelTest() =
    let serializedChannelJson = """
{
  "version": "0.3.211.0",
  "typeName": "GWallet.Backend.UtxoCoin.Lightning.SerializedChannel",
  "value": {
    "channelIndex": 303655427,
    "network": "RegTest",
    "chanState": {
      "case": "Normal",
      "fields": [
        {
          "commitments": {
            "channelId": "665448ef7f783d6b775f1655ff2aa5237c5dd9909ada5e42cbca8dbf5c6d8169",
            "channelFlags": 1,
            "fundingScriptCoin": {
              "isP2SH": false,
              "redeemType": 1,
              "redeem": "5221035fc98b7dc5b66d6bb9bfeb8216018387db3513a9b44daa7ff5f0c58663909edc2103dd76abbf83dd23dd4a9bb7b18eca1c6ea6255335c105dc04ed00ba454814804c52ae",
              "canGetScriptCode": true,
              "outpoint": "69816d5cbf8dcacb425eda9a90d95d7c23a52aff55165f776b3d787fef48546600000000",
              "txOut": {
                "scriptPubKey": "0020c4833fdbf6323d74fd40b8dc90d76090e52a92fc495559472423528917311872",
                "value": 200000
              },
              "amount": 200000,
              "scriptPubKey": "0020c4833fdbf6323d74fd40b8dc90d76090e52a92fc495559472423528917311872"
            },
            "localChanges": {
              "proposed": [],
              "signed": [],
              "acKed": []
            },
            "localCommit": {
              "index": 0,
              "spec": {
                "htlCs": {},
                "feeRatePerKw": {
                  "case": "FeeRatePerKw",
                  "fields": [
                    12500
                  ]
                },
                "toLocal": {
                  "case": "LNMoney",
                  "fields": [
                    0
                  ]
                },
                "toRemote": {
                  "case": "LNMoney",
                  "fields": [
                    200000000
                  ]
                },
                "totalFunds": {
                  "case": "LNMoney",
                  "fields": [
                    200000000
                  ]
                }
              },
              "publishableTxs": {
                "commitTx": {
                  "case": "FinalizedTx",
                  "fields": [
                    {
                      "rbf": true,
                      "version": 2,
                      "totalOut": 190950,
                      "lockTime": 544455406,
                      "inputs": [
                        {
                          "sequence": 2150073653,
                          "prevOut": "69816d5cbf8dcacb425eda9a90d95d7c23a52aff55165f776b3d787fef48546600000000",
                          "scriptSig": "",
                          "witScript": "040047304402207d586ba70e3355e6861c6e83bc1b21f33faaecd6617802fc47ee22ccd5aca6a20220237262357650d1be3eabf4fbc8602b7f957f3991632dfcdad74d96181f404f140147304402200445260c98b69ab57287c4cccbfc25ff75d8dd9bd87c5149d17a313793b7d92b02200d7a3861d57a244296c7b1687ee531c755f36c2f02479dd35f2799559e9dd9a001475221035fc98b7dc5b66d6bb9bfeb8216018387db3513a9b44daa7ff5f0c58663909edc2103dd76abbf83dd23dd4a9bb7b18eca1c6ea6255335c105dc04ed00ba454814804c52ae",
                          "isFinal": false
                        }
                      ],
                      "outputs": [
                        {
                          "scriptPubKey": "0014f2a369d9d497bc84a1b6beb75c9bfa5e89ed1c2f",
                          "value": 190950
                        }
                      ],
                      "isCoinBase": false,
                      "hasWitness": true
                    }
                  ]
                },
                "htlcTxs": []
              },
              "pendingHTLCSuccessTxs": []
            },
            "localNextHTLCId": {
              "case": "HTLCId",
              "fields": [
                0
              ]
            },
            "localParams": {
              "nodeId": {
                "case": "NodeId",
                "fields": [
                  "02531631e8c6035917f74967c2c5ab7c747030123819e2155112bf54660e7318ff"
                ]
              },
              "channelPubKeys": {
                "fundingPubKey": {
                  "case": "FundingPubKey",
                  "fields": [
                    "035fc98b7dc5b66d6bb9bfeb8216018387db3513a9b44daa7ff5f0c58663909edc"
                  ]
                },
                "revocationBasepoint": {
                  "case": "RevocationBasepoint",
                  "fields": [
                    "03a2ca70d1d75d04c872f0aa779c54fd1d97c1cf49657e51d2b77c7aeeb858c0b7"
                  ]
                },
                "paymentBasepoint": {
                  "case": "PaymentBasepoint",
                  "fields": [
                    "020de8025a483b7aa22d7d95e29c245bf9e4c1d0db6df1a7ef5471234fb0828b41"
                  ]
                },
                "delayedPaymentBasepoint": {
                  "case": "DelayedPaymentBasepoint",
                  "fields": [
                    "023ffb464454459bacc952b31158f6f243579435f72de27110a396c914247fbee8"
                  ]
                },
                "htlcBasepoint": {
                  "case": "HtlcBasepoint",
                  "fields": [
                    "0234b85dc90eb9d2440689f97727378fa7f8ce3bc5f1aa0c9fa1a807fe04d6e238"
                  ]
                }
              },
              "dustLimitSatoshis": 200,
              "maxHTLCValueInFlightMSat": {
                "case": "LNMoney",
                "fields": [
                  10000
                ]
              },
              "channelReserveSatoshis": 2000,
              "htlcMinimumMSat": {
                "case": "LNMoney",
                "fields": [
                  1000
                ]
              },
              "toSelfDelay": {
                "case": "BlockHeightOffset16",
                "fields": [
                  6
                ]
              },
              "maxAcceptedHTLCs": 10,
              "isFunder": false,
              "defaultFinalScriptPubKey": "a91439cb278a6d43742808ee6d1ef8ebca5d5805202087",
              "features": "10"
            },
            "originChannels": {},
            "remoteChanges": {
              "proposed": [],
              "signed": [],
              "acKed": []
            },
            "remoteCommit": {
              "index": 0,
              "spec": {
                "htlCs": {},
                "feeRatePerKw": {
                  "case": "FeeRatePerKw",
                  "fields": [
                    12500
                  ]
                },
                "toLocal": {
                  "case": "LNMoney",
                  "fields": [
                    200000000
                  ]
                },
                "toRemote": {
                  "case": "LNMoney",
                  "fields": [
                    0
                  ]
                },
                "totalFunds": {
                  "case": "LNMoney",
                  "fields": [
                    200000000
                  ]
                }
              },
              "txId": {
                "case": "TxId",
                "fields": [
                  "77af2e789a1cd752c633389d84cd4e19bc97254c04295138068d28d8d8466b4a"
                ]
              },
              "remotePerCommitmentPoint": {
                "case": "PerCommitmentPoint",
                "fields": [
                  "03209f80716bfaefa9d37453497c29f40cd94c6ef9c862771f87f1a4154cf8c8b3"
                ]
              }
            },
            "remoteNextCommitInfo": {
              "case": "Revoked",
              "fields": [
                {
                  "case": "PerCommitmentPoint",
                  "fields": [
                    "03a890b39e0de045dc2c19c2a8ff9f46a0e79517686f9655793704680e62a0f8f4"
                  ]
                }
              ]
            },
            "remoteNextHTLCId": {
              "case": "HTLCId",
              "fields": [
                0
              ]
            },
            "remoteParams": {
              "nodeId": {
                "case": "NodeId",
                "fields": [
                  "02531631e8c6035917f74967c2c5ab7c747030123819e2155112bf54660e7318ff"
                ]
              },
              "dustLimitSatoshis": 573,
              "maxHTLCValueInFlightMSat": {
                "case": "LNMoney",
                "fields": [
                  198000000
                ]
              },
              "channelReserveSatoshis": 2000,
              "htlcMinimumMSat": {
                "case": "LNMoney",
                "fields": [
                  1
                ]
              },
              "toSelfDelay": {
                "case": "BlockHeightOffset16",
                "fields": [
                  144
                ]
              },
              "maxAcceptedHTLCs": 483,
              "channelPubKeys": {
                "fundingPubKey": {
                  "case": "FundingPubKey",
                  "fields": [
                    "03dd76abbf83dd23dd4a9bb7b18eca1c6ea6255335c105dc04ed00ba454814804c"
                  ]
                },
                "revocationBasepoint": {
                  "case": "RevocationBasepoint",
                  "fields": [
                    "03a16572214b45b8568c0ea2e60ac77143df4b0c9d388014341971e34f26b7001f"
                  ]
                },
                "paymentBasepoint": {
                  "case": "PaymentBasepoint",
                  "fields": [
                    "03ad3095101965bd5050fbfa54888579fc22831622a45901e70ba281c17df3c973"
                  ]
                },
                "delayedPaymentBasepoint": {
                  "case": "DelayedPaymentBasepoint",
                  "fields": [
                    "02dbd2d4ee2bf059335452c9e8dd101316b5b3e0e5c203ee7f904e8f9012a09d68"
                  ]
                },
                "htlcBasepoint": {
                  "case": "HtlcBasepoint",
                  "fields": [
                    "034e945128ac878ed10f630acd3d4267f31d30855907926203028d6cc48f0e8418"
                  ]
                }
              },
              "features": "000000101010001010100001",
              "minimumDepth": {
                "case": "BlockHeightOffset32",
                "fields": [
                  1
                ]
              }
            },
            "remotePerCommitmentSecrets": []
          },
          "shortChannelId": {
            "blockHeight": {
              "case": "BlockHeight",
              "fields": [
                102
              ]
            },
            "blockIndex": {
              "case": "TxIndexInBlock",
              "fields": [
                1
              ]
            },
            "txOutIndex": {
              "case": "TxOutIndex",
              "fields": [
                0
              ]
            },
            "asString": "102x1x0"
          },
          "buried": true,
          "channelAnnouncement": null,
          "channelUpdate": {
            "signature@": {
              "case": "LNECDSASignature",
              "fields": [
                "30440220735696a3515c80b97562ae01bf4a1c1e4ccd6a44193a00002d4df2093618672302201c37cc298829cbcfcdabe8f13b021399a90745767cd8710acc5e8bbe67e3587e"
              ]
            },
            "contents@": {
              "chainHash@": "0f9188f13cb7b2c71f2a335e3a4fc328bf5beb436012afca590b1a11466e2206",
              "shortChannelId@": {
                "blockHeight": {
                  "case": "BlockHeight",
                  "fields": [
                    102
                  ]
                },
                "blockIndex": {
                  "case": "TxIndexInBlock",
                  "fields": [
                    1
                  ]
                },
                "txOutIndex": {
                  "case": "TxOutIndex",
                  "fields": [
                    0
                  ]
                },
                "asString": "102x1x0"
              },
              "timestamp@": 1609240018,
              "messageFlags@": 0,
              "channelFlags@": 2,
              "cltvExpiryDelta@": {
                "case": "BlockHeightOffset16",
                "fields": [
                  6
                ]
              },
              "htlcMinimumMSat@": {
                "case": "LNMoney",
                "fields": [
                  1
                ]
              },
              "feeBaseMSat@": {
                "case": "LNMoney",
                "fields": [
                  9274000
                ]
              },
              "feeProportionalMillionths@": 100,
              "htlcMaximumMSat@": null,
              "chainHash": "0f9188f13cb7b2c71f2a335e3a4fc328bf5beb436012afca590b1a11466e2206",
              "shortChannelId": {
                "blockHeight": {
                  "case": "BlockHeight",
                  "fields": [
                    102
                  ]
                },
                "blockIndex": {
                  "case": "TxIndexInBlock",
                  "fields": [
                    1
                  ]
                },
                "txOutIndex": {
                  "case": "TxOutIndex",
                  "fields": [
                    0
                  ]
                },
                "asString": "102x1x0"
              },
              "timestamp": 1609240018,
              "messageFlags": 0,
              "channelFlags": 2,
              "cltvExpiryDelta": {
                "case": "BlockHeightOffset16",
                "fields": [
                  6
                ]
              },
              "htlcMinimumMSat": {
                "case": "LNMoney",
                "fields": [
                  1
                ]
              },
              "feeBaseMSat": {
                "case": "LNMoney",
                "fields": [
                  9274000
                ]
              },
              "feeProportionalMillionths": 100,
              "htlcMaximumMSat": null
            },
            "signature": {
              "case": "LNECDSASignature",
              "fields": [
                "30440220735696a3515c80b97562ae01bf4a1c1e4ccd6a44193a00002d4df2093618672302201c37cc298829cbcfcdabe8f13b021399a90745767cd8710acc5e8bbe67e3587e"
              ]
            },
            "contents": {
              "chainHash@": "0f9188f13cb7b2c71f2a335e3a4fc328bf5beb436012afca590b1a11466e2206",
              "shortChannelId@": {
                "blockHeight": {
                  "case": "BlockHeight",
                  "fields": [
                    102
                  ]
                },
                "blockIndex": {
                  "case": "TxIndexInBlock",
                  "fields": [
                    1
                  ]
                },
                "txOutIndex": {
                  "case": "TxOutIndex",
                  "fields": [
                    0
                  ]
                },
                "asString": "102x1x0"
              },
              "timestamp@": 1609240018,
              "messageFlags@": 0,
              "channelFlags@": 2,
              "cltvExpiryDelta@": {
                "case": "BlockHeightOffset16",
                "fields": [
                  6
                ]
              },
              "htlcMinimumMSat@": {
                "case": "LNMoney",
                "fields": [
                  1
                ]
              },
              "feeBaseMSat@": {
                "case": "LNMoney",
                "fields": [
                  9274000
                ]
              },
              "feeProportionalMillionths@": 100,
              "htlcMaximumMSat@": null,
              "chainHash": "0f9188f13cb7b2c71f2a335e3a4fc328bf5beb436012afca590b1a11466e2206",
              "shortChannelId": {
                "blockHeight": {
                  "case": "BlockHeight",
                  "fields": [
                    102
                  ]
                },
                "blockIndex": {
                  "case": "TxIndexInBlock",
                  "fields": [
                    1
                  ]
                },
                "txOutIndex": {
                  "case": "TxOutIndex",
                  "fields": [
                    0
                  ]
                },
                "asString": "102x1x0"
              },
              "timestamp": 1609240018,
              "messageFlags": 0,
              "channelFlags": 2,
              "cltvExpiryDelta": {
                "case": "BlockHeightOffset16",
                "fields": [
                  6
                ]
              },
              "htlcMinimumMSat": {
                "case": "LNMoney",
                "fields": [
                  1
                ]
              },
              "feeBaseMSat": {
                "case": "LNMoney",
                "fields": [
                  9274000
                ]
              },
              "feeProportionalMillionths": 100,
              "htlcMaximumMSat": null
            },
            "isNode1": true
          },
          "localShutdown": null,
          "remoteShutdown": null,
          "channelId": {
            "case": "ChannelId",
            "fields": [
              "665448ef7f783d6b775f1655ff2aa5237c5dd9909ada5e42cbca8dbf5c6d8169"
            ]
          }
        }
      ]
    },
    "accountFileName": "035e5a215294ee353b8f23536684c0a589567a8eedf9d62e45986a50f73ff4fc6b",
    "counterpartyIP": [
      "127.0.0.1",
      57944
    ],
    "remoteNodeId": {
      "case": "NodeId",
      "fields": [
        "02531631e8c6035917f74967c2c5ab7c747030123819e2155112bf54660e7318ff"
      ]
    },
    "minSafeDepth": {
      "case": "BlockHeightOffset32",
      "fields": [
        1
      ]
    },
    "localForceCloseSpendingTxOpt": null
  }
}
"""

    [<Test>]
    member __.``deserialized and reserialize without change``() =
        let serializedChannel = 
            Marshalling.DeserializeCustom<SerializedChannel> (
                serializedChannelJson,
                SerializedChannel.LightningSerializerSettings Currency.BTC
            )
        let reserializedChannelJson =
            Marshalling.SerializeCustom(
                serializedChannel,
                SerializedChannel.LightningSerializerSettings Currency.BTC
            )
        if serializedChannelJson.Trim() <> reserializedChannelJson then
            failwith ("deserializing and reserializing a channel changed the json:\n" + reserializedChannelJson)



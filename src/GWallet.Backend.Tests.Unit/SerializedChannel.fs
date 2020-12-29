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
    "channelIndex": 903692971,
    "network": "RegTest",
    "chanState": {
      "case": "Normal",
      "fields": [
        {
          "commitments": {
            "channelId": "ac5ce55b5d8ae0fa06a58a645300e35ea436eae8bcb3211e87e9023cb8c82f23",
            "channelFlags": 1,
            "fundingScriptCoin": {
              "isP2SH": false,
              "redeemType": 1,
              "redeem": "5221025ec158997cbe74136495ac172df99ea003e7e94042ade06590991bfacafce4bf21036e220d166adf482b4311704f3517ff5f32688096a566add72535484d6f02bcc652ae",
              "canGetScriptCode": true,
              "outpoint": "232fc8b83c02e9871e21b3bce8ea36a45ee30053648aa506fae08a5d5be55cac00000000",
              "txOut": {
                "scriptPubKey": "0020c11ba76a3d4c52c8dd6c55b69f3bc7c494922cadc70ed297a4af443383c5db71",
                "value": 200000
              },
              "amount": 200000,
              "scriptPubKey": "0020c11ba76a3d4c52c8dd6c55b69f3bc7c494922cadc70ed297a4af443383c5db71"
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
                      "lockTime": 541687920,
                      "inputs": [
                        {
                          "sequence": 2163014744,
                          "prevOut": "232fc8b83c02e9871e21b3bce8ea36a45ee30053648aa506fae08a5d5be55cac00000000",
                          "scriptSig": "",
                          "witScript": "040047304402205f54247e4a3036a9c9cdb31fa88e8c6681b0c2ce3d73f90be73a3e324187991202207bb1c7ef8f9eeb98494a9d2f0bbc738dde85881320713126feb07d526680a1d90147304402200f86f9f0ecc97f0db06fa2cc5df15662b0c16c758b41472bb6419b3e4249bdf502204360d3b7677fdd94865445002a417995c1932d5787a099ddbf55b93779bb5bee01475221025ec158997cbe74136495ac172df99ea003e7e94042ade06590991bfacafce4bf21036e220d166adf482b4311704f3517ff5f32688096a566add72535484d6f02bcc652ae",
                          "isFinal": false
                        }
                      ],
                      "outputs": [
                        {
                          "scriptPubKey": "0014f649d8ab6a723a59b830b789e299b58f5c40caef",
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
                  "02359f49a1f8314274bd6b389c0408a77772f6339ac15bf6c30f12e88670e94d1e"
                ]
              },
              "channelPubKeys": {
                "fundingPubKey": {
                  "case": "FundingPubKey",
                  "fields": [
                    "036e220d166adf482b4311704f3517ff5f32688096a566add72535484d6f02bcc6"
                  ]
                },
                "revocationBasepoint": {
                  "case": "RevocationBasepoint",
                  "fields": [
                    "02b991bcec8eb90cca4df3b2623e60cfe0ba7b9aea243f1c6b1cacd4323bad5141"
                  ]
                },
                "paymentBasepoint": {
                  "case": "PaymentBasepoint",
                  "fields": [
                    "0377f77a5e50a309483ff5a73016d18b51b9340414d1999da84f4be73af16fa69d"
                  ]
                },
                "delayedPaymentBasepoint": {
                  "case": "DelayedPaymentBasepoint",
                  "fields": [
                    "022500be5526d6ef578566950b3788ac575ad1e69fb74f9523bc9bf891553bb7c7"
                  ]
                },
                "htlcBasepoint": {
                  "case": "HtlcBasepoint",
                  "fields": [
                    "025b97f531c44485d19ae4c459ebd18273ae1c168373df09286337ea304959ba46"
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
              "defaultFinalScriptPubKey": "a914ac351a902327793cb7fdcda4c024ec90ef45e8fa87",
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
                  "3b18008b7e82ed5c6cc48974deae21ab61ea95012de946564f44fc7889d85489"
                ]
              },
              "remotePerCommitmentPoint": {
                "case": "PerCommitmentPoint",
                "fields": [
                  "0348445245bc5b0d4c003b0ed6c00dc34c4096dd8dbee55508a4f618080f9f8376"
                ]
              }
            },
            "remoteNextCommitInfo": {
              "case": "Revoked",
              "fields": [
                {
                  "case": "PerCommitmentPoint",
                  "fields": [
                    "03ae10084012d3ff29e7fc45b0a93e5233a1abb38a4f3c1b96c9788c688333709a"
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
                  "02359f49a1f8314274bd6b389c0408a77772f6339ac15bf6c30f12e88670e94d1e"
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
                    "025ec158997cbe74136495ac172df99ea003e7e94042ade06590991bfacafce4bf"
                  ]
                },
                "revocationBasepoint": {
                  "case": "RevocationBasepoint",
                  "fields": [
                    "03ea5cb6a7a314ebc2f0a5298c206786c718486e4708df361b81529b3aa50d1ad9"
                  ]
                },
                "paymentBasepoint": {
                  "case": "PaymentBasepoint",
                  "fields": [
                    "024dd54d57bc292e5778ca440aab0748115ea06a6bbc90367df16824d7f2e6a77c"
                  ]
                },
                "delayedPaymentBasepoint": {
                  "case": "DelayedPaymentBasepoint",
                  "fields": [
                    "024a2b9de1a687ce31f5422fbe01f0bcc898bd7c4413404df17b3c80575f44d8d8"
                  ]
                },
                "htlcBasepoint": {
                  "case": "HtlcBasepoint",
                  "fields": [
                    "026089e7eeaa8c5a92f0ae369b502fd11087888919622e2bcd6d4e457a4e9a9f3b"
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
                "30440220460d44f1e0c7363d29bf055b2c788dd9e66cf05bc36e90e19fcbb9454f5789fc022019944e53046a0f3de29c5945ac8b9692b39e76d6270f9ec6c6ab9b4d552427f2"
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
              "timestamp@": 1609236445,
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
              "timestamp": 1609236445,
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
                "30440220460d44f1e0c7363d29bf055b2c788dd9e66cf05bc36e90e19fcbb9454f5789fc022019944e53046a0f3de29c5945ac8b9692b39e76d6270f9ec6c6ab9b4d552427f2"
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
              "timestamp@": 1609236445,
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
              "timestamp": 1609236445,
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
              "ac5ce55b5d8ae0fa06a58a645300e35ea436eae8bcb3211e87e9023cb8c82f23"
            ]
          }
        }
      ]
    },
    "accountFileName": "0252b4e4866425fc57691094c2629794aba6c48667d46d865ac2cbe543c86051f1",
    "counterpartyIP": [
      "127.0.0.1",
      55106
    ],
    "remoteNodeId": {
      "case": "NodeId",
      "fields": [
        "02359f49a1f8314274bd6b389c0408a77772f6339ac15bf6c30f12e88670e94d1e"
      ]
    },
    "minSafeDepth": {
      "case": "BlockHeightOffset32",
      "fields": [
        1
      ]
    }
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



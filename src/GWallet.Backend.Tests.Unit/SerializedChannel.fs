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
    "channelIndex": 815823485,
    "network": "RegTest",
    "chanState": {
      "case": "Normal",
      "fields": [
        {
          "commitments": {
            "channelId": "09f1edcaa21d5024079bca6cf79bc5507e9b9d170f338ada93ea258c5e57a999",
            "channelFlags": 1,
            "fundingScriptCoin": {
              "isP2SH": false,
              "redeemType": 1,
              "redeem": "52210208889b6551eca8d76752f3a5da5a7e348a16b7afe1e39a3867fcc90f1087d28521026ca9005eea0d8338b27714f6b0b62659ce738e579c93fc4f62d819391c27102352ae",
              "canGetScriptCode": true,
              "isMalleable": false,
              "outpoint": "99a9575e8c25ea93da8a330f179d9b7e50c59bf76cca9b0724501da2caedf10900000000",
              "txOut": {
                "scriptPubKey": "0020739632cc42677272830070a0c6336ab1ec5af348bcf69e36e2c947977f58646e",
                "value": 200000
              },
              "amount": 200000,
              "scriptPubKey": "0020739632cc42677272830070a0c6336ab1ec5af348bcf69e36e2c947977f58646e"
            },
            "localChanges": {
              "proposed": [],
              "signed": [],
              "acKed": [],
              "all": []
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
                      "lockTime": 544035012,
                      "inputs": [
                        {
                          "sequence": 2149693378,
                          "prevOut": "99a9575e8c25ea93da8a330f179d9b7e50c59bf76cca9b0724501da2caedf10900000000",
                          "scriptSig": "",
                          "witScript": "040047304402200b21b303d3548e8f065f457765c5cb2f00cda81c1dabd26322fa085cf73d789002205b1e5a1c6b99377e6b34ceda40b593011a716a07fa34e49c89a6cb3f82b2629a0147304402206217d9ea1123776d57b4a9bb3638575f8e58e8add7f67969d187bd735aa753ec02202e063f56b4f08a3fb22947207cd928d087b9181a9095da405acb9df9289aa8a6014752210208889b6551eca8d76752f3a5da5a7e348a16b7afe1e39a3867fcc90f1087d28521026ca9005eea0d8338b27714f6b0b62659ce738e579c93fc4f62d819391c27102352ae",
                          "isFinal": false
                        }
                      ],
                      "outputs": [
                        {
                          "scriptPubKey": "0014c9748f45b8efe5f28ab2ef3a843b6fe0cf457ed8",
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
                  "02740ea7c4611204649e2e1669590b75d5353e4cc9b07c1ab8a4dcb29cc7c44ba1"
                ]
              },
              "channelPubKeys": {
                "fundingPubKey": "0208889b6551eca8d76752f3a5da5a7e348a16b7afe1e39a3867fcc90f1087d285",
                "revocationBasepoint": "02c966f9273237fcc26039d0823f0af2043c404d21dbb55c86fa5a1bcaa7bcf689",
                "paymentBasepoint": "038727cd715995e69ed96c8b827df2c4f1a7bcb09926d245113c0132a9e0f3ded1",
                "delayedPaymentBasepoint": "02f0c6c63a008825b2ac7b3f08c399090144364728f7f382362d5ba0f9709adfba",
                "htlcBasepoint": "030fe357eb73a25f171617b00c08a060735973f4e2144290c3c5f05888cf93175b"
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
              "defaultFinalScriptPubKey": "a914a1577eb21d64bbba8c1fe6b0ac950cfceedb2b7f87",
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
                  "4a0b2d6c5d4c1b8336b86792ad7e3a93299209c03dfa1793293c4d38f0556504"
                ]
              },
              "remotePerCommitmentPoint": "03896c34f91cbf0174b135c1c377228b7e5d5528b96229e0d6fea57f735b439ea9"
            },
            "remoteNextCommitInfo": {
              "case": "Revoked",
              "fields": [
                "03eae50823f7e614a0be73b8cb246760202fa2b698d71ac4043958e36aaed31932"
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
                  "02740ea7c4611204649e2e1669590b75d5353e4cc9b07c1ab8a4dcb29cc7c44ba1"
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
                "fundingPubKey": "026ca9005eea0d8338b27714f6b0b62659ce738e579c93fc4f62d819391c271023",
                "revocationBasepoint": "03cd002a80cfc2b8ae23fa749c1d438ce79a7264dd1d991623b0a27a48540a1b62",
                "paymentBasepoint": "028ca5022205cdd9d39e3824643f3b02e2e57996fb5fc65480a1febe0e744d8955",
                "delayedPaymentBasepoint": "03eaf079d483e09d2a0b4587325f1a945a9b122be2b29d31b799b96338bfb82bba",
                "htlcBasepoint": "027505b785755b87c59cd447183833e1a481403b408d50c4061532589a69444441"
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
                "304402206d0f66a94d69c5ece15dcdf58b345451f01650e92d63f837707807e4d7e96fa802200f1a3a691fc1311151b9f65ed29d6777cbcf3e342b526f66ce3f87af376cb5a1"
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
              "timestamp@": 1605082932,
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
              "timestamp": 1605082932,
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
                "304402206d0f66a94d69c5ece15dcdf58b345451f01650e92d63f837707807e4d7e96fa802200f1a3a691fc1311151b9f65ed29d6777cbcf3e342b526f66ce3f87af376cb5a1"
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
              "timestamp@": 1605082932,
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
              "timestamp": 1605082932,
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
              "09f1edcaa21d5024079bca6cf79bc5507e9b9d170f338ada93ea258c5e57a999"
            ]
          }
        }
      ]
    },
    "accountFileName": "022117dc6cb26162a23b7c8cd3d47f8cf40531fa71bb47d01ba75b5676c5d4457d",
    "counterpartyIP": [
      "127.0.0.1",
      49456
    ],
    "remoteNodeId": {
      "case": "NodeId",
      "fields": [
        "02740ea7c4611204649e2e1669590b75d5353e4cc9b07c1ab8a4dcb29cc7c44ba1"
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



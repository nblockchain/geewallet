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
    "channelIndex": 672938070,
    "chanState": {
      "case": "Normal",
      "fields": [
        {
          "commitments": {
            "channelId": "2c0e386c42c30b0c4032e8406ab632d6916636df1e5edf66eb083badda023afa",
            "channelFlags": 0,
            "fundingScriptCoin": {
              "isP2SH": false,
              "redeemType": 1,
              "redeem": "522102fdcf55c55324c7ddf78e5491a2efb2745e465180ff1c905c171cb08293644bb421038bfc09a2b108e8f63f3377b9374bd4642e9e57c2b8e53b08f7ab18dee284eba452ae",
              "canGetScriptCode": true,
              "outpoint": "fa3a02daad3b08eb66df5e1edf366691d632b66a40e832400c0bc3426c380e2c00000000",
              "txOut": {
                "scriptPubKey": "0020725a67d7c34f41ef89dd52a3171c4d13f6e1840186e5d7fb6693f3e179a36bf6",
                "value": 200000
              },
              "amount": 200000,
              "scriptPubKey": "0020725a67d7c34f41ef89dd52a3171c4d13f6e1840186e5d7fb6693f3e179a36bf6"
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
              "publishableTxs": {
                "commitTx": {
                  "case": "FinalizedTx",
                  "fields": [
                    {
                      "rbf": true,
                      "version": 2,
                      "totalOut": 190950,
                      "lockTime": 546235839,
                      "inputs": [
                        {
                          "sequence": 2154815875,
                          "prevOut": "fa3a02daad3b08eb66df5e1edf366691d632b66a40e832400c0bc3426c380e2c00000000",
                          "scriptSig": "",
                          "witScript": "040047304402205bb63f4aabe600d429ef13cf542daf2aed29611534e5f0b14610634fb4f484d602205ffb16408f7f6b5dca3fa4ecf605845bd1156042d19c92e0f6e7be896ac73a8601473044022033f44c97d22dc09682008d1439cc26f2f60b2aeded58fd7b16fff2e31e7f59d0022058f22d55e22090ca3b01bcf71313d1cbfcc1459908a238243e75e6fd000b529f0147522102fdcf55c55324c7ddf78e5491a2efb2745e465180ff1c905c171cb08293644bb421038bfc09a2b108e8f63f3377b9374bd4642e9e57c2b8e53b08f7ab18dee284eba452ae",
                          "isFinal": false
                        }
                      ],
                      "outputs": [
                        {
                          "scriptPubKey": "0020362e380f0665c3062b97933931c2f176204cd26873763694b0689c2a19369b87",
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
                "case": "GNodeId",
                "fields": [
                  "02323d281f323fc7513660602736a7bcd90d9d06057c9fa9e8d2deb0de5aeb2231"
                ]
              },
              "channelPubKeys": {
                "fundingPubKey": "02fdcf55c55324c7ddf78e5491a2efb2745e465180ff1c905c171cb08293644bb4",
                "revocationBasePubKey": "03408d1c4a0db3671695bc31b8a7528bdafc91af0a6a208507ef7bcdb2133d6397",
                "paymentBasePubKey": "021ca7cbadc640a6aba7aa62e976a53a9d79bd8181aa30700c8c486580efa3365b",
                "delayedPaymentBasePubKey": "02a6c7282ee4c98d7caea59a78b55e5d7a8797fdec9fa97107b39e48d234a37084",
                "htlcBasePubKey": "031f8050b624096531a0c9f3bba7e5bc7c571ad3eaba1a7587202b067d3986cd81"
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
              "isFunder": true,
              "defaultFinalScriptPubKey": "a914ee63c76cda9f5a4928a24710e3b950f2bed0bcc787",
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
              "txId": {
                "case": "GTxId",
                "fields": [
                  "2ce238b4fe973636e02004a6af2dd434a4221b1757ea62b0b0abac810674f3e8"
                ]
              },
              "remotePerCommitmentPoint": "032451f412d0ff745d333c50a32d35444c8f88f24b6348c5bbf4f20a6df70ce2ae"
            },
            "remoteNextCommitInfo": {
              "case": "Revoked",
              "fields": [
                "0272cbf08b7f138e5d07cefa136f154ba619143c4c87b8333c14ef6057e7df7e1a"
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
                "case": "GNodeId",
                "fields": [
                  "02323d281f323fc7513660602736a7bcd90d9d06057c9fa9e8d2deb0de5aeb2231"
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
              "paymentBasePoint": "034badd7bcd0cdaaae7a099d24e4966a42e9088b8fd9575435428e2ce52ce40132",
              "fundingPubKey": "038bfc09a2b108e8f63f3377b9374bd4642e9e57c2b8e53b08f7ab18dee284eba4",
              "revocationBasePoint": "03f1590ac91a6a78c4dc41b5d8caed115c51a9c1b87b6d43a73a5f5502a3ab20cf",
              "delayedPaymentBasePoint": "0291698f62ba42f26f9fe61fd9509119ff671659410d8d3e3682860f7f5d760645",
              "htlcBasePoint": "02579a84cb68f6fd41e864d6b5a9b130187584cd338bfd3fc45921b5acfee075fb",
              "features": "000000101010001010100001",
              "minimumDepth": {
                "case": "BlockHeightOffset32",
                "fields": [
                  3
                ]
              }
            },
            "remotePerCommitmentSecrets": []
          },
          "shortChannelId": {
            "blockHeight": {
              "case": "BlockHeight",
              "fields": [
                109
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
            "asString": "109x1x0"
          },
          "buried": true,
          "channelAnnouncement": null,
          "channelUpdate": {
            "signature@": {
              "case": "LNECDSASignature",
              "fields": [
                "30440220769a6c21a0cacb27fca747b98f6637878164c21c35b6d33131469ba13fed290d02201552dfc5db9980996064a9d432ac28563c6c347b66ef4bd99ad3d0a3873efdf2"
              ]
            },
            "contents@": {
              "chainHash@": "06226e46111a0b59caaf126043eb5bbf28c34f3a5e332a1fc7b2b73cf188910f",
              "shortChannelId@": {
                "blockHeight": {
                  "case": "BlockHeight",
                  "fields": [
                    109
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
                "asString": "109x1x0"
              },
              "timestamp@": 1597127189,
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
                  11424000
                ]
              },
              "feeProportionalMillionths@": 100,
              "htlcMaximumMSat@": null,
              "chainHash": "06226e46111a0b59caaf126043eb5bbf28c34f3a5e332a1fc7b2b73cf188910f",
              "shortChannelId": {
                "blockHeight": {
                  "case": "BlockHeight",
                  "fields": [
                    109
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
                "asString": "109x1x0"
              },
              "timestamp": 1597127189,
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
                  11424000
                ]
              },
              "feeProportionalMillionths": 100,
              "htlcMaximumMSat": null
            },
            "signature": {
              "case": "LNECDSASignature",
              "fields": [
                "30440220769a6c21a0cacb27fca747b98f6637878164c21c35b6d33131469ba13fed290d02201552dfc5db9980996064a9d432ac28563c6c347b66ef4bd99ad3d0a3873efdf2"
              ]
            },
            "contents": {
              "chainHash@": "06226e46111a0b59caaf126043eb5bbf28c34f3a5e332a1fc7b2b73cf188910f",
              "shortChannelId@": {
                "blockHeight": {
                  "case": "BlockHeight",
                  "fields": [
                    109
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
                "asString": "109x1x0"
              },
              "timestamp@": 1597127189,
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
                  11424000
                ]
              },
              "feeProportionalMillionths@": 100,
              "htlcMaximumMSat@": null,
              "chainHash": "06226e46111a0b59caaf126043eb5bbf28c34f3a5e332a1fc7b2b73cf188910f",
              "shortChannelId": {
                "blockHeight": {
                  "case": "BlockHeight",
                  "fields": [
                    109
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
                "asString": "109x1x0"
              },
              "timestamp": 1597127189,
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
                  11424000
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
            "case": "GChannelId",
            "fields": [
              "2c0e386c42c30b0c4032e8406ab632d6916636df1e5edf66eb083badda023afa"
            ]
          }
        }
      ]
    },
    "accountFileName": "0284bf7562262bbd6940085748f3be6afa52ae317155181ece31b66351ccffa4b0",
    "remoteNodeId": {
      "case": "GNodeId",
      "fields": [
        "02323d281f323fc7513660602736a7bcd90d9d06057c9fa9e8d2deb0de5aeb2231"
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
                SerializedChannel.LightningSerializerSettings
            )
        let reserializedChannelJson =
            Marshalling.SerializeCustom(
                serializedChannel,
                SerializedChannel.LightningSerializerSettings
            )
        if serializedChannelJson.Trim() <> reserializedChannelJson then
            failwith ("deserializing and reserializing a channel changed the json:\n" + reserializedChannelJson)



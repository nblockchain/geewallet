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
    "channelIndex": 349215525,
    "network": "RegTest",
    "chanState": {
      "case": "Normal",
      "fields": [
        {
          "commitments": {
            "channelId": "6e38deed8bd6094150060cddd5cb88ce759c2094eae1ba91253b2580935f6a1a",
            "channelFlags": 1,
            "fundingScriptCoin": {
              "isP2SH": false,
              "redeemType": 1,
              "redeem": "522102276692862e087e4682aaa7fd8e89fafb87f1f9bcce81df7785ad9661bd828716210344984cfb61fc3eff63a7f9ab4f7f41e840040682fda4b1682ca9e969f733b2be52ae",
              "canGetScriptCode": true,
              "isMalleable": false,
              "outpoint": "1a6a5f9380253b2591bae1ea94209c75ce88cbd5dd0c06504109d68bedde386e00000000",
              "txOut": {
                "scriptPubKey": "002048f211c32b6ef594f97b83b0b89b0a19b1a58f04965e26d1257c95012bc08530",
                "value": 200000
              },
              "amount": 200000,
              "scriptPubKey": "002048f211c32b6ef594f97b83b0b89b0a19b1a58f04965e26d1257c95012bc08530"
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
                      "lockTime": 546862683,
                      "inputs": [
                        {
                          "sequence": 2154926303,
                          "prevOut": "1a6a5f9380253b2591bae1ea94209c75ce88cbd5dd0c06504109d68bedde386e00000000",
                          "scriptSig": "",
                          "witScript": "040047304402204e845edf4bfd39b7dfc99c75cff3e36473dcf07d1fd3b8a74c801bc7d77f203102207bb749c837be85e432f91bbc9365697bb374bd655bda46ce5bf563cc7f685cc201463043021f17ded9c83862470eaa29de969a1358dda09af92374c556f1f24cdfd40e896602205f4e86ba6242d6bf0f25441113d12a0a79697ee0fbb8dedb6243e4b5a40604c90147522102276692862e087e4682aaa7fd8e89fafb87f1f9bcce81df7785ad9661bd828716210344984cfb61fc3eff63a7f9ab4f7f41e840040682fda4b1682ca9e969f733b2be52ae",
                          "isFinal": false
                        }
                      ],
                      "outputs": [
                        {
                          "scriptPubKey": "00146c2b0a420448524157264ac05a17ec1341d1c3fd",
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
                  "03a291695ef49daf5aecea435e79968bc42154a286ce67ad39512b662a2ec622b5"
                ]
              },
              "channelPubKeys": {
                "fundingPubKey": "0344984cfb61fc3eff63a7f9ab4f7f41e840040682fda4b1682ca9e969f733b2be",
                "revocationBasepoint": "024a1ca3c9d0ca5c889972fdbb29917ab35f4bfa0290e3b7e11220524b6029b6ea",
                "paymentBasepoint": "02b3d28a972f921122a4c2c4169a64202ad8ec4535057b4e18fa4ef754391efde4",
                "delayedPaymentBasepoint": "035272da6339ec2331749d2cefd80cc6e45ef9f94ba120d3a314038f26aa930c1f",
                "htlcBasepoint": "0326597b57900fd556283f92ebc2d3455425d8ea8d064c1281a2196e5e64ae80bc"
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
              "defaultFinalScriptPubKey": "a914f0dba68a8d457c11eb65e2d599da9742a04f83b987",
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
                  "947fae75c64a02cb538c3ed300a7fb1da3b46eaf808202f89c243fa091fe04c2"
                ]
              },
              "remotePerCommitmentPoint": "03091f4290ff7886dbd3cc01ee02a9a3e6501587ccdcec6d95ae49cb307c80207c"
            },
            "remoteNextCommitInfo": {
              "case": "Revoked",
              "fields": [
                "023bf2af16d8a8726188db129c6410d365ef1d7070a6d94d272155915dbb3e4cc6"
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
                  "03a291695ef49daf5aecea435e79968bc42154a286ce67ad39512b662a2ec622b5"
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
                "fundingPubKey": "02276692862e087e4682aaa7fd8e89fafb87f1f9bcce81df7785ad9661bd828716",
                "revocationBasepoint": "029eb3242f1b33261fddd39f69af7ae5d9491246460a13cf35c4b15e86daab5cdb",
                "paymentBasepoint": "03b666bc3c17d3d0958782320076b8ac4e6ec93704eac0b014e647e993cf529dac",
                "delayedPaymentBasepoint": "032526f9e176fe4af89cfebc608cfb6ec42def39dcd2ceda86dca76a30cad96084",
                "htlcBasepoint": "0245372a6713e326437d3cc40e4cf41efcd1f038c91edb8bbde72dc98d409edad5"
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
                "3044022026de8e8705109bf29d60886c95e263269668a4a86bacd4fe4410e379d7dab77e022052f48eb574b592a3b0d8d16892a05e2cd857a343b35abd30f80af83f32d925f0"
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
              "timestamp@": 1605607826,
              "messageFlags@": 0,
              "channelFlags@": 3,
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
              "timestamp": 1605607826,
              "messageFlags": 0,
              "channelFlags": 3,
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
                "3044022026de8e8705109bf29d60886c95e263269668a4a86bacd4fe4410e379d7dab77e022052f48eb574b592a3b0d8d16892a05e2cd857a343b35abd30f80af83f32d925f0"
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
              "timestamp@": 1605607826,
              "messageFlags@": 0,
              "channelFlags@": 3,
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
              "timestamp": 1605607826,
              "messageFlags": 0,
              "channelFlags": 3,
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
            "isNode1": false
          },
          "localShutdown": null,
          "remoteShutdown": null,
          "channelId": {
            "case": "ChannelId",
            "fields": [
              "6e38deed8bd6094150060cddd5cb88ce759c2094eae1ba91253b2580935f6a1a"
            ]
          }
        }
      ]
    },
    "accountFileName": "023e263e03cde681d1e53cf5c8e2d8152e1f067a20ef175fb52f14033e2a55a2a4",
    "counterpartyIP": [
      "127.0.0.1",
      52626
    ],
    "remoteNodeId": {
      "case": "NodeId",
      "fields": [
        "03a291695ef49daf5aecea435e79968bc42154a286ce67ad39512b662a2ec622b5"
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
            Marshalling.SerializeCustom
                serializedChannel
                (SerializedChannel.LightningSerializerSettings Currency.BTC)
                Newtonsoft.Json.Formatting.Indented
        if serializedChannelJson.Trim() <> reserializedChannelJson then
            failwith ("deserializing and reserializing a channel changed the json:\n" + reserializedChannelJson)



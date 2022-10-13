namespace GWallet.Backend.Tests.Unit

open NUnit.Framework

open GWallet.Backend
open GWallet.Backend.UtxoCoin.Lightning

[<TestFixture>]
type ChannelMarshalling () =
    let serializedChannelJson = """
{
  "version": "{version}",
  "typeName": "GWallet.Backend.UtxoCoin.Lightning.SerializedChannel",
  "value": {
    "channelIndex": 712266779,
    "savedChannelState": {
      "staticChannelConfig": {
        "announceChannel": false,
        "remoteNodeId": {
          "case": "NodeId",
          "fields": [
            "03394a72b8256a110c589a116d18ac22087ee23178ecb39d71a4c0bdd9aec28c16"
          ]
        },
        "network": "RegTest",
        "fundingTxMinimumDepth": {
          "case": "BlockHeightOffset32",
          "fields": [
            3
          ]
        },
        "localStaticShutdownScriptPubKey": {
          "case": "Some",
          "fields": [
            "a914e8a40a255aba66e5d3dab807054ecdbf17cb2f8b87"
          ]
        },
        "remoteStaticShutdownScriptPubKey": null,
        "isFunder": true,
        "fundingScriptCoin": {
          "isP2SH": false,
          "redeemType": 1,
          "redeem": "5221033c53bb43e3e05883e7b8192880466bada0569f06a23cf98406cd10530fc8bdec2103f16ef5959b54c358eadd94689edbd4b62096b8d1a772ea1af91b3a4c4c42267d52ae",
          "canGetScriptCode": true,
          "isMalleable": false,
          "outpoint": "2f1f1d004f9de2237bb2167f0a113aa18bef17aeaaf544a9e1e8055b46a8177800000000",
          "txOut": {
            "scriptPubKey": "00209a3fbc31f6802dd50fa6b2d4877f536676e86a72ed1f486e7b3d92f27fe26e2d",
            "value": 10000000
          },
          "amount": 10000000,
          "scriptPubKey": "00209a3fbc31f6802dd50fa6b2d4877f536676e86a72ed1f486e7b3d92f27fe26e2d"
        },
        "localParams": {
          "dustLimitSatoshis": 354,
          "maxHTLCValueInFlightMSat": {
            "case": "LNMoney",
            "fields": [
              10000000000
            ]
          },
          "channelReserveSatoshis": 100000,
          "htlcMinimumMSat": {
            "case": "LNMoney",
            "fields": [
              1000
            ]
          },
          "toSelfDelay": {
            "case": "BlockHeightOffset16",
            "fields": [
              2
            ]
          },
          "maxAcceptedHTLCs": 10,
          "features": "10000000001000100000010"
        },
        "remoteParams": {
          "dustLimitSatoshis": 354,
          "maxHTLCValueInFlightMSat": {
            "case": "LNMoney",
            "fields": [
              9900000000
            ]
          },
          "channelReserveSatoshis": 100000,
          "htlcMinimumMSat": {
            "case": "LNMoney",
            "fields": [
              1
            ]
          },
          "toSelfDelay": {
            "case": "BlockHeightOffset16",
            "fields": [
              1201
            ]
          },
          "maxAcceptedHTLCs": 483,
          "features": "10000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001000000000000010000000100000100101001010100001"
        },
        "remoteChannelPubKeys": {
          "fundingPubKey": {
            "case": "FundingPubKey",
            "fields": [
              "03f16ef5959b54c358eadd94689edbd4b62096b8d1a772ea1af91b3a4c4c42267d"
            ]
          },
          "revocationBasepoint": {
            "case": "RevocationBasepoint",
            "fields": [
              "03246aaff22fef0d7620fe2befe77d02163ecf117732caad3e2348fca37cb75ea9"
            ]
          },
          "paymentBasepoint": {
            "case": "PaymentBasepoint",
            "fields": [
              "03f266626694ed558db0e75adf58c466e69e7041417fb24bdef69b09bd6d749caa"
            ]
          },
          "delayedPaymentBasepoint": {
            "case": "DelayedPaymentBasepoint",
            "fields": [
              "03652d01d282b607ac39da86105f7cc420d6a23d21bf5fe1a5f4bef0ef91435b2a"
            ]
          },
          "htlcBasepoint": {
            "case": "HtlcBasepoint",
            "fields": [
              "03e2392d50fe1c8be20493155f7f30b218f8d2c50ce7e5975415121ad01add5779"
            ]
          }
        },
        "localChannelPubKeys": {
          "fundingPubKey": {
            "case": "FundingPubKey",
            "fields": [
              "033c53bb43e3e05883e7b8192880466bada0569f06a23cf98406cd10530fc8bdec"
            ]
          },
          "revocationBasepoint": {
            "case": "RevocationBasepoint",
            "fields": [
              "03ea342c09ddb0ad5d07af09b7b5bf4f974688ca4f1578a3713cab57c0767ef53c"
            ]
          },
          "paymentBasepoint": {
            "case": "PaymentBasepoint",
            "fields": [
              "029669cdbdfedaa5d433cfd928a2c586891b9773b261792fada24292244a74ab72"
            ]
          },
          "delayedPaymentBasepoint": {
            "case": "DelayedPaymentBasepoint",
            "fields": [
              "02acaf755e05582437f85aca2505e6aa28fe6884f44e0d84d0ecdb6cebf5ff497a"
            ]
          },
          "htlcBasepoint": {
            "case": "HtlcBasepoint",
            "fields": [
              "02c9d36c4b72310d8c8981ffd5891f3a4960b2db73c28cbcb1cbb57206ae0cb25b"
            ]
          }
        },
        "type": {
          "case": "Anchor"
        }
      },
      "remotePerCommitmentSecrets": [],
      "historicalLocalCommits": {
        "71115f8f09d9a02bb9a310b38ef143dc795f22c8f405d36ca902cdccf2e6c0e6": {
          "index": 0,
          "perCommitmentPoint": {
            "case": "PerCommitmentPoint",
            "fields": [
              "024937d9f4a106e62525b9b9bcfee02b5dac100b2a61800be83dae2f66d2763e6f"
            ]
          },
          "spec": {
            "outgoingHTLCs": {},
            "incomingHTLCs": {},
            "feeRatePerKw": {
              "case": "FeeRatePerKw",
              "fields": [
                5000
              ]
            },
            "toLocal": {
              "case": "LNMoney",
              "fields": [
                10000000000
              ]
            },
            "toRemote": {
              "case": "LNMoney",
              "fields": [
                0
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
                  "totalOut": 9994050,
                  "lockTime": 539122062,
                  "inputs": [
                    {
                      "sequence": 2158724978,
                      "prevOut": "2f1f1d004f9de2237bb2167f0a113aa18bef17aeaaf544a9e1e8055b46a8177800000000",
                      "scriptSig": "",
                      "witScript": "040047304402201e647fdedd1198326d06829eb8948b161452e5b2711cfcebbdb7b2f73d021f3d02205d2a1fb518deed9d38f1c84cb49611cac6e44aa7710e595148dc527487c6495a01483045022100c0971de70e9bd5e1412119794d78b17ea0ba8c2cd6ccf8afbbf1e2110b0387a30220383ace466207117f36cecf0397d56e4f11cf732cc7e2346649c852f8341c54e001475221033c53bb43e3e05883e7b8192880466bada0569f06a23cf98406cd10530fc8bdec2103f16ef5959b54c358eadd94689edbd4b62096b8d1a772ea1af91b3a4c4c42267d52ae",
                      "isFinal": false
                    }
                  ],
                  "outputs": [
                    {
                      "scriptPubKey": "00206b3e21d3a254ef7b3f1d0b344252474f10088be6490d1deac8b0f0e7da3b03ad",
                      "value": 330
                    },
                    {
                      "scriptPubKey": "00207e3b6ef0353ae32061d92d0469414cc69bb947de0e694fa978acf9b4a5d2102c",
                      "value": 9993720
                    }
                  ],
                  "isCoinBase": false,
                  "hasWitness": true
                }
              ]
            }
          },
          "incomingHtlcTxRemoteSigs": {},
          "outgoingHtlcTxRemoteSigs": {}
        }
      },
      "historicalRemoteCommits": {
        "740888a0361671b1f0fafd4fd05089b41441095cdddce7c1248adc873584cd37": {
          "index": 0,
          "spec": {
            "outgoingHTLCs": {},
            "incomingHTLCs": {},
            "feeRatePerKw": {
              "case": "FeeRatePerKw",
              "fields": [
                5000
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
                10000000000
              ]
            }
          },
          "txId": {
            "case": "TxId",
            "fields": [
              "740888a0361671b1f0fafd4fd05089b41441095cdddce7c1248adc873584cd37"
            ]
          },
          "remotePerCommitmentPoint": {
            "case": "PerCommitmentPoint",
            "fields": [
              "03c3c978b5a8753f3bf18c6bc95af061d5d8f260ab76f3598a7ddc1df51c548ee5"
            ]
          },
          "sentAfterLocalCommitIndex": 0
        }
      },
      "shortChannelId": {
        "case": "Some",
        "fields": [
          {
            "blockHeight": {
              "case": "BlockHeight",
              "fields": [
                110
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
            "asString": "110x1x0"
          }
        ]
      },
      "localCommit": {
        "index": 0,
        "perCommitmentPoint": {
          "case": "PerCommitmentPoint",
          "fields": [
            "024937d9f4a106e62525b9b9bcfee02b5dac100b2a61800be83dae2f66d2763e6f"
          ]
        },
        "spec": {
          "outgoingHTLCs": {},
          "incomingHTLCs": {},
          "feeRatePerKw": {
            "case": "FeeRatePerKw",
            "fields": [
              5000
            ]
          },
          "toLocal": {
            "case": "LNMoney",
            "fields": [
              10000000000
            ]
          },
          "toRemote": {
            "case": "LNMoney",
            "fields": [
              0
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
                "totalOut": 9994050,
                "lockTime": 539122062,
                "inputs": [
                  {
                    "sequence": 2158724978,
                    "prevOut": "2f1f1d004f9de2237bb2167f0a113aa18bef17aeaaf544a9e1e8055b46a8177800000000",
                    "scriptSig": "",
                    "witScript": "040047304402201e647fdedd1198326d06829eb8948b161452e5b2711cfcebbdb7b2f73d021f3d02205d2a1fb518deed9d38f1c84cb49611cac6e44aa7710e595148dc527487c6495a01483045022100c0971de70e9bd5e1412119794d78b17ea0ba8c2cd6ccf8afbbf1e2110b0387a30220383ace466207117f36cecf0397d56e4f11cf732cc7e2346649c852f8341c54e001475221033c53bb43e3e05883e7b8192880466bada0569f06a23cf98406cd10530fc8bdec2103f16ef5959b54c358eadd94689edbd4b62096b8d1a772ea1af91b3a4c4c42267d52ae",
                    "isFinal": false
                  }
                ],
                "outputs": [
                  {
                    "scriptPubKey": "00206b3e21d3a254ef7b3f1d0b344252474f10088be6490d1deac8b0f0e7da3b03ad",
                    "value": 330
                  },
                  {
                    "scriptPubKey": "00207e3b6ef0353ae32061d92d0469414cc69bb947de0e694fa978acf9b4a5d2102c",
                    "value": 9993720
                  }
                ],
                "isCoinBase": false,
                "hasWitness": true
              }
            ]
          }
        },
        "incomingHtlcTxRemoteSigs": {},
        "outgoingHtlcTxRemoteSigs": {}
      },
      "remoteCommit": {
        "index": 0,
        "spec": {
          "outgoingHTLCs": {},
          "incomingHTLCs": {},
          "feeRatePerKw": {
            "case": "FeeRatePerKw",
            "fields": [
              5000
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
              10000000000
            ]
          }
        },
        "txId": {
          "case": "TxId",
          "fields": [
            "740888a0361671b1f0fafd4fd05089b41441095cdddce7c1248adc873584cd37"
          ]
        },
        "remotePerCommitmentPoint": {
          "case": "PerCommitmentPoint",
          "fields": [
            "03c3c978b5a8753f3bf18c6bc95af061d5d8f260ab76f3598a7ddc1df51c548ee5"
          ]
        },
        "sentAfterLocalCommitIndex": 0
      },
      "localChanges": {
        "signed": [],
        "acKed": []
      },
      "remoteChanges": {
        "signed": [],
        "acKed": []
      },
      "remoteCurrentPerCommitmentPoint": null
    },
    "commitments": {
      "proposedLocalChanges": [],
      "proposedRemoteChanges": [],
      "localNextHTLCId": {
        "case": "HTLCId",
        "fields": [
          0
        ]
      },
      "remoteNextHTLCId": {
        "case": "HTLCId",
        "fields": [
          0
        ]
      },
      "originChannels": {}
    },
    "remoteNextCommitInfo": {
      "case": "Some",
      "fields": [
        {
          "case": "Revoked",
          "fields": [
            {
              "case": "PerCommitmentPoint",
              "fields": [
                "034e075503d856199b38fd1397a54418165b3fbb35d40c68dc2ddad148c714611a"
              ]
            }
          ]
        }
      ]
    },
    "negotiatingState": {
      "localRequestedShutdown": null,
      "remoteRequestedShutdown": null,
      "localClosingFeesProposed": [],
      "remoteClosingFeeProposed": null
    },
    "accountFileName": "02d16190f2ebba8678a98c1fdcf020766a7ea6a9768a46e704056db1b3cdf28dee",
    "forceCloseTxIdOpt": null,
    "localChannelPubKeys": {
      "fundingPubKey": {
        "case": "FundingPubKey",
        "fields": [
          "033c53bb43e3e05883e7b8192880466bada0569f06a23cf98406cd10530fc8bdec"
        ]
      },
      "revocationBasepoint": {
        "case": "RevocationBasepoint",
        "fields": [
          "03ea342c09ddb0ad5d07af09b7b5bf4f974688ca4f1578a3713cab57c0767ef53c"
        ]
      },
      "paymentBasepoint": {
        "case": "PaymentBasepoint",
        "fields": [
          "029669cdbdfedaa5d433cfd928a2c586891b9773b261792fada24292244a74ab72"
        ]
      },
      "delayedPaymentBasepoint": {
        "case": "DelayedPaymentBasepoint",
        "fields": [
          "02acaf755e05582437f85aca2505e6aa28fe6884f44e0d84d0ecdb6cebf5ff497a"
        ]
      },
      "htlcBasepoint": {
        "case": "HtlcBasepoint",
        "fields": [
          "02c9d36c4b72310d8c8981ffd5891f3a4960b2db73c28cbcb1cbb57206ae0cb25b"
        ]
      }
    },
    "mainBalanceRecoveryStatus": {
      "case": "Unresolved"
    },
    "htlcDelayedTxs": [],
    "broadcastedHtlcTxs": [],
    "broadcastedHtlcRecoveryTxs": [],
    "nodeTransportType": {
      "case": "Server",
      "fields": [
        {
          "case": "Tcp",
          "fields": [
            [
              "127.0.0.1",
              9735
            ]
          ]
        }
      ]
    },
    "closingTimestampUtc": null
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
            Marshalling.SerializeCustom (
                serializedChannel,
                SerializedChannel.LightningSerializerSettings Currency.BTC,
                Marshalling.DefaultFormatting
            )
        Assert.That(reserializedChannelJson |> MarshallingData.Sanitize,
                    Is.EqualTo (serializedChannelJson.Trim() |> MarshallingData.Sanitize))


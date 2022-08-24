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
    "channelIndex": 244773690,
    "savedChannelState": {
      "staticChannelConfig": {
        "announceChannel": false,
        "remoteNodeId": {
          "case": "NodeId",
          "fields": [
            "02c2c2b76a215d9acc3ec100abd6f6efa60797eb941ae3bd9df7c82cc97732beab"
          ]
        },
        "network": "RegTest",
        "fundingTxMinimumDepth": {
          "case": "BlockHeightOffset32",
          "fields": [
            2
          ]
        },
        "localStaticShutdownScriptPubKey": {
          "case": "Some",
          "fields": [
            "a914684e8b042ae0437a9666e5ef695856208379713e87"
          ]
        },
        "remoteStaticShutdownScriptPubKey": {
          "case": "Some",
          "fields": [
            "a91488d22420f8d76ae5e48e3894385fa84a2ee4594d87"
          ]
        },
        "isFunder": false,
        "fundingScriptCoin": {
          "isP2SH": false,
          "redeemType": 1,
          "redeem": "522102c50cf6d17bf83be3a2f8d4b3f9541ae6ca037445ab490e64f395a1d85f54023b2103f099d2f4ca7aa42652f3ddc7a9c6ecbfa5512e8b189fd3be8d50086837a55b2452ae",
          "canGetScriptCode": true,
          "isMalleable": false,
          "outpoint": "d3a9d4a9f81c5005dad93ece2f8c0ca8ad6e0258bbd8cff85a9b09712a22cf1700000000",
          "txOut": {
            "scriptPubKey": "0020cada82da7ed6cbe831db4727164aaaf60e592ccf3ebebd5ef41d3eeca42732bf",
            "value": 10000000
          },
          "amount": 10000000,
          "scriptPubKey": "0020cada82da7ed6cbe831db4727164aaaf60e592ccf3ebebd5ef41d3eeca42732bf"
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
          "features": "010000000001000100000010"
        },
        "remoteChannelPubKeys": {
          "fundingPubKey": {
            "case": "FundingPubKey",
            "fields": [
              "02c50cf6d17bf83be3a2f8d4b3f9541ae6ca037445ab490e64f395a1d85f54023b"
            ]
          },
          "revocationBasepoint": {
            "case": "RevocationBasepoint",
            "fields": [
              "029ab034be7720cd9348eeee7d54dafa30add0076164357ea5c31a4ae0ffc2eaf6"
            ]
          },
          "paymentBasepoint": {
            "case": "PaymentBasepoint",
            "fields": [
              "03ebf10fae384093d65cb3a1b6b54d7cc13747c5e1b991b4571da7d39e6e4b94de"
            ]
          },
          "delayedPaymentBasepoint": {
            "case": "DelayedPaymentBasepoint",
            "fields": [
              "0229d0d325dd831c9354bf6528d879ba4511e0006774abe937704fa870f7ed0f25"
            ]
          },
          "htlcBasepoint": {
            "case": "HtlcBasepoint",
            "fields": [
              "02ce5c132f98a34e1c30c5321c2e645ae1c27ec6a09a02de4d10bd9be7fb15d459"
            ]
          }
        },
        "localChannelPubKeys": {
          "fundingPubKey": {
            "case": "FundingPubKey",
            "fields": [
              "03f099d2f4ca7aa42652f3ddc7a9c6ecbfa5512e8b189fd3be8d50086837a55b24"
            ]
          },
          "revocationBasepoint": {
            "case": "RevocationBasepoint",
            "fields": [
              "0264ec9bb3edff39f22f136375e0f19ef351f675e79b4194da4b515ffb0a7dd439"
            ]
          },
          "paymentBasepoint": {
            "case": "PaymentBasepoint",
            "fields": [
              "03a72a22006b8ed77ea71e411c4ec28c7b13831b5e19a69453bb497c905729676d"
            ]
          },
          "delayedPaymentBasepoint": {
            "case": "DelayedPaymentBasepoint",
            "fields": [
              "0378828c7f5ad4fdad208a6ef8c28a04d3eb25de818da87a932cc2a48cc2dd8f31"
            ]
          },
          "htlcBasepoint": {
            "case": "HtlcBasepoint",
            "fields": [
              "032758d33a3ef2f2df68114cf6ae6899bb27e32b7039639a8daf27af0161f611c6"
            ]
          }
        },
        "type": {
          "case": "Anchor"
        }
      },
      "remotePerCommitmentSecrets": [],
      "historicalLocalCommits": {
        "1af5826757294f310610a524445cc32030c66920e80a19cb76274256b47c15df": {
          "index": 0,
          "perCommitmentPoint": {
            "case": "PerCommitmentPoint",
            "fields": [
              "03405b92bc8e209b4a122c9da42d4fe0ab9d90b92002dc1b8bd2683e76a7b83403"
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
          "publishableTxs": {
            "commitTx": {
              "case": "FinalizedTx",
              "fields": [
                {
                  "rbf": true,
                  "version": 2,
                  "totalOut": 9994050,
                  "lockTime": 543945744,
                  "inputs": [
                    {
                      "sequence": 2163994788,
                      "prevOut": "d3a9d4a9f81c5005dad93ece2f8c0ca8ad6e0258bbd8cff85a9b09712a22cf1700000000",
                      "scriptSig": "",
                      "witScript": "040047304402201394df26356c5b2a4f141fd9242be7d64fce6a9848023a46260f068daa6ad450022072f2cac634d5e69767d0144db80310a4ee17edbcde5eeea67f6f443cb1b8ab3a014730440220780fbc0baeee53bfedc0d7a49ed32795e69ddf1856a6cefdbe63277dffe4fe8002200ea2b037ee3d9f220acc9bc2a3cf9a83546f5c816d13a33d9b8d96b2be5d78350147522102c50cf6d17bf83be3a2f8d4b3f9541ae6ca037445ab490e64f395a1d85f54023b2103f099d2f4ca7aa42652f3ddc7a9c6ecbfa5512e8b189fd3be8d50086837a55b2452ae",
                      "isFinal": false
                    }
                  ],
                  "outputs": [
                    {
                      "scriptPubKey": "00204fa909b441dc7a42a1c62dc9247aecc01c576366f4f2540d7ea3e228b03ecdc7",
                      "value": 330
                    },
                    {
                      "scriptPubKey": "00208570b4745331e106f1dc29e9da50717e14331a41562b58709ddb4176654a6ecd",
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
        "e4fbfb6cd1ca820f4d76f578b5f1ff3bb37318f5a28b15d3f106f1f4f5414317": {
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
          "txId": {
            "case": "TxId",
            "fields": [
              "e4fbfb6cd1ca820f4d76f578b5f1ff3bb37318f5a28b15d3f106f1f4f5414317"
            ]
          },
          "remotePerCommitmentPoint": {
            "case": "PerCommitmentPoint",
            "fields": [
              "02e72e735ca8a51fa76793eff3f2b2c92a1df8a08541b9b44fe82f4f179188bcb0"
            ]
          }
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
            "03405b92bc8e209b4a122c9da42d4fe0ab9d90b92002dc1b8bd2683e76a7b83403"
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
        "publishableTxs": {
          "commitTx": {
            "case": "FinalizedTx",
            "fields": [
              {
                "rbf": true,
                "version": 2,
                "totalOut": 9994050,
                "lockTime": 543945744,
                "inputs": [
                  {
                    "sequence": 2163994788,
                    "prevOut": "d3a9d4a9f81c5005dad93ece2f8c0ca8ad6e0258bbd8cff85a9b09712a22cf1700000000",
                    "scriptSig": "",
                    "witScript": "040047304402201394df26356c5b2a4f141fd9242be7d64fce6a9848023a46260f068daa6ad450022072f2cac634d5e69767d0144db80310a4ee17edbcde5eeea67f6f443cb1b8ab3a014730440220780fbc0baeee53bfedc0d7a49ed32795e69ddf1856a6cefdbe63277dffe4fe8002200ea2b037ee3d9f220acc9bc2a3cf9a83546f5c816d13a33d9b8d96b2be5d78350147522102c50cf6d17bf83be3a2f8d4b3f9541ae6ca037445ab490e64f395a1d85f54023b2103f099d2f4ca7aa42652f3ddc7a9c6ecbfa5512e8b189fd3be8d50086837a55b2452ae",
                    "isFinal": false
                  }
                ],
                "outputs": [
                  {
                    "scriptPubKey": "00204fa909b441dc7a42a1c62dc9247aecc01c576366f4f2540d7ea3e228b03ecdc7",
                    "value": 330
                  },
                  {
                    "scriptPubKey": "00208570b4745331e106f1dc29e9da50717e14331a41562b58709ddb4176654a6ecd",
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
        "txId": {
          "case": "TxId",
          "fields": [
            "e4fbfb6cd1ca820f4d76f578b5f1ff3bb37318f5a28b15d3f106f1f4f5414317"
          ]
        },
        "remotePerCommitmentPoint": {
          "case": "PerCommitmentPoint",
          "fields": [
            "02e72e735ca8a51fa76793eff3f2b2c92a1df8a08541b9b44fe82f4f179188bcb0"
          ]
        }
      },
      "localChanges": {
        "signed": [],
        "acKed": []
      },
      "remoteChanges": {
        "signed": [],
        "acKed": []
      }
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
                "035c31dac7c37cb1d44c7532ef3a8c47b8bd0dafdfa6548d98e20ec2c9fd8840fb"
              ]
            }
          ]
        }
      ]
    },
    "negotiatingState": {
      "localRequestedShutdown": {
        "case": "Some",
        "fields": [
          "a914684e8b042ae0437a9666e5ef695856208379713e87"
        ]
      },
      "remoteRequestedShutdown": {
        "case": "Some",
        "fields": [
          "a91488d22420f8d76ae5e48e3894385fa84a2ee4594d87"
        ]
      },
      "localClosingFeesProposed": [],
      "remoteClosingFeeProposed": null
    },
    "accountFileName": "02ff2db62fb931e211665825b8fd39b6b78e08ee82522778739d14a1ddca764ba7",
    "forceCloseTxIdOpt": null,
    "localChannelPubKeys": {
      "fundingPubKey": {
        "case": "FundingPubKey",
        "fields": [
          "03f099d2f4ca7aa42652f3ddc7a9c6ecbfa5512e8b189fd3be8d50086837a55b24"
        ]
      },
      "revocationBasepoint": {
        "case": "RevocationBasepoint",
        "fields": [
          "0264ec9bb3edff39f22f136375e0f19ef351f675e79b4194da4b515ffb0a7dd439"
        ]
      },
      "paymentBasepoint": {
        "case": "PaymentBasepoint",
        "fields": [
          "03a72a22006b8ed77ea71e411c4ec28c7b13831b5e19a69453bb497c905729676d"
        ]
      },
      "delayedPaymentBasepoint": {
        "case": "DelayedPaymentBasepoint",
        "fields": [
          "0378828c7f5ad4fdad208a6ef8c28a04d3eb25de818da87a932cc2a48cc2dd8f31"
        ]
      },
      "htlcBasepoint": {
        "case": "HtlcBasepoint",
        "fields": [
          "032758d33a3ef2f2df68114cf6ae6899bb27e32b7039639a8daf27af0161f611c6"
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
    "closingTimestampUtc": {
      "case": "Some",
      "fields": [
        1661342681
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
            Marshalling.SerializeCustom (
                serializedChannel,
                SerializedChannel.LightningSerializerSettings Currency.BTC,
                Marshalling.DefaultFormatting
            )
        Assert.That(reserializedChannelJson |> MarshallingData.Sanitize,
                    Is.EqualTo (serializedChannelJson.Trim() |> MarshallingData.Sanitize))


namespace GWallet.Backend.Tests.Unit

open NUnit.Framework

open GWallet.Backend
open GWallet.Backend.UtxoCoin.Lightning

[<TestFixture>]
type ChannelMarshalling () =
    let serializedChannelJson = """
{
  "version": "0.3.255.0",
  "typeName": "GWallet.Backend.UtxoCoin.Lightning.SerializedChannel",
  "value": {
    "channelIndex": 259788224,
    "savedChannelState": {
      "staticChannelConfig": {
        "announceChannel": false,
        "remoteNodeId": {
          "case": "NodeId",
          "fields": [
            "02a47396aac0ba1c802861c96ab6e9ac118d7d027b2f204dd312d22d239f8e593d"
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
            "a91405ec9cf390787d6f54390f554225abdf3fdee2ac87"
          ]
        },
        "isFunder": false,
        "fundingScriptCoin": {
          "isP2SH": false,
          "redeemType": 1,
          "redeem": "5221023f2ffd015c7b39f948e48e8cde2032a68c4918f6a704a0d5cc8705f3c7a7a23a2103afd1a43b1982ca2834ebd5c9751bd521f92afb5317ae2587d2f10c7ba793692652ae",
          "canGetScriptCode": true,
          "isMalleable": false,
          "outpoint": "4f3cfdaba41e3923da47fa680f93d3af9e77e75d28478afb44b99e90a3b1703801000000",
          "txOut": {
            "scriptPubKey": "002039b696421cfed30a33a980bcdf48acdb1e1be37f3950b9ababc676decfa1abad",
            "value": 10000000
          },
          "amount": 10000000,
          "scriptPubKey": "002039b696421cfed30a33a980bcdf48acdb1e1be37f3950b9ababc676decfa1abad"
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
              "023f2ffd015c7b39f948e48e8cde2032a68c4918f6a704a0d5cc8705f3c7a7a23a"
            ]
          },
          "revocationBasepoint": {
            "case": "RevocationBasepoint",
            "fields": [
              "027876519d63fc443795d9342a73a3dacf089a8a47601ad428c99cf80e66cbca39"
            ]
          },
          "paymentBasepoint": {
            "case": "PaymentBasepoint",
            "fields": [
              "02efc3e27b39c70d426ecaf486c7efb2ebdc0258404c04e4de0e81c9c78c241c50"
            ]
          },
          "delayedPaymentBasepoint": {
            "case": "DelayedPaymentBasepoint",
            "fields": [
              "033ecbef2fe4190a27846372ee4a7b6c2d4bf7d50731ad96589bd4a9438e6ea789"
            ]
          },
          "htlcBasepoint": {
            "case": "HtlcBasepoint",
            "fields": [
              "03b927b3dcb5ddec8a1e8b983325b458d9d8d5f96bb576646643b0c9e1968e109f"
            ]
          }
        },
        "localChannelPubKeys": {
          "fundingPubKey": {
            "case": "FundingPubKey",
            "fields": [
              "03afd1a43b1982ca2834ebd5c9751bd521f92afb5317ae2587d2f10c7ba7936926"
            ]
          },
          "revocationBasepoint": {
            "case": "RevocationBasepoint",
            "fields": [
              "0352eb6952deb64a7e59d1018dbd6851d4843929147241bcde191c04479aa0f69e"
            ]
          },
          "paymentBasepoint": {
            "case": "PaymentBasepoint",
            "fields": [
              "027c62389a5a71427d3b2f23fb0f0b1a4cf16941c4d791bc8c6c7f167f057fa690"
            ]
          },
          "delayedPaymentBasepoint": {
            "case": "DelayedPaymentBasepoint",
            "fields": [
              "02128f76fc3bfc67066d4901103975f6baddba0beb7cc2c5f0c6123af24885b284"
            ]
          },
          "htlcBasepoint": {
            "case": "HtlcBasepoint",
            "fields": [
              "03ad250834d4d426d7d8ed2d2d8cd7babb94f2b022c3b61640eb98aba4abf8810d"
            ]
          }
        },
        "type": {
          "case": "Anchor"
        }
      },
      "remotePerCommitmentSecrets": [],
      "historicalLocalCommits": {
        "e053c90175ee336bee62d95acdb6cea5d0a6b675e59ee834ebbee22b7babb50a": {
          "index": 0,
          "perCommitmentPoint": {
            "case": "PerCommitmentPoint",
            "fields": [
              "0206425ad8fedd8bef8bedf2cdedbd6ace250f408a6a7377a7e0ef1d1aa6717c3e"
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
                  "lockTime": 544538115,
                  "inputs": [
                    {
                      "sequence": 2161236978,
                      "prevOut": "4f3cfdaba41e3923da47fa680f93d3af9e77e75d28478afb44b99e90a3b1703801000000",
                      "scriptSig": "",
                      "witScript": "04004730440220501373b7fd5427ffde8d3e356b9aaaf7aeb95a552f36d27f74f31b992c7062c902206aab73488debc902ce2915f4d1e597a3cc3eb4f2faf05a1811b78c3a2f78aa14014730440220257b7f77dd49f7be4019a818507c53ffed492eb42370684b85dad78a6c070b090220610de670cabb58103d9eea5985810ebf8ad3bd9e9ad27b262b0a3f1f02547fb401475221023f2ffd015c7b39f948e48e8cde2032a68c4918f6a704a0d5cc8705f3c7a7a23a2103afd1a43b1982ca2834ebd5c9751bd521f92afb5317ae2587d2f10c7ba793692652ae",
                      "isFinal": false
                    }
                  ],
                  "outputs": [
                    {
                      "scriptPubKey": "002071cacc1d1fe1841d8f2b4b4705d4bcc40e5d2c8a9c3be82ee715a9b539419502",
                      "value": 330
                    },
                    {
                      "scriptPubKey": "0020b70aa68fae5eb7d94a1c622ac880e3f79ea9dbaebf5517fa57bcdf5dbd053f06",
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
        "015c6283ecc224345d01f9a56a8a9ac54a4593f52e3dff1a6fbbb5391cf64a99": {
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
              "015c6283ecc224345d01f9a56a8a9ac54a4593f52e3dff1a6fbbb5391cf64a99"
            ]
          },
          "remotePerCommitmentPoint": {
            "case": "PerCommitmentPoint",
            "fields": [
              "039aa8dc16c1d5a876bd3d269caa500e8cc185299893e60930fdba776363e436aa"
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
                1
              ]
            },
            "asString": "110x1x1"
          }
        ]
      },
      "localCommit": {
        "index": 0,
        "perCommitmentPoint": {
          "case": "PerCommitmentPoint",
          "fields": [
            "0206425ad8fedd8bef8bedf2cdedbd6ace250f408a6a7377a7e0ef1d1aa6717c3e"
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
                "lockTime": 544538115,
                "inputs": [
                  {
                    "sequence": 2161236978,
                    "prevOut": "4f3cfdaba41e3923da47fa680f93d3af9e77e75d28478afb44b99e90a3b1703801000000",
                    "scriptSig": "",
                    "witScript": "04004730440220501373b7fd5427ffde8d3e356b9aaaf7aeb95a552f36d27f74f31b992c7062c902206aab73488debc902ce2915f4d1e597a3cc3eb4f2faf05a1811b78c3a2f78aa14014730440220257b7f77dd49f7be4019a818507c53ffed492eb42370684b85dad78a6c070b090220610de670cabb58103d9eea5985810ebf8ad3bd9e9ad27b262b0a3f1f02547fb401475221023f2ffd015c7b39f948e48e8cde2032a68c4918f6a704a0d5cc8705f3c7a7a23a2103afd1a43b1982ca2834ebd5c9751bd521f92afb5317ae2587d2f10c7ba793692652ae",
                    "isFinal": false
                  }
                ],
                "outputs": [
                  {
                    "scriptPubKey": "002071cacc1d1fe1841d8f2b4b4705d4bcc40e5d2c8a9c3be82ee715a9b539419502",
                    "value": 330
                  },
                  {
                    "scriptPubKey": "0020b70aa68fae5eb7d94a1c622ac880e3f79ea9dbaebf5517fa57bcdf5dbd053f06",
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
            "015c6283ecc224345d01f9a56a8a9ac54a4593f52e3dff1a6fbbb5391cf64a99"
          ]
        },
        "remotePerCommitmentPoint": {
          "case": "PerCommitmentPoint",
          "fields": [
            "039aa8dc16c1d5a876bd3d269caa500e8cc185299893e60930fdba776363e436aa"
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
                "03ef16e8291ede53034f79307003f1f0c73bc4e7bc3a212ff2ab72aa7235f3596d"
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
          "a91405ec9cf390787d6f54390f554225abdf3fdee2ac87"
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
          "03afd1a43b1982ca2834ebd5c9751bd521f92afb5317ae2587d2f10c7ba7936926"
        ]
      },
      "revocationBasepoint": {
        "case": "RevocationBasepoint",
        "fields": [
          "0352eb6952deb64a7e59d1018dbd6851d4843929147241bcde191c04479aa0f69e"
        ]
      },
      "paymentBasepoint": {
        "case": "PaymentBasepoint",
        "fields": [
          "027c62389a5a71427d3b2f23fb0f0b1a4cf16941c4d791bc8c6c7f167f057fa690"
        ]
      },
      "delayedPaymentBasepoint": {
        "case": "DelayedPaymentBasepoint",
        "fields": [
          "02128f76fc3bfc67066d4901103975f6baddba0beb7cc2c5f0c6123af24885b284"
        ]
      },
      "htlcBasepoint": {
        "case": "HtlcBasepoint",
        "fields": [
          "03ad250834d4d426d7d8ed2d2d8cd7babb94f2b022c3b61640eb98aba4abf8810d"
        ]
      }
    },
    "recoveryTxIdOpt": null,
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
        1653907861
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


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
    "channelIndex": 413789112,
    "savedChannelState": {
      "staticChannelConfig": {
        "announceChannel": true,
        "remoteNodeId": {
          "case": "NodeId",
          "fields": [
            "022edc400bad121619304bf5b09095c677aa9ae7ec1a6d4c4129bbaf18df38ad61"
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
            "a914b10069ee748761bfcf452b1435343f1c47c2adbe87"
          ]
        },
        "remoteStaticShutdownScriptPubKey": null,
        "isFunder": false,
        "fundingScriptCoin": {
          "isP2SH": false,
          "redeemType": 1,
          "redeem": "52210278b51d813f11e02b8515bd466a554b8ab15b0d6e1572d5231821c14ef01d4fb321027b7a9fbce40561c98954af6e6cb1515e6794572f13b5e119719bc7a522eca70252ae",
          "canGetScriptCode": true,
          "isMalleable": false,
          "outpoint": "d36259ee3de9ab58453a13f844c6941e81e665b6ecb5f07808ab048c285733c300000000",
          "txOut": {
            "scriptPubKey": "002063c26c37c68b09c9ec2ac2021a94a103cf32bd32e1b15e559cbbaa948a6e6301",
            "value": 10000000
          },
          "amount": 10000000,
          "scriptPubKey": "002063c26c37c68b09c9ec2ac2021a94a103cf32bd32e1b15e559cbbaa948a6e6301"
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
              "027b7a9fbce40561c98954af6e6cb1515e6794572f13b5e119719bc7a522eca702"
            ]
          },
          "revocationBasepoint": {
            "case": "RevocationBasepoint",
            "fields": [
              "035b0a44c37faba81561469488d5b44e7fa7bfcb70787ea3d25290426d424aebee"
            ]
          },
          "paymentBasepoint": {
            "case": "PaymentBasepoint",
            "fields": [
              "02a5915414e21bb2b8f59fb8d100b1a2e9a665e07525d001f50e1a78c3ade1a365"
            ]
          },
          "delayedPaymentBasepoint": {
            "case": "DelayedPaymentBasepoint",
            "fields": [
              "02a22ae67688f8e664c8381d91c38ee69707d2b5fc9254cf0e897101b4925217b9"
            ]
          },
          "htlcBasepoint": {
            "case": "HtlcBasepoint",
            "fields": [
              "0285506bdb830dfb19b2fb835759b9730e90fe338e3bb59fdb7fa1e0b3af13bf24"
            ]
          }
        },
        "localChannelPubKeys": {
          "fundingPubKey": {
            "case": "FundingPubKey",
            "fields": [
              "0278b51d813f11e02b8515bd466a554b8ab15b0d6e1572d5231821c14ef01d4fb3"
            ]
          },
          "revocationBasepoint": {
            "case": "RevocationBasepoint",
            "fields": [
              "02d89b94c8c009aa414c6abb435803abe1bb0b56d2ec2007659350d2fb649a44a9"
            ]
          },
          "paymentBasepoint": {
            "case": "PaymentBasepoint",
            "fields": [
              "038824f37e514560681ce2b70cd729a882e71cd7739e1539579093b855a1e13009"
            ]
          },
          "delayedPaymentBasepoint": {
            "case": "DelayedPaymentBasepoint",
            "fields": [
              "03bf10a934f886f25f9e423d252ddc0cf649398939dc057ead462984b9948bd423"
            ]
          },
          "htlcBasepoint": {
            "case": "HtlcBasepoint",
            "fields": [
              "0355245904d1a9abdf129ec8dcedc59ca25913cc157e607db09fce39ad8c7422b7"
            ]
          }
        },
        "type": {
          "case": "Anchor"
        }
      },
      "remotePerCommitmentSecrets": [],
      "historicalLocalCommits": {
        "57493d56a2d7e784a12fb499813cf733fd110adeb2ded44597a23e87d4601342": {
          "index": 0,
          "perCommitmentPoint": {
            "case": "PerCommitmentPoint",
            "fields": [
              "03a41f2d5ff34b881b1e466a2fc6f4660351c003a4ee771d332f769e66536d29e8"
            ]
          },
          "spec": {
            "outgoingHTLCs": {},
            "incomingHTLCs": {},
            "feeRatePerKw": {
              "case": "FeeRatePerKw",
              "fields": [
                2500
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
                  "totalOut": 9996860,
                  "lockTime": 547926825,
                  "inputs": [
                    {
                      "sequence": 2162075220,
                      "prevOut": "d36259ee3de9ab58453a13f844c6941e81e665b6ecb5f07808ab048c285733c300000000",
                      "scriptSig": "",
                      "witScript": "0400473044022041716ed501394d27f72552e77137e1fc2e0c07a5f8f376ff1c95eab676c000b00220590fec18dd570f681466999e27794e14ca96a70136293671739a68aafcdf54e80147304402204a19a7d56ac34cb5df5abb609d5dd3dbad16009896cfb697e10837fd9c4c98c8022011c1e8014f78b3e9a6fa7c37c9fd2feb545f78a5a8e9b3c176d11b8863e62717014752210278b51d813f11e02b8515bd466a554b8ab15b0d6e1572d5231821c14ef01d4fb321027b7a9fbce40561c98954af6e6cb1515e6794572f13b5e119719bc7a522eca70252ae",
                      "isFinal": false
                    }
                  ],
                  "outputs": [
                    {
                      "scriptPubKey": "00201cb10cf10edfd281bb4dfb47cfdd69dfac54c63a2ff9c1538b03d9ea4e38843e",
                      "value": 330
                    },
                    {
                      "scriptPubKey": "0020e6f6d21889ee8196c5966fcd3b9ece03964d3557e1c40a5fd808321eb27db3a8",
                      "value": 9996530
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
        "936a65d4a1083fefe95d44e355a5e2744dd76e7646f8b270b2e804961df796eb": {
          "index": 0,
          "spec": {
            "outgoingHTLCs": {},
            "incomingHTLCs": {},
            "feeRatePerKw": {
              "case": "FeeRatePerKw",
              "fields": [
                2500
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
              "936a65d4a1083fefe95d44e355a5e2744dd76e7646f8b270b2e804961df796eb"
            ]
          },
          "remotePerCommitmentPoint": {
            "case": "PerCommitmentPoint",
            "fields": [
              "032202b11619602370512690aade7e096cae6798739ee3075c7210690abde473b3"
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
                103
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
            "asString": "103x1x0"
          }
        ]
      },
      "localCommit": {
        "index": 0,
        "perCommitmentPoint": {
          "case": "PerCommitmentPoint",
          "fields": [
            "03a41f2d5ff34b881b1e466a2fc6f4660351c003a4ee771d332f769e66536d29e8"
          ]
        },
        "spec": {
          "outgoingHTLCs": {},
          "incomingHTLCs": {},
          "feeRatePerKw": {
            "case": "FeeRatePerKw",
            "fields": [
              2500
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
                "totalOut": 9996860,
                "lockTime": 547926825,
                "inputs": [
                  {
                    "sequence": 2162075220,
                    "prevOut": "d36259ee3de9ab58453a13f844c6941e81e665b6ecb5f07808ab048c285733c300000000",
                    "scriptSig": "",
                    "witScript": "0400473044022041716ed501394d27f72552e77137e1fc2e0c07a5f8f376ff1c95eab676c000b00220590fec18dd570f681466999e27794e14ca96a70136293671739a68aafcdf54e80147304402204a19a7d56ac34cb5df5abb609d5dd3dbad16009896cfb697e10837fd9c4c98c8022011c1e8014f78b3e9a6fa7c37c9fd2feb545f78a5a8e9b3c176d11b8863e62717014752210278b51d813f11e02b8515bd466a554b8ab15b0d6e1572d5231821c14ef01d4fb321027b7a9fbce40561c98954af6e6cb1515e6794572f13b5e119719bc7a522eca70252ae",
                    "isFinal": false
                  }
                ],
                "outputs": [
                  {
                    "scriptPubKey": "00201cb10cf10edfd281bb4dfb47cfdd69dfac54c63a2ff9c1538b03d9ea4e38843e",
                    "value": 330
                  },
                  {
                    "scriptPubKey": "0020e6f6d21889ee8196c5966fcd3b9ece03964d3557e1c40a5fd808321eb27db3a8",
                    "value": 9996530
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
              2500
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
            "936a65d4a1083fefe95d44e355a5e2744dd76e7646f8b270b2e804961df796eb"
          ]
        },
        "remotePerCommitmentPoint": {
          "case": "PerCommitmentPoint",
          "fields": [
            "032202b11619602370512690aade7e096cae6798739ee3075c7210690abde473b3"
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
                "023b287dd9c75e83bb2dc8c27678298b608fb67ae57e45f0b3072c96f9232fa60e"
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
    "accountFileName": "02eaa6209d6d1a974b5fdfb523611a966a13ea0e9edd1ed7acccb84436a11f57fd",
    "forceCloseTxIdOpt": null,
    "localChannelPubKeys": {
      "fundingPubKey": {
        "case": "FundingPubKey",
        "fields": [
          "0278b51d813f11e02b8515bd466a554b8ab15b0d6e1572d5231821c14ef01d4fb3"
        ]
      },
      "revocationBasepoint": {
        "case": "RevocationBasepoint",
        "fields": [
          "02d89b94c8c009aa414c6abb435803abe1bb0b56d2ec2007659350d2fb649a44a9"
        ]
      },
      "paymentBasepoint": {
        "case": "PaymentBasepoint",
        "fields": [
          "038824f37e514560681ce2b70cd729a882e71cd7739e1539579093b855a1e13009"
        ]
      },
      "delayedPaymentBasepoint": {
        "case": "DelayedPaymentBasepoint",
        "fields": [
          "03bf10a934f886f25f9e423d252ddc0cf649398939dc057ead462984b9948bd423"
        ]
      },
      "htlcBasepoint": {
        "case": "HtlcBasepoint",
        "fields": [
          "0355245904d1a9abdf129ec8dcedc59ca25913cc157e607db09fce39ad8c7422b7"
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


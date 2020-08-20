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
    "accountFileName": "0284bf7562262bbd6940085748f3be6afa52ae317155181ece31b66351ccffa4b0",
    "remoteNodeId": {
      "case": "NodeId",
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



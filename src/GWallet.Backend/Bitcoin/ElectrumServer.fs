namespace GWallet.Backend.Bitcoin

open System
open System.IO
open System.Reflection

open FSharp.Data
open FSharp.Data.JsonExtensions

open GWallet.Backend

type internal ElectrumServer =
    {
        Fqdn: string;
        Pruning: string;
        PrivatePort: Option<int>;
        UnencryptedPort: Option<int>;
        Version: string;
    }

module internal ElectrumServerSeedList =

    let private defaultList =
        let assembly = Assembly.GetExecutingAssembly()
        let embeddedServerResourceName = "btc-servers.json"
        use stream = assembly.GetManifestResourceStream embeddedServerResourceName
        if (stream = null) then
            failwithf "Embedded resource %s not found" embeddedServerResourceName
        use reader = new StreamReader(stream)
        let list = reader.ReadToEnd()
        let serversParsed = JsonValue.Parse list
        let servers =
            seq {
                for (key,value) in serversParsed.Properties do
                    let maybeUnencryptedPort = value.TryGetProperty "t"
                    let unencryptedPort =
                        match maybeUnencryptedPort with
                        | None -> None
                        | Some portAsString -> Some (Int32.Parse (portAsString.AsString()))
                    let maybeEncryptedPort = value.TryGetProperty "s"
                    let encryptedPort =
                        match maybeEncryptedPort with
                        | None -> None
                        | Some portAsString -> Some (Int32.Parse (portAsString.AsString()))
                    yield { Fqdn = key;
                            Pruning = value?pruning.AsString();
                            PrivatePort = encryptedPort;
                            UnencryptedPort = unencryptedPort;
                            Version = value?version.AsString(); }
            }
        servers |> List.ofSeq

    let Randomize() =
        Shuffler.Unsort defaultList

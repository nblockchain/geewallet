namespace GWallet.Backend

open System
open System.IO
open System.Text
open System.Reflection
open System.IO.Compression
open System.Text.RegularExpressions

open Newtonsoft.Json
open Newtonsoft.Json.Serialization

type DeserializationException =
   inherit Exception

   new(message: string, innerException: Exception) = { inherit Exception(message, innerException) }
   new(message: string) = { inherit Exception(message) }

type SerializationException(message:string, innerException: Exception) =
    inherit Exception (message, innerException)

type VersionMismatchDuringDeserializationException (message:string, innerException: Exception) =
   inherit DeserializationException (message, innerException)

module VersionHelper =
    let CurrentVersion ()=
        typedefof<DeserializationException>.GetTypeInfo().Assembly.GetName().Version.ToString()

type SerializableValue<'T>(value: 'T) =
    member val Version: string =
        VersionHelper.CurrentVersion() with get

    member val TypeName: string =
        typeof<'T>.FullName with get

    member val Value: 'T = value with get

type DeserializableValueInfo(version: string, typeName: string) =

    member this.Version
        with get() = version 

    member this.TypeName
        with get() = typeName 

type DeserializableValue<'T>(version, typeName, value: 'T) =
    inherit DeserializableValueInfo(version, typeName)

    member this.Value
        with get() = value

type private PascalCase2LowercasePlusUnderscoreContractResolver() =
    inherit DefaultContractResolver()

    // https://stackoverflow.com/a/20952003/544947
    let pascalToUnderScoreRegex = Regex("((?<=.)[A-Z][a-zA-Z]*)|((?<=[a-zA-Z])\d+)", RegexOptions.Multiline)
    let pascalToUnderScoreReplacementExpression = "_$1$2"
    override this.ResolvePropertyName (propertyName: string) =
        pascalToUnderScoreRegex.Replace(propertyName, pascalToUnderScoreReplacementExpression).ToLower()

module Marshalling =

    let internal PascalCase2LowercasePlusUnderscoreConversionSettings =
        JsonSerializerSettings(ContractResolver = PascalCase2LowercasePlusUnderscoreContractResolver())

    let private currentVersion = VersionHelper.CurrentVersion()

    let ExtractType(json: string): Type =
        let typeInfo =
            try
                JsonConvert.DeserializeObject<DeserializableValueInfo> json
            with
            | ex -> raise (DeserializationException("Could not extract type", ex))
        let fullTypeName = (typeInfo).TypeName
        Type.GetType(fullTypeName)

    let Deserialize<'S,'T when 'S:> DeserializableValue<'T>>(json: string): 'T =
        if (json = null) then
            raise (ArgumentNullException("json"))
        if (String.IsNullOrWhiteSpace(json)) then
            raise (ArgumentException("empty or whitespace json", "json"))

        let deserialized: 'S =
            try
                JsonConvert.DeserializeObject<'S>(json)
            with
            | ex ->
                let versionJsonTag = "\"Version\":\""
                if (json.Contains(versionJsonTag)) then
                    let jsonSinceVersion = json.Substring(json.IndexOf(versionJsonTag) + versionJsonTag.Length)
                    let endVersionIndex = jsonSinceVersion.IndexOf("\"")
                    let version = jsonSinceVersion.Substring(0, endVersionIndex)
                    if (version <> currentVersion) then
                        let msg = sprintf "Incompatible marshalling version found (%s vs. current %s) while trying to deserialize JSON"
                                          version currentVersion
                        raise (new VersionMismatchDuringDeserializationException(msg, ex))
                raise (new DeserializationException(sprintf "Exception when trying to deserialize '%s'" json, ex))


        if Object.ReferenceEquals(deserialized, null) then
            raise (new DeserializationException(sprintf "JsonConvert.DeserializeObject returned null when trying to deserialize '%s'"
                                                        json))
        if Object.ReferenceEquals(deserialized.Value, null) then
            raise (new DeserializationException(sprintf "JsonConvert.DeserializeObject could not deserialize the Value member of '%s'"
                                                        json))
        deserialized.Value

    let private SerializeInternal<'S>(value: 'S): string =
        JsonConvert.SerializeObject(SerializableValue<'S>(value),
                                    JsonSerializerSettings(DateTimeZoneHandling = DateTimeZoneHandling.Utc))

    let Serialize<'S>(value: 'S): string =
        try
            SerializeInternal value
        with
        | exn ->
            raise(SerializationException(sprintf "Could not serialize object of type '%s' and value '%A'"
                                                  (typeof<'S>.FullName) value, exn))

    type CompressionOrDecompressionException(msg: string, innerException: Exception) =
        inherit Exception(msg, innerException)

    // https://stackoverflow.com/a/43357353/544947
    let Decompress (compressedString: string): string =
        try
            use decompressedStream = new MemoryStream()
            use compressedStream = new MemoryStream(Convert.FromBase64String compressedString)
            let decompressorStream = new DeflateStream(compressedStream, CompressionMode.Decompress)
            decompressorStream.CopyTo(decompressedStream)
            decompressorStream.Dispose()
            Encoding.UTF8.GetString(decompressedStream.ToArray())
        with
        | ex ->
            raise(CompressionOrDecompressionException("Could not decompress", ex))

    let Compress (uncompressedString: string): string =
        try
            use compressedStream = new MemoryStream()
            use uncompressedStream = new MemoryStream(Encoding.UTF8.GetBytes uncompressedString)
            let compressorStream = new DeflateStream(compressedStream, CompressionMode.Compress)
            uncompressedStream.CopyTo compressorStream
            // can't use "use" because it needs to be dissposed manually before getting the data
            compressorStream.Dispose()
            Convert.ToBase64String(compressedStream.ToArray())
        with
        | ex ->
            raise(CompressionOrDecompressionException("Could not compress", ex))

namespace GWallet.Backend

open System
open System.IO
open System.Text
open System.Reflection
open System.IO.Compression
open System.Text.RegularExpressions

open Newtonsoft.Json
open Newtonsoft.Json.Serialization

open GWallet.Backend.FSharpUtil.UwpHacks

type DeserializationException =
    inherit Exception

    new(message: string, innerException: Exception) = { inherit Exception(message, innerException) }
    new(message: string) = { inherit Exception(message) }

type SerializationException(message:string, innerException: Exception) =
    inherit Exception (message, innerException)

type VersionMismatchDuringDeserializationException (message:string, innerException: Exception) =
    inherit DeserializationException (message, innerException)

module internal VersionHelper =
    let internal CURRENT_VERSION =
        typedefof<DeserializationException>.GetTypeInfo().Assembly.GetName().Version.ToString()

type MarshallingWrapper<'T> =
    {
        Version: string
        TypeName: string
        Value: 'T
    }
    static member New value =
        {
            Value = value
            Version = VersionHelper.CURRENT_VERSION
            TypeName = typeof<'T>.FullName
        }

type private PascalCase2LowercasePlusUnderscoreContractResolver() =
    inherit DefaultContractResolver()

    // https://stackoverflow.com/a/20952003/544947
    let pascalToUnderScoreRegex = Regex("((?<=.)[A-Z][a-zA-Z]*)|((?<=[a-zA-Z])\d+)", RegexOptions.Multiline)
    let pascalToUnderScoreReplacementExpression = "_$1$2"
    override __.ResolvePropertyName (propertyName: string) =
        pascalToUnderScoreRegex.Replace(propertyName, pascalToUnderScoreReplacementExpression).ToLower()

// combine https://stackoverflow.com/a/48330214/544947 with https://stackoverflow.com/a/29660550/544947
// (because null values should map to None values in the case of Option<> types, otherwise tests fail)
type RequireAllPropertiesContractResolver() =
    inherit DefaultContractResolver()

    override __.CreateObjectContract(objectType: Type) =
        let contract = base.CreateObjectContract objectType
        contract.ItemRequired <- Nullable<Required> Required.Always
        contract

    override __.CreateProperty(memberInfo: MemberInfo, memberSerialization: MemberSerialization) =
        let property = base.CreateProperty(memberInfo, memberSerialization)
        // https://stackoverflow.com/questions/20696262/reflection-to-find-out-if-property-is-of-option-type
        let isOption =
            property.PropertyType.IsGenericType &&
            property.PropertyType.GetGenericTypeDefinition() = typedefof<Option<_>>
        if isOption then
            property.Required <- Required.AllowNull
        property

module Marshalling =

    let private DefaultFormatting =
#if DEBUG
        Formatting.Indented
#else
        Formatting.None
#endif

    let internal PascalCase2LowercasePlusUnderscoreConversionSettings =
        JsonSerializerSettings(ContractResolver = PascalCase2LowercasePlusUnderscoreContractResolver())

    let internal DefaultSettings =
        JsonSerializerSettings(MissingMemberHandling = MissingMemberHandling.Error,
                               ContractResolver = RequireAllPropertiesContractResolver(),
                               DateTimeZoneHandling = DateTimeZoneHandling.Utc)

    let private currentVersion = VersionHelper.CURRENT_VERSION

    let ExtractType(json: string): Type =
        let typeInfo =
            try
                JsonConvert.DeserializeObject<MarshallingWrapper<obj>> json
            with
            | ex -> raise (DeserializationException("Could not extract type", ex))
        let fullTypeName = typeInfo.TypeName
        Type.GetType(fullTypeName)

    let DeserializeCustom<'T>(json: string, settings: JsonSerializerSettings): 'T =
        if (json = null) then
            raise (ArgumentNullException("json"))
        if (String.IsNullOrWhiteSpace(json)) then
            raise (ArgumentException("empty or whitespace json", "json"))

        let deserialized =
            try
                JsonConvert.DeserializeObject<MarshallingWrapper<'T>>(json, settings)
            with
            | ex ->
                let versionJsonTag = "\"Version\":\""
                if (json.Contains(versionJsonTag)) then
                    let jsonSinceVersion = json.Substring(json.IndexOf(versionJsonTag) + versionJsonTag.Length)
                    let endVersionIndex = jsonSinceVersion.IndexOf("\"")
                    let version = jsonSinceVersion.Substring(0, endVersionIndex)
                    if (version <> currentVersion) then
                        let msg = SPrintF2 "Incompatible marshalling version found (%s vs. current %s) while trying to deserialize JSON"
                                          version currentVersion
                        raise <| VersionMismatchDuringDeserializationException(msg, ex)
                raise <| DeserializationException(SPrintF1 "Exception when trying to deserialize '%s'" json, ex)


        if Object.ReferenceEquals(deserialized, null) then
            raise <| DeserializationException(SPrintF1 "JsonConvert.DeserializeObject returned null when trying to deserialize '%s'"
                                                      json)
        if Object.ReferenceEquals(deserialized.Value, null) then
            raise <| DeserializationException(SPrintF1 "JsonConvert.DeserializeObject could not deserialize the Value member of '%s'"
                                                      json)
        deserialized.Value

    let Deserialize<'T>(json: string): 'T =
        DeserializeCustom(json, DefaultSettings)

    let private SerializeInternal<'T>(value: 'T) (settings: JsonSerializerSettings): string =
        JsonConvert.SerializeObject(MarshallingWrapper<'T>.New value,
                                    DefaultFormatting,
                                    settings)

    let SerializeCustom<'T>(value: 'T, settings: JsonSerializerSettings): string =
        try
            SerializeInternal value settings
        with
        | exn ->
            raise (SerializationException(SPrintF2 "Could not serialize object of type '%s' and value '%A'"
                                                  (typeof<'T>.FullName) value, exn))

    let Serialize<'T>(value: 'T): string =
        SerializeCustom(value, DefaultSettings)

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



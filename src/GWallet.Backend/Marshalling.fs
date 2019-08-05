namespace GWallet.Backend

open System
open System.Reflection
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
        Assembly.GetExecutingAssembly().GetName().Version.ToString()

type MarshallingWrapper<'T> =
    {
        Version: string
        TypeName: string
        Value: 'T
    }
    static member New value =
        {
            Value = value
            Version = VersionHelper.CurrentVersion()
            TypeName = typeof<'T>.FullName
        }

type private PascalCase2LowercasePlusUnderscoreContractResolver() =
    inherit DefaultContractResolver()

    // https://stackoverflow.com/a/20952003/544947
    let pascalToUnderScoreRegex = Regex("((?<=.)[A-Z][a-zA-Z]*)|((?<=[a-zA-Z])\d+)", RegexOptions.Multiline)
    let pascalToUnderScoreReplacementExpression = "_$1$2"
    override __.ResolvePropertyName (propertyName: string) =
        pascalToUnderScoreRegex.Replace(propertyName, pascalToUnderScoreReplacementExpression).ToLower()

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
                               DateTimeZoneHandling = DateTimeZoneHandling.Utc)

    let private currentVersion = VersionHelper.CurrentVersion()

    let ExtractType(json: string): Type =
        let fullTypeName = (JsonConvert.DeserializeObject<MarshallingWrapper<obj>> json).TypeName
        Type.GetType(fullTypeName)

    let Deserialize<'T>(json: string): 'T =
        if (json = null) then
            raise (ArgumentNullException("json"))
        if (String.IsNullOrWhiteSpace(json)) then
            raise (ArgumentException("empty or whitespace json", "json"))

        let deserialized =
            try
                JsonConvert.DeserializeObject<MarshallingWrapper<'T>>(json, DefaultSettings)
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
                        raise <| VersionMismatchDuringDeserializationException(msg, ex)
                raise <| DeserializationException(sprintf "Exception when trying to deserialize '%s'" json, ex)


        if Object.ReferenceEquals(deserialized, null) then
            raise <| DeserializationException(sprintf "JsonConvert.DeserializeObject returned null when trying to deserialize '%s'"
                                                      json)
        if Object.ReferenceEquals(deserialized.Value, null) then
            raise <| DeserializationException(sprintf "JsonConvert.DeserializeObject could not deserialize the Value member of '%s'"
                                                      json)
        deserialized.Value

    let private SerializeInternal<'T>(value: 'T): string =
        JsonConvert.SerializeObject(MarshallingWrapper<'T>.New value,
                                    DefaultFormatting,
                                    DefaultSettings)

    let Serialize<'T>(value: 'T): string =
        try
            SerializeInternal value
        with
        | exn ->
            raise(SerializationException(sprintf "Could not serialize object of type '%s' and value '%A'"
                                                  (typeof<'T>.FullName) value, exn))


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
        let fullTypeName = (JsonConvert.DeserializeObject<DeserializableValueInfo> json).TypeName
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


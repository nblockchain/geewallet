﻿namespace GWallet.Backend

open System
open System.IO
open System.Text
open System.Diagnostics
open System.Runtime.Serialization

open Sentry

open GWallet.Backend.FSharpUtil.UwpHacks

type SentryReportingException =
    inherit Exception

    new(message: string, innerException: Exception) = { inherit Exception(message, innerException) }
    new (info: SerializationInfo, context: StreamingContext) =
        { inherit Exception (info, context) }

module Infrastructure =

    let md5 = System.Security.Cryptography.MD5.Create()

    let private sentryUrl = "https://4d1c6170ee37412fab20f8c63a2ade24:fc5e2c50990e48929d190fc283513f87@sentry.io/187797"
    let private sentryClient = new SentryClient(SentryOptions(Dsn = sentryUrl, Release = VersionHelper.CURRENT_VERSION))
    let private captureLock = obj()

    let private GetTelemetryDir (meta: bool) =
        let cacheDir = Config.GetCacheDir()
        let telemetryBaseDir = Path.Combine(cacheDir.FullName, "telemetry") |> DirectoryInfo
        if not telemetryBaseDir.Exists then
            telemetryBaseDir.Create()
        if not meta then
            telemetryBaseDir
        else
            let telemetryMetaDir = Path.Combine(telemetryBaseDir.FullName, "meta") |> DirectoryInfo
            if not telemetryMetaDir.Exists then
                telemetryMetaDir.Create()
            telemetryMetaDir

    // from https://stackoverflow.com/a/10520086/544947
    let private Hash (text: string) =
        let preHash =
            text
            |> Encoding.UTF8.GetBytes
            |> md5.ComputeHash
            |> BitConverter.ToString
        preHash
            .Replace("-", String.Empty)
            .ToLowerInvariant()

    let internal LogCrash (ex: Exception) (meta: bool) =
        let marshalledEx = Marshalling.Serialize ex
        let hash = Hash marshalledEx
        let fileForException = Path.Combine ((GetTelemetryDir meta).FullName, hash) |> FileInfo
        File.WriteAllText (fileForException.FullName, marshalledEx)
        fileForException

    let private ReportInner (sentryEvent: SentryEvent) =
        try
            lock captureLock (fun _ ->
                sentryClient.CaptureEvent sentryEvent
                |> ignore<SentryId>
                true
            )
        with
        | ex ->
            let newEx = SentryReportingException("Error while trying to send Sentry report", ex)
            LogCrash newEx true
            |> ignore<FileInfo>
            false

    let internal Flush () =
        Console.Out.Flush ()
        Console.Error.Flush ()
        Debug.Flush ()

    let LogInfo (log: string) =
        Console.WriteLine log
        Debug.WriteLine log
        Flush ()

    let LogError (log: string) =
        Console.Error.WriteLine log
        Debug.WriteLine log
        Flush ()

    let LogDebug (log: string) =
        if Config.DebugLog then
            LogInfo <| SPrintF1 "DEBUG: %s" log

    let internal ReportMessage (message: string)
#if DEBUG
                               (_         : SentryLevel)
#else
                               (errorLevel: SentryLevel)
#endif
                               : bool =
#if DEBUG
        failwith message
#else
        let sentryEvent = SentryEvent(Message = SentryMessage(Message = message), Level = Nullable errorLevel)
        ReportInner sentryEvent
#endif

    let internal ReportError (errorMessage: string): bool =
        ReportMessage errorMessage SentryLevel.Error

    let private Report (ex: Exception)
                       (errorLevel: SentryLevel)
                       : bool =

        // TODO: log this in a file (log4net?), as well as printing to the console, before sending to sentry
        Console.Error.WriteLine ex
        Debug.WriteLine ex
        Flush ()

#if DEBUG
        if errorLevel = SentryLevel.Error then
            raise ex
        false
#else
        try
            let ev = SentryEvent(ex, Level = Nullable errorLevel)
            ReportInner ev
        with
        | ex ->
            if errorLevel = SentryLevel.Error then
                reraise()

                //unreachable
                false

            else
                // e.g. if in cold-storage mode, trying to report a warning would cause a crash, but let's ignore it:
                false
#endif

    let ReportWarning (ex: Exception): bool =
        Report ex SentryLevel.Warning

    let ReportWarningMessage (warning: string): bool =
        ReportMessage warning SentryLevel.Warning

    let LogOrReportCrash (ex: Exception) =
#if !DEBUG
        let loggedEx = LogCrash ex false
        let reported =
#else
        let _reported =
#endif
            Report ex SentryLevel.Fatal

#if DEBUG
            |> ignore<bool>
#else
        if reported then
            loggedEx.Delete()
#endif
        ()

    let private OnUnhandledException (_: obj) (args: UnhandledExceptionEventArgs) =
        let ex = args.ExceptionObject :?> Exception
        LogOrReportCrash ex

    // TODO: Should report the meta exceptions too? but it may cause more meta exceptions... mmm
    //       Maybe it should check number of meta exceptions at beginning, and check number of meta exceptions at the end?:
    //       if number is equal, then report?
    let private ReportAllPastExceptions() =
        for loggedEx in ((GetTelemetryDir false).EnumerateFiles()) do
            let serializedException = File.ReadAllText loggedEx.FullName
            let deserializedException = Marshalling.Deserialize<Exception> serializedException
            let reported = Report deserializedException SentryLevel.Fatal
            if reported then
                loggedEx.Delete ()

    let public SetupExceptionHook () =
#if !DEBUG
        ReportAllPastExceptions ()
#endif
        AppDomain.CurrentDomain.UnhandledException.AddHandler (UnhandledExceptionEventHandler (OnUnhandledException))

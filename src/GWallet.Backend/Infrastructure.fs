namespace GWallet.Backend

open System
open System.Diagnostics

open SharpRaven
open SharpRaven.Data

open GWallet.Backend.FSharpUtil.UwpHacks

module Infrastructure =

    let private sentryUrl = "https://4d1c6170ee37412fab20f8c63a2ade24:fc5e2c50990e48929d190fc283513f87@sentry.io/187797"
    let private ravenClient = RavenClient(sentryUrl, Release = VersionHelper.CURRENT_VERSION)

    let private ReportInner (sentryEvent: SentryEvent) =
        ravenClient.Capture sentryEvent |> ignore

    let private timestamp(log: string): string =
        let now = DateTime.Now.ToString("yyyy-MM-dd:HH:mm:ss.ffff")
        log.Split([| "\r\n"; "\r"; "\n" |], StringSplitOptions.None)
        |> Seq.map(fun line -> now + ": " + line)
        |> String.concat Environment.NewLine

    let internal Flush () =
        Console.Out.Flush ()
        Console.Error.Flush ()
        Debug.Flush ()

    let LogInfo (log: string) =
        let timestamped = timestamp log
        Console.WriteLine timestamped
        Debug.WriteLine timestamped
        Flush ()

    let LogError (log: string) =
        let timestamped = timestamp log
        Console.Error.WriteLine timestamped
        Debug.WriteLine timestamped
        Flush ()

    let LogDebug (log: string) =
        if Config.DebugLog then
            LogInfo <| SPrintF1 "DEBUG: %s" log

    let internal ReportMessage (message: string)
#if DEBUG
                               (_         : ErrorLevel)
#else
                               (errorLevel: ErrorLevel)
#endif
                           =
#if DEBUG
        failwith message
#else
        let sentryEvent =  SentryEvent(SentryMessage message, Level = errorLevel)
        ReportInner sentryEvent
#endif

    let internal ReportError (errorMessage: string) =
        ReportMessage errorMessage ErrorLevel.Error

    let private Report (ex: Exception)
#if DEBUG
                       (_         : ErrorLevel)
#else
                       (errorLevel: ErrorLevel)
#endif
                       =

        // TODO: log this in a file (log4net?), as well as printing to the console, before sending to sentry
        let timestamped = timestamp (ex.ToString())
        Console.Error.WriteLine timestamped
        Debug.WriteLine timestamped
        Flush ()

#if DEBUG
        raise ex
#else
        let ev = SentryEvent(ex, Level = errorLevel)
        ReportInner ev
#endif

    let ReportWarning (ex: Exception) =
        Report ex ErrorLevel.Warning

    let ReportWarningMessage (warning: string) =
        ReportMessage warning ErrorLevel.Warning

    let ReportCrash (ex: Exception) =
        Report ex ErrorLevel.Fatal

    let private OnUnhandledException (_: obj) (args: UnhandledExceptionEventArgs) =
        ReportCrash (args.ExceptionObject :?> Exception)

    let public SetupSentryHook () =
        AppDomain.CurrentDomain.UnhandledException.AddHandler (UnhandledExceptionEventHandler (OnUnhandledException))

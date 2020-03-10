namespace GWallet.Backend

open System

open SharpRaven
open SharpRaven.Data

module Infrastructure =

    let private sentryUrl = "https://4d1c6170ee37412fab20f8c63a2ade24:fc5e2c50990e48929d190fc283513f87@sentry.io/187797"
    let private ravenClient = RavenClient(sentryUrl, Release = VersionHelper.CURRENT_VERSION)

    let private ReportInner (sentryEvent: SentryEvent) =
        ravenClient.Capture sentryEvent |> ignore

    let internal ReportMessage (message: string) (errorLevel: ErrorLevel) =
#if DEBUG
        failwith message
#else
        let sentryEvent =  SentryEvent(SentryMessage message, Level = errorLevel)
        ReportInner sentryEvent
#endif

    let internal ReportError (errorMessage: string) =
        ReportMessage errorMessage ErrorLevel.Error

    let private Report (ex: Exception) (errorLevel: ErrorLevel) =

        // TODO: log this in a file (log4net?), as well as printing to the console, before sending to sentry
        Console.Error.WriteLine ex

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

    let private OnUnhandledException (sender: obj) (args: UnhandledExceptionEventArgs) =
        ReportCrash (args.ExceptionObject :?> Exception)

    let public SetupSentryHook () =
        AppDomain.CurrentDomain.UnhandledException.AddHandler (UnhandledExceptionEventHandler (OnUnhandledException))

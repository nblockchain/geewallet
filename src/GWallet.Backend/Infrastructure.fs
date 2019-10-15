namespace GWallet.Backend

open System

open SharpRaven
open SharpRaven.Data

module Infrastructure =

    let private sentryUrl = "https://4d1c6170ee37412fab20f8c63a2ade24:fc5e2c50990e48929d190fc283513f87@sentry.io/187797"
    let private ravenClient = RavenClient(sentryUrl, Release = VersionHelper.CURRENT_VERSION)

    let internal ReportError (errorMessage: string) =
        ravenClient.Capture (SentryEvent (SentryMessage (errorMessage), Level = ErrorLevel.Error)) |> ignore

    let private ReportInner (ex: Exception) (errorLevel: ErrorLevel) =

        // TODO: log this in a file (log4net?), as well as printing to the console, before sending to sentry
        Console.Error.WriteLine ex

#if DEBUG
        raise ex
#else
        let ev = SentryEvent(ex, Level = errorLevel)
        ravenClient.Capture ev |> ignore
#endif

    let ReportWarning (ex: Exception) =
        ReportInner ex ErrorLevel.Warning

    let ReportCrash (ex: Exception) =
        ReportInner ex ErrorLevel.Fatal

    let private OnUnhandledException (sender: obj) (args: UnhandledExceptionEventArgs) =
        ReportCrash (args.ExceptionObject :?> Exception)

    let public SetupSentryHook () =
        AppDomain.CurrentDomain.UnhandledException.AddHandler (UnhandledExceptionEventHandler (OnUnhandledException))

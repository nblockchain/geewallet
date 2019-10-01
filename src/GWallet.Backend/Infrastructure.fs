namespace GWallet.Backend

open System
open System.Reflection

open SharpRaven
open SharpRaven.Data

module Infrastructure =

    let private sentryUrl = "https://4d1c6170ee37412fab20f8c63a2ade24:fc5e2c50990e48929d190fc283513f87@sentry.io/187797"
    let private appVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString()
    let private appNameInSentry = "gwallet"
    let private ravenClient = RavenClient(sentryUrl, Release = sprintf "%s@%s" appNameInSentry appVersion)

    let internal ReportError (errorMessage: string) =
        ravenClient.Capture (SentryEvent (SentryMessage (errorMessage))) |> ignore

    let public Report (ex: Exception) =

        // TODO: log this in a file (log4net?), as well as printing to the console, before sending to sentry
        Console.Error.WriteLine ex

#if DEBUG
        raise ex
#else
        ravenClient.Capture (SentryEvent (ex)) |> ignore
#endif

    let private OnUnhandledException (sender: obj) (args: UnhandledExceptionEventArgs) =
        Report (args.ExceptionObject :?> Exception)

    let public SetupSentryHook () =
        AppDomain.CurrentDomain.UnhandledException.AddHandler (UnhandledExceptionEventHandler (OnUnhandledException))

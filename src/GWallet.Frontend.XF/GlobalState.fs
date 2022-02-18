namespace GWallet.Frontend.XF

open Xamarin.Forms


module DummyPageConstructorHelper =

    [<Literal>]
    let Warning =
        "DO NOT USE THIS! This paramaterless constructor is only here to allow the VS designer to render page"

    let GlobalFuncToRaiseExceptionIfUsedAtRuntime(): FrontendHelpers.IGlobalAppState =
        failwith Warning

    let PageFuncToRaiseExceptionIfUsedAtRuntime(): Page =
        failwith Warning

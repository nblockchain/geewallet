namespace GWallet.Frontend.XF.Android

open Android.Widget

open Xamarin.Essentials
open Xamarin.Forms
open Xamarin.Forms.Platform.Android

open GWallet.Backend
open GWallet.Backend.FSharpUtil.UwpHacks

// Custom renderer for Xamarin.Forms.DatePicker
// which displays the year selection first
type DatePickerYearFirstRenderer (context) =
    inherit DatePickerRenderer (context)

    override self.CreateDatePickerDialog (year, month, day) =
        let dialog = base.CreateDatePickerDialog (year, month, day)

        // Below is a hack
        // The mono DatePicker implementation does not expose the underlying
        // Android.Widget.DatePicker implementation
        // We navigate through multiple layers of child elements until we arrive
        // at the text element which displays the year and emit a click on it
        let warningMsg =
            let maybeLayoutA = dialog.DatePicker.GetChildAt 0
            match maybeLayoutA with
            | :? LinearLayout as layoutA ->
                let maybeLayoutB = layoutA.GetChildAt 0
                match maybeLayoutB with
                | :? LinearLayout as layoutB ->
                    let maybeLayoutC = layoutB.GetChildAt 0
                    match maybeLayoutC with
                    | :? LinearLayout as layoutC ->
                        let yearText = layoutC.GetChildAt 0
                        yearText.PerformClick () |> ignore
                        None
                    | null ->
                        Some "Unexpected DatePicker layout when trying to find layoutC (got null)"
                    | _ ->
                        Some <| SPrintF1 "Unexpected DatePicker layout when trying to find layoutC (got %s)"
                            (maybeLayoutC.GetType().FullName)
                | null ->
                    Some "Unexpected DatePicker layout when trying to find layoutB (got null)"
                | _ ->
                    Some <| SPrintF1 "Unexpected DatePicker layout when trying to find layoutB (got %s)"
                        (maybeLayoutB.GetType().FullName)
            | null ->
                Some <| "Unexpected DatePicker layout when trying to find layoutA (got null)"
            | _ ->
                Some <| SPrintF1 "Unexpected DatePicker layout when trying to find layoutA (got %s)"
                    (maybeLayoutA.GetType().FullName)

        match warningMsg with
        | Some msg ->
            let devInfo =
                SPrintF6 " [DevInfo: (Type=%s, Idiom=%s, Platform=%s, Version=%s, Manufacturer=%s, Model=%s)]"
                    (DeviceInfo.DeviceType.ToString())
                    (DeviceInfo.Idiom.ToString())
                    (DeviceInfo.Platform.ToString())
                    DeviceInfo.VersionString
                    DeviceInfo.Manufacturer
                    DeviceInfo.Model

            Infrastructure.ReportWarningMessage (msg + devInfo) |> ignore<bool>
        | _ -> ()

        dialog


[<assembly:ExportRenderer(typeof<DatePicker>, typeof<DatePickerYearFirstRenderer>)>]
do ()

namespace GWallet.Frontend.XF.Android

open Xamarin.Forms
open Xamarin.Forms.Platform.Android

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
        let layoutA = dialog.DatePicker.GetChildAt 0 :?> Android.Widget.LinearLayout
        let layoutB = layoutA.GetChildAt 0 :?> Android.Widget.LinearLayout
        let layoutC = layoutB.GetChildAt 0 :?> Android.Widget.LinearLayout
        let yearText = layoutC.GetChildAt 0
        yearText.PerformClick () |> ignore
        dialog

[<assembly:ExportRenderer(typeof<DatePicker>, typeof<DatePickerYearFirstRenderer>)>]
do ()

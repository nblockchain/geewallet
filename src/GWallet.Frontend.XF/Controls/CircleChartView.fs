namespace GWallet.Frontend.XF.Controls

open System

open Xamarin.Forms
open SkiaSharp

type CircleChartView () =
    inherit Image () 

    member self.DrawPie (width: float) (height: float) =
        let defaultScaleFactor = 5.f
        let imageInfo = SKImageInfo(int width * int defaultScaleFactor, int height * int defaultScaleFactor)

        use surface = SKSurface.Create imageInfo
        if surface = null then
            failwithf "Strangely enough, surface created was null (w: %f, h: %f)" width height
        surface.Canvas.Clear SKColors.Empty
        let halfWidth = float32 width / 2.f
        let halfHeight = float32 height / 2.f
        let center = SKPoint(halfWidth, halfHeight)
        let radius = Math.Min(halfWidth, halfHeight)
                        // add some padding, otherwise it hits the limits of the square
                        - 5.f

        let color = SkiaSharp.Views.Forms.Extensions.ToSKColor Color.Blue
        use fillPaint = new SKPaint (Style = SKPaintStyle.Fill, Color = color, IsAntialias = true)
        surface.Canvas.Scale(defaultScaleFactor, defaultScaleFactor)
        surface.Canvas.DrawCircle(center.X, center.Y, radius, fillPaint)

        surface.Canvas.Flush()
        surface.Canvas.Save() |> ignore

        use image = surface.Snapshot()
        let data = image.Encode(SKEncodedImageFormat.Png, Int32.MaxValue)
        self.Source <- ImageSource.FromStream(fun _ -> data.AsStream())

    member self.Draw () =
        let width = base.Width
        let height = base.Height

        if width <= 0. || 
           height <= 0. || 
           not base.IsVisible then
            ()
        else
            self.DrawPie width height


    override self.OnPropertyChanged(propertyName: string) =
        base.OnPropertyChanged(propertyName)
        if propertyName = VisualElement.HeightProperty.PropertyName ||
           propertyName = VisualElement.WidthProperty.PropertyName ||
           propertyName = VisualElement.IsVisibleProperty.PropertyName then
            self.Draw()

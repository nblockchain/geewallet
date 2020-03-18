namespace GWallet.Frontend.XF.Controls

open System
open System.Text
open System.Linq
open System.IO

open Xamarin.Forms
open SkiaSharp

open GWallet.Frontend.XF
open GWallet.Backend.FSharpUtil.UwpHacks

type SegmentInfo = 
    {
        Color: Color
        Percentage: float
    }

type CircleChartView () =
    inherit Image () 

    let svgMainImagePattern = @"<?xml version=""1.0"" encoding=""utf-8""?>
    <svg version=""1.1"" id=""Layer_1"" xmlns=""http://www.w3.org/2000/svg"" xmlns:xlink=""http://www.w3.org/1999/xlink"" x=""0px"" y=""0px""
    width=""{0}px"" height=""{0}px"" viewBox=""0 0 {0} {0}"" enable-background=""new 0 0 {0} {0}"" xml:space=""preserve"">
    {1}
    {2}
    </svg>"
    let svgSegmentPattern = @"<path fill=""{0}"" d=""M{1},{2} L{1},0 A{1},{1} 1 {3},1 {4},{5} L{6},{7} A{8},{8} 1 {3},0 {1},{2} z"" transform=""rotate({9}, {1}, {1})"" />"
    let svgCirclePattern = @"<circle cx=""{0}"" cy=""{0}"" r=""{1}"" fill=""{2}""/>"
    let degree360 = 360.
    let degree180 = 180.
    let degree90 = 90.

    let mutable firstWidth = None
    let mutable firstHeight = None

    let BuildColorPart(part: float): int =
        int(part * 255.)

    let GetHexColor(color: Color): string =
        let red =  BuildColorPart color.R
        let green = BuildColorPart color.G
        let blue = BuildColorPart color.B
        let alpha = BuildColorPart color.A
        String.Format("{0:X2}{1:X2}{2:X2}{3:X2}", alpha, red, green, blue)

    let GetArcCoordinates radius angle shift =
        let angleCalculated = 
            if angle > degree180 then 
                degree360 - angle 
            else 
                angle

        let angleRad = angleCalculated * Math.PI / degree180

        let perpendicularDistance = 
            if angleCalculated > degree90 then 
                float(radius) * Math.Sin((degree180 - angleCalculated) * Math.PI / degree180) 
            else 
                float(radius) * Math.Sin(angleRad)

        let topPointDistance =
            Math.Sqrt(float(2 * radius * radius) - (float (2 * radius * radius) * Math.Cos(angleRad)))
        let arcEndY = Math.Sqrt(topPointDistance * topPointDistance - perpendicularDistance * perpendicularDistance)
        let arcEndX = 
            if angle > degree180 then 
                float(radius) - perpendicularDistance 
            else 
                float(radius) + perpendicularDistance

        arcEndX + shift, arcEndY + shift

    let CollectSegment color startX startY radius innerRadius angle rotation (segmentsBuilder: StringBuilder) =
        let bigArcEndX, bigArcEndY = GetArcCoordinates radius angle 0.
        let shift = float(radius - innerRadius)
        let smallArcEndX, smallArcEndY = GetArcCoordinates innerRadius angle shift

        let obtuseAngleFlag = 
            if angle > degree180 then
                1 
            else 
                0

        segmentsBuilder.AppendLine(
            String.Format(svgSegmentPattern, 
                          color, 
                          startX, 
                          startY, 
                          obtuseAngleFlag, 
                          bigArcEndX, 
                          bigArcEndY, 
                          smallArcEndX, 
                          smallArcEndY, 
                          innerRadius, 
                          rotation)
        ) |> ignore

    let PrepareSegmentsSvgBuilder segmentsToDraw rotation startY radius innerRadius: StringBuilder =
        let rec prepareSegmentsSvgBuilderInner segmentsToDraw rotation startY radius innerRadius segmentsBuilder =
            match segmentsToDraw with
            | [] ->
                ()
            | item::tail ->
                let angle = degree360 * item.Percentage
                if item.Color.A > 0. then
                    let color = GetHexColor item.Color
                    let startX = radius

                    if angle >= 360. then
                        CollectSegment color startX startY radius innerRadius 180. rotation segmentsBuilder
                        CollectSegment color startX startY radius innerRadius 180. 180. segmentsBuilder
                    else
                        CollectSegment color startX startY radius innerRadius angle rotation segmentsBuilder

                let newRotation = rotation + angle
                prepareSegmentsSvgBuilderInner tail newRotation startY radius innerRadius segmentsBuilder

        let segmentsBuilder = StringBuilder()
        prepareSegmentsSvgBuilderInner segmentsToDraw rotation startY radius innerRadius segmentsBuilder
        segmentsBuilder

    let ConvertXamFormsColorToSKColor (color: Xamarin.Forms.Color) =
        (* original C# source from https://github.com/mono/SkiaSharp/blob/master/source/SkiaSharp.Views.Forms/SkiaSharp.Views.Forms.Shared/Extensions.cs#L63-L66
        public static SKColor ToSKColor(this Color color)
        {
            return new SKColor((byte)(color.R * 255), (byte)(color.G * 255), (byte)(color.B * 255), (byte)(color.A * 255));
        }
        *)
        let newR = color.R * 255. |> byte
        let newG = color.G * 255. |> byte
        let newB = color.B * 255. |> byte
        let newA = color.A * 255. |> byte
        SKColor(newR, newG, newB, newA)
            
    static let segmentsSourceProperty =
        BindableProperty.Create("SegmentsSource",
                                typeof<seq<SegmentInfo>>, typeof<CircleChartView>, null) 
    static let separatorPercentageProperty =
        BindableProperty.Create("SeparatorPercentage",
                                typeof<float>, typeof<CircleChartView>, 0.)
    static let centerCirclePercentageProperty =
        BindableProperty.Create("CenterCirclePercentage",
                                // NOTE: if this below has a value higher than 0 (and less than 1) it'll be back a donut
                                typeof<float>, typeof<CircleChartView>, 0.)
    static let separatorColorProperty =
        BindableProperty.Create("SeparatorColor",
                                typeof<Color>, typeof<CircleChartView>, Color.Transparent)
    static let defaultImageSourceProperty =
        BindableProperty.Create("DefaultImageSource",
                                typeof<ImageSource>, typeof<CircleChartView>, null)

    static member SegmentsSourceProperty = segmentsSourceProperty
    static member SeparatorPercentageProperty = separatorPercentageProperty
    static member CenterCirclePercentageProperty = centerCirclePercentageProperty
    static member SeparatorColorProperty = separatorColorProperty
    static member DefaultImageSourceProperty = defaultImageSourceProperty

    member self.SegmentsSource
        with get () = self.GetValue segmentsSourceProperty :?> seq<SegmentInfo>
        and set (value: seq<SegmentInfo>) = self.SetValue(segmentsSourceProperty, value)

    member self.SeparatorPercentage
        with get () = self.GetValue separatorPercentageProperty :?> float
        and set (value: float) = self.SetValue(separatorPercentageProperty, value)

    member self.CenterCirclePercentage
        with get () = self.GetValue centerCirclePercentageProperty :?> float
        and set (value: float) = self.SetValue(centerCirclePercentageProperty, value)

    member self.SeparatorColor
        with get () = self.GetValue separatorColorProperty :?> Color
        and set (value: Color) = self.SetValue(separatorColorProperty, value)

    member self.DefaultImageSource
        with get () = self.GetValue defaultImageSourceProperty :?> ImageSource
        and set (value: ImageSource) = self.SetValue(defaultImageSourceProperty, value)

    member self.DrawSkiaPieFallback (width: float) (height: float) (items: seq<SegmentInfo>) =
        if not (items.Any()) then
            failwith "chart data should not be empty to draw a pie"

        // WORKAROUND for Android issue about chart resizing to too big after coming back from ReceivePage
        // TODO: report this as a bug to Xamarin.Forms or SkiaSharp!
        let workaroundLaunched =
            // because in platforms where you can resize the app, this workaround makes a mess
            // (and I've only reproduced the bug anyway with iOS&Android)
            if FrontendHelpers.IsDesktop() then
                false
            else
                let widthWorkaroundLaunched =
                    match firstWidth with
                    | None ->
                        firstWidth <- Some width
                        false
                    | Some w ->
                        if base.Width > 0. && base.Width <> w then
                            self.WidthRequest <- w
                            true
                        else
                            false

                let heightWorkaroundLaunched =
                    match firstHeight with
                    | None ->
                        firstHeight <- Some height
                        false
                    | Some h ->
                        if base.Height > 0. && base.Height <> h then
                            self.HeightRequest <- h
                            true
                        else
                            false

                widthWorkaroundLaunched || heightWorkaroundLaunched
        // </WORKAROUND>*)

        if not workaroundLaunched then

            let defaultScaleFactor = 5.f
            let imageInfo = SKImageInfo(int width * int defaultScaleFactor, int height * int defaultScaleFactor)

            use surface = SKSurface.Create imageInfo
            if surface = null then
                failwith <| SPrintF2 "Strangely enough, surface created was null (w: %f, h: %f)" width height
            surface.Canvas.Clear SKColors.Empty
            let halfWidth = float32 width / 2.f
            let halfHeight = float32 height / 2.f
            let center = SKPoint(halfWidth, halfHeight)
            let radius = Math.Min(halfWidth, halfHeight)
                            // add some padding, otherwise it hits the limits of the square
                            - 5.f

            let total = items.Sum(fun i -> i.Percentage) |> float32
            if items.Count() = 1 then
                let item = items.Single()
                let color = ConvertXamFormsColorToSKColor item.Color
                use fillPaint = new SKPaint (Style = SKPaintStyle.Fill, Color = color, IsAntialias = true)
                surface.Canvas.Scale(defaultScaleFactor, defaultScaleFactor)
                surface.Canvas.DrawCircle(center.X, center.Y, radius, fillPaint)
            else
                let rect = SKRect(center.X - radius, center.Y - radius, center.X + radius, center.Y + radius)
                let mutable startAngle = 0.f
                for item in items do
                    let sweepAngle = 360.f * float32 item.Percentage / total

                    use path = new SKPath ()
                    let color = ConvertXamFormsColorToSKColor item.Color
                    use fillPaint = new SKPaint (Style = SKPaintStyle.Fill, Color = color, IsAntialias = true)
                    path.MoveTo center
                    path.ArcTo(rect, startAngle, sweepAngle, false)
                    path.Close()

                    surface.Canvas.Save() |> ignore
                    surface.Canvas.Scale(defaultScaleFactor, defaultScaleFactor)
                    surface.Canvas.DrawPath(path, fillPaint)
                    surface.Canvas.Restore()

                    startAngle <- startAngle + sweepAngle

            surface.Canvas.Flush()
            surface.Canvas.Save() |> ignore

            use image = surface.Snapshot()
            let data = image.Encode(SKEncodedImageFormat.Png, Int32.MaxValue)
            self.Source <- ImageSource.FromStream(fun _ -> data.AsStream())

    member self.DrawSvgBasedPie (width: float) (height: float) (items: seq<SegmentInfo>) =
        if not (items.Any()) then
            failwith "chart data should not be empty to draw the SVG-based chart"

        // FIXME: rework this workaround when we upgrade to an XF version where this bug is fixed:
        // https://github.com/xamarin/Xamarin.Forms/issues/8652 (to still detect 0.0 but send sentry warning)
        let defaultScaleFactor = 2.0
        let platformScaleFactor = Device.Info.ScalingFactor
        let scaleFactor =
            // somehow, even with a scaleFactor higher than zero, this gives problems when resizing on macOS
            // (and on UWP you don't even need to resize to see the circle grow indefinitely)
            // TODO: report this bug in the Xamarin.Forms issue tracker
            if Device.RuntimePlatform = Device.macOS || Device.RuntimePlatform = Device.UWP then
                1.0
            elif platformScaleFactor <= 0.0 then
                defaultScaleFactor
            else
                platformScaleFactor

        let size = int(Math.Floor(Math.Min(width, height) * scaleFactor))
        let halfSize =
            if size / 2 % 2 = 0 then
                size / 2
            else
                size / 2 - 1

        let itemsCount = items.Count()
        let separatorsTotalPercentage =
            if itemsCount > 1 then
                float(itemsCount) * self.SeparatorPercentage
            else
                0.

        let segmentsTotalPercentage = 1. - separatorsTotalPercentage

        let segmentsToDraw =
            if itemsCount = 1 then
                let item = items.First()
                let segment = {
                    Color = item.Color
                    Percentage = item.Percentage * segmentsTotalPercentage
                }
                segment::List.Empty
            else
                items
                    |> Seq.map (fun i ->
                           let separator = {
                               Color = self.SeparatorColor
                               Percentage = self.SeparatorPercentage
                           }
                           let segment = {
                               Color = i.Color
                               Percentage = i.Percentage * segmentsTotalPercentage
                           }
                           [separator; segment]
                        )
                    |> List.concat

        let innerRadius = int(float(halfSize) * self.CenterCirclePercentage)
        let startY = int((1. - self.CenterCirclePercentage) * float(halfSize))
        let segmentsBuilder = PrepareSegmentsSvgBuilder segmentsToDraw 0. startY halfSize innerRadius

        let centerCiclerSvg =
            if self.SeparatorColor.A > 0. then
                String.Format(svgCirclePattern,
                              halfSize,
                              float(halfSize) * self.CenterCirclePercentage,
                              GetHexColor self.SeparatorColor)
             else
                 String.Empty

        let fullSvg = String.Format(svgMainImagePattern, size, segmentsBuilder, centerCiclerSvg)
        let svgHolder = SkiaSharp.Extended.Svg.SKSvg()

        use stream = new MemoryStream(Encoding.UTF8.GetBytes fullSvg)
        svgHolder.Load stream |> ignore

        let canvasSize = svgHolder.CanvasSize
        let cullRect = svgHolder.Picture.CullRect

        use bitmap = new SKBitmap(int canvasSize.Width, int canvasSize.Height)
        use canvas = new SKCanvas(bitmap)
        let canvasMin = Math.Min(canvasSize.Width, canvasSize.Height)
        let svgMax = Math.Max(cullRect.Width, cullRect.Height)
        let scale = canvasMin / svgMax
        let matrix = SKMatrix.MakeScale(scale, scale)
        canvas.Clear SKColor.Empty
        canvas.DrawPicture(svgHolder.Picture, ref matrix)
        canvas.Flush()
        canvas.Save() |> ignore
        use image = SKImage.FromBitmap bitmap
        let data = image.Encode(SKEncodedImageFormat.Png, Int32.MaxValue)
        self.Source <- ImageSource.FromStream(fun _ -> data.AsStream())

    member self.Draw () =
        let width = 
            if base.WidthRequest > 0. then 
                base.WidthRequest 
            else 
                base.Width
        let height = 
            if base.HeightRequest > 0. then
                base.HeightRequest 
            else 
                base.Height

        if width <= 0. || 
           height <= 0. || 
           not base.IsVisible then
            ()
        else
            let nonZeroItems =
                if self.SegmentsSource <> null then
                    self.SegmentsSource.Where(fun s -> s.Percentage > 0.)
                else
                    Seq.empty<SegmentInfo>

            if nonZeroItems.Any() then
                // let's be careful about enabling the Pie for all platforms in the future (instead of Android
                // exclusively) because there are bugs in macOS & GTK...
                if Device.RuntimePlatform = Device.Android then
                    self.DrawSkiaPieFallback width height nonZeroItems
                else
                    self.DrawSvgBasedPie width height nonZeroItems
            else
                self.Source <- self.DefaultImageSource


    override self.OnPropertyChanged(propertyName: string) =
        base.OnPropertyChanged(propertyName)
        if propertyName = VisualElement.HeightProperty.PropertyName ||
           propertyName = VisualElement.WidthProperty.PropertyName ||
           propertyName = VisualElement.HeightRequestProperty.PropertyName || 
           propertyName = VisualElement.WidthRequestProperty.PropertyName || 
           propertyName = VisualElement.IsVisibleProperty.PropertyName ||
           propertyName = CircleChartView.SegmentsSourceProperty.PropertyName ||
           propertyName = CircleChartView.SeparatorPercentageProperty.PropertyName ||
           propertyName = CircleChartView.CenterCirclePercentageProperty.PropertyName ||
           propertyName = CircleChartView.SeparatorColorProperty.PropertyName || 
           propertyName = CircleChartView.DefaultImageSourceProperty.PropertyName then
            self.Draw()

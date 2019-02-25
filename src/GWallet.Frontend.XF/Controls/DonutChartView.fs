namespace GWallet.Frontend.XF.Controls

open System.Text
open System.Linq
open Xamarin.Forms
open System.IO
open SkiaSharp
open System
open GWallet.Frontend.XF

type SegmentInfo = 
    {
        Color: Color
        Percentage: float
    }

type DonutChartView () =
    inherit Image () 

    let svgMainImagePattern = @"<?xml version=""1.0"" encoding=""utf-8""?>
    <svg version=""1.1"" id=""Layer_1"" xmlns=""http://www.w3.org/2000/svg"" xmlns:xlink=""http://www.w3.org/1999/xlink"" x=""0px"" y=""0px""
    width=""{0}px"" height=""{0}px"" viewBox=""0 0 {0} {0}"" enable-background=""new 0 0 {0} {0}"" xml:space=""preserve"">
    {1}
    {2}
    </svg>"
    let svgSegmentPattern = @"<path fill=""{5}"" d=""M{0},{0} L{0},0 A{0},{0} 1 {4},1 {1}, {2} z"" transform=""rotate({3}, {0}, {0})"" />"
    let svgCirclePattern = @"<circle cx=""{0}"" cy=""{0}"" r=""{1}"" fill=""{2}""/>"
    let degree360 = 360.
    let degree180 = 180.
    let degree90 = 90.

    let BuildColorPart(part: float): int =
        int(part * 255.)

    let GetHexColor(color: Color): string =
        let red =  BuildColorPart color.R
        let green = BuildColorPart color.G
        let blue = BuildColorPart color.B
        let alpha = BuildColorPart color.A
        String.Format("{0:X2}{1:X2}{2:X2}{3:X2}", alpha, red, green, blue)

    let rec PrepareSegmentsSvgBuilder segmentsToDraw rotation halfSize (segmentsBuilder: StringBuilder) =
        match segmentsToDraw with 
        | [] -> 
            ()
        | item::tail -> 
            let angle = degree360 * item.Percentage
            let color = GetHexColor(item.Color)

            if angle >= 360. then
                segmentsBuilder.AppendLine(String.Format(svgCirclePattern, halfSize, halfSize, color)) |> ignore
            else
                let angleCalculated = 
                    if angle > degree180 then 
                        degree360 - angle 
                    else 
                        angle

                let angleRad = angleCalculated * Math.PI / degree180
                let perpendicularDistance = 
                    if angleCalculated > degree90 then 
                        float(halfSize) * Math.Sin((degree180 - angleCalculated) * Math.PI / degree180) 
                    else 
                        float(halfSize) * Math.Sin(angleRad)

                let topPointDistance =
                    Math.Sqrt(float(2 * halfSize * halfSize) - (float (2 * halfSize * halfSize) * Math.Cos(angleRad)))
                let y = Math.Sqrt(topPointDistance * topPointDistance - perpendicularDistance * perpendicularDistance)
                let x = 
                    if angle > degree180 then 
                        float(halfSize) - perpendicularDistance 
                    else 
                        float(halfSize) + perpendicularDistance

                let obtuseAngleFlag = 
                    if angle > degree180 then
                        1 
                    else 
                        0

                segmentsBuilder.AppendLine(
                    String.Format(svgSegmentPattern, halfSize, x, y, rotation, obtuseAngleFlag, color)
                ) |> ignore

            let newRotation = rotation + angle
            PrepareSegmentsSvgBuilder tail newRotation halfSize segmentsBuilder

    static let segmentsSourceName = "SegmentsSource"
    static let segmentsSourceProperty =
        BindableProperty.Create(segmentsSourceName,
                                typeof<seq<SegmentInfo>>, typeof<DonutChartView>, null)
    static let separatorPercentageName = "SeparatorPercentage"
    static let separatorPercentageProperty =
        BindableProperty.Create(separatorPercentageName,
                                typeof<float>, typeof<DonutChartView>, 0.)
    static let centerCirclePercentageName = "CenterCirclePercentage"
    static let centerCirclePercentageProperty =
        BindableProperty.Create(centerCirclePercentageName,
                                typeof<float>, typeof<DonutChartView>, 0.5)
    static let separatorColorName = "SeparatorColor"
    static let separatorColorProperty =
        BindableProperty.Create(separatorColorName,
                                typeof<Color>, typeof<DonutChartView>, Color.White)

    static member SegmentsSourceProperty = segmentsSourceProperty
    static member SeparatorPercentageProperty = separatorPercentageProperty
    static member CenterCirclePercentageProperty = centerCirclePercentageProperty
    static member SeparatorColorProperty = separatorColorProperty

    member this.SegmentsSource
        with get () = this.GetValue segmentsSourceProperty :?> seq<SegmentInfo>
        and set (value:seq<SegmentInfo>) = this.SetValue(segmentsSourceProperty, value)

    member this.SeparatorPercentage
        with get () = this.GetValue separatorPercentageProperty :?> float
        and set (value:float) = this.SetValue(separatorPercentageProperty, value)

    member this.CenterCirclePercentage
        with get () = this.GetValue centerCirclePercentageProperty :?> float
        and set (value:float) = this.SetValue(centerCirclePercentageProperty, value)

    member this.SeparatorColor
        with get () = this.GetValue separatorColorProperty :?> Color
        and set (value:Color) = this.SetValue(separatorColorProperty, value)

    member this.Draw () =
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
           this.SegmentsSource = null || 
           not(this.SegmentsSource.Any()) then
            ()
        else

            let size = int(Math.Min(width, height))
            let halfSize = size / 2

            let nonZeroItems = this.SegmentsSource.Where(fun s -> s.Percentage > 0.)
            let itemsCount = nonZeroItems.Count()
            if itemsCount = 0 then
                this.Source <- FrontendHelpers.GetSizedImageSource "logo" 512
            else
                let separatorsTotalPercentage = 
                    if itemsCount > 1 then 
                        float(itemsCount) * this.SeparatorPercentage 
                    else 
                        0.

                let segmentsTotalPercentage = 1. - separatorsTotalPercentage

                let segmentsToDraw = 
                    if itemsCount = 1 then
                        let item = nonZeroItems.First()
                        let segment = { 
                            Color = item.Color
                            Percentage = item.Percentage * segmentsTotalPercentage
                        }
                        [segment]
                    else
                        nonZeroItems
                            |> Seq.map (fun i -> 
                                   let separator = { 
                                       Color = this.SeparatorColor
                                       Percentage = this.SeparatorPercentage
                                   } 
                                   let segment = { 
                                       Color = i.Color
                                       Percentage = i.Percentage * segmentsTotalPercentage
                                   }
                                   [separator; segment]
                                )
                            |> List.concat
                            
                let segmentsBuilder = StringBuilder()
                
                PrepareSegmentsSvgBuilder segmentsToDraw 0. halfSize segmentsBuilder

                let centerCiclerSvg = String.Format(svgCirclePattern,
                                                    halfSize,
                                                    float(halfSize) * this.CenterCirclePercentage,
                                                    GetHexColor this.SeparatorColor)
                let fullSvg = String.Format(svgMainImagePattern, size, segmentsBuilder, centerCiclerSvg)
                let svgHolder = SkiaSharp.Extended.Svg.SKSvg()

                use stream = new MemoryStream(Encoding.UTF8.GetBytes fullSvg)
                svgHolder.Load(stream) |> ignore

                let canvasSize = svgHolder.CanvasSize
                let cullRect = svgHolder.Picture.CullRect

                use bitmap = new SKBitmap(int(canvasSize.Width), int(canvasSize.Height))       
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
                this.Source <- ImageSource.FromStream(fun _ -> data.AsStream())
        ()

    override this.OnPropertyChanged(propertyName: string) =
        base.OnPropertyChanged(propertyName)
        if propertyName = "Height" ||
           propertyName = "Width" ||
           propertyName = "WidthRequest" || 
           propertyName = "HeightRequest" || 
           propertyName = segmentsSourceName ||
           propertyName = separatorPercentageName ||
           propertyName = centerCirclePercentageName ||
           propertyName = separatorColorName then
            this.Draw()
        ()

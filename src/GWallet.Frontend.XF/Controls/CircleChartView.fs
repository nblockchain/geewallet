namespace GWallet.Frontend.XF.Controls

open System
open System.Text
open System.Linq
open System.Globalization
open System.IO

open Xamarin.Forms
open Xamarin.Forms.Shapes

open GWallet.Frontend.XF
open GWallet.Backend.FSharpUtil.UwpHacks

type SegmentInfo = 
    {
        Color: Color
        Percentage: float
    }

type CircleChartView () =
    inherit ContentView () 

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
    let shapesPath = @"M{0},{1} A{2},{2} 0 {3} 1 {4} {5} L {6} {7}"

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

    member self.DrawShapesBasedPie (width: float) (height: float) (items: seq<SegmentInfo>) =
       if not (items.Any()) then
           failwith "chart data should not be empty to draw the Shapes-based chart"

       let halfWidth = float32 width / 2.f
       let halfHeight = float32 height / 2.f
       let radius = Math.Min(halfWidth, halfHeight) |> float
       let total = items.Sum(fun i -> i.Percentage) |> float

       let x = float halfWidth
       let y = float halfHeight

       let converter = PathGeometryConverter ()
       let nfi = NumberFormatInfo (NumberDecimalSeparator = ".")
       let gridLayout = Grid ()

       if items.Count() = 1 then
            // this is a workaround (to create a circle instead) to a Xamarin.Forms' Shapes bug:
            // https://github.com/xamarin/Xamarin.Forms/issues/13893
            let size =  radius * 2.
            let color = (items.ElementAt 0).Color
            let pieCircle =
                Ellipse (
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    HeightRequest = size,
                    WidthRequest = size,
                    Fill = SolidColorBrush color,
                    StrokeThickness = 0.,
                    Stroke = null
                )
            gridLayout.Children.Add pieCircle
       else
            let rec addSliceToView items cumulativePercent =
                match items with
                | [] ->
                    ()
                | item::tail ->
                    let startCoordinatesX = x + (radius * Math.Cos(2.0 * Math.PI * cumulativePercent))
                    let startCoordinatesY = y + (radius * Math.Sin(2.0 * Math.PI * cumulativePercent))

                    let endPercentage = item.Percentage + cumulativePercent

                    let endCoordinatesX = x + (radius * Math.Cos(2.0 * Math.PI * endPercentage))
                    let endCoordinatesY = y + (radius * Math.Sin(2.0 * Math.PI * endPercentage))
                    
                    let arc =
                        if item.Percentage > 0.5 then
                            "1"
                        else
                            "0"
                    
                    let path =
                        String.Format (
                            shapesPath,
                            startCoordinatesX.ToString nfi,
                            startCoordinatesY.ToString nfi,
                            radius.ToString nfi,
                            arc,
                            endCoordinatesX.ToString nfi,
                            endCoordinatesY.ToString nfi,
                            x.ToString nfi,
                            y.ToString nfi
                        )                    

                    let pathGeometry = converter.ConvertFromInvariantString path :?> Geometry
                    let helperView =
                        Path (
                            Data = pathGeometry,
                            Fill = SolidColorBrush item.Color,
                            StrokeThickness = 0.,
                            Stroke = null
                        )
                    gridLayout.Children.Add helperView

                    addSliceToView tail endPercentage
                   
            let itemsList = items |> Seq.toList
            addSliceToView itemsList 0.

       self.Content <- gridLayout :> View
       ()         

    member private self.CreateAndSetImageSource (imageSource : ImageSource) =
        let image =
            Image (
                HorizontalOptions = LayoutOptions.FillAndExpand,
                VerticalOptions = LayoutOptions.FillAndExpand,
                Aspect = Aspect.AspectFit,
                Source = imageSource
            )
        self.Content <- image

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
                    self.SegmentsSource.Where(fun s -> s.Percentage > 0.) |> Some
                else
                    None

            match nonZeroItems with
            | None -> ()
            | Some items when items.Any() ->
                self.DrawShapesBasedPie width height items
            | Some _ ->
                self.CreateAndSetImageSource self.DefaultImageSource

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

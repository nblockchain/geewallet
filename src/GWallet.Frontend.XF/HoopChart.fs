﻿namespace Frontend.FSharp


open Xamarin.Forms
open Xamarin.Forms.Shapes



type SegmentInfo = 
    {
        Color: Color
        Amount: decimal
    }


type private HoopChartState =
    | Uninitialized
    | Empty
    | NonEmpty of seq<SegmentInfo>


type private HoopSector =
    {
        Fraction: float
        Color: Color
    }


type HoopChartView() =
    inherit Layout<View>()

    let mutable state = Uninitialized

    // Child UI elements
    let balanceLabel = Label(HorizontalTextAlignment = TextAlignment.Center, FontSize = 25.0, MaxLines=1)
    let balanceTagLabel = 
        Label( 
            Text = "Account Balance", 
            FontSize = 15.0, 
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Margin = Thickness(0.0, -7.5)
        )

    let balanceFrame = 
        let frame = 
            Frame(
                HasShadow = false,
                BackgroundColor = Color.Transparent,
                BorderColor = Color.Transparent,
                Padding = Thickness(0.0)
            )
        let stackLayout = 
            StackLayout(
                Orientation = StackOrientation.Vertical,
                VerticalOptions = LayoutOptions.CenterAndExpand,
                HorizontalOptions = LayoutOptions.Center
            )
        stackLayout.Children.Add(balanceTagLabel)
        stackLayout.Children.Add(balanceLabel)
        frame.Content <- stackLayout

        frame

    let defaultImage = 
        Image (
            HorizontalOptions = LayoutOptions.FillAndExpand,
            VerticalOptions = LayoutOptions.FillAndExpand,
            Aspect = Aspect.AspectFit
        )

    let hoop = Grid()

    // Properties
    static let segmentsSourceProperty =
        BindableProperty.Create("SegmentsSource", typeof<seq<SegmentInfo>>, typeof<HoopChartView>, null) 
    static let defaultImageSourceProperty =
        BindableProperty.Create("DefaultImageSource", typeof<ImageSource>, typeof<HoopChartView>, null)

    static member SegmentsSourceProperty = segmentsSourceProperty
    static member DefaultImageSourceProperty = defaultImageSourceProperty

    member self.SegmentsSource
        with get () = self.GetValue segmentsSourceProperty :?> seq<SegmentInfo>
        and set (value: seq<SegmentInfo>) = self.SetValue(segmentsSourceProperty, value)

    member self.DefaultImageSource
        with get () = self.GetValue defaultImageSourceProperty :?> ImageSource
        and set (value: ImageSource) = self.SetValue(defaultImageSourceProperty, value)

    member this.BalanceLabel = balanceLabel
    member this.BalanceFrame = balanceFrame

    // Layout properties
    member this.MinimumChartSize = 200.0
    member this.MinimumLogoSize = 50.0
    member this.HoopStrokeThickness = 7.5
    
    // Chart shapes
    member private this.GetHoopShapes(segments: seq<SegmentInfo>, radius: float) : seq<Shape> =
        let deg2rad angle = System.Math.PI * (angle / 180.0)
        let thickness = this.HoopStrokeThickness
        let minorRadius = thickness/2.0
        let circleRadius = radius - minorRadius
        let angleToPoint angle =
            Point(cos (deg2rad angle) * circleRadius + radius, sin (deg2rad angle) * circleRadius + radius)
        
        let circleLength = circleRadius * System.Math.PI * 2.0
        let spacingFraction = thickness / circleLength * 0.75

        let visibleSectors =
            let sum = segments |> Seq.sumBy (fun each -> each.Amount)
            segments |> Seq.choose (fun segment -> 
                let fraction = float(segment.Amount / sum)
                if fraction >= spacingFraction then
                    Some({ Fraction=fraction; Color=segment.Color })
                else 
                    None)

        let normalizedSectors =
            let sum = visibleSectors |> Seq.sumBy (fun x -> x.Fraction)
            visibleSectors |> Seq.map (fun x -> { x with Fraction=x.Fraction/sum })

        let spacingAngle = 360.0 * spacingFraction
        let angles =
            normalizedSectors
            |> Seq.scan (fun currAngle sector -> currAngle + (360.0 * sector.Fraction)) 0.0
        let anglePairs = Seq.append (Seq.pairwise angles) (Seq.singleton ((Seq.last angles), 360.0))
        
        Seq.map2
            (fun sector (startAngle, endAngle) ->
                let startPoint = angleToPoint (startAngle + spacingAngle)
                let endPoint = angleToPoint (endAngle - spacingAngle)
                let arcAngle = endAngle - startAngle - spacingAngle*2.0
                let path = Path()
                let geom = PathGeometry()
                let figure = PathFigure()
                figure.StartPoint <- startPoint
                let segment = ArcSegment(endPoint, Size(circleRadius, circleRadius), arcAngle, SweepDirection.Clockwise, arcAngle > 180.0)
                figure.Segments.Add(segment)
                geom.Figures.Add(figure)
                path.Data <- geom
                path.Stroke <- SolidColorBrush(sector.Color)
                path.StrokeThickness <- thickness
                path.StrokeLineCap <- PenLineCap.Round
                path :> Shape)
            normalizedSectors
            anglePairs
    
    member private this.RepopulateHoop(segments, sideLength) =
        hoop.Children.Clear()
        this.GetHoopShapes(segments, sideLength / 2.0) |> Seq.iter hoop.Children.Add

    // Layout
    override this.LayoutChildren(x, y, width, height) = 
        match state with
        | Uninitialized -> ()
        | Empty -> 
            let bounds = Rectangle.FromLTRB(x, y, x + width, y + height)
            defaultImage.Layout(bounds)
        | NonEmpty(segments) -> 
            let smallerSide = min width height
            let dx = (max 0.0 (width - smallerSide)) / 2.0
            let dy = (max 0.0 (height - smallerSide)) / 2.0
            let bounds = Rectangle.FromLTRB(x + dx, y + dy, x + dx + smallerSide, y + dy + smallerSide)

            balanceFrame.Layout(bounds)

            if abs(hoop.Height - smallerSide) > 0.1 then
                this.RepopulateHoop(segments, smallerSide)
            
            hoop.Layout(bounds)

    override this.OnMeasure(widthConstraint, heightConstraint) =
        let smallerRequestedSize = min widthConstraint heightConstraint |> min this.MinimumChartSize
        let minSize = 
            match state with
            | Uninitialized -> 0.0
            | Empty -> this.MinimumLogoSize
            | NonEmpty(_) -> 
                let size = balanceLabel.Measure(smallerRequestedSize, smallerRequestedSize).Request
                (sqrt(size.Width*size.Width + size.Height*size.Height) + this.HoopStrokeThickness) * 1.1
        let sizeToRequest = max smallerRequestedSize minSize
        SizeRequest(Size(sizeToRequest, sizeToRequest), Size(minSize, minSize))
    
    // Updates
    member private this.SetState(newState: HoopChartState) =
        if newState <> state then
            state <- newState
            match state with
            | Uninitialized -> failwith "Invalid state"
            | Empty ->
                this.Children.Clear()
                this.Children.Add(defaultImage)
            | NonEmpty(segments) ->
                this.Children.Clear()
                if this.Width > 0.0 && this.Height > 0.0 then
                    this.RepopulateHoop(segments, min this.Width this.Height)
                this.Children.Add(hoop)
                this.Children.Add(balanceFrame)

    member private this.UpdateChart() =
        let nonZeroSegments =
            match this.SegmentsSource with
            | null ->  Seq.empty
            | segments -> segments |> Seq.filter (fun x -> x.Amount > 0.0m)

        if nonZeroSegments |> Seq.isEmpty |> not then
            this.SetState(NonEmpty(nonZeroSegments))
        else
            this.SetState(Empty)

    override this.OnPropertyChanged(propertyName: string) =
        base.OnPropertyChanged(propertyName)
        if propertyName = HoopChartView.SegmentsSourceProperty.PropertyName then
            this.UpdateChart()
        elif propertyName = HoopChartView.DefaultImageSourceProperty.PropertyName then
            defaultImage.Source <- this.DefaultImageSource
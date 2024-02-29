namespace GWallet.Frontend.XF.Controls


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
    with
        static member Create(fraction: float, color: Color) =
            assert(fraction >= 0.0 && fraction <= 1.0)
            { Fraction = fraction; Color = color }


type HoopChartView() =
    inherit Layout<View>()

    let mutable state = Uninitialized

    // Child UI elements
    let balanceLabel = Label(HorizontalTextAlignment = TextAlignment.Center, FontSize = 25.0, MaxLines=1)
    let balanceTagLabel = 
        Label( 
            Text = "Total Assets:", 
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
                Padding = Thickness(0.0),
                HorizontalOptions = LayoutOptions.CenterAndExpand
            )
        let stackLayout = 
            StackLayout(
                Orientation = StackOrientation.Vertical,
                VerticalOptions = LayoutOptions.CenterAndExpand,
                HorizontalOptions = LayoutOptions.Center
            )
        stackLayout.Children.Add balanceTagLabel
        stackLayout.Children.Add balanceLabel
        frame.Content <- stackLayout

        frame

    let defaultImage = 
        Image (
            HorizontalOptions = LayoutOptions.FillAndExpand,
            VerticalOptions = LayoutOptions.FillAndExpand,
            Aspect = Aspect.AspectFit
        )

    let hoop = Grid()
    let emptyStateWidget = StackLayout(Orientation = StackOrientation.Horizontal)

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

    member self.BalanceLabel = balanceLabel
    member self.BalanceFrame = balanceFrame

    // Layout properties
    member self.MinimumChartSize = 200.0
    member self.MinimumLogoSize = 50.0
    member self.HoopStrokeThickness = 7.5
    
    // Chart shapes
    member private self.GetHoopShapes(segments: seq<SegmentInfo>, radius: float) : seq<Shape> =
        let deg2rad angle = System.Math.PI * (angle / 180.0)
        let thickness = self.HoopStrokeThickness
        let minorRadius = thickness/2.0
        let circleRadius = radius - minorRadius
        let angleToPoint angle =
            Point(cos (deg2rad angle) * circleRadius + radius, sin (deg2rad angle) * circleRadius + radius)
        
        let circleLength = circleRadius * System.Math.PI * 2.0
        let spacingFraction = thickness / circleLength * 1.5

        let visibleSectors =
            let sum = segments |> Seq.sumBy (fun each -> each.Amount)
            segments |> Seq.choose (fun segment -> 
                let fraction = float(segment.Amount / sum)
                if fraction >= spacingFraction then
                    Some(HoopSector.Create(fraction, segment.Color))
                else 
                    None)

        let normalizedSectors =
            let sum = visibleSectors |> Seq.sumBy (fun each -> each.Fraction)
            visibleSectors |> Seq.map (fun each -> { each with Fraction = each.Fraction/sum })

        let spacingAngle = 360.0 * spacingFraction
        let angles =
            normalizedSectors
            |> Seq.scan (fun currAngle sector -> currAngle + (360.0 * sector.Fraction)) 0.0
        let anglePairs = Seq.pairwise angles
        
        Seq.map2
            (fun sector (startAngle, endAngle) ->
                let startPoint = angleToPoint (startAngle + spacingAngle/2.0)
                let endPoint = angleToPoint (endAngle - spacingAngle/2.0)
                let arcAngle = endAngle - startAngle - spacingAngle
                let path =
                    // Workaround for Android where very small arcs wouldn't render
                    if arcAngle / 360.0 * circleLength < 1.0 then 
                        let midPoint = Point((startPoint.X + endPoint.X) / 2.0, (startPoint.Y + endPoint.Y) / 2.0)
                        let geom = EllipseGeometry(midPoint, thickness/2.0, thickness/2.0)
                        Path(Data = geom, Fill = SolidColorBrush sector.Color)
                    else
                        let geom = PathGeometry()
                        let figure = PathFigure(StartPoint = startPoint)
                        let segment = ArcSegment(endPoint, Size(circleRadius, circleRadius), arcAngle, SweepDirection.Clockwise, arcAngle > 180.0)
                        figure.Segments.Add segment
                        geom.Figures.Add figure
                        Path(
                            Data = geom, 
                            Stroke = SolidColorBrush sector.Color, 
                            StrokeThickness = thickness, 
                            StrokeLineCap = PenLineCap.Round
                        )
                path :> Shape)
            normalizedSectors
            anglePairs
    
    member private self.RepopulateHoop(segments, sideLength) =
        hoop.Children.Clear()
        self.GetHoopShapes(segments, sideLength / 2.0) |> Seq.iter hoop.Children.Add

    // Layout
    override self.LayoutChildren(xCoord, yCoord, width, height) = 
        match state with
        | Uninitialized -> ()
        | Empty -> 
            let bounds = Rectangle.FromLTRB(xCoord, yCoord, xCoord + width, yCoord + height)
            emptyStateWidget.Layout bounds
        | NonEmpty(segments) -> 
            let smallerSide = min width height
            let xOffset = (max 0.0 (width - smallerSide)) / 2.0
            let yOffset = (max 0.0 (height - smallerSide)) / 2.0
            let bounds = Rectangle.FromLTRB(xCoord + xOffset, yCoord + yOffset, xCoord + xOffset + smallerSide, yCoord + yOffset + smallerSide)

            balanceFrame.Layout bounds

            if abs(hoop.Height - smallerSide) > 0.1 then
                self.RepopulateHoop(segments, smallerSide)
            
            hoop.Layout bounds

    override self.OnMeasure(widthConstraint, heightConstraint) =
        let smallerRequestedSize = min widthConstraint heightConstraint |> min self.MinimumChartSize
        let minSize = 
            match state with
            | Uninitialized -> 0.0
            | Empty -> self.MinimumLogoSize
            | NonEmpty _ -> 
                let size = balanceLabel.Measure(infinity, smallerRequestedSize).Request
                let factor = 1.1 // to add some visual space between label and chart
                (sqrt(size.Width*size.Width + size.Height*size.Height) + self.HoopStrokeThickness) * factor
        let sizeToRequest = max smallerRequestedSize minSize
        SizeRequest(Size(sizeToRequest, sizeToRequest), Size(minSize, minSize))
    
    // Updates
    member private self.SetState(newState: HoopChartState) =
        if newState <> state then
            state <- newState
            match state with
            | Uninitialized -> failwith "Invalid state"
            | Empty ->
                self.Children.Clear()
                emptyStateWidget.Children.Clear()
                emptyStateWidget.Children.Add balanceFrame
                emptyStateWidget.Children.Add defaultImage
                self.Children.Add emptyStateWidget
            | NonEmpty(segments) ->
                self.Children.Clear()
                if self.Width > 0.0 && self.Height > 0.0 then
                    self.RepopulateHoop(segments, min self.Width self.Height)
                self.Children.Add hoop
                self.Children.Add balanceFrame

    member private self.UpdateChart() =
        let nonZeroSegments =
            match self.SegmentsSource with
            | null -> Seq.empty
            | segments -> segments |> Seq.filter (fun segment -> segment.Amount > 0.0m)

        if nonZeroSegments |> Seq.isEmpty |> not then
            self.SetState(NonEmpty(nonZeroSegments))
        else
            self.SetState(Empty)

    override self.OnPropertyChanged(propertyName: string) =
        base.OnPropertyChanged propertyName
        if propertyName = HoopChartView.SegmentsSourceProperty.PropertyName then
            self.UpdateChart()
        elif propertyName = HoopChartView.DefaultImageSourceProperty.PropertyName then
            defaultImage.Source <- self.DefaultImageSource

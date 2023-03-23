#if XAMARIN
namespace GWallet.Frontend.XF.Controls
#else
namespace GWallet.Frontend.Maui.Controls
#endif

#if XAMARIN
open Xamarin.Forms
open Xamarin.Forms.Shapes
type Rect = Xamarin.Forms.Rectangle
#else
open Microsoft.Maui
open Microsoft.Maui.Controls
open Microsoft.Maui.Graphics
open Microsoft.Maui.Controls.Shapes
open Microsoft.Maui.Layouts
#endif


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
    inherit AbsoluteLayout()

    let mutable state = Uninitialized

    // Child UI elements
    let balanceLabel = Label(HorizontalTextAlignment = TextAlignment.Center, FontSize = 25.0, MaxLines=1)
    let balanceTagLabel = 
        Label( 
            Text = "Total Assets:", 
            FontSize = 15.0, 
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        )

    let balanceFrame = 
        let frame = 
            Frame(
                HasShadow = false,
#if XAMARIN
                BackgroundColor = Color.Transparent,
                BorderColor = Color.Transparent,
#else
                BackgroundColor = Colors.Transparent,
                BorderColor = Colors.Transparent,
#endif
                Padding = Thickness(0.0),
                VerticalOptions = LayoutOptions.Center
            )
        let gridLayout = Grid(HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center)
        gridLayout.RowDefinitions.Add(RowDefinition())
        gridLayout.RowDefinitions.Add(RowDefinition())
        gridLayout.ColumnDefinitions.Add(ColumnDefinition())
#if !XAMARIN && GTK
        gridLayout.Padding <- Thickness(15.0, 0.0, 0.0, 0.0)
#endif

        gridLayout.Children.Add balanceTagLabel
        gridLayout.Children.Add balanceLabel
#if XAMARIN
        Grid.SetRow(balanceTagLabel, 0)
        Grid.SetRow(balanceLabel, 1)
#else
        gridLayout.SetRow(balanceTagLabel, 0)
        gridLayout.SetRow(balanceLabel, 1)
#endif
        frame.Content <- gridLayout

        frame

    let defaultImage = 
        Image (
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
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
                            // workaround for https://github.com/dotnet/maui/issues/9089
                            Aspect = enum 4
                        )
                path :> Shape)
            normalizedSectors
            anglePairs
    
    member private this.RepopulateHoop(segments, sideLength) =
        hoop.Children.Clear()
        this.GetHoopShapes(segments, sideLength / 2.0) |> Seq.iter hoop.Children.Add

    // Layout
    member this.ArrangeChildren(layoutBounds: Rect) = 
        match state with
        | Uninitialized -> layoutBounds
        | Empty -> 
#if XAMARIN
            AbsoluteLayout.SetLayoutBounds(emptyStateWidget, layoutBounds)
#else
            this.SetLayoutBounds(emptyStateWidget, layoutBounds)
#endif
            layoutBounds
        | NonEmpty(segments) -> 
            let smallerSide = min layoutBounds.Width layoutBounds.Height
            let xOffset = (max 0.0 (layoutBounds.Width - smallerSide)) / 2.0
            let yOffset = (max 0.0 (layoutBounds.Height - smallerSide)) / 2.0
            let bounds = 
                Rect(layoutBounds.X + xOffset, layoutBounds.Y + yOffset, smallerSide, smallerSide)
            if abs(hoop.Height - smallerSide) > 0.1 then
                this.RepopulateHoop(segments, smallerSide)
#if XAMARIN
            AbsoluteLayout.SetLayoutBounds(balanceFrame, bounds)
            AbsoluteLayout.SetLayoutBounds(hoop, bounds)
#else
            this.SetLayoutBounds(balanceFrame, bounds)
            this.SetLayoutBounds(hoop, bounds)
#endif
            bounds

    member this.Measure(widthConstraint, heightConstraint) =
        let smallerRequestedSize = min widthConstraint heightConstraint |> max this.MinimumChartSize
        let minSize = 
            match state with
            | Uninitialized -> 0.0
            | Empty -> this.MinimumLogoSize
            | NonEmpty _ -> 
                let size = balanceLabel.Measure(infinity, infinity).Request
                let factor = 1.1 // to add some visual space between label and chart
                (sqrt(size.Width*size.Width + size.Height*size.Height) + this.HoopStrokeThickness) * factor
        let sizeToRequest = max smallerRequestedSize minSize
        SizeRequest(Size(sizeToRequest, sizeToRequest), Size(minSize, minSize))

#if XAMARIN
    override this.LayoutChildren(xCoord, yCoord, width, height) = 
        this.ArrangeChildren(Rect(xCoord, yCoord, width, height))
        |> ignore
        base.LayoutChildren(xCoord, yCoord, width, height)

    override this.OnMeasure(widthConstraint, heightConstraint) =
        this.Measure(widthConstraint, heightConstraint)
#else
    override this.CreateLayoutManager() =
        new HoopChartLayoutManager(this)
#endif

    // Updates
    member private this.SetState(newState: HoopChartState) =
        if newState <> state then
            state <- newState
            match state with
            | Uninitialized -> failwith "Invalid state"
            | Empty ->
                this.Children.Clear()
                emptyStateWidget.Children.Clear()
                emptyStateWidget.Children.Add balanceFrame
                emptyStateWidget.Children.Add defaultImage
                this.Children.Add emptyStateWidget
            | NonEmpty(segments) ->
                this.Children.Clear()
                if this.Width > 0.0 && this.Height > 0.0 then
                    this.RepopulateHoop(segments, min this.Width this.Height)
                this.Children.Add hoop
                this.Children.Add balanceFrame

    member private this.UpdateChart() =
        let nonZeroSegments =
            match this.SegmentsSource with
            | null -> Seq.empty
            | segments -> segments |> Seq.filter (fun segment -> segment.Amount > 0.0m)

        if nonZeroSegments |> Seq.isEmpty |> not then
            this.SetState(NonEmpty(nonZeroSegments))
        else
            this.SetState(Empty)

    override this.OnPropertyChanged(propertyName: string) =
        base.OnPropertyChanged propertyName
        if propertyName = HoopChartView.SegmentsSourceProperty.PropertyName then
            this.UpdateChart()
        elif propertyName = HoopChartView.DefaultImageSourceProperty.PropertyName then
            defaultImage.Source <- this.DefaultImageSource
#if !XAMARIN
and HoopChartLayoutManager(hoopChart: HoopChartView) =
    inherit AbsoluteLayoutManager(hoopChart)
    
    override this.Measure(widthConstraint, heightConstraint) = 
        let requestedSize = hoopChart.Measure(widthConstraint, heightConstraint).Request
        base.Measure(requestedSize.Width, requestedSize.Height) |> ignore
        requestedSize

    override this.ArrangeChildren(layoutBounds) = 
        base.ArrangeChildren(hoopChart.ArrangeChildren(layoutBounds))
#endif
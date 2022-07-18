namespace Frontend.FSharp


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


type HoopChartView() =
    inherit Layout<View>()

    let mutable state = Uninitialized

    let referenceHoopRadius = 100.0
    let hoopStrokeThickness = 5.0 // make a property?

    // Child UI elements
    let balanceLabel = Label(HorizontalTextAlignment = TextAlignment.Center, FontSize = 25.0)
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

    let hoop = AbsoluteLayout()

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
    
    // Chart shapes
    member private this.GetHoopShapes(segments: seq<SegmentInfo>) : seq<Shape> =
        // not using actual data for now
        let deg2rad angle = System.Math.PI * (angle / 180.0)
        let rSmall = hoopStrokeThickness/2.0
        let r = referenceHoopRadius - rSmall
        let angleToPoint angle =
            Point(cos (deg2rad angle) * r + r + rSmall, sin (deg2rad angle) * r + r + rSmall)
        seq { 
            for angle in 0.0 .. 90.0 .. 360.0 do
                let startPoint = angleToPoint angle
                let endPoint = angleToPoint (angle + 90.0)
                let path = Path()
                let geom = PathGeometry()
                let figure = PathFigure()
                figure.StartPoint <- startPoint
                let segment = ArcSegment(endPoint, Size(r, r), 90.0, SweepDirection.Clockwise, false)
                figure.Segments.Add(segment)
                geom.Figures.Add(figure)
                path.Data <- geom
                path.Stroke <- SolidColorBrush(Color.FromHsv(angle/360.0, 0.7, 0.8))
                path.StrokeThickness <- hoopStrokeThickness
                yield path 
        }

    // Layout
    override this.LayoutChildren(x, y, width, height) = 
        match state with
        | Uninitialized -> ()
        | Empty -> 
            let bounds = Rectangle.FromLTRB(x, y, x + width, y + height)
            defaultImage.Layout(bounds)
        | NonEmpty(_) -> 
            let smallerSide = min width height
            let dx = (max 0.0 (width - smallerSide)) / 2.0
            let dy = (max 0.0 (height - smallerSide)) / 2.0
            let bounds = Rectangle.FromLTRB(x + dx, y + dy, x + dx + smallerSide, y + dy + smallerSide)

            balanceFrame.Layout(bounds)

            hoop.AnchorX <- 0.0
            hoop.AnchorY <- 0.0
            hoop.Scale <- smallerSide / (referenceHoopRadius * 2.0)
            hoop.Layout(bounds)

    override this.OnMeasure(widthConstraint, heightConstraint) =
        let smallerRequestedSize = min widthConstraint heightConstraint |> min 200.0
        let minWidth = 
            match state with
            | Uninitialized -> 0.0
            | Empty -> 50.0
            | NonEmpty(_) -> 
                let sizeFactor = 1.1 // maybe calculate actual factor based on geometry?
                balanceFrame.Measure(smallerRequestedSize, smallerRequestedSize).Request.Width * sizeFactor
        let sizeToRequest = max smallerRequestedSize minWidth
        SizeRequest(Size(sizeToRequest, sizeToRequest), Size(minWidth, minWidth))
    
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
                hoop.Children.Clear()
                this.GetHoopShapes(segments) |> Seq.iter hoop.Children.Add
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
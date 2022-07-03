namespace Frontend.FSharp

open System
open System.Linq
open System.Text
open System.Globalization
open System.IO
open Xamarin.Forms
open Xamarin.Forms.Shapes
open System.Threading.Tasks
open System.ComponentModel
open Microsoft.FSharp.Collections

type SegmentInfo = 
    {
        Color: Color
        Amount: decimal
    }
type Percentage =  { mutable Percent: double;  Stroke: Brush } 


module HoopChart =

    let centerX = 50.
    let centerY = 50.

    let computeCartesianCoordinate (angle:double) (radius:double) = 

        let angleRad = (Math.PI / 180.) * (angle - 90.)

        let x = radius *  Math.Cos(angleRad) + (radius + centerX)
        
        let y = radius *  Math.Sin(angleRad) + (radius + centerY)

        Point(x,y)

    let renderArc (pathRoot:Path) (pathFigure:PathFigure) (arc:ArcSegment) (startAngle:double) (endAngle:double) = 
        
        let radius = 150.
        let angle =  0.
        let largeArc = 
            if endAngle > 180. then
                true
            else
                false

        pathRoot.StrokeLineCap <- PenLineCap.Round
        pathRoot.StrokeThickness <- 12.
        
        arc.SweepDirection <- SweepDirection.Clockwise
        arc.Size <- Size(radius, radius)
        arc.RotationAngle <- angle
        arc.IsLargeArc <- largeArc

        pathFigure.StartPoint <- computeCartesianCoordinate startAngle  radius
        arc.Point <- computeCartesianCoordinate (startAngle + endAngle) radius
        ()

    let setArcAngle (lengthOfArc:double) (gap:double) (arcAngle:double) (path:Path) (pathFigure:PathFigure) (arcSegment:ArcSegment) =
    
        let lowestNaturalNumber = 1.
        let arcAngle =
            if lengthOfArc > lowestNaturalNumber then
                renderArc path pathFigure arcSegment (arcAngle + gap) (lengthOfArc - gap * 2.)
                arcAngle + lengthOfArc                            
            else
                renderArc path pathFigure arcSegment (arcAngle - gap) (lengthOfArc + gap * 2.)
                arcAngle + lengthOfArc
        arcAngle

    let Normalize (segments: seq<SegmentInfo>)  =
        
        let minimumShowablePercentage =  2.
        let visiblePercentageLimit = 1.
        let fullPie = 100.

        let total = segments |> Seq.sumBy(fun x -> x.Amount) |> float

        
        let innerNormalize (segments: SegmentInfo) : Option<Percentage> =
            
            let percent = float(segments.Amount) * fullPie / total
            
            if fullPie  >= percent && percent >= minimumShowablePercentage then        
      
                {Percentage.Percent = percent; Stroke = SolidColorBrush(segments.Color)} |> Some
            
            elif minimumShowablePercentage > percent && percent >= visiblePercentageLimit then
            
                {Percentage.Percent = minimumShowablePercentage; Stroke = SolidColorBrush(segments.Color)} |> Some
            
            else 
                None

        let pies =  
            segments |> Seq.choose innerNormalize//List.choose innerNormalize
        
        let wholePie = 
            pies |> Seq.sumBy(fun x -> x.Percent)

        let sortedPies = 
            pies |> Seq.sortByDescending(fun x -> x.Percent)
            
        let result = 
            sortedPies |> Seq.mapi (fun index  x -> 

                if index = 0 && wholePie > fullPie then
                    {x with Percent =  x.Percent - (wholePie - fullPie)}
                elif index = (Seq.length(sortedPies)-1) && wholePie < fullPie then
                    {x with Percent = x.Percent + (fullPie - wholePie)}
                else
                    x
            )


        result


    let makePies (segments: seq<SegmentInfo>) : Grid =
    
        let mutable arcAngle = 0.
        let slices = Seq.length(segments)
        let pies  = Normalize segments
        let grid = Grid(HorizontalOptions = LayoutOptions.Center)
        
        for pie in pies do

            if slices > 1 then

                
                let lengthOfArc = ( pie.Percent ) * 360. / 100.
                let gap = 2.5    

                let path = Path(Stroke = pie.Stroke)
                let pathG = PathGeometry ()
                let pathFC = PathFigureCollection()
                let pathF =  PathFigure()
                let pathSC = PathSegmentCollection()
                let arcS = ArcSegment()

                arcAngle <- setArcAngle lengthOfArc gap arcAngle path pathF arcS
                    
                path.Data <- pathG
                pathG.Figures <- pathFC
                pathFC.Add(pathF)
                pathF.Segments.Add(arcS)

                grid.Children.Add(path)
            
            else
                
                let lengthOfArc = 360.
                let gap = 0.
                
                let path = Path(Stroke = pie.Stroke)
                let pathG = PathGeometry ()
                let pathFC = PathFigureCollection()
                let pathF =  PathFigure()
                let pathSC = PathSegmentCollection()
                let arcS = ArcSegment()
                
                renderArc path pathF arcS (arcAngle + gap) (lengthOfArc - gap * 2.)
                
                path.Data <- pathG
                pathG.Figures <- pathFC
                pathFC.Add(pathF)
                pathF.Segments.Add(arcS)

                grid.Children.Add(path)                          

        grid




type HoopChartView() =
    inherit ContentView()

    let balanceLabel = 
        Label( 
            Text = "...", 
            HorizontalTextAlignment = TextAlignment.Center,
            FontSize = 25.)
            
    let balanceTagLabel = 
        Label( 
            Text = "Amount Balance", 
            FontSize = 15., 
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center)
    
    let balanceFrame = 
        let frame = 
            // we add {V|H}Options=Center* not only to the Label, as a workaround to
            // https://github.com/xamarin/Xamarin.Forms/issues/4655
            Frame(
                HasShadow=false,
                BackgroundColor=Color.Transparent,
                BorderColor=Color.Transparent,
                HorizontalOptions = LayoutOptions.Center, 
                VerticalOptions = LayoutOptions.Center,
                TranslationX = HoopChart.centerX / 2.0,
                TranslationY = HoopChart.centerY / 2.0,
                Padding=Thickness(0.0),
                Margin=Thickness(0.0))
        let stackLayout = 
            StackLayout(
                Orientation=StackOrientation.Vertical,
                VerticalOptions=LayoutOptions.CenterAndExpand,
                HorizontalOptions=LayoutOptions.Center,
                Margin=Thickness(0.0))
        stackLayout.Children.Add(balanceLabel)
        stackLayout.Children.Add(balanceTagLabel)
        frame.Content <- stackLayout

        frame

    static let segmentsSourceProperty =
        BindableProperty.Create("SegmentsSource",
                                typeof<seq<SegmentInfo>>, typeof<HoopChartView>, null) 
    static let defaultImageSourceProperty =
        BindableProperty.Create("DefaultImageSource",
                                typeof<ImageSource>, typeof<HoopChartView>, null)

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
                    self.SegmentsSource.Where(fun s -> s.Amount > 0.0m) |> Some
                else
                    None

            match nonZeroItems with
            | None -> ()
            | Some items when items.Any() ->
                let chart = Grid(MinimumWidthRequest = width, MinimumHeightRequest = height)
                let pies = HoopChart.makePies items
                // It's important that balanceFrame is added after pie chart, 
                // because otherwise balanceFrame's input events are blocked
                chart.Children.Add(pies)
                chart.Children.Add(balanceFrame)

                self.Content <- chart
            | Some _ ->
                self.CreateAndSetImageSource self.DefaultImageSource

    override self.OnPropertyChanged(propertyName: string) =
        base.OnPropertyChanged(propertyName)
        if propertyName = VisualElement.HeightProperty.PropertyName ||
           propertyName = VisualElement.WidthProperty.PropertyName ||
           propertyName = VisualElement.HeightRequestProperty.PropertyName || 
           propertyName = VisualElement.WidthRequestProperty.PropertyName || 
           propertyName = VisualElement.IsVisibleProperty.PropertyName ||
           propertyName = HoopChartView.SegmentsSourceProperty.PropertyName ||
           propertyName = HoopChartView.DefaultImageSourceProperty.PropertyName then
            self.Draw()
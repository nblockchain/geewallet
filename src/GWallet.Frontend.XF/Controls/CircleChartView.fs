namespace GWallet.Frontend.XF.Controls

open System
open System.Linq
open System.Globalization

open Xamarin.Forms
open Xamarin.Forms.Shapes

type SegmentInfo = 
    {
        Color: Color
        Percentage: float
    }

type CircleChartView () =
    inherit ContentView () 

    let shapesPath = @"M{0},{1} A{2},{2} 0 {3} 1 {4} {5} L {6} {7}"
            
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

       let x = float halfWidth
       let y = float halfHeight

       let converter = PathGeometryConverter ()
       let nfi = NumberFormatInfo (NumberDecimalSeparator = ".")
       let gridLayout = Grid ()

       if items.Count() = 1 then
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
            failwith "Not supported"

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

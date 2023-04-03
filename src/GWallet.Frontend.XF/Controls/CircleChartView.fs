#if XAMARIN
namespace GWallet.Frontend.XF.Controls
#else
namespace GWallet.Frontend.Maui.Controls
// added because of deprecated expansion options for StackLayout in using LayoutOptions.FillAndExpand
#nowarn "44"
#endif

open System
open System.Linq
open System.Globalization

#if !XAMARIN
open Microsoft.Maui
open Microsoft.Maui.Controls
open Microsoft.Maui.Graphics
open Microsoft.Maui.Controls.Shapes
#else
open Xamarin.Forms
open Xamarin.Forms.Shapes
#endif

type SegmentInfo =
    {
        Color: Color
        Percentage: float
    }

type CircleChartView () =
    inherit ContentView () 

    // "Z" closes the shape. Without it, our shape would not be drawn in Maui.
    let shapesPath = @"M{0},{1} A{2},{2} 0 {3} 1 {4} {5} L {6} {7}Z"

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
                                typeof<Color>, typeof<CircleChartView>,
#if XAMARIN                                
                                Color.Transparent
#else
                                Colors.Transparent
#endif
                                )
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
                    
                    let sectorIsTooSmall =
#if !XAMARIN && GTK                        
                        let arcLength = item.Percentage * 2.0 * Math.PI * radius
                        arcLength < 1.0
#else
                        false
#endif
                    if not sectorIsTooSmall then
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
#if !XAMARIN
                        // workaround for https://github.com/dotnet/maui/issues/9089
                        helperView.Aspect <- enum 4
#endif
                        gridLayout.Children.Add helperView

                    addSliceToView tail endPercentage
                   
            let itemsList = items |> Seq.toList
            addSliceToView itemsList 0.

       self.Content <- gridLayout :> View
       ()         

    member private self.CreateAndSetImageSource (imageSource : ImageSource) =
        let image =
            Image (
                // TODO: [FS0044] This construct is deprecated. The StackLayout expansion options are deprecated; please use a Grid instead.
                HorizontalOptions = LayoutOptions.FillAndExpand,
                VerticalOptions = LayoutOptions.FillAndExpand,
                Aspect = Aspect.AspectFit,
                Source = imageSource
            )
            
#if !XAMARIN && GTK
        // TODO: We should revert this when Image.Aspect is fixed for Maui/Gtk
        let size = min self.Width self.Height
        image.WidthRequest <- size
        image.HeightRequest <- size
#endif
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
                if isNull self.SegmentsSource then
                    None
                else
                    self.SegmentsSource.Where(fun s -> s.Percentage > 0.) |> Some


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

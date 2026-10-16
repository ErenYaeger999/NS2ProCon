using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NS2Pro.Core;
using Windows.Foundation;
using ShapePath = Microsoft.UI.Xaml.Shapes.Path;

namespace NS2Pro.App.Controls;

/// <summary>Editable native vector illustration based on the official NS2 Pro front view.</summary>
public sealed class ControllerFaceView : UserControl, IDisposable
{
    private readonly Dictionary<string, Key> keys = new();
    private readonly TranslateTransform leftMovement = new(), rightMovement = new();
    private readonly Ellipse leftRim, rightRim;
    private readonly Brush accent = Solid("228F85"), ink = Solid("BFC8CB");
    private readonly Brush keyFill = Gradient("41484C", "252C30");
    private readonly Brush shoulderFill = Gradient("D3D9DA", "9CA7AC");
    private ControllerInput? previous;
    private bool updated;
    private sealed record Key(Shape Surface, Brush Rest, TranslateTransform Movement);

    public ControllerFaceView()
    {
        Height = 320;
        IsHitTestVisible = false;
        var canvas = new Canvas { Width = 975, Height = 724 };
        // Light shoulder covers sit above the dark shell; the grips themselves are dark.
        AddKey(canvas, "L", Path("M 146 111 C 167 61 230 28 305 26 C 339 26 360 43 377 61 L 352 101 Z", shoulderFill), shoulderFill, "", 0, 0);
        AddKey(canvas, "R", Path("M 599 61 C 616 43 637 26 671 26 C 746 28 809 61 830 111 L 624 101 Z", shoulderFill), shoulderFill, "", 0, 0);
        var body = Path("M 147 101 C 185 63 273 39 381 42 L 594 42 C 702 39 790 63 828 101 C 865 149 883 280 899 398 L 925 534 C 938 607 911 666 865 672 C 823 678 790 639 759 586 L 713 506 C 705 491 691 486 670 486 L 305 486 C 284 486 270 491 262 506 L 216 586 C 185 639 152 678 110 672 C 64 666 37 607 50 534 L 76 398 C 92 280 110 149 147 101 Z", Gradient("3E474D", "20272B"));
        body.Stroke = Solid("141C21"); body.StrokeThickness = 2; canvas.Children.Add(body);
        var panel = Path("M 158 112 C 220 72 340 64 488 65 C 636 64 756 72 818 112 C 853 179 864 298 840 373 C 822 430 785 460 728 461 L 248 461 C 191 460 154 430 136 373 C 112 298 123 179 158 112 Z", Gradient("41494E", "2A3237"));
        panel.Stroke = Solid("1B2328"); panel.StrokeThickness = 2; canvas.Children.Add(panel);
        // Sticks are separate vector groups, translated directly by the calibrated raw axes.
        leftRim = AddStick(canvas, 250, 224, 80, leftMovement);
        rightRim = AddStick(canvas, 601, 351, 77, rightMovement);
        AddRoundKey(canvas,"X",715,163,35,"X"); AddRoundKey(canvas,"Y",645,224,35,"Y");
        AddRoundKey(canvas,"A",785,224,35,"A"); AddRoundKey(canvas,"B",715,285,35,"B");
        AddRoundKey(canvas,"−",382,153,21,"−",22); AddRoundKey(canvas,"+",593,153,21,"+",22);
        var home = AddRoundKey(canvas,"HOME",541,224,23,"",27);
        var homeSymbol = Path("M 12 0 L 25 11 L 22 11 L 22 25 L 15 25 L 15 17 L 9 17 L 9 25 L 2 25 L 2 11 L 0 11 Z", Solid("C5CED1"));
        Canvas.SetLeft(homeSymbol, 15); Canvas.SetTop(homeSymbol, 13); home.Children.Add(homeSymbol);
        AddSquareKey(canvas,"截图",433,224,38,"○",22);
        AddSquareKey(canvas,"C",487,415,36,"C",22);
        var dpad = Path("M 338 280 Q 338 274 344 274 L 375 274 Q 381 274 381 280 L 381 318 L 420 318 Q 426 318 426 324 L 426 355 Q 426 361 420 361 L 381 361 L 381 400 Q 381 406 375 406 L 344 406 Q 338 406 338 400 L 338 361 L 299 361 Q 293 361 293 355 L 293 324 Q 293 318 299 318 L 338 318 Z", keyFill);
        dpad.Stroke=Solid("121A1F"); dpad.StrokeThickness=5; canvas.Children.Add(dpad);
        AddDpadKey(canvas,"↑","M 342 278 L 377 278 L 377 321 L 342 321 Z",359,296,"▲");
        AddDpadKey(canvas,"↓","M 342 358 L 377 358 L 377 402 L 342 402 Z",359,382,"▼");
        AddDpadKey(canvas,"←","M 297 322 L 340 322 L 340 357 L 297 357 Z",315,339,"◀");
        AddDpadKey(canvas,"→","M 379 322 L 422 322 L 422 357 L 379 357 Z",404,339,"▶");
        var mark = new Switch2Mark { Width = 128, Height = 106 };
        Canvas.SetLeft(mark, 424); Canvas.SetTop(mark, 76); canvas.Children.Add(mark);
        // Controls outside the front silhouette are clearly labeled indicators, not fake body parts.
        Indicator(canvas,"ZL",230,13); Indicator(canvas,"ZR",746,13);
        Indicator(canvas,"GL",379,698); Indicator(canvas,"GR",487,698); Indicator(canvas,"LS",595,698); Indicator(canvas,"RS",703,698);
        Content=new Viewbox { Child=canvas, Stretch=Stretch.Uniform };
        AutomationProperties.SetName(this,"NS2 Pro 正面矢量预览，按键高亮，摇杆随输入移动");
    }
    private Ellipse AddStick(Canvas canvas,double x,double y,double radius,TranslateTransform movement)
    {
        Circle(canvas,x,y,radius,Gradient("20282D","171F24"),Solid("4A555D"),2);
        var group=new Canvas { Width=radius*2, Height=radius*2, RenderTransform=movement };
        Canvas.SetLeft(group,x-radius);Canvas.SetTop(group,y-radius);canvas.Children.Add(group);
        var rim=Circle(group,radius,radius,radius*.71,Gradient("67737A","252F35"),Solid("859098"),2);
        Circle(group,radius,radius,radius*.64,Gradient("3B474F","202A31"),Solid("151E24"),3);
        Circle(group,radius,radius,radius*.54,Gradient("3E4A52","2D3840"),Solid("52616A"),1);
        return rim;
    }
    private Canvas AddRoundKey(Canvas canvas,string name,double x,double y,double radius,string label,double font=30)
    {
        var group=new Canvas { Width=radius*2+8, Height=radius*2+8 };
        Canvas.SetLeft(group,x-radius-4);Canvas.SetTop(group,y-radius-4);canvas.Children.Add(group);
        Circle(group,radius+4,radius+6,radius+1,Solid("172025"),null,0);
        var face=Circle(group,radius+4,radius+3,radius,keyFill,Solid("131C22"),2);
        var movement=new TranslateTransform();group.RenderTransform=movement;
        if(label.Length > 0) Label(group,label,radius+4,radius+3,font,"D4DCDD");keys[name]=new(face,keyFill,movement);
        return group;
    }
    private void AddSquareKey(Canvas canvas,string name,double x,double y,double size,string label,double font)
    {
        var group=new Canvas { Width=size,Height=size };Canvas.SetLeft(group,x-size/2);Canvas.SetTop(group,y-size/2);canvas.Children.Add(group);
        var shape=new Rectangle { Width=size,Height=size,RadiusX=7,RadiusY=7,Fill=keyFill,Stroke=Solid("141D23"),StrokeThickness=2 };
        group.Children.Add(shape);Label(group,label,size/2,size/2,font,"C5CED1");
        var movement=new TranslateTransform();group.RenderTransform=movement;keys[name]=new(shape,keyFill,movement);
    }
    private void AddDpadKey(Canvas canvas,string name,string data,double x,double y,string label)
    {
        var shape=Path(data,Solid("000000",0));canvas.Children.Add(shape);
        Label(canvas,label,x,y,12,"A6B1B7"); keys[name]=new(shape,shape.Fill,new());
    }
    private void AddKey(Canvas canvas,string name,Shape shape,Brush rest,string label,double x,double y)
    {
        var movement=new TranslateTransform();shape.RenderTransform=movement;canvas.Children.Add(shape);keys[name]=new(shape,rest,movement);
    }
    private void Indicator(Canvas canvas,string name,double x,double y)
    {
        var rect=new Rectangle { Width=70,Height=24,RadiusX=10,RadiusY=10,Fill=Solid("E7ECEC"),Stroke=Solid("D5DEDF"),StrokeThickness=1 };
        Canvas.SetLeft(rect,x-35);Canvas.SetTop(rect,y-12);canvas.Children.Add(rect);
        Label(canvas,name,x,y,13,"586B73",true);keys[name]=new(rect,rect.Fill,new());
    }
    private static Ellipse Circle(Canvas canvas,double x,double y,double r,Brush fill,Brush? stroke,double thickness)
    {
        var circle=new Ellipse { Width=r*2,Height=r*2,Fill=fill,Stroke=stroke,StrokeThickness=thickness };Canvas.SetLeft(circle,x-r);Canvas.SetTop(circle,y-r);canvas.Children.Add(circle);return circle;
    }
    private static void Label(Canvas canvas,string label,double x,double y,double font,string color,bool bold=false)
    {
        var slot=new Grid { Width=110,Height=font*1.5 };
        slot.Children.Add(new TextBlock { Text=label,FontSize=font,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Foreground=Solid(color),FontWeight=bold?Microsoft.UI.Text.FontWeights.SemiBold:Microsoft.UI.Text.FontWeights.Normal });
        Canvas.SetLeft(slot,x-55);Canvas.SetTop(slot,y-font*.75);canvas.Children.Add(slot);
    }
    private static ShapePath Path(string data,Brush fill)
    {
        var path=(ShapePath)XamlReader.Load($"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{data}'/>");path.Fill=fill;return path;
    }
    private static SolidColorBrush Solid(string hex,byte alpha=255)=>new(ColorHelper.FromArgb(alpha,Convert.ToByte(hex[..2],16),Convert.ToByte(hex[2..4],16),Convert.ToByte(hex[4..6],16)));
    private static LinearGradientBrush Gradient(string top,string bottom)=>new() { StartPoint=new Point(0,0),EndPoint=new Point(.3,1),GradientStops={new GradientStop { Color=Solid(top).Color,Offset=0 },new GradientStop { Color=Solid(bottom).Color,Offset=1 }} };
    public void Update(ControllerInput? state)
    {
        if(updated&&ReferenceEquals(previous,state))return; previous=state;updated=true;
        foreach(var (name,key) in keys) {
            bool down=ControllerProtocol.IsPressed(name,state);key.Surface.Fill=down?accent:key.Rest;key.Movement.Y=down?2:0;
        }
        leftMovement.X=(state?.UnfilteredLeft.X??0)*20;leftMovement.Y=-(state?.UnfilteredLeft.Y??0)*20;
        rightMovement.X=(state?.UnfilteredRight.X??0)*20;rightMovement.Y=-(state?.UnfilteredRight.Y??0)*20;
        leftRim.Stroke=ControllerProtocol.IsPressed("LS",state)?accent:ink;rightRim.Stroke=ControllerProtocol.IsPressed("RS",state)?accent:ink;
    }
    public void Dispose() { }
}

using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SdqTerminal;

internal static partial class Program
{
    private static void CheckAutomaticSplit()
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var window = new MainWindow();
        var tabs=(IList)typeof(MainWindow).GetField("_tabs",flags)!.GetValue(window)!;
        var tabType=typeof(MainWindow).GetNestedType("TerminalTab",BindingFlags.NonPublic)!;
        var profile=typeof(MainWindow).GetNestedType("TerminalProfile",BindingFlags.NonPublic)!;
        object? Call(string name,params object?[] args)=>typeof(MainWindow).GetMethods(flags).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(window,args);
        object Get(object o,string p)=>o.GetType().GetProperty(p)!.GetValue(o)!;
        var pane=Call("CreatePane",Enum.ToObject(profile,0))!;
        var tab=Activator.CreateInstance(tabType,pane)!; tabs.Add(tab); Call("ShowTab",tab);
        var root=(FrameworkElement)window.Content;
        void Layout(double width=980,double height=740)
        {
            for(var i=0;i<3;i++)
            {
                root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
                root.Dispatcher.Invoke(()=>{},DispatcherPriority.ContextIdle);
            }
        }
        Rect Bounds(object p)
        {
            var frame=(Border)Get(p,"Frame");
            return new Rect(frame.TranslatePoint(new Point(),root),new Size(frame.ActualWidth,frame.ActualHeight));
        }
        Layout();
        if (((MenuItem)window.FindName("MovePaneMenuItem")).IsEnabled) throw new Exception("Move pane command is enabled for an unsplit tab");
        var direction=Call("GetSplitDirection",pane)!;
        if(direction.ToString()!="SideBySide")throw new Exception("Wide pane did not prefer side-by-side split");
        var right=Call("AddSplitPane",tab,pane,direction)!;Layout();
        var leftBounds=Bounds(pane);var rightBounds=Bounds(right);
        if(rightBounds.Left<=leftBounds.Left || Math.Abs(rightBounds.Top-leftBounds.Top)>1)throw new Exception("Initial horizontal split is misplaced");
        // Make only the right pane narrow: its next split must stack, leaving the left pane alone.
        var node=Get(tab,"LayoutRoot");node.GetType().GetProperty("Ratio")!.SetValue(node,.65);
        Call("LayoutPanes");Layout();
        leftBounds=Bounds(pane);rightBounds=Bounds(right);
        direction=Call("GetSplitDirection",right)!;
        if(direction.ToString()!="Stacked")throw new Exception("Narrow pane did not fall back to vertical split");
        var bottom=Call("AddSplitPane",tab,right,direction)!;Layout();
        var topBounds=Bounds(right);var bottomBounds=Bounds(bottom);
        if(Math.Abs(topBounds.Left-bottomBounds.Left)>1 || Math.Abs(topBounds.Width-bottomBounds.Width)>1 || bottomBounds.Top<=topBounds.Top || Bounds(pane)!=leftBounds)
            throw new Exception("Vertical fallback moved its neighbour or split the wrong pane");
        Call("Render",pane,new TerminalBuffer(60,20).Process("Left pane / پنل اول"));
        Call("Render",right,new TerminalBuffer(30,12).Process("Top / پنل بالا"));
        Call("Render",bottom,new TerminalBuffer(30,12).Process("Bottom / پنل پایین"));Layout();
        var bitmap=new RenderTargetBitmap(980,740,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var stream=File.Create("artifacts/render-checks/automatic-vertical-split.png"))encoder.Save(stream);
        // The lower pane has neither enough width nor enough height for another split.
        if(Call("GetSplitDirection",bottom)!.ToString()!="None")throw new Exception("An unusably small pane still accepts another split");
        var otherPane=Call("CreatePane",Enum.ToObject(profile,0))!;
        var otherTab=Activator.CreateInstance(tabType,otherPane)!;tabs.Add(otherTab);
        Call("ShowTab",otherTab);Layout();Call("ShowTab",tab);Layout();
        if(Bounds(pane)!=leftBounds || Math.Abs(Bounds(bottom).Top-bottomBounds.Top)>1)throw new Exception("Tab switching lost nested split sizes");
        Call("ActivatePane",bottom);Call("MovePaneToNewTab",bottom);Layout();
        if(((IList)Get(tab,"Panes")).Count!=2)throw new Exception("Detaching a nested pane lost the remaining panes");
        Call("ShowTab",tab);Layout();
        if(Math.Abs(Bounds(right).Height-Bounds(pane).Height)>1)throw new Exception("Removing a stacked child left an empty layout cell");
        Call("ClosePane",right);Layout();
        if(((IList)Get(tab,"Panes")).Count!=1 || Bounds(pane).Width<900)throw new Exception("Collapsing split tree did not restore remaining pane");
        if (((MenuItem)window.FindName("MovePaneMenuItem")).IsEnabled) throw new Exception("Move pane command did not disable after closing the last split");
        window.Close();
        Console.WriteLine("PASS automatic narrow-pane vertical split, neighbour preservation, small-pane rejection, nested size retention and collapse/detach");
    }
}

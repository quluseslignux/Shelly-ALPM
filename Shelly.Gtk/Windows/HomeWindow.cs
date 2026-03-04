using Gtk;

namespace Shelly.Gtk.Windows;

public class HomeWindow : IShellyWindow
{
    public static Box CreateWindow()
    {
        var builder = Builder.NewFromFile("UiFiles/HomeWindow.ui");
        var box = (Box)builder.GetObject("HomeWindow")!;
        
        return box;
    }
}
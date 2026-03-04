using Gtk;
using Microsoft.Extensions.DependencyInjection;
using Shelly.Gtk.Services;
using Shelly.Gtk.Windows;

namespace Shelly.Gtk;

sealed class Program
{
    private static ServiceCollection _serviceCollection;
    public static int Main(string[] args)
    {
        CreateDependencyInjection();
        var application = global::Gtk.Application.New("com.shellyorg.shelly", Gio.ApplicationFlags.DefaultFlags);

        application.OnActivate += (sender, args) =>
        {
            var mainBuilder = Builder.NewFromFile("UiFiles/MainWindow.ui");
            var window = (ApplicationWindow)mainBuilder.GetObject("MainWindow")!;

            window.SetIconName("shelly"); 
            window.Application = application;

            var menuBuilder = Builder.NewFromFile("UiFiles/MainMenu.ui");
            var appMenu = (Gio.Menu)menuBuilder.GetObject("AppMenu")!;
            application.Menubar = appMenu;

            var quitAction = Gio.SimpleAction.New("quit", null);
            quitAction.OnActivate += (sender, args) => application.Quit();
            application.AddAction(quitAction);

            var preferencesAction = Gio.SimpleAction.New("preferences", null);
            preferencesAction.OnActivate += (sender, args) => Console.WriteLine("Preferences clicked");
            application.AddAction(preferencesAction);

            var aboutAction = Gio.SimpleAction.New("about", null);
            aboutAction.OnActivate += (sender, args) => Console.WriteLine("About clicked");
            application.AddAction(aboutAction);

            var contentArea = (Box)mainBuilder.GetObject("ContentArea")!;
    

            var homeBox = HomeWindow.CreateWindow();
            contentArea.Append(homeBox);

            window.Show();
        };

        return application.Run(args);
    }

    private static void CreateDependencyInjection()
    {
        _serviceCollection = [];
        _serviceCollection.AddSingleton<IPrivilegedOperationService, PrivilegedOperationService>();
        _serviceCollection.AddSingleton<IUnprivilegedOperationService, UnprivilegedOperationService>();
        _serviceCollection.AddTransient<HomeWindow>();
    }
}
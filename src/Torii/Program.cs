using System;
using Avalonia;

namespace Torii
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        // The XAML previewer looks for this by name
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                      .UsePlatformDetect()
                      .WithInterFont()
                      .LogToTrace();
    }
}

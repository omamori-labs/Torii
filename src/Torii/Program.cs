using System;
using Avalonia;
using Velopack;

namespace Torii
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            VelopackApp.Build().Run();

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // Previewer finds this by name
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                      .UsePlatformDetect()
                      .WithInterFont()
                      .LogToTrace();
    }
}

using System;
using Avalonia;
using Torii.Core.Library;
using Velopack;

namespace Torii
{
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            VelopackApp.Build().Run();

            if (ElevatedLink.TryRun(args, out int exitCode))
                return exitCode;

            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // Previewer finds this by name
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                      .UsePlatformDetect()
                      .WithInterFont()
                      .LogToTrace();
    }
}

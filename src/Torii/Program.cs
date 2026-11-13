using System;
using Avalonia;
using Optris.Icons.Avalonia;
using Optris.Icons.Avalonia.MaterialDesign;
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
        public static AppBuilder BuildAvaloniaApp()
        {
            IconProvider.Current.Register<MaterialDesignIconProvider>();

            return AppBuilder.Configure<App>()
                             .UsePlatformDetect()
                             .WithInterFont()
                             .LogToTrace();
        }
    }
}

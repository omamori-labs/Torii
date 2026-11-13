using Avalonia;
using Avalonia.Headless;
using Optris.Icons.Avalonia;
using Optris.Icons.Avalonia.MaterialDesign;
using Torii.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Torii.Tests
{
    public static class TestAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp()
        {
            IconProvider.Current.Register<MaterialDesignIconProvider>();

            return AppBuilder.Configure<TestApp>()
                             .WithInterFont()
                             .UseSkia()
                             .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
        }
    }
}

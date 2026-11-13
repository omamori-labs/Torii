using Avalonia;
using Avalonia.Headless;
using Torii.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Torii.Tests
{
    public static class TestAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<TestApp>()
                      .WithInterFont()
                      .UseSkia()
                      .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}

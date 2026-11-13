using System.Net.Http;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Torii.Core;
using Torii.Core.Lazer;
using Torii.Core.Releases;

namespace Torii
{
    public partial class App : Application
    {
        private static readonly HttpClient http = new HttpClient();

        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Instance folder can be passed in, handy for testing
                string folder = desktop.Args is [string given, ..] ? given : LazerInstance.DefaultDirectory();

                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainViewModel(new LazerInstance(folder), new ReleaseFeed(http), new InstanceSetup(http)),
                };
            }

            _ = SelfUpdate.RunAsync();

            base.OnFrameworkInitializationCompleted();
        }
    }
}

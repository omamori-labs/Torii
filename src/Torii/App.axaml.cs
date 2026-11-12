using System.Net.Http;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Torii.Core;

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
                // A folder on the command line is for trying Torii against a sandbox
                string data = desktop.Args is [string given, ..] ? given : OsuData.Resolve(OsuData.DefaultDirectory());

                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainViewModel(new OmamoriInstall(data), new ReleaseFeed(http), new ReleaseInstaller(http)),
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}

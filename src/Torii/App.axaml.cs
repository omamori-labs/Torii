using System.Net.Http;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Torii.Core;
using Torii.Core.Instances;
using Torii.Core.Lazer;
using Torii.Core.Library;
using Torii.Core.Releases;
using Torii.ViewModels;
using Torii.Views;

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
                LazerInstance? given = desktop.Args is [string folder, ..] ? new LazerInstance(folder) : null;

                var feed = new ReleaseFeed(http);
                var setup = new InstanceSetup(http);
                var library = new SharedLibrary(SharedLibrary.DefaultDirectory(), ElevatedLink.Request);
                var seed = new LibrarySeed();
                var store = new InstanceStore(InstanceStore.DefaultDirectory(), LazerInstance.DefaultDirectory());

                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainViewModel(store, instance => new InstanceViewModel(instance, feed, setup, library, seed), given),
                };
            }

            _ = SelfUpdate.RunAsync();

            base.OnFrameworkInitializationCompleted();
        }
    }
}

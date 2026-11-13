using System;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Torii.Core;
using Torii.Core.Instances;
using Torii.Core.Lazer;
using Torii.Core.Library;
using Torii.Core.Releases;
using Torii.Core.Settings;
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

                var settings = new SettingsStore(SettingsStore.DefaultFile());
                AppSettings current = settings.Load();

                var shell = new MainViewModel(store, instance => new InstanceViewModel(instance, feed, setup, library, seed), given, current.SelectedInstance);
                var window = new MainWindow { DataContext = shell };

                if (current.WindowWidth is double width && current.WindowHeight is double height)
                {
                    window.Width = Math.Max(width, window.MinWidth);
                    window.Height = Math.Max(height, window.MinHeight);
                }

                if (given == null)
                    shell.Remember = directory => settings.Save(current = current with { SelectedInstance = directory });

                window.Closing += (_, _) =>
                {
                    if (window.WindowState == WindowState.Normal)
                        settings.Save(current = current with { WindowWidth = window.ClientSize.Width, WindowHeight = window.ClientSize.Height });
                };

                desktop.MainWindow = window;
            }

            _ = SelfUpdate.RunAsync();

            base.OnFrameworkInitializationCompleted();
        }
    }
}

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Torii.Core;

namespace Torii
{
    public partial class App : Application
    {
        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // A folder on the command line is for trying Torii against a sandbox
                string data = desktop.Args is [string given, ..] ? given : OsuData.Resolve(OsuData.DefaultDirectory());

                desktop.MainWindow = new MainWindow { DataContext = new MainViewModel(new OmamoriInstall(data)) };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}

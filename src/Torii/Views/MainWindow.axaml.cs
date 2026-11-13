using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Torii.ViewModels;

namespace Torii.Views
{
    public partial class MainWindow : Window
    {
        private static readonly FilePickerFileType mod_zip = new FilePickerFileType("Omamori mod") { Patterns = ["*.zip"] };

        private readonly DispatcherTimer osuCheck = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };

        public MainWindow()
        {
            InitializeComponent();

            osuCheck.Tick += (_, _) => (DataContext as MainViewModel)?.RefreshOsuRunning();

            // Mods can be dropped into the folder by hand
            Activated += (_, _) => (DataContext as MainViewModel)?.RefreshMods();
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            if (DataContext is not MainViewModel model)
                return;

            model.PickModZip = pickModZip;
            model.LoadReleasesCommand.Execute(null);
            osuCheck.Start();
        }

        protected override void OnClosed(EventArgs e)
        {
            osuCheck.Stop();

            base.OnClosed(e);
        }

        private async Task<string?> pickModZip()
        {
            var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Add a mod",
                AllowMultiple = false,
                FileTypeFilter = [mod_zip],
            });

            return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
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

            osuCheck.Tick += async (_, _) =>
            {
                if (DataContext is MainViewModel model)
                    await model.RefreshAsync();
            };

            // Mods can be dropped into the folder by hand
            Activated += (_, _) => (DataContext as MainViewModel)?.RefreshMods();

            DragDrop.SetAllowDrop(this, true);
            DragDrop.AddDragOverHandler(this, onDragOver);
            DragDrop.AddDropHandler(this, onDrop);

            _ = new ReorderDrag(InstanceList, InstanceDropLine, (item, index) => (DataContext as MainViewModel)?.Move((InstanceViewModel)item, index));
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            if (DataContext is not MainViewModel model)
                return;

            model.PickModZip = pickModZip;
            model.PickOsuFolder = pickOsuFolder;
            model.ShowProblem = message => MessageDialog.ShowAsync(this, message);

            model.AskName = prompt => NameDialog.ShowAsync(this, prompt);
            model.OpenFolder = async path => await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));

            osuCheck.Start();
        }

        protected override void OnClosed(EventArgs e)
        {
            osuCheck.Stop();

            base.OnClosed(e);
        }

        private void onDragOver(object? sender, DragEventArgs e)
        {
            bool accepted = (DataContext as MainViewModel)?.Selected is { } selected
                            && selected.AcceptsMods()
                            && e.DataTransfer.TryGetFiles() is { } files
                            && files.Any(file => file.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

            e.DragEffects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void onDrop(object? sender, DragEventArgs e)
        {
            if ((DataContext as MainViewModel)?.Selected is not { } selected || e.DataTransfer.TryGetFiles() is not { } files)
                return;

            selected.AddMods(files.Select(file => file.TryGetLocalPath()).OfType<string>());
        }

        private async Task<string?> pickOsuFolder()
        {
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Pick osu!'s data folder",
                AllowMultiple = false,
            });

            return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
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

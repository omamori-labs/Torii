using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Torii.Core;

namespace Torii
{
    public sealed partial class MainViewModel : ObservableObject
    {
        private readonly OmamoriInstall install;
        private readonly ReleaseFeed feed;
        private readonly ReleaseInstaller installer;
        private readonly Func<bool> osuRunning;
        private readonly ModLibrary library;

        private string? installedVersion;

        public MainViewModel(OmamoriInstall install, ReleaseFeed feed, ReleaseInstaller installer, Func<bool>? osuRunning = null)
        {
            this.install = install;
            this.feed = feed;
            this.installer = installer;
            this.osuRunning = osuRunning ?? OsuProcess.IsRunning;
            library = new ModLibrary(install, this.osuRunning);

            DataDirectory = install.DataDirectory;
            Warning = OsuData.LooksLikeData(install.DataDirectory) ? null : "osu! hasn't been run with this as its data folder.";
            Summary = string.Empty;

            refreshInstall();
            RefreshOsuRunning();
            RefreshMods();
        }

        public ObservableCollection<ModItemViewModel> Mods { get; } = [];

        public bool HasMods => Mods.Count > 0;

        /// <summary>Whether anything in the data folder may be changed right now</summary>
        public bool CanEdit => !IsBusy && !OsuRunning;

        public string DataDirectory { get; }

        public string? Warning { get; }

        public ObservableCollection<OmamoriRelease> Releases { get; } = [];

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(InstallLabel))]
        [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
        public partial string Summary { get; private set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(InstallLabel))]
        [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
        public partial OmamoriRelease? SelectedRelease { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEdit))]
        [NotifyCanExecuteChangedFor(nameof(InstallCommand), nameof(UninstallCommand))]
        public partial bool IsBusy { get; private set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEdit))]
        [NotifyCanExecuteChangedFor(nameof(InstallCommand), nameof(UninstallCommand))]
        public partial bool OsuRunning { get; private set; }

        /// <summary>Download progress, 0 to 1</summary>
        [ObservableProperty]
        public partial double Progress { get; private set; }

        [ObservableProperty]
        public partial string? Status { get; private set; }

        public string InstallLabel
        {
            get
            {
                if (installedVersion == null || SelectedRelease == null)
                    return "Install";

                if (SelectedRelease.Version == installedVersion)
                    return "Reinstall";

                // Newest first. A version that isn't a release (a local build) has no place in the list
                int installed = Releases.ToList().FindIndex(r => r.Version == installedVersion);
                if (installed < 0)
                    return "Install";

                return Releases.IndexOf(SelectedRelease) < installed ? "Update" : "Downgrade";
            }
        }

        /// <summary>Called on a timer, the game can be opened or closed at any point</summary>
        public void RefreshOsuRunning()
        {
            bool wasRunning = OsuRunning;
            OsuRunning = osuRunning();

            // The game writes the state files, so what's on screen may be stale once it closes
            if (wasRunning && !OsuRunning)
                RefreshMods();
        }

        /// <summary>Rereads the mods folder. Rows are updated in place when the same mods are still there</summary>
        public void RefreshMods()
        {
            var mods = library.List();

            if (mods.Count == Mods.Count && mods.Zip(Mods).All(pair => pair.First.Directory == pair.Second.Mod.Directory))
            {
                foreach ((InstalledMod mod, ModItemViewModel row) in mods.Zip(Mods))
                    row.Mod = mod;

                return;
            }

            Mods.Clear();
            foreach (InstalledMod mod in mods)
                Mods.Add(new ModItemViewModel(this, mod));

            OnPropertyChanged(nameof(HasMods));
        }

        internal void SetEnabled(ModItemViewModel row, bool enabled)
        {
            try
            {
                library.SetEnabled(row.Mod.Manifest!.Id, enabled);
                Status = $"{row.Label} is {(enabled ? "on" : "off")} the next time osu! starts.";
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                Status = $"Couldn't turn {row.Label} {(enabled ? "on" : "off")}. {e.Message}";
            }

            RefreshMods();
            row.Revert();
        }

        [RelayCommand]
        public async Task LoadReleasesAsync()
        {
            Status = "Checking for releases...";

            try
            {
                var releases = await feed.ListAsync();

                Releases.Clear();
                foreach (OmamoriRelease release in releases)
                    Releases.Add(release);

                SelectedRelease = Releases.FirstOrDefault();
                Status = Releases.Count == 0 ? "No releases found." : null;
            }
            catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException)
            {
                Status = $"Couldn't check for releases. {e.Message}";
            }
        }

        [RelayCommand(CanExecute = nameof(canInstall))]
        public async Task InstallAsync()
        {
            OmamoriRelease release = SelectedRelease!;

            IsBusy = true;
            Progress = 0;
            Status = $"Downloading Omamori {release.Version}...";

            try
            {
                await installer.InstallAsync(install, release, new Progress<double>(p => Progress = p));
                Status = $"Omamori {release.Version} is installed.";
            }
            catch (Exception e) when (e is HttpRequestException or InvalidDataException or IOException or UnauthorizedAccessException or TaskCanceledException)
            {
                Status = $"Couldn't install Omamori {release.Version}. {e.Message}";
            }
            finally
            {
                IsBusy = false;
                refreshInstall();
            }
        }

        [RelayCommand(CanExecute = nameof(canUninstall))]
        public void Uninstall()
        {
            try
            {
                OmamoriInstaller.Uninstall(install);
                Status = "Omamori is uninstalled. Your mods and their settings were kept.";
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Status = $"Couldn't uninstall Omamori. {e.Message}";
            }
            finally
            {
                refreshInstall();
            }
        }

        private bool canInstall() => SelectedRelease != null && !IsBusy && !OsuRunning;

        private bool canUninstall() => install.IsInstalled && !IsBusy && !OsuRunning;

        private void refreshInstall()
        {
            installedVersion = install.IsInstalled ? install.ReadVersion() ?? "unknown" : null;

            Summary = installedVersion switch
            {
                null => "Not installed",
                "unknown" => "Installed, version unknown",
                _ => $"Version {installedVersion} is installed",
            };
        }
    }
}

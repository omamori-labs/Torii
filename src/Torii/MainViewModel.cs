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

        private string? installedVersion;

        public MainViewModel(OmamoriInstall install, ReleaseFeed feed, ReleaseInstaller installer, Func<bool>? osuRunning = null)
        {
            this.install = install;
            this.feed = feed;
            this.installer = installer;
            this.osuRunning = osuRunning ?? OsuProcess.IsRunning;

            DataDirectory = install.DataDirectory;
            Warning = OsuData.LooksLikeData(install.DataDirectory) ? null : "osu! hasn't been run with this as its data folder.";
            Summary = string.Empty;

            refreshInstall();
            RefreshOsuRunning();
        }

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
        [NotifyCanExecuteChangedFor(nameof(InstallCommand), nameof(UninstallCommand))]
        public partial bool IsBusy { get; private set; }

        [ObservableProperty]
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
        public void RefreshOsuRunning() => OsuRunning = osuRunning();

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

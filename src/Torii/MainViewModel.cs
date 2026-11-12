using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
        private readonly LazerInstance instance;
        private readonly ReleaseFeed feed;
        private readonly InstanceSetup setup;
        private readonly Func<bool> osuRunning;
        private readonly ModLibrary library;
        private readonly GameLauncher launcher;

        private string? installedVersion;

        public MainViewModel(LazerInstance instance, ReleaseFeed feed, InstanceSetup setup, Func<bool>? osuRunning = null, GameLauncher? launcher = null)
        {
            this.instance = instance;
            this.feed = feed;
            this.setup = setup;
            this.osuRunning = osuRunning ?? (() => OsuProcess.IsRunningIn(instance.Directory));
            library = new ModLibrary(instance.Omamori, this.osuRunning);
            this.launcher = launcher ?? new GameLauncher();

            Summary = string.Empty;

            refreshInstance();
            RefreshOsuRunning();
            RefreshMods();
        }

        public ObservableCollection<ModItemViewModel> Mods { get; } = [];

        public bool HasMods => Mods.Count > 0;

        /// <summary>Whether anything in the instance may be changed right now</summary>
        public bool CanEdit => !IsBusy && !OsuRunning;

        public ObservableCollection<OmamoriRelease> Releases { get; } = [];

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(InstallLabel))]
        public partial string Summary { get; private set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(InstallLabel))]
        [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
        public partial OmamoriRelease? SelectedRelease { get; set; }

        /// <summary>Whether there is a game to play</summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
        public partial bool IsSetUp { get; private set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEdit))]
        [NotifyCanExecuteChangedFor(nameof(InstallCommand), nameof(PlayCommand))]
        public partial bool IsBusy { get; private set; }

        /// <summary>The instance's game, the player's own osu! doesn't count</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEdit))]
        [NotifyCanExecuteChangedFor(nameof(InstallCommand), nameof(PlayCommand))]
        public partial bool OsuRunning { get; private set; }

        /// <summary>Progress of the current download, 0 to 1</summary>
        [ObservableProperty]
        public partial double Progress { get; private set; }

        [ObservableProperty]
        public partial string? Status { get; private set; }

        public string InstallLabel
        {
            get
            {
                if (!instance.IsInstalled)
                    return "Set up";

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

            try
            {
                await setup.SetUpAsync(instance, release, new Progress<SetupProgress>(p => showProgress(release, p)));
                Status = $"Omamori {release.Version} is ready.";
            }
            catch (Exception e) when (e is HttpRequestException or InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException or TaskCanceledException
                                          or PlatformNotSupportedException)
            {
                Status = $"Couldn't set up Omamori {release.Version}. {e.Message}";
            }
            finally
            {
                IsBusy = false;
                refreshInstance();
                RefreshMods();
            }
        }

        [RelayCommand(CanExecute = nameof(canPlay))]
        public void Play()
        {
            try
            {
                launcher.Launch(instance);
                Status = "Starting osu!...";
            }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception or IOException)
            {
                Status = $"Couldn't start osu!. {e.Message}";
            }

            RefreshOsuRunning();
        }

        private bool canInstall() => SelectedRelease != null && !IsBusy && !OsuRunning;

        private bool canPlay() => IsSetUp && !IsBusy && !OsuRunning;

        private void showProgress(OmamoriRelease release, SetupProgress progress)
        {
            // Progress<T> can deliver the last report after the setup has finished
            if (!IsBusy)
                return;

            Progress = progress.Fraction;
            Status = progress.Stage switch
            {
                SetupStage.DownloadingOmamori => $"Downloading Omamori {release.Version}...",
                SetupStage.DownloadingOsu => "Downloading osu!...",
                _ => "Installing...",
            };
        }

        private void refreshInstance()
        {
            string? osu = instance.IsInstalled ? instance.ReadVersion() ?? "unknown version" : null;
            installedVersion = instance.Omamori.IsInstalled ? instance.Omamori.ReadVersion() ?? "unknown" : null;

            IsSetUp = osu != null;

            Summary = (osu, installedVersion) switch
            {
                (null, _) => "Not set up yet",
                (_, null) => $"osu! {osu} without Omamori",
                _ => $"Omamori {installedVersion} on osu! {osu}",
            };
        }
    }
}

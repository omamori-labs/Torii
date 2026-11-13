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
using Torii.Core.Lazer;
using Torii.Core.Library;
using Torii.Core.Mods;
using Torii.Core.Omamori;
using Torii.Core.Releases;

namespace Torii.ViewModels
{
    public sealed partial class MainViewModel : ObservableObject
    {
        private readonly LazerInstance instance;
        private readonly ReleaseFeed feed;
        private readonly InstanceSetup setup;
        private readonly SharedLibrary library;
        private readonly Func<bool> osuRunning;
        private readonly GameLauncher launcher;

        private string? installedVersion;

        public MainViewModel(LazerInstance instance, ReleaseFeed feed, InstanceSetup setup, SharedLibrary library, Func<bool>? osuRunning = null, GameLauncher? launcher = null)
        {
            this.instance = instance;
            this.feed = feed;
            this.setup = setup;
            this.library = library;
            this.osuRunning = osuRunning ?? (() => OsuProcess.IsRunningIn(instance.Directory));
            this.launcher = launcher ?? new GameLauncher();

            ModList = new ModListViewModel(new ModLibrary(instance.Omamori, this.osuRunning), status => Status = status);
            ModList.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ModListViewModel.HasMods))
                    OnPropertyChanged(nameof(NoMods));
            };

            Summary = string.Empty;

            refreshInstance();
            RefreshOsuRunning();
        }

        public ModListViewModel ModList { get; }

        public bool NoMods => IsSetUp && !ModList.HasMods;

        public Func<Task<string?>>? PickModZip { get; set; }

        public bool CanEdit => !IsBusy && !OsuRunning;

        public ObservableCollection<OmamoriRelease> Releases { get; } = [];

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(InstallLabel))]
        public partial string Summary { get; private set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(InstallLabel))]
        [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
        public partial OmamoriRelease? SelectedRelease { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(NoMods))]
        [NotifyCanExecuteChangedFor(nameof(PlayCommand), nameof(AddModCommand))]
        public partial bool IsSetUp { get; private set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEdit))]
        [NotifyCanExecuteChangedFor(nameof(InstallCommand), nameof(PlayCommand), nameof(AddModCommand))]
        public partial bool IsBusy { get; private set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEdit))]
        [NotifyCanExecuteChangedFor(nameof(InstallCommand), nameof(PlayCommand), nameof(AddModCommand))]
        public partial bool OsuRunning { get; private set; }

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

                // Local builds aren't in the list
                int installed = Releases.ToList().FindIndex(r => r.Version == installedVersion);
                if (installed < 0)
                    return "Install";

                return Releases.IndexOf(SelectedRelease) < installed ? "Update" : "Downgrade";
            }
        }

        public void RefreshOsuRunning()
        {
            bool wasRunning = OsuRunning;
            OsuRunning = osuRunning();

            // The game rewrites the state files while it runs
            if (wasRunning && !OsuRunning)
                ModList.Refresh();
        }

        [RelayCommand(CanExecute = nameof(canAddMod))]
        public async Task AddModAsync()
        {
            if (PickModZip == null || await PickModZip() is not string zip)
                return;

            ModList.AddFromZip(zip);
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
                ModList.Refresh();
            }
        }

        [RelayCommand(CanExecute = nameof(canPlay))]
        public async Task PlayAsync()
        {
            IsBusy = true;

            try
            {
                await Task.Run(() =>
                {
                    library.Attach(instance);
                    library.Use(instance);
                });
                launcher.Launch(instance);
                Status = "Starting osu!...";
            }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception or IOException or UnauthorizedAccessException)
            {
                Status = $"Couldn't start osu!. {e.Message}";
            }
            finally
            {
                IsBusy = false;
            }

            RefreshOsuRunning();
        }

        private bool canInstall() => SelectedRelease != null && !IsBusy && !OsuRunning;

        private bool canPlay() => IsSetUp && !IsBusy && !OsuRunning;

        private bool canAddMod() => IsSetUp && !IsBusy && !OsuRunning;

        private void showProgress(OmamoriRelease release, SetupProgress progress)
        {
            // Progress<T> can fire late
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

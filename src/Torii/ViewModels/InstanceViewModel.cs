using System;
using System.Collections.Generic;
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
    public sealed partial class InstanceViewModel : ObservableObject
    {
        private readonly LazerInstance instance;
        private readonly ReleaseFeed feed;
        private readonly InstanceSetup setup;
        private readonly SharedLibrary library;
        private readonly LibrarySeed seed;
        private readonly Func<bool> osuRunning;
        private readonly GameLauncher launcher;

        private string? installedVersion;
        private bool releasesLoaded;

        public InstanceViewModel(LazerInstance instance, ReleaseFeed feed, InstanceSetup setup, SharedLibrary library, LibrarySeed seed, Func<bool>? osuRunning = null, GameLauncher? launcher = null)
        {
            this.instance = instance;
            this.feed = feed;
            this.setup = setup;
            this.library = library;
            this.seed = seed;
            this.osuRunning = osuRunning ?? (() => OsuProcess.IsRunningIn(instance.Directory));
            this.launcher = launcher ?? new GameLauncher();

            ModList = new ModListViewModel(new ModLibrary(instance.Omamori, this.osuRunning), status => Status = status, message => _ = problem(message), path => OpenFolder?.Invoke(path) ?? Task.CompletedTask);
            ModList.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ModListViewModel.HasMods))
                    OnPropertyChanged(nameof(NoMods));
            };
            ModList.Mods.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ModsLabel));

            Summary = string.Empty;

            refreshInstance();
            OsuRunning = this.osuRunning();
        }

        public ModListViewModel ModList { get; }

        public LazerInstance Instance => instance;

        public string Name => instance.Name;

        public string ModsLabel => ModList.Mods.Count == 1 ? "1 mod" : $"{ModList.Mods.Count} mods";

        public bool NoMods => IsSetUp && !ModList.HasMods;

        public Func<Task<string?>>? PickModZip { get; set; }

        public Func<string, Task>? ShowProblem { get; set; }

        public Func<string, Task>? OpenFolder { get; set; }

        public Func<Task>? RequestRename { get; set; }

        public Func<Task>? RequestDelete { get; set; }

        public bool IsManaged { get; set; }

        public Func<Task<string?>>? PickOsuFolder { get; set; }

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
        [NotifyCanExecuteChangedFor(nameof(InstallCommand), nameof(PlayCommand), nameof(AddModCommand), nameof(ImportCommand), nameof(RenameCommand), nameof(DeleteCommand))]
        public partial bool IsBusy { get; private set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEdit))]
        [NotifyCanExecuteChangedFor(nameof(InstallCommand), nameof(PlayCommand), nameof(AddModCommand), nameof(ImportCommand), nameof(RenameCommand), nameof(DeleteCommand))]
        public partial bool OsuRunning { get; private set; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
        public partial bool ImportAvailable { get; private set; }

        [ObservableProperty]
        public partial double Progress { get; private set; }

        [ObservableProperty]
        public partial bool IsIndeterminate { get; private set; }

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

        public async Task RefreshOsuRunningAsync()
        {
            bool wasRunning = OsuRunning;
            OsuRunning = osuRunning();

            if (!wasRunning || OsuRunning)
                return;

            // The game rewrites the state files while it runs
            ModList.Refresh();

            IsBusy = true;
            IsIndeterminate = true;

            try
            {
                await Task.Run(() => library.Attach(instance));
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                Status = $"Couldn't move the game's data into the library. {e.Message}";
            }
            finally
            {
                IsBusy = false;
                IsIndeterminate = false;
                refreshImport();
            }
        }

        [RelayCommand(CanExecute = nameof(canAddMod))]
        public async Task AddModAsync()
        {
            if (PickModZip == null || await PickModZip() is not string zip)
                return;

            ModList.AddFromZip(zip);
        }

        public bool AcceptsMods() => canAddMod();

        public void AddMods(IEnumerable<string> paths)
        {
            List<string> zips = [.. paths.Where(path => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))];

            if (zips.Count > 0 && canAddMod())
                ModList.AddFromZips(zips);
        }

        [RelayCommand]
        public async Task OpenFolderAsync()
        {
            if (OpenFolder != null)
                await OpenFolder(instance.Directory);
        }

        [RelayCommand(CanExecute = nameof(canManage))]
        public async Task RenameAsync()
        {
            if (RequestRename != null)
                await RequestRename();
        }

        [RelayCommand(CanExecute = nameof(canManage))]
        public async Task DeleteAsync()
        {
            if (RequestDelete != null)
                await RequestDelete();
        }

        [RelayCommand(CanExecute = nameof(canImport))]
        public async Task ImportAsync()
        {
            string? source = seed.FindSource();

            if (source == null && PickOsuFolder != null)
                source = await PickOsuFolder();

            if (source == null)
                return;

            string? version = seed.ReadSourceVersion();

            IsBusy = true;
            Progress = 0;
            Status = "Importing your osu! maps and skins...";
            string? failure = null;

            try
            {
                await seed.ImportAsync(library, source, version, new Progress<double>(fraction =>
                {
                    if (IsBusy)
                        Progress = fraction;
                }));

                Status = version == null
                    ? "Your osu! maps and skins are imported. Torii couldn't tell which osu! version they are from."
                    : "Your osu! maps and skins are imported.";
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                failure = $"Couldn't import from osu!. {e.Message}";
            }
            finally
            {
                IsBusy = false;
                refreshImport();
            }

            if (failure != null)
                await problem(failure);
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

        public async Task EnsureReleasesAsync()
        {
            if (releasesLoaded)
                return;

            releasesLoaded = true;
            await LoadReleasesAsync();
        }

        [RelayCommand(CanExecute = nameof(canInstall))]
        public async Task InstallAsync()
        {
            OmamoriRelease release = SelectedRelease!;
            string? failure = null;

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
                failure = $"Couldn't set up Omamori {release.Version}. {e.Message}";
            }
            finally
            {
                IsBusy = false;
                IsIndeterminate = false;
                refreshInstance();
                ModList.Refresh();
            }

            if (failure != null)
                await problem(failure);
        }

        [RelayCommand(CanExecute = nameof(canPlay))]
        public async Task PlayAsync()
        {
            string? failure = null;

            IsBusy = true;
            IsIndeterminate = true;

            try
            {
                await Task.Run(() =>
                {
                    if (library.FindRunningInstance() is string other)
                        throw new InvalidOperationException($"osu! is open in {Path.GetFileName(other)} and shares the library. Close it first.");

                    library.Attach(instance);
                    library.Use(instance);
                });
                launcher.Launch(instance);
                Status = null;
            }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception or IOException or UnauthorizedAccessException)
            {
                failure = $"Couldn't start osu!. {e.Message}";
            }
            finally
            {
                IsBusy = false;
                IsIndeterminate = false;
                refreshImport();
            }

            if (failure != null)
                await problem(failure);

            await RefreshOsuRunningAsync();
        }

        private async Task problem(string message)
        {
            if (ShowProblem != null)
                await ShowProblem(message);
            else
                Status = message;
        }

        private bool canInstall() => SelectedRelease != null && !IsBusy && !OsuRunning;

        private bool canPlay() => IsSetUp && !IsBusy && !OsuRunning;

        private bool canAddMod() => IsSetUp && !IsBusy && !OsuRunning;

        private bool canImport() => ImportAvailable && !IsBusy && !OsuRunning;

        private bool canManage() => IsManaged && !IsBusy && !OsuRunning;

        private void refreshImport() => ImportAvailable = LibrarySeed.CanSeed(library, instance);

        private void showProgress(OmamoriRelease release, SetupProgress progress)
        {
            // Progress<T> can fire late
            if (!IsBusy)
                return;

            Progress = progress.Fraction;
            IsIndeterminate = progress.Stage == SetupStage.Installing && progress.Fraction < 1;
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

            refreshImport();
        }
    }
}

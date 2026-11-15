using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Torii.Core.Mods;

namespace Torii.ViewModels
{
    public sealed class ModBrowserViewModel : ObservableObject
    {
        private readonly ModIndexFeed feed;
        private readonly IndexedModInstaller installer;
        private readonly ModLibrary library;
        private readonly Action refreshInstalled;
        private readonly Action<string?> report;
        private readonly Func<string, Task> problem;
        private readonly Func<bool> canInstall;
        private readonly Action<bool> setBusy;
        private readonly Action<double> setProgress;

        private IReadOnlyList<IndexedMod> index = [];
        private string search = string.Empty;
        private string? failure;
        private bool isLoading;
        private bool loaded;

        public ModBrowserViewModel(ModIndexFeed feed, IndexedModInstaller installer, ModLibrary library, Action refreshInstalled, Action<string?> report, Func<string, Task> problem, Func<bool> canInstall, Action<bool> setBusy, Action<double> setProgress)
        {
            this.feed = feed;
            this.installer = installer;
            this.library = library;
            this.refreshInstalled = refreshInstalled;
            this.report = report;
            this.problem = problem;
            this.canInstall = canInstall;
            this.setBusy = setBusy;
            this.setProgress = setProgress;
        }

        public ObservableCollection<BrowsedModViewModel> Mods { get; } = [];

        public ObservableCollection<BrowsedModViewModel> Visible { get; } = [];

        public bool CanInstall => canInstall();

        public bool NoMatches => loaded && Visible.Count == 0;

        public string Search
        {
            get => search;
            set
            {
                if (SetProperty(ref search, value ?? string.Empty))
                    applyFilter();
            }
        }

        public string? Failure
        {
            get => failure;
            private set => SetProperty(ref failure, value);
        }

        public bool IsLoading
        {
            get => isLoading;
            private set => SetProperty(ref isLoading, value);
        }

        public async Task EnsureLoadedAsync()
        {
            if (loaded || IsLoading)
            {
                refreshRows();
                return;
            }

            IsLoading = true;
            Failure = null;

            try
            {
                index = await feed.ListAsync();
                loaded = true;

                Mods.Clear();
                foreach (IndexedMod mod in index.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
                    Mods.Add(new BrowsedModViewModel(this, mod));

                refreshRows();
            }
            catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException)
            {
                Failure = $"Couldn't load the mod list. {e.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        public void CanInstallChanged()
        {
            OnPropertyChanged(nameof(CanInstall));

            foreach (BrowsedModViewModel row in Mods)
                row.CanInstallChanged();
        }

        internal async Task InstallAsync(BrowsedModViewModel row)
        {
            IReadOnlyList<PlannedInstall> plan;

            try
            {
                plan = ModIndexPlanner.Plan(index, library.List(), row.Mod, row.Target, library.ProvidedApi());
            }
            catch (InvalidOperationException e)
            {
                await problem($"Couldn't install {row.Name}. {e.Message}");
                return;
            }

            string? error = null;
            setBusy(true);

            try
            {
                for (int i = 0; i < plan.Count; i++)
                {
                    PlannedInstall step = plan[i];
                    int done = i;

                    report($"Downloading {step.Mod.Name} {step.Version.Version}...");

                    try
                    {
                        // Progress<T> can fire late
                        await installer.InstallAsync(library, step.Mod, step.Version, new Progress<double>(fraction => setProgress((done + fraction) / plan.Count)));
                    }
                    catch (Exception e) when (e is HttpRequestException or InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException or TaskCanceledException)
                    {
                        error = $"Couldn't install {step.Mod.Name} {step.Version.Version}. {e.Message}";
                        break;
                    }
                }

                if (error == null)
                    report($"{row.Name} {row.Version} is installed.");
            }
            finally
            {
                setBusy(false);
                refreshInstalled();
                refreshRows();
            }

            if (error != null)
                await problem(error);
        }

        private void refreshRows()
        {
            IReadOnlyList<InstalledMod> installed = library.List();
            string? api = library.ProvidedApi();
            HashSet<string> updates = [.. ModIndexPlanner.Updates(index, installed, api).Select(u => u.Mod.Id)];

            foreach (BrowsedModViewModel row in Mods)
            {
                IndexedModVersion? newest = ModIndexPlanner.Newest(row.Mod, api);

                if (newest == null)
                    row.Refresh(BrowsedModState.Incompatible, row.Mod.Latest);
                else if (updates.Contains(row.Mod.Id))
                    row.Refresh(BrowsedModState.Update, newest);
                else if (installed.Any(m => m.Manifest?.Id == row.Mod.Id))
                    row.Refresh(BrowsedModState.Installed, newest);
                else
                    row.Refresh(BrowsedModState.Available, newest);
            }

            applyFilter();
        }

        private void applyFilter()
        {
            string query = search.Trim();

            List<BrowsedModViewModel> matching =
            [
                .. Mods.Where(row => query.Length == 0
                                     || row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                                     || row.Mod.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                                     || row.Byline.Contains(query, StringComparison.OrdinalIgnoreCase)
                                     || row.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
            ];

            if (!matching.SequenceEqual(Visible))
            {
                Visible.Clear();
                foreach (BrowsedModViewModel row in matching)
                    Visible.Add(row);
            }

            OnPropertyChanged(nameof(NoMatches));
        }
    }
}

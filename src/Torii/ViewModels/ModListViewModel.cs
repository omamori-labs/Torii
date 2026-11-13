using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Torii.Core.Mods;

namespace Torii.ViewModels
{
    public sealed class ModListViewModel : ObservableObject
    {
        private readonly ModLibrary library;
        private readonly Action<string> problem;
        private readonly Func<string, Task>? openFolder;

        private readonly string? orderFile;

        private string search = string.Empty;
        private ModFilter filter;
        private bool customOrder;

        public ModListViewModel(ModLibrary library, Action<string?> report, Action<string>? problem = null, Func<string, Task>? openFolder = null, string? orderFile = null)
        {
            this.library = library;
            this.problem = problem ?? (message => report(message));
            this.openFolder = openFolder;
            this.orderFile = orderFile;
            customOrder = orderFile != null && ModOrder.Load(orderFile) != null;

            Refresh();
        }

        public ObservableCollection<ModItemViewModel> Mods { get; } = [];

        public ObservableCollection<ModItemViewModel> Visible { get; } = [];

        public bool HasMods => Mods.Count > 0;

        public bool NoMatches => HasMods && Visible.Count == 0;

        public bool CanReorder => search.Trim().Length == 0 && filter == ModFilter.All;

        public string Search
        {
            get => search;
            set
            {
                if (!SetProperty(ref search, value ?? string.Empty))
                    return;

                OnPropertyChanged(nameof(CanReorder));
                applyFilter();
            }
        }

        public IReadOnlyList<ModFilter> Filters { get; } = [.. Enum.GetValues<ModFilter>()];

        public ModFilter Filter
        {
            get => filter;
            set
            {
                if (!SetProperty(ref filter, value))
                    return;

                OnPropertyChanged(nameof(CanReorder));
                applyFilter();
            }
        }

        public void Move(ModItemViewModel row, int index)
        {
            int from = Mods.IndexOf(row);
            int to = Math.Clamp(index, 0, Mods.Count - 1);

            if (!CanReorder || from < 0 || from == to)
                return;

            Mods.Move(from, to);
            customOrder = true;
            applyFilter();
            saveOrder();
        }

        public void Refresh()
        {
            var mods = ModOrder.Apply(library.List(), customOrder && orderFile != null ? ModOrder.Load(orderFile) : null);

            if (mods.Count == Mods.Count && mods.Zip(Mods).All(pair => pair.First.Directory == pair.Second.Mod.Directory))
            {
                foreach ((InstalledMod mod, ModItemViewModel row) in mods.Zip(Mods))
                    row.Mod = mod;

                applyFilter();
                return;
            }

            Mods.Clear();
            foreach (InstalledMod mod in mods)
                Mods.Add(new ModItemViewModel(this, mod));

            OnPropertyChanged(nameof(HasMods));
            applyFilter();

            if (customOrder && orderFile != null && ModOrder.Load(orderFile) is { } saved && !saved.SequenceEqual(Mods.Select(row => Path.GetFileName(row.Mod.Directory))))
                saveOrder();
        }

        private void saveOrder()
        {
            if (orderFile == null)
                return;

            try
            {
                ModOrder.Save(orderFile, Mods.Select(row => Path.GetFileName(row.Mod.Directory)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                problem($"Couldn't save the mod order. {e.Message}");
            }
        }

        private void applyFilter()
        {
            List<ModItemViewModel> matching = [.. Mods.Where(matches)];

            if (!matching.SequenceEqual(Visible))
            {
                Visible.Clear();
                foreach (ModItemViewModel row in matching)
                    Visible.Add(row);
            }

            OnPropertyChanged(nameof(NoMatches));
        }

        private bool matches(ModItemViewModel row)
        {
            bool stateMatches = filter switch
            {
                ModFilter.On => row.Mod.State == ModState.On,
                ModFilter.Off => row.Mod.State == ModState.Off,
                ModFilter.Problems => row.Mod.State is ModState.Crashed or ModState.Problem,
                _ => true,
            };

            if (!stateMatches)
                return false;

            string query = search.Trim();

            return query.Length == 0
                   || row.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
                   || (row.Mod.Manifest?.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                   || row.Byline.Contains(query, StringComparison.OrdinalIgnoreCase)
                   || row.Description.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        public void AddFromZip(string zip)
        {
            try
            {
                library.InstallFromZip(zip);
            }
            catch (Exception e) when (e is InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                problem($"Couldn't add the mod. {e.Message}");
            }

            Refresh();
        }

        public void AddFromZips(IReadOnlyList<string> zips)
        {
            if (zips.Count == 1)
            {
                AddFromZip(zips[0]);
                return;
            }

            var failures = new List<string>();

            foreach (string zip in zips)
            {
                try
                {
                    library.InstallFromZip(zip);
                }
                catch (Exception e) when (e is InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException)
                {
                    failures.Add($"{Path.GetFileName(zip)}: {e.Message}");
                }
            }

            Refresh();

            if (failures.Count > 0)
                problem($"Couldn't add {failures.Count} of {zips.Count} mods.{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
        }

        internal Task OpenFolder(ModItemViewModel row) => openFolder?.Invoke(row.Mod.Directory) ?? Task.CompletedTask;

        internal void SetEnabled(ModItemViewModel row, bool enabled)
        {
            try
            {
                library.SetEnabled(row.Mod.Manifest!.Id, enabled);
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                problem($"Couldn't turn {row.Label} {(enabled ? "on" : "off")}. {e.Message}");
            }

            Refresh();
            row.Revert();
        }

        internal void Remove(ModItemViewModel row)
        {
            try
            {
                library.Remove(row.Mod);
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                problem($"Couldn't remove {row.Label}. {e.Message}");
            }

            Refresh();
        }

        internal void Retry(ModItemViewModel row)
        {
            try
            {
                library.Retry(row.Mod.Manifest!.Id);
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                problem($"Couldn't reset {row.Label}. {e.Message}");
            }

            Refresh();
        }
    }
}

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Torii.Core.Mods;

namespace Torii.ViewModels
{
    public sealed class ModListViewModel : ObservableObject
    {
        private readonly ModLibrary library;
        private readonly Action<string?> report;
        private readonly Action<string> problem;

        public ModListViewModel(ModLibrary library, Action<string?> report, Action<string>? problem = null)
        {
            this.library = library;
            this.report = report;
            this.problem = problem ?? (message => report(message));

            Refresh();
        }

        public ObservableCollection<ModItemViewModel> Mods { get; } = [];

        public bool HasMods => Mods.Count > 0;

        public void Refresh()
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

        public void AddFromZip(string zip)
        {
            try
            {
                InstalledMod mod = library.InstallFromZip(zip);
                report($"{mod.Label} {mod.Manifest?.Version} is installed.");
            }
            catch (Exception e) when (e is InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                problem($"Couldn't add the mod. {e.Message}");
            }

            Refresh();
        }

        internal void SetEnabled(ModItemViewModel row, bool enabled)
        {
            try
            {
                library.SetEnabled(row.Mod.Manifest!.Id, enabled);
                report($"{row.Label} is {(enabled ? "on" : "off")} the next time osu! starts.");
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
                report($"{row.Label} is removed. Its settings were kept.");
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
                report($"{row.Label} is tried again the next time osu! starts.");
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                problem($"Couldn't reset {row.Label}. {e.Message}");
            }

            Refresh();
        }
    }
}

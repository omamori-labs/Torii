using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Torii.Core.Mods;

namespace Torii.ViewModels
{
    public sealed partial class ModItemViewModel : ObservableObject
    {
        private readonly ModListViewModel owner;

        public ModItemViewModel(ModListViewModel owner, InstalledMod mod)
        {
            this.owner = owner;
            Mod = mod;
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Label), nameof(Version), nameof(Byline), nameof(Description), nameof(Problem), nameof(Patches), nameof(CanToggle), nameof(CanRetry), nameof(IsOn))]
        public partial InstalledMod Mod { get; set; }

        public string Label => Mod.Label;

        public string Version => Mod.Manifest?.Version ?? string.Empty;

        public string Byline => string.IsNullOrEmpty(Mod.Manifest?.Author) ? string.Empty : $"by {Mod.Manifest.Author}";

        public string Description => Mod.Manifest?.Description ?? string.Empty;

        public string Problem => Mod.Problem ?? string.Empty;

        public bool Patches => Mod.Manifest?.Patches == true;

        public bool CanToggle => Mod.State != ModState.Problem;

        public bool CanRetry => Mod.State == ModState.Crashed;

        [RelayCommand]
        public void Remove() => owner.Remove(this);

        [RelayCommand]
        public void Retry() => owner.Retry(this);

        [RelayCommand]
        public Task OpenFolderAsync() => owner.OpenFolder(this);

        // Crashed mods are still on in settings
        public bool IsOn
        {
            get => Mod.State is ModState.On or ModState.Crashed;
            set
            {
                if (value != IsOn)
                    owner.SetEnabled(this, value);
            }
        }

        public void Revert() => OnPropertyChanged(nameof(IsOn));
    }
}

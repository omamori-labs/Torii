using CommunityToolkit.Mvvm.ComponentModel;
using Torii.Core;

namespace Torii
{
    public sealed partial class ModItemViewModel : ObservableObject
    {
        private readonly MainViewModel owner;

        public ModItemViewModel(MainViewModel owner, InstalledMod mod)
        {
            this.owner = owner;
            Mod = mod;
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Label), nameof(Version), nameof(Byline), nameof(Description), nameof(Problem), nameof(Patches), nameof(CanToggle), nameof(IsOn))]
        public partial InstalledMod Mod { get; set; }

        public string Label => Mod.Label;

        public string Version => Mod.Manifest?.Version ?? string.Empty;

        public string Byline => string.IsNullOrEmpty(Mod.Manifest?.Author) ? string.Empty : $"by {Mod.Manifest.Author}";

        public string Description => Mod.Manifest?.Description ?? string.Empty;

        public string Problem => Mod.Problem ?? string.Empty;

        public bool Patches => Mod.Manifest?.Patches == true;

        /// <summary>A mod that can't load as it is has nothing to switch</summary>
        public bool CanToggle => Mod.State != ModState.Problem;

        // A crashed mod is still on in settings, Omamori is the one skipping it
        public bool IsOn
        {
            get => Mod.State is ModState.On or ModState.Crashed;
            set
            {
                if (value != IsOn)
                    owner.SetEnabled(this, value);
            }
        }

        /// <summary>Puts the switch back after a change that didn't go through</summary>
        public void Revert() => OnPropertyChanged(nameof(IsOn));
    }
}

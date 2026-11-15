using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Torii.Core.Mods;

namespace Torii.ViewModels
{
    public sealed partial class BrowsedModViewModel : ObservableObject
    {
        private readonly ModBrowserViewModel owner;

        public BrowsedModViewModel(ModBrowserViewModel owner, IndexedMod mod)
        {
            this.owner = owner;
            Mod = mod;
            Target = mod.Latest;
        }

        public IndexedMod Mod { get; }

        public string Name => Mod.Name;

        public string Byline => Mod.Author.Length > 0 ? $"by {Mod.Author}" : string.Empty;

        public string Description => Mod.Description;

        public string Version => Target.Version;

        public string ActionLabel => State == BrowsedModState.Update ? "Update" : "Install";

        public string Problem => State == BrowsedModState.Incompatible ? "Not compatible with the installed Omamori" : string.Empty;

        public bool CanInstall => State is BrowsedModState.Available or BrowsedModState.Update && owner.CanInstall;

        public bool ShowsAction => State is BrowsedModState.Available or BrowsedModState.Update;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Version))]
        public partial IndexedModVersion Target { get; private set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ActionLabel), nameof(Problem), nameof(CanInstall), nameof(ShowsAction))]
        [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
        public partial BrowsedModState State { get; private set; }

        public void Refresh(BrowsedModState state, IndexedModVersion target)
        {
            Target = target;
            State = state;
        }

        public void CanInstallChanged()
        {
            OnPropertyChanged(nameof(CanInstall));
            InstallCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanInstall))]
        public Task InstallAsync() => owner.InstallAsync(this);
    }
}

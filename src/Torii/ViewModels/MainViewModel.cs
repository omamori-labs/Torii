using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Torii.Core.Instances;
using Torii.Core.Lazer;

namespace Torii.ViewModels
{
    public sealed partial class MainViewModel : ObservableObject
    {
        private readonly InstanceStore store;
        private readonly Func<LazerInstance, InstanceViewModel> createInstance;

        public MainViewModel(InstanceStore store, Func<LazerInstance, InstanceViewModel> createInstance, LazerInstance? given = null)
        {
            this.store = store;
            this.createInstance = createInstance;

            Instances.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasInstances));

            foreach (LazerInstance instance in store.List())
                Instances.Add(wrap(instance));

            if (given != null && Instances.FirstOrDefault(i => sameFolder(i.Instance, given)) is null)
                insert(wrap(given));

            Selected = given == null ? Instances.FirstOrDefault() : Instances.First(i => sameFolder(i.Instance, given));
        }

        public ObservableCollection<InstanceViewModel> Instances { get; } = [];

        public bool HasInstances => Instances.Count > 0;

        public Func<Task<string?>>? PickModZip { get; set; }

        public Func<Task<string?>>? PickOsuFolder { get; set; }

        public Func<string, Task>? ShowProblem { get; set; }

        public Func<Func<string, string?>, Task<string?>>? AskName { get; set; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
        public partial InstanceViewModel? Selected { get; set; }

        public async Task RefreshAsync()
        {
            foreach (InstanceViewModel instance in Instances.ToList())
                await instance.RefreshOsuRunningAsync();
        }

        public void RefreshMods()
        {
            foreach (InstanceViewModel instance in Instances)
                instance.ModList.Refresh();
        }

        [RelayCommand]
        public async Task NewInstanceAsync()
        {
            if (AskName == null || await AskName(store.Validate) is not string name)
                return;

            try
            {
                InstanceViewModel created = wrap(store.Create(name));
                insert(created);
                Selected = created;
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                await askProblem($"Couldn't create {name}. {e.Message}");
            }
        }

        [RelayCommand(CanExecute = nameof(canDelete))]
        public async Task DeleteAsync()
        {
            if (Selected is not { } doomed || !canDelete())
                return;

            try
            {
                store.Delete(doomed.Instance);
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                await askProblem($"Couldn't delete {doomed.Name}. {e.Message}");
                return;
            }

            int index = Instances.IndexOf(doomed);
            Instances.Remove(doomed);
            Selected = Instances.Count == 0 ? null : Instances[Math.Min(index, Instances.Count - 1)];
        }

        partial void OnSelectedChanged(InstanceViewModel? oldValue, InstanceViewModel? newValue)
        {
            if (oldValue != null)
                oldValue.PropertyChanged -= selectedChanged;

            if (newValue != null)
            {
                newValue.PropertyChanged += selectedChanged;
                _ = newValue.EnsureReleasesAsync();
            }
        }

        private static bool sameFolder(LazerInstance a, LazerInstance b) =>
            string.Equals(a.Directory, b.Directory, StringComparison.OrdinalIgnoreCase);

        private bool canDelete() => Selected != null && store.Manages(Selected.Instance) && !Selected.IsBusy && !Selected.OsuRunning;

        private void selectedChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(InstanceViewModel.IsBusy) or nameof(InstanceViewModel.OsuRunning))
                DeleteCommand.NotifyCanExecuteChanged();
        }

        private Task<string?> askModZip() => PickModZip?.Invoke() ?? Task.FromResult<string?>(null);

        private Task<string?> askOsuFolder() => PickOsuFolder?.Invoke() ?? Task.FromResult<string?>(null);

        private Task askProblem(string message) => ShowProblem?.Invoke(message) ?? Task.CompletedTask;

        private InstanceViewModel wrap(LazerInstance instance)
        {
            InstanceViewModel model = createInstance(instance);
            model.PickModZip = askModZip;
            model.PickOsuFolder = askOsuFolder;
            model.ShowProblem = askProblem;

            return model;
        }

        private void insert(InstanceViewModel model)
        {
            int index = Instances.TakeWhile(i => string.Compare(i.Name, model.Name, StringComparison.OrdinalIgnoreCase) < 0).Count();
            Instances.Insert(index, model);
        }
    }
}

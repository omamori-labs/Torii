using System;
using System.Collections.ObjectModel;
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

        public MainViewModel(InstanceStore store, Func<LazerInstance, InstanceViewModel> createInstance, LazerInstance? given = null, string? remembered = null)
        {
            this.store = store;
            this.createInstance = createInstance;

            Instances.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasInstances));

            foreach (LazerInstance instance in store.List())
                Instances.Add(wrap(instance));

            if (given != null && Instances.FirstOrDefault(i => sameFolder(i.Instance, given)) is null)
                insert(wrap(given));

            Selected = given != null
                ? Instances.First(i => sameFolder(i.Instance, given))
                : Instances.FirstOrDefault(i => string.Equals(i.Instance.Directory, remembered, StringComparison.OrdinalIgnoreCase)) ?? Instances.FirstOrDefault();
        }

        public ObservableCollection<InstanceViewModel> Instances { get; } = [];

        public bool HasInstances => Instances.Count > 0;

        public Func<Task<string?>>? PickModZip { get; set; }

        public Func<Task<string?>>? PickOsuFolder { get; set; }

        public Func<string, Task>? ShowProblem { get; set; }

        public Func<NamePrompt, Task<string?>>? AskName { get; set; }

        public Func<string, Task>? OpenFolder { get; set; }

        public Action<string?>? Remember { get; set; }

        [ObservableProperty]
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
            if (AskName == null || await AskName(new NamePrompt("New instance", "Create", string.Empty, candidate => store.Validate(candidate))) is not string name)
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

        partial void OnSelectedChanged(InstanceViewModel? oldValue, InstanceViewModel? newValue)
        {
            Remember?.Invoke(newValue?.Instance.Directory);

            if (newValue != null)
                _ = newValue.EnsureReleasesAsync();
        }

        private static bool sameFolder(LazerInstance a, LazerInstance b) =>
            string.Equals(a.Directory, b.Directory, StringComparison.OrdinalIgnoreCase);

        private async Task renameAsync(InstanceViewModel target)
        {
            if (!target.RenameCommand.CanExecute(null))
                return;

            if (AskName == null || await AskName(new NamePrompt("Rename instance", "Rename", target.Name, candidate => store.Validate(candidate, target.Instance))) is not string name)
                return;

            try
            {
                LazerInstance renamed = store.Rename(target.Instance, name);

                if (ReferenceEquals(renamed, target.Instance))
                    return;

                bool wasSelected = Selected == target;
                InstanceViewModel moved = wrap(renamed);

                Instances.Remove(target);
                insert(moved);

                if (wasSelected)
                    Selected = moved;
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                await askProblem($"Couldn't rename {target.Name}. {e.Message}");
            }
        }

        private async Task deleteAsync(InstanceViewModel doomed)
        {
            if (!doomed.DeleteCommand.CanExecute(null))
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
            bool wasSelected = Selected == doomed;
            Instances.Remove(doomed);

            if (wasSelected)
                Selected = Instances.Count == 0 ? null : Instances[Math.Min(index, Instances.Count - 1)];
        }

        private Task<string?> askModZip() => PickModZip?.Invoke() ?? Task.FromResult<string?>(null);

        private Task<string?> askOsuFolder() => PickOsuFolder?.Invoke() ?? Task.FromResult<string?>(null);

        private Task askProblem(string message) => ShowProblem?.Invoke(message) ?? Task.CompletedTask;

        private Task askOpenFolder(string path) => OpenFolder?.Invoke(path) ?? Task.CompletedTask;

        private InstanceViewModel wrap(LazerInstance instance)
        {
            InstanceViewModel model = createInstance(instance);
            model.PickModZip = askModZip;
            model.PickOsuFolder = askOsuFolder;
            model.ShowProblem = askProblem;
            model.OpenFolder = askOpenFolder;
            model.IsManaged = store.Manages(instance);
            model.RequestRename = () => renameAsync(model);
            model.RequestDelete = () => deleteAsync(model);

            return model;
        }

        private void insert(InstanceViewModel model)
        {
            int index = Instances.TakeWhile(i => string.Compare(i.Name, model.Name, StringComparison.OrdinalIgnoreCase) < 0).Count();
            Instances.Insert(index, model);
        }
    }
}

using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Torii.Core.Releases;
using Torii.Updates;

namespace Torii.ViewModels
{
    public sealed partial class UpdateViewModel : ObservableObject
    {
        private readonly IUpdateClient client;
        private readonly Action<string>? remember;
        private readonly UpdateLog? log;
        private readonly Func<bool> busy;

        private string? skipped;

        public UpdateViewModel(IUpdateClient client, string? skipped = null, Action<string>? remember = null, UpdateLog? log = null, Func<bool>? busy = null)
        {
            this.client = client;
            this.skipped = skipped;
            this.remember = remember;
            this.log = log;
            this.busy = busy ?? (() => false);

            Version = string.Empty;
            Problem = string.Empty;
        }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(UpdateCommand), nameof(SkipCommand), nameof(DismissCommand), nameof(RestartCommand))]
        public partial bool IsVisible { get; private set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsOffer), nameof(IsAvailable), nameof(IsDownloading), nameof(IsReady), nameof(IsFailed), nameof(Title), nameof(Detail))]
        [NotifyCanExecuteChangedFor(nameof(UpdateCommand), nameof(SkipCommand), nameof(DismissCommand), nameof(RestartCommand))]
        public partial UpdateState State { get; private set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Detail))]
        public partial string Version { get; private set; }

        [ObservableProperty]
        public partial double Progress { get; private set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Detail))]
        public partial string Problem { get; private set; }

        public bool IsOffer => State is UpdateState.Available or UpdateState.Downloading;

        public bool IsAvailable => State == UpdateState.Available;

        public bool IsDownloading => State == UpdateState.Downloading;

        public bool IsReady => State == UpdateState.Ready;

        public bool IsFailed => State == UpdateState.Failed;

        public string Title => State switch
        {
            UpdateState.Downloading => "Downloading update",
            UpdateState.Ready => "Update ready",
            UpdateState.Failed => "Update failed",
            _ => "Update available",
        };

        public string Detail => State switch
        {
            UpdateState.Ready => $"{Version} installs when Torii closes.",
            UpdateState.Failed => Problem,
            _ => $"Torii {Version}",
        };

        public async Task CheckAsync()
        {
            if (State is UpdateState.Downloading or UpdateState.Ready)
                return;

            try
            {
                string? version = await client.CheckAsync();

                if (version == null)
                {
                    IsVisible = false;
                    return;
                }

                if (string.Equals(version, skipped, StringComparison.Ordinal))
                {
                    log?.Write($"{version} is available and skipped");
                    IsVisible = false;
                    return;
                }

                Version = version;
                Problem = string.Empty;
                State = UpdateState.Available;
                IsVisible = true;
                log?.Write($"{version} is available");
            }
            catch (Exception e)
            {
                log?.Write($"update check failed: {e.GetType().Name}: {e.Message}");
            }
        }

        public void BusyChanged() => RestartCommand.NotifyCanExecuteChanged();

        public void ApplyOnExit()
        {
            if (State != UpdateState.Ready)
                return;

            try
            {
                log?.Write($"installing {Version} as Torii closes");
                client.ApplyOnExit();
            }
            catch (Exception e)
            {
                log?.Write($"update failed: {e.GetType().Name}: {e.Message}");
            }
        }

        [RelayCommand(CanExecute = nameof(canChoose))]
        public async Task UpdateAsync()
        {
            State = UpdateState.Downloading;
            Progress = 0;
            Problem = string.Empty;
            log?.Write($"downloading {Version}");

            try
            {
                // Progress<T> can fire late
                await client.DownloadAsync(new Progress<double>(value =>
                {
                    if (State == UpdateState.Downloading)
                        Progress = value;
                }));

                Progress = 1;
                State = UpdateState.Ready;
                log?.Write($"{Version} is downloaded");
            }
            catch (Exception e)
            {
                fail(e);
            }
        }

        [RelayCommand(CanExecute = nameof(canChoose))]
        public void Skip()
        {
            skipped = Version;
            remember?.Invoke(Version);
            log?.Write($"skipping {Version}");
            IsVisible = false;
        }

        [RelayCommand(CanExecute = nameof(canDismiss))]
        public void Dismiss() => IsVisible = false;

        [RelayCommand(CanExecute = nameof(canRestart))]
        public void Restart()
        {
            try
            {
                log?.Write($"restarting into {Version}");
                client.ApplyAndRestart();
            }
            catch (Exception e)
            {
                fail(e);
            }
        }

        private bool canChoose() => IsVisible && State is UpdateState.Available or UpdateState.Failed;

        private bool canDismiss() => IsVisible && State is UpdateState.Available or UpdateState.Failed or UpdateState.Ready;

        private bool canRestart() => IsVisible && State == UpdateState.Ready && !busy();

        private void fail(Exception e)
        {
            Problem = e.Message;
            State = UpdateState.Failed;
            log?.Write($"update failed: {e.GetType().Name}: {e.Message}");
        }
    }
}

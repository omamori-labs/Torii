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
        [NotifyPropertyChangedFor(nameof(IsVisible), nameof(IsAvailable), nameof(IsDownloading), nameof(IsReady), nameof(IsFailed), nameof(Title))]
        [NotifyCanExecuteChangedFor(nameof(UpdateCommand), nameof(SkipCommand), nameof(DismissCommand), nameof(RestartCommand))]
        public partial UpdateState State { get; private set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Title))]
        public partial string Version { get; private set; }

        [ObservableProperty]
        public partial double Progress { get; private set; }

        [ObservableProperty]
        public partial string Problem { get; private set; }

        public bool IsVisible => State != UpdateState.None;

        public bool IsAvailable => State == UpdateState.Available;

        public bool IsDownloading => State == UpdateState.Downloading;

        public bool IsReady => State == UpdateState.Ready;

        public bool IsFailed => State == UpdateState.Failed;

        public string Title => State switch
        {
            UpdateState.Ready => $"Torii {Version} is ready",
            UpdateState.Failed => $"Couldn't update to {Version}",
            _ => $"Torii {Version} is available",
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
                    State = UpdateState.None;
                    return;
                }

                if (string.Equals(version, skipped, StringComparison.Ordinal))
                {
                    log?.Write($"{version} is available and skipped");
                    State = UpdateState.None;
                    return;
                }

                Version = version;
                Problem = string.Empty;
                State = UpdateState.Available;
                log?.Write($"{version} is available");
            }
            catch (Exception e)
            {
                log?.Write($"update check failed: {e.GetType().Name}: {e.Message}");
            }
        }

        public void Refresh() => RestartCommand.NotifyCanExecuteChanged();

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

                client.ApplyOnExit();
                Progress = 1;
                State = UpdateState.Ready;
                log?.Write($"{Version} is downloaded and gets applied when Torii closes");
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
            State = UpdateState.None;
        }

        [RelayCommand(CanExecute = nameof(canDismiss))]
        public void Dismiss() => State = UpdateState.None;

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

        private bool canChoose() => State is UpdateState.Available or UpdateState.Failed;

        private bool canDismiss() => State is UpdateState.Available or UpdateState.Failed or UpdateState.Ready;

        private bool canRestart() => State == UpdateState.Ready && !busy();

        private void fail(Exception e)
        {
            Problem = e.Message;
            State = UpdateState.Failed;
            log?.Write($"update failed: {e.GetType().Name}: {e.Message}");
        }
    }
}

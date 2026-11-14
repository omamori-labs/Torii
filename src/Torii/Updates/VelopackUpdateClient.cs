using System;
using System.Reflection;
using System.Threading.Tasks;
using Torii.Core.Releases;
using Velopack;
using Velopack.Sources;

namespace Torii.Updates
{
    internal sealed class VelopackUpdateClient : IUpdateClient
    {
        public const string REPOSITORY = "https://github.com/omamori-labs/Torii";

        private UpdateManager? manager;
        private UpdateInfo? update;

        public async Task<string?> CheckAsync()
        {
            string version = typeof(VelopackUpdateClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;

            manager ??= new UpdateManager(new GithubSource(REPOSITORY, null, ReleaseChannel.IsPrerelease(version)));

            // Not installed when run from the IDE
            if (!manager.IsInstalled)
                return null;

            update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);

            return update?.TargetFullRelease.Version.ToString();
        }

        public Task DownloadAsync(IProgress<double> progress)
        {
            var (current, offer) = pending();

            return current.DownloadUpdatesAsync(offer, percent => progress.Report(percent / 100.0));
        }

        public void ApplyOnExit()
        {
            var (current, offer) = pending();

            current.WaitExitThenApplyUpdates(offer, silent: true, restart: false);
        }

        public void ApplyAndRestart()
        {
            var (current, offer) = pending();

            current.ApplyUpdatesAndRestart(offer);
        }

        private (UpdateManager Manager, UpdateInfo Update) pending() =>
            manager != null && update != null ? (manager, update) : throw new InvalidOperationException("There is no update to install.");
    }
}

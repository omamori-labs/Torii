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
        private const string repository = "https://github.com/omamori-labs/Torii";

        private readonly UpdateLog log;

        private UpdateManager? manager;
        private UpdateInfo? update;

        public VelopackUpdateClient(UpdateLog log)
        {
            this.log = log;
        }

        public async Task<string?> CheckAsync()
        {
            string version = typeof(VelopackUpdateClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;
            bool prerelease = ReleaseChannel.IsPrerelease(version);

            manager ??= new UpdateManager(new GithubSource(repository, null, prerelease));

            // Not installed when run from the IDE
            if (!manager.IsInstalled)
                return null;

            log.Write($"Torii {version} is checking for updates, {(prerelease ? "prereleases included" : "releases only")}");

            update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);

            if (update == null)
                log.Write("no newer version");

            return update?.TargetFullRelease.Version.ToString();
        }

        public Task DownloadAsync(IProgress<double> progress)
        {
            var (current, offer) = pending();

            return current.DownloadUpdatesAsync(offer, percent => progress.Report(percent / 100.0));
        }

        // Velopack's updater only waits 60 seconds for the exit
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

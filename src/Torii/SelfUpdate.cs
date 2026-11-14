using System;
using System.Reflection;
using System.Threading.Tasks;
using Torii.Core.Releases;
using Velopack;
using Velopack.Sources;

namespace Torii
{
    internal static class SelfUpdate
    {
        public const string REPOSITORY = "https://github.com/omamori-labs/Torii";

        public static async Task RunAsync()
        {
            var log = new UpdateLog(UpdateLog.DefaultFile());

            try
            {
                string version = typeof(SelfUpdate).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;
                bool prerelease = ReleaseChannel.IsPrerelease(version);
                var manager = new UpdateManager(new GithubSource(REPOSITORY, null, prerelease));

                // Not installed when run from the IDE
                if (!manager.IsInstalled)
                    return;

                log.Write($"Torii {version} is checking for updates, {(prerelease ? "prereleases included" : "releases only")}");

                UpdateInfo? update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);

                if (update == null)
                {
                    log.Write("no newer version");
                    return;
                }

                string target = update.TargetFullRelease.Version.ToString();

                log.Write($"downloading {target}");
                await manager.DownloadUpdatesAsync(update).ConfigureAwait(false);
                manager.WaitExitThenApplyUpdates(update, silent: true, restart: false);
                log.Write($"{target} is downloaded and gets applied when Torii closes");
            }
            catch (Exception e)
            {
                // Offline, rate limited, no releases yet
                log.Write($"update failed: {e.GetType().Name}: {e.Message}");
            }
        }
    }
}

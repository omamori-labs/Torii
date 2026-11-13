using System;
using System.Reflection;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace Torii
{
    internal static class SelfUpdate
    {
        public const string REPOSITORY = "https://github.com/omamori-labs/Torii";

        public static async Task RunAsync()
        {
            try
            {
                var manager = new UpdateManager(new GithubSource(REPOSITORY, null, isPrerelease()));

                // Not installed when run from the IDE
                if (!manager.IsInstalled)
                    return;

                UpdateInfo? update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);

                if (update == null)
                    return;

                await manager.DownloadUpdatesAsync(update).ConfigureAwait(false);
                manager.WaitExitThenApplyUpdates(update, silent: true, restart: false);
            }
            catch (Exception)
            {
                // Offline, rate limited, no releases yet
            }
        }

        private static bool isPrerelease()
        {
            string version = typeof(SelfUpdate).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;
            return version.Split('+')[0].Contains('-', StringComparison.Ordinal);
        }
    }
}

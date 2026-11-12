using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Torii.Core
{
    /// <summary>Makes an instance run a given Omamori release, bringing in the osu! version that release was built for</summary>
    public sealed class InstanceSetup
    {
        private readonly ReleaseInstaller releases;
        private readonly LazerFeed lazerFeed;
        private readonly LazerInstaller lazerInstaller;
        private readonly Func<LazerInstance, bool> isRunning;

        public InstanceSetup(HttpClient http, string? temporaryDirectory = null, Func<LazerInstance, bool>? isRunning = null)
        {
            releases = new ReleaseInstaller(http, temporaryDirectory);
            lazerFeed = new LazerFeed(http);
            lazerInstaller = new LazerInstaller(http, temporaryDirectory);
            this.isRunning = isRunning ?? (instance => OsuProcess.IsRunningIn(instance.Directory));
        }

        /// <summary>Sets up a new instance, or updates an existing one and keeps its data and mods</summary>
        /// <exception cref="InvalidOperationException">The instance is running, or it has a newer osu! than the release needs</exception>
        /// <exception cref="InvalidDataException">A download can't be trusted or isn't what it should be</exception>
        /// <exception cref="HttpRequestException">A download failed</exception>
        public async Task SetUpAsync(LazerInstance instance, OmamoriRelease release, IProgress<SetupProgress>? progress = null, CancellationToken cancellation = default)
        {
            if (isRunning(instance))
                throw new InvalidOperationException("Close the modded osu! first.");

            string zip = await releases.DownloadAsync(release, stage(progress, SetupStage.DownloadingOmamori), cancellation).ConfigureAwait(false);

            try
            {
                string lazer = ReleaseZip.LazerVersion(zip)
                               ?? throw new InvalidDataException($"Omamori {release.Version} doesn't say which osu! version it needs.");

                if (!instance.IsInstalled || instance.ReadVersion() != lazer)
                {
                    LazerPackage package = await lazerFeed.FindAsync(lazer, cancellation).ConfigureAwait(false);
                    await lazerInstaller.InstallAsync(instance, package, stage(progress, SetupStage.DownloadingOsu), cancellation).ConfigureAwait(false);
                }

                progress?.Report(new SetupProgress(SetupStage.Installing, 0));
                OmamoriInstaller.InstallFromZip(instance.Omamori, zip);
                progress?.Report(new SetupProgress(SetupStage.Installing, 1));
            }
            finally
            {
                File.Delete(zip);
            }
        }

        private static StageProgress? stage(IProgress<SetupProgress>? progress, SetupStage stage) =>
            progress == null ? null : new StageProgress(progress, stage);

        // Not Progress<T>, that one reports on another thread and loses the order
        private sealed class StageProgress : IProgress<double>
        {
            private readonly IProgress<SetupProgress> progress;
            private readonly SetupStage stage;

            public StageProgress(IProgress<SetupProgress> progress, SetupStage stage)
            {
                this.progress = progress;
                this.stage = stage;
            }

            public void Report(double value) => progress.Report(new SetupProgress(stage, value));
        }
    }
}

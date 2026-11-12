using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Torii.Core
{
    /// <summary>
    ///     downloads a release, checks it's the file github says it is, and installs it
    /// </summary>
    public sealed class ReleaseInstaller
    {
        private readonly HttpClient http;
        private readonly string temporaryDirectory;

        public ReleaseInstaller(HttpClient http, string? temporaryDirectory = null)
        {
            this.http = http;
            this.temporaryDirectory = temporaryDirectory ?? Path.GetTempPath();
        }

        /// <param name="install">where to install to</param>
        /// <param name="release">what to install</param>
        /// <param name="progress">how much of the download is done, 0 to 1</param>
        /// <param name="cancellation">stops the download, the install itself isn't interrupted</param>
        /// <exception cref="InvalidDataException">the download can't be trusted or isn't an omamori release, nothing was changed</exception>
        /// <exception cref="HttpRequestException">the download failed, nothing was changed</exception>
        public async Task InstallAsync(OmamoriInstall install, OmamoriRelease release, IProgress<double>? progress = null, CancellationToken cancellation = default)
        {
            // what's in the zip runs inside osu!, so an unchecked one isn't installed
            if (release.Sha256 == null)
                throw new InvalidDataException($"Omamori {release.Version} has no checksum to check the download against.");

            string zip = Path.Combine(temporaryDirectory, $"torii-{Guid.NewGuid():N}.zip");

            try
            {
                string hash = await download(release, zip, progress, cancellation).ConfigureAwait(false);

                if (!string.Equals(hash, release.Sha256, StringComparison.Ordinal))
                    throw new InvalidDataException($"The download of Omamori {release.Version} doesn't match its checksum.");

                OmamoriInstaller.InstallFromZip(install, zip);
            }
            finally
            {
                File.Delete(zip);
            }
        }

        private async Task<string> download(OmamoriRelease release, string path, IProgress<double>? progress, CancellationToken cancellation)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, release.Download);
            request.Headers.UserAgent.ParseAdd("Torii");

            using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            long total = response.Content.Headers.ContentLength ?? release.Size;

            using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[81920];
            long done = 0;

            Stream source = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                FileStream target = File.Create(path);
                await using (target.ConfigureAwait(false))
                {
                    int read;

                    while ((read = await source.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
                    {
                        sha256.AppendData(buffer, 0, read);
                        await target.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);

                        done += read;
                        if (total > 0)
                            progress?.Report(Math.Min(1, (double)done / total));
                    }
                }
            }

            progress?.Report(1);

            return Convert.ToHexStringLower(sha256.GetHashAndReset());
        }
    }
}

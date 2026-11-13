using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Torii.Core.Omamori;

namespace Torii.Core.Releases
{
    public sealed class ReleaseInstaller
    {
        private readonly HttpClient http;
        private readonly string temporaryDirectory;

        public ReleaseInstaller(HttpClient http, string? temporaryDirectory = null)
        {
            this.http = http;
            this.temporaryDirectory = temporaryDirectory ?? Path.GetTempPath();
        }

        // Caller deletes the file
        public async Task<string> DownloadAsync(OmamoriRelease release, IProgress<double>? progress = null, CancellationToken cancellation = default)
        {
            if (release.Sha256 == null)
                throw new InvalidDataException($"Omamori {release.Version} has no checksum to check the download against.");

            string zip = Path.Combine(temporaryDirectory, $"torii-{Guid.NewGuid():N}.zip");

            try
            {
                string hash = await Downloads.ToFileAsync(http, release.Download, zip, release.Size, progress, cancellation).ConfigureAwait(false);

                if (!string.Equals(hash, release.Sha256, StringComparison.Ordinal))
                    throw new InvalidDataException($"The download of Omamori {release.Version} doesn't match its checksum.");

                return zip;
            }
            catch
            {
                File.Delete(zip);
                throw;
            }
        }
    }
}

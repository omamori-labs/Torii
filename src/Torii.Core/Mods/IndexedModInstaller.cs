using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Torii.Core.Mods
{
    public sealed class IndexedModInstaller
    {
        private readonly HttpClient http;
        private readonly string temporaryDirectory;

        public IndexedModInstaller(HttpClient http, string? temporaryDirectory = null)
        {
            this.http = http;
            this.temporaryDirectory = temporaryDirectory ?? Path.GetTempPath();
        }

        public async Task<InstalledMod> InstallAsync(ModLibrary library, IndexedMod mod, IndexedModVersion version, IProgress<double>? progress = null, CancellationToken cancellation = default)
        {
            string zip = Path.Combine(temporaryDirectory, $"torii-{Guid.NewGuid():N}.zip");

            try
            {
                string hash = await Downloads.ToFileAsync(http, version.Download, zip, version.Size, progress, cancellation).ConfigureAwait(false);

                if (!string.Equals(hash, version.Sha256, StringComparison.Ordinal))
                    throw new InvalidDataException($"The download of {mod.Name} {version.Version} doesn't match its checksum.");

                return library.InstallFromZip(zip, mod.Id, version.Version);
            }
            finally
            {
                File.Delete(zip);
            }
        }
    }
}

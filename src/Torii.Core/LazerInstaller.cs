using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Torii.Core
{
    public sealed class LazerInstaller
    {
        private const string package_prefix = "lib/app/";

        private readonly HttpClient http;
        private readonly string temporaryDirectory;

        public LazerInstaller(HttpClient http, string? temporaryDirectory = null)
        {
            this.http = http;
            this.temporaryDirectory = temporaryDirectory ?? Path.GetTempPath();
        }

        public async Task InstallAsync(LazerInstance instance, LazerPackage package, IProgress<double>? progress = null, CancellationToken cancellation = default)
        {
            refuseDowngrade(instance, package.Version);

            string file = Path.Combine(temporaryDirectory, $"torii-{Guid.NewGuid():N}.nupkg");

            try
            {
                string hash = await Downloads.ToFileAsync(http, package.Download, file, package.Size, progress, cancellation).ConfigureAwait(false);

                if (!string.Equals(hash, package.Sha256, StringComparison.Ordinal))
                    throw new InvalidDataException($"The download of osu! {package.Version} doesn't match its checksum.");

                InstallFromPackage(instance, file);
            }
            finally
            {
                File.Delete(file);
            }
        }

        public static void InstallFromPackage(LazerInstance instance, string packagePath)
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Torii can only set up osu! on Windows for now.");

            using ZipArchive archive = Zips.Open(packagePath);

            var files = new List<(ZipArchiveEntry Entry, string Relative, string Destination)>();

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string name = Paths.EntryName(entry.FullName);

                if (!name.StartsWith(package_prefix, StringComparison.Ordinal) || name.EndsWith('/'))
                    continue;

                // Paths are url-encoded, osu!.exe is osu%21.exe
                string relative = Uri.UnescapeDataString(name[package_prefix.Length..]);
                string destination = Path.GetFullPath(Path.Combine(instance.Directory, relative));

                if (!Paths.IsInside(instance.Directory, destination))
                    throw new InvalidDataException($"This isn't an osu! package, it has {name} in it.");

                files.Add((entry, Path.GetRelativePath(instance.Directory, destination), destination));
            }

            if (!files.Any(f => f.Relative == "osu.Game.dll"))
                throw new InvalidDataException("This isn't an osu! package, it has no game in it.");

            Directory.CreateDirectory(instance.Directory);
            removeGameFiles(instance);

            foreach ((ZipArchiveEntry entry, _, string destination) in files)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }

            File.WriteAllLines(instance.GameFilesList, files.Select(f => f.Relative));

            if (!File.Exists(instance.PortableMarker))
                File.WriteAllText(instance.PortableMarker, string.Empty);
        }

        // Data lives in the same folder, only delete what was extracted
        private static void removeGameFiles(LazerInstance instance)
        {
            if (!File.Exists(instance.GameFilesList))
                return;

            foreach (string relative in File.ReadAllLines(instance.GameFilesList).Where(l => l.Length > 0))
            {
                string path = Path.GetFullPath(Path.Combine(instance.Directory, relative));

                if (Paths.IsInside(instance.Directory, path) && File.Exists(path))
                    File.Delete(path);
            }
        }

        // Older osu! can't always open newer data
        private static void refuseDowngrade(LazerInstance instance, string version)
        {
            string? installed = instance.ReadVersion();

            if (installed != null && LazerVersions.Compare(installed, version) > 0)
                throw new InvalidOperationException($"This would replace osu! {installed} with the older {version}, which may not be able to read your data.");
        }
    }
}

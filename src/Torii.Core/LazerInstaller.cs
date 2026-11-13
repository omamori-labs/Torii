using System;
using System.Collections.Generic;
using System.Diagnostics;
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

                InstallFromPackage(instance, file, temporaryDirectory);
            }
            finally
            {
                File.Delete(file);
            }
        }

        public static void InstallFromPackage(LazerInstance instance, string packagePath, string? temporaryDirectory = null, Action<string, string>? unpackAppImage = null)
        {
            using ZipArchive archive = Zips.Open(packagePath);

            // The Linux package is one AppImage, the Windows one has the game's files as they are
            ZipArchiveEntry? appImage = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".AppImage", StringComparison.Ordinal));

            if (appImage != null)
                installAppImage(instance, appImage, temporaryDirectory ?? Path.GetTempPath(), unpackAppImage ?? runAppImage);
            else
                installFiles(instance, archive);

            if (!File.Exists(instance.PortableMarker))
                File.WriteAllText(instance.PortableMarker, string.Empty);
        }

        private static void installFiles(LazerInstance instance, ZipArchive archive)
        {
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
        }

        private static void installAppImage(LazerInstance instance, ZipArchiveEntry appImage, string temporaryDirectory, Action<string, string> unpack)
        {
            string work = Path.Combine(temporaryDirectory, $"torii-appimage-{Guid.NewGuid():N}");
            Directory.CreateDirectory(work);

            try
            {
                string file = Path.Combine(work, "osulazer.AppImage");
                appImage.ExtractToFile(file);

                unpack(file, work);

                // --appimage-extract always unpacks to squashfs-root
                string game = Path.Combine(work, "squashfs-root", "usr", "bin");

                if (!File.Exists(Path.Combine(game, "osu.Game.dll")))
                    throw new InvalidDataException("This isn't an osu! package, it has no game in it.");

                Directory.CreateDirectory(instance.Directory);
                removeGameFiles(instance);

                var copied = new List<string>();

                // File.Copy keeps the unix mode, so osu! stays executable
                foreach (string source in Directory.EnumerateFiles(game, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(game, source);
                    string destination = Path.Combine(instance.Directory, relative);

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(source, destination, overwrite: true);
                    copied.Add(relative);
                }

                File.WriteAllLines(instance.GameFilesList, copied);
            }
            finally
            {
                Directory.Delete(work, recursive: true);
            }
        }

        private static void runAppImage(string appImage, string workDirectory)
        {
            if (OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("The Linux osu! package can't be unpacked on Windows.");

            File.SetUnixFileMode(appImage, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            // Unpacking itself doesn't need FUSE, running the AppImage normally would
            var info = new ProcessStartInfo(appImage, "--appimage-extract")
            {
                WorkingDirectory = workDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using Process process = Process.Start(info) ?? throw new InvalidDataException("The osu! package couldn't be unpacked.");

            // It lists every file it writes, an unread pipe would fill up and stall it
            process.OutputDataReceived += (_, _) => { };
            process.ErrorDataReceived += (_, _) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new InvalidDataException("The osu! package couldn't be unpacked.");
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

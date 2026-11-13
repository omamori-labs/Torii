using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Torii.Core.Library;

namespace Torii.Core.Lazer
{
    public sealed class GameCache
    {
        private const string staging_prefix = "staging-";

        private readonly Func<string, string, bool> link;
        private readonly Dictionary<string, int> busy = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Lock gate = new Lock();

        public GameCache(string directory, Func<string, string, bool>? link = null)
        {
            Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            this.link = link ?? HardLinks.TryCreate;
        }

        public string Root { get; }

        public static string DefaultDirectory() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Torii", "games");

        public bool Has(string version)
        {
            string directory = pathOf(version);
            string list = new LazerInstance(directory).GameFilesList;

            return File.Exists(list) && File.ReadAllLines(list).Where(l => l.Length > 0).All(relative => File.Exists(Path.Combine(directory, relative)));
        }

        public async Task InstallAsync(string version, LazerInstance instance, Func<string, Task> fill)
        {
            string directory = pathOf(version);

            lock (gate)
                busy[version] = busy.GetValueOrDefault(version) + 1;

            try
            {
                await add(version, directory, fill).ConfigureAwait(false);
                apply(directory, instance);
            }
            finally
            {
                lock (gate)
                {
                    if (--busy[version] == 0)
                        busy.Remove(version);
                }
            }
        }

        public void Prune(IEnumerable<string> inUse)
        {
            if (!Directory.Exists(Root))
                return;

            var keep = new HashSet<string>(inUse, StringComparer.OrdinalIgnoreCase);

            foreach (string directory in Directory.EnumerateDirectories(Root))
            {
                string name = Path.GetFileName(directory);

                try
                {
                    if (name.StartsWith(staging_prefix, StringComparison.Ordinal))
                    {
                        if (Directory.GetLastWriteTimeUtc(directory) < DateTime.UtcNow.AddDays(-1))
                            Directory.Delete(directory, recursive: true);

                        continue;
                    }

                    lock (gate)
                    {
                        if (!keep.Contains(name) && !busy.ContainsKey(name))
                            Directory.Delete(directory, recursive: true);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Windows won't delete a game that is running from linked files, the next prune gets it
                }
            }
        }

        private async Task add(string version, string directory, Func<string, Task> fill)
        {
            if (Has(version))
                return;

            Directory.CreateDirectory(Root);

            string staging = Path.Combine(Root, staging_prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);

            try
            {
                await fill(staging).ConfigureAwait(false);

                if (!File.Exists(new LazerInstance(staging).GameFilesList))
                    throw new InvalidDataException("The osu! package left no game files.");

                lock (gate)
                {
                    if (Has(version))
                        return;

                    if (Directory.Exists(directory))
                        Directory.Delete(directory, recursive: true);

                    Directory.Move(staging, directory);
                }
            }
            finally
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
            }
        }

        private void apply(string directory, LazerInstance instance)
        {
            string[] files = [.. File.ReadAllLines(new LazerInstance(directory).GameFilesList).Where(l => l.Length > 0)];

            Directory.CreateDirectory(instance.Directory);
            LazerInstaller.RemoveGameFiles(instance);

            bool linking = true;

            foreach (string relative in files)
            {
                string source = Path.Combine(directory, relative);
                string destination = Path.GetFullPath(Path.Combine(instance.Directory, relative));

                if (!Paths.IsInside(instance.Directory, destination))
                    throw new InvalidDataException($"The cached osu! has {relative} in it.");

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                if (File.Exists(destination))
                    File.Delete(destination);

                if (linking && link(destination, source))
                    continue;

                linking = false;
                File.Copy(source, destination);
            }

            File.WriteAllLines(instance.GameFilesList, files);

            if (!File.Exists(instance.PortableMarker))
                File.WriteAllText(instance.PortableMarker, string.Empty);
        }

        private string pathOf(string version)
        {
            if (version.Length == 0 || version is "." or ".." || version != Path.GetFileName(version) || version.StartsWith(staging_prefix, StringComparison.Ordinal))
                throw new ArgumentException($"'{version}' isn't an osu! version.", nameof(version));

            return Path.Combine(Root, version);
        }
    }
}

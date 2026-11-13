using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Torii.Core.Omamori
{
    public static class OmamoriInstaller
    {
        public static void InstallFromZip(OmamoriInstall install, string zipPath)
        {
            using ZipArchive archive = Zips.Open(zipPath);

            var files = new List<(ZipArchiveEntry Entry, string Destination)>();

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string name = Paths.EntryName(entry.FullName);
                if (name.EndsWith('/'))
                    continue;

                files.Add((entry, destination(install, name)));
            }

            if (!files.Any(f => Paths.Same(f.Destination, install.RulesetAssembly)) || !files.Any(f => Paths.Same(f.Destination, install.CoreAssembly)))
                throw new InvalidDataException("This isn't an Omamori release, it has no loader in it.");

            // Stale dlls would still get loaded
            if (Directory.Exists(install.CoreDirectory))
                Directory.Delete(install.CoreDirectory, recursive: true);

            foreach ((ZipArchiveEntry entry, string path) in files)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                entry.ExtractToFile(path, overwrite: true);
            }
        }

        public static void Uninstall(OmamoriInstall install, bool removeMods = false)
        {
            if (Directory.Exists(install.RulesetsDirectory))
            {
                // The bootstrap ships with a pdb
                foreach (string file in Directory.EnumerateFiles(install.RulesetsDirectory, Path.ChangeExtension(OmamoriInstall.RULESET_FILENAME, ".*")))
                    File.Delete(file);
            }

            if (Directory.Exists(install.CoreDirectory))
                Directory.Delete(install.CoreDirectory, recursive: true);

            if (!Directory.Exists(install.Root))
                return;

            if (removeMods || !Directory.EnumerateFileSystemEntries(install.Root).Any())
                Directory.Delete(install.Root, recursive: true);
        }

        private static string destination(OmamoriInstall install, string name)
        {
            string path = Path.GetFullPath(Path.Combine(install.DataDirectory, name));

            if (!Paths.IsInside(install.RulesetsDirectory, path) && !Paths.IsInside(install.CoreDirectory, path))
                throw new InvalidDataException($"This isn't an Omamori release, it has {name} in it.");

            return path;
        }
    }
}

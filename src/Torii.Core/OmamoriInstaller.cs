using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Torii.Core
{
    /// <summary>Installs and removes the loader. osu! must be closed, it keeps these files open</summary>
    public static class OmamoriInstaller
    {
        /// <summary>Installs or updates. Mods and state files are left alone</summary>
        /// <exception cref="InvalidDataException">Not an Omamori release. Nothing was changed</exception>
        public static void InstallFromZip(OmamoriInstall install, string zipPath)
        {
            using ZipArchive archive = open(zipPath);

            var files = new List<(ZipArchiveEntry Entry, string Destination)>();

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                // Zips made on Windows can use either separator
                string name = entry.FullName.Replace('\\', '/');
                if (name.EndsWith('/'))
                    continue;

                files.Add((entry, destination(install, name)));
            }

            if (!files.Any(f => samePath(f.Destination, install.RulesetAssembly)) || !files.Any(f => samePath(f.Destination, install.CoreAssembly)))
                throw new InvalidDataException("This isn't an Omamori release, it has no loader in it.");

            // Otherwise dlls dropped by the new release would still get loaded
            if (Directory.Exists(install.CoreDirectory))
                Directory.Delete(install.CoreDirectory, recursive: true);

            foreach ((ZipArchiveEntry entry, string path) in files)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                entry.ExtractToFile(path, overwrite: true);
            }
        }

        /// <summary>Removes the loader, and with <paramref name="removeMods"/> the mods and state too</summary>
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

        private static ZipArchive open(string zipPath)
        {
            try
            {
                return ZipFile.OpenRead(zipPath);
            }
            catch (InvalidDataException e)
            {
                throw new InvalidDataException("This isn't a zip file.", e);
            }
        }

        private static string destination(OmamoriInstall install, string name)
        {
            string path = Path.GetFullPath(Path.Combine(install.DataDirectory, name));

            bool allowed = isInside(install.RulesetsDirectory, path) || isInside(install.Root, path);
            bool players = isInside(install.ModsDirectory, path) || samePath(path, install.SettingsFile) || samePath(path, install.FaultsFile);

            if (!allowed || players)
                throw new InvalidDataException($"This isn't an Omamori release, it has {name} in it.");

            return path;
        }

        private static bool isInside(string directory, string path) =>
            path.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, path_comparison);

        private static bool samePath(string a, string b) => string.Equals(a, b, path_comparison);

        private static readonly StringComparison path_comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
    }
}

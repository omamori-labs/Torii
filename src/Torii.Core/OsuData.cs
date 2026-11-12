using System;
using System.IO;

namespace Torii.Core
{
    /// <summary>Data folder of osu!lazer (beatmaps, scores), not its install folder. Omamori installs here</summary>
    public static class OsuData
    {
        /// <summary>Written by lazer into the default folder when the player moves their data</summary>
        public const string STORAGE_CONFIG = "storage.ini";

        private const string full_path_key = "FullPath";

        /// <summary>Where the data is unless the player moved it</summary>
        public static string DefaultDirectory()
        {
            Environment.SpecialFolder parent = OperatingSystem.IsLinux()
                ? Environment.SpecialFolder.LocalApplicationData
                : Environment.SpecialFolder.ApplicationData;

            return Path.Combine(Environment.GetFolderPath(parent), "osu");
        }

        /// <summary>The folder actually in use, following <see cref="STORAGE_CONFIG"/></summary>
        public static string Resolve(string defaultDirectory)
        {
            string config = Path.Combine(defaultDirectory, STORAGE_CONFIG);

            try
            {
                if (!File.Exists(config))
                    return defaultDirectory;

                foreach (string line in File.ReadLines(config))
                {
                    int separator = line.IndexOf('=', StringComparison.Ordinal);
                    if (separator < 0 || !line.AsSpan(0, separator).Trim().SequenceEqual(full_path_key))
                        continue;

                    string moved = line[(separator + 1)..].Trim();
                    return moved.Length > 0 ? moved : defaultDirectory;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return defaultDirectory;
        }

        /// <summary>True once lazer has run with this as its data folder</summary>
        public static bool LooksLikeData(string directory) =>
            File.Exists(Path.Combine(directory, "client.realm")) || Directory.Exists(Path.Combine(directory, "files"));
    }
}

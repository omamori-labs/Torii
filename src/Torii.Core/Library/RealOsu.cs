using System;
using System.IO;
using Torii.Core.Lazer;

namespace Torii.Core.Library
{
    public static class RealOsu
    {
        public static string? FindDataDirectory() => FindDataDirectory(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        public static string? ReadVersion() => ReadVersion(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "/Applications");

        internal static string? FindDataDirectory(string applicationData, string localApplicationData, string home)
        {
            string[] defaults =
            [
                Path.Combine(applicationData, "osu"),
                Path.Combine(localApplicationData, "osu"),
                Path.Combine(home, "Library", "Application Support", "osu"),
            ];

            foreach (string folder in defaults)
            {
                string data = movedTo(folder) ?? folder;

                if (File.Exists(Path.Combine(data, "client.realm")))
                    return data;
            }

            return null;
        }

        internal static string? ReadVersion(string localApplicationData, string applications)
        {
            string[] installs =
            [
                Path.Combine(localApplicationData, "osulazer", "current"),
                Path.Combine(applications, "osu!.app", "Contents", "Resources"),
            ];

            foreach (string folder in installs)
            {
                if (new LazerInstance(folder).ReadVersion() is string version)
                    return version;
            }

            return null;
        }

        // osu! keeps its data wherever storage.ini says once it was moved
        private static string? movedTo(string folder)
        {
            string ini = Path.Combine(folder, "storage.ini");

            try
            {
                if (!File.Exists(ini))
                    return null;

                foreach (string line in File.ReadLines(ini))
                {
                    int equals = line.IndexOf('=', StringComparison.Ordinal);

                    if (equals > 0 && line[..equals].Trim() == "FullPath" && line[(equals + 1)..].Trim() is { Length: > 0 } path)
                        return path;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }

            return null;
        }
    }
}

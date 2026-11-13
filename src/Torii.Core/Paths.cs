using System;
using System.IO;

namespace Torii.Core
{
    internal static class Paths
    {
        private static readonly StringComparison comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        public static bool IsInside(string directory, string path) =>
            path.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, comparison);

        public static bool Same(string a, string b) => string.Equals(a, b, comparison);

        // Zips made on Windows can use backslashes
        public static string EntryName(string fullName) => fullName.Replace('\\', '/');
    }
}

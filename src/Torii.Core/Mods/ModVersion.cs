using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Torii.Core.Mods
{
    internal sealed partial class ModVersion : IComparable<ModVersion>
    {
        private readonly int major;
        private readonly int minor;
        private readonly int patch;
        private readonly string[] prerelease;

        private ModVersion(int major, int minor, int patch, string[] prerelease)
        {
            this.major = major;
            this.minor = minor;
            this.patch = patch;
            this.prerelease = prerelease;
        }

        public int Major => major;

        public int Minor => minor;

        public static bool TryParse(string text, out ModVersion version)
        {
            version = null!;

            Match match = pattern().Match(text.Trim());

            if (!match.Success)
                return false;

            if (!int.TryParse(match.Groups[1].Value, out int major)
                || !tryNumber(match.Groups[2], out int minor)
                || !tryNumber(match.Groups[3], out int patch))
                return false;

            string[] prerelease = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : [];

            if (prerelease.Any(p => p.Length == 0 || (p.All(char.IsAsciiDigit) && p.Length > 1 && p[0] == '0')))
                return false;

            version = new ModVersion(major, minor, patch, prerelease);
            return true;
        }

        public int CompareTo(ModVersion? other)
        {
            if (other is null)
                return 1;

            int core = major.CompareTo(other.major);
            if (core == 0) core = minor.CompareTo(other.minor);
            if (core == 0) core = patch.CompareTo(other.patch);
            if (core != 0)
                return core;

            // A prerelease sorts before its release
            if (prerelease.Length == 0 || other.prerelease.Length == 0)
                return other.prerelease.Length.CompareTo(prerelease.Length);

            for (int i = 0; i < Math.Min(prerelease.Length, other.prerelease.Length); i++)
            {
                int result = compareIdentifier(prerelease[i], other.prerelease[i]);

                if (result != 0)
                    return result;
            }

            return prerelease.Length.CompareTo(other.prerelease.Length);
        }

        private static int compareIdentifier(string a, string b)
        {
            bool aNumeric = a.All(char.IsAsciiDigit);
            bool bNumeric = b.All(char.IsAsciiDigit);

            if (aNumeric && bNumeric)
                return a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);

            if (aNumeric != bNumeric)
                return aNumeric ? -1 : 1;

            return string.CompareOrdinal(a, b);
        }

        private static bool tryNumber(Group group, out int value)
        {
            value = 0;
            return !group.Success || int.TryParse(group.Value, out value);
        }

        [GeneratedRegex(@"^[vV]?(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$")]
        private static partial Regex pattern();
    }
}

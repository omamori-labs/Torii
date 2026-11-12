using System;
using System.Globalization;
using System.Linq;

namespace Torii.Core
{
    internal static class LazerVersions
    {
        /// <summary>Orders versions like 2026.1005.0-lazer by their numbers. Versions that don't parse compare as equal</summary>
        public static int Compare(string a, string b)
        {
            int[]? left = parse(a);
            int[]? right = parse(b);

            if (left == null || right == null)
                return 0;

            for (int i = 0; i < Math.Max(left.Length, right.Length); i++)
            {
                int difference = left.ElementAtOrDefault(i).CompareTo(right.ElementAtOrDefault(i));

                if (difference != 0)
                    return difference;
            }

            return 0;
        }

        private static int[]? parse(string version)
        {
            string[] parts = version.Split('-')[0].Split('.');
            int[] numbers = new int[parts.Length];

            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                    return null;
            }

            return numbers;
        }
    }
}

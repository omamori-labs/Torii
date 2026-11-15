using System;
using System.Collections.Generic;
using System.Linq;
using Torii.Core.Omamori;

namespace Torii.Core.Releases
{
    public static class ReleaseChannel
    {
        public static bool IsPrerelease(string version) => version.Split('+')[0].Contains('-', StringComparison.Ordinal);

        public static string? NewerThan(IReadOnlyList<OmamoriRelease> releases, string installed)
        {
            int position = -1;

            for (int i = 0; i < releases.Count && position < 0; i++)
            {
                if (releases[i].Version == installed)
                    position = i;
            }

            bool prerelease = IsPrerelease(installed);

            return position < 1 ? null : releases.Take(position).FirstOrDefault(r => prerelease || !r.Prerelease)?.Version;
        }
    }
}

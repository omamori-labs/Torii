using System;

namespace Torii.Core.Releases
{
    public static class ReleaseChannel
    {
        // Someone on an alpha keeps getting alphas, someone on a release only gets releases
        public static bool IsPrerelease(string version) => version.Split('+')[0].Contains('-', StringComparison.Ordinal);
    }
}

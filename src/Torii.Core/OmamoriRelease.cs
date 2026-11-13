using System;

namespace Torii.Core
{
    public sealed record OmamoriRelease(string Version, bool Prerelease, Uri Download, string? Sha256, long Size, DateTimeOffset? Published);
}

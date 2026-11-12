using System;

namespace Torii.Core
{
    /// <summary>A release with an installable zip. Sha256 is lowercase hex, null when GitHub gave no digest</summary>
    public sealed record OmamoriRelease(string Version, bool Prerelease, Uri Download, string? Sha256, long Size, DateTimeOffset? Published);
}

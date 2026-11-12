using System;

namespace Torii.Core
{
    /// <summary>A downloadable osu!lazer build for this platform. Sha256 is lowercase hex</summary>
    public sealed record LazerPackage(string Version, Uri Download, string Sha256, long Size);
}

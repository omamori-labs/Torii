using System;

namespace Torii.Core
{
    /// <summary>one omamori release that has a zip players can install</summary>
    /// <param name="Version">the release version, the tag without its v</param>
    /// <param name="Prerelease">alphas and betas</param>
    /// <param name="Download">the omamori-&lt;version&gt;.zip that extracts over the data folder</param>
    /// <param name="Sha256">lowercase hex of the zip's hash, null when github didn't give one</param>
    /// <param name="Size">the zip's size in bytes</param>
    /// <param name="Published">when it was released</param>
    public sealed record OmamoriRelease(string Version, bool Prerelease, Uri Download, string? Sha256, long Size, DateTimeOffset? Published);
}

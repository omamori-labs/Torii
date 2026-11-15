using System;

namespace Torii.Core.Mods
{
    public sealed record IndexedModVersion(string Version, string ApiVersion, Uri Download, string Sha256, long Size);
}

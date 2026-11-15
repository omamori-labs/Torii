using System;
using System.Collections.Generic;

namespace Torii.Core.Mods
{
    public sealed record IndexedModVersion(string Version, string ApiVersion, Uri Download, string Sha256, long Size, IReadOnlyList<ModDependency> Dependencies);
}

using System.Collections.Generic;
using System.Linq;

namespace Torii.Core.Mods
{
    public sealed record ModInfo(string Id, string Name, string Version, string Author, string Description, string Entry, IReadOnlyList<string> Capabilities, IReadOnlyList<ModDependency> Dependencies)
    {
        public bool Patches => Capabilities.Contains("Patches");
    }
}

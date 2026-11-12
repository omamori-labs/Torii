using System.Collections.Generic;
using System.Linq;

namespace Torii.Core
{
    /// <summary>The parts of a mod.json Torii shows. Omamori does the full validation when it loads the mod</summary>
    public sealed record ModInfo(string Id, string Name, string Version, string Author, string Description, string Entry, IReadOnlyList<string> Capabilities)
    {
        /// <summary>The mod changes osu!'s own code</summary>
        public bool Patches => Capabilities.Contains("Patches");
    }
}

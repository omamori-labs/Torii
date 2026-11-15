using System;
using System.Collections.Generic;

namespace Torii.Core.Mods
{
    public sealed record IndexedMod(string Id, string Name, string Author, string Description, Uri? Repository, IReadOnlyList<IndexedModVersion> Versions)
    {
        public IndexedModVersion Latest => Versions[0];
    }
}

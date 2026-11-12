using System.IO;

namespace Torii.Core
{
    /// <summary>A folder in omamori/mods. Manifest is null when its mod.json can't be read</summary>
    public sealed record InstalledMod(string Directory, ModInfo? Manifest, ModState State, string? Problem)
    {
        public string Label => string.IsNullOrWhiteSpace(Manifest?.Name) ? Path.GetFileName(Directory) : Manifest.Name;
    }
}

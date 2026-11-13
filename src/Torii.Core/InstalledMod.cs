using System.IO;

namespace Torii.Core
{
    public sealed record InstalledMod(string Directory, ModInfo? Manifest, ModState State, string? Problem)
    {
        public string Label => string.IsNullOrWhiteSpace(Manifest?.Name) ? Path.GetFileName(Directory) : Manifest.Name;
    }
}

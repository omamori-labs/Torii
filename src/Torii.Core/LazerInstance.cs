using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Torii.Core
{
    /// <summary>A copy of osu!lazer in its own folder, keeping its data there too instead of in the player's osu! folder</summary>
    public sealed class LazerInstance
    {
        public LazerInstance(string directory)
        {
            Directory = Path.GetFullPath(directory);
        }

        public string Directory { get; }

        /// <summary>Where Torii keeps its instance unless told otherwise</summary>
        public static string DefaultDirectory() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Torii", "instance");

        public string Executable => Path.Combine(Directory, OperatingSystem.IsWindows() ? "osu!.exe" : "osu!");

        /// <summary>osu!framework only keeps data beside the game when this file is there</summary>
        public string PortableMarker => Path.Combine(Directory, "framework.ini");

        /// <summary>What the last install extracted, so an update can remove files the new version dropped</summary>
        public string GameFilesList => Path.Combine(Directory, "torii-game-files.txt");

        /// <summary>The instance is its own data folder</summary>
        public OmamoriInstall Omamori => new OmamoriInstall(Directory);

        /// <summary>Without the marker the game would open the player's real data folder</summary>
        public bool IsInstalled => File.Exists(Path.Combine(Directory, "osu.Game.dll")) && File.Exists(PortableMarker);

        /// <summary>As ppy/osu tags it, e.g. 2026.1005.0-lazer. Null when no game is there</summary>
        public string? ReadVersion()
        {
            // Velopack leaves the package's nuspec here
            string path = Path.Combine(Directory, "sq.version");

            try
            {
                if (!File.Exists(path))
                    return null;

                string? version = XDocument.Load(path).Descendants().FirstOrDefault(e => e.Name.LocalName == "version")?.Value.Trim();
                return string.IsNullOrEmpty(version) ? null : version;
            }
            catch (Exception e) when (e is XmlException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}

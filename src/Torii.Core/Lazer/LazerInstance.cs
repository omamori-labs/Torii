using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Torii.Core.Omamori;

namespace Torii.Core.Lazer
{
    public sealed class LazerInstance
    {
        public LazerInstance(string directory)
        {
            Directory = Path.GetFullPath(directory);
        }

        public string Directory { get; }

        public static string DefaultDirectory() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Torii", "instance");

        public string Executable => Path.Combine(Directory, OperatingSystem.IsWindows() ? "osu!.exe" : "osu!");

        // osu!framework only keeps data beside the game when this file exists
        public string PortableMarker => Path.Combine(Directory, "framework.ini");

        public string GameFilesList => Path.Combine(Directory, "torii-game-files.txt");

        public OmamoriInstall Omamori => new OmamoriInstall(Directory);

        // No marker and the game opens the player's real data
        public bool IsInstalled => File.Exists(Path.Combine(Directory, "osu.Game.dll")) && File.Exists(PortableMarker);

        public string? ReadVersion()
        {
            // Velopack leaves the nuspec here
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

using System;
using System.Diagnostics;
using System.IO;

namespace Torii.Core
{
    /// <summary>Where Omamori's files go in one osu! data folder. Mirrors Layout in Omamori's source</summary>
    public sealed class OmamoriInstall
    {
        /// <summary>The bootstrap, which lazer loads as a ruleset</summary>
        public const string RULESET_FILENAME = "osu.Game.Rulesets.Omamori.dll";

        public const string CORE_FILENAME = "Omamori.Core.dll";

        public OmamoriInstall(string dataDirectory)
        {
            DataDirectory = Path.GetFullPath(dataDirectory);
        }

        public string DataDirectory { get; }

        public string RulesetsDirectory => Path.Combine(DataDirectory, "rulesets");

        public string RulesetAssembly => Path.Combine(RulesetsDirectory, RULESET_FILENAME);

        /// <summary>Loader, mods and state files</summary>
        public string Root => Path.Combine(DataDirectory, "omamori");

        public string CoreDirectory => Path.Combine(Root, "core");

        public string CoreAssembly => Path.Combine(CoreDirectory, CORE_FILENAME);

        public string ModsDirectory => Path.Combine(Root, "mods");

        /// <summary>Mods the player turned off</summary>
        public string SettingsFile => Path.Combine(Root, "settings.json");

        /// <summary>Crash history per mod</summary>
        public string FaultsFile => Path.Combine(Root, "faults.json");

        /// <summary>Needs both the bootstrap and the core it starts</summary>
        public bool IsInstalled => File.Exists(RulesetAssembly) && File.Exists(CoreAssembly);

        /// <summary>Null when not installed or the core has no version stamp</summary>
        public string? ReadVersion()
        {
            if (!IsInstalled)
                return null;

            try
            {
                // Stamped as version+commit
                string? stamped = FileVersionInfo.GetVersionInfo(CoreAssembly).ProductVersion;
                string version = (stamped ?? string.Empty).Split('+')[0].Trim();

                return version.Length > 0 ? version : null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}

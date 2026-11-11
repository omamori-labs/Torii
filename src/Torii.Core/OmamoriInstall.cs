using System;
using System.Diagnostics;
using System.IO;

namespace Torii.Core
{
    /// <summary>
    ///     where omamori's files go inside one osu! data folder, whether it's installed there or not
    ///     mirrors Layout in omamori's source
    /// </summary>
    public sealed class OmamoriInstall
    {
        /// <summary>the bootstrap lazer loads as a ruleset</summary>
        public const string RULESET_FILENAME = "osu.Game.Rulesets.Omamori.dll";

        public const string CORE_FILENAME = "Omamori.Core.dll";

        public OmamoriInstall(string dataDirectory)
        {
            DataDirectory = Path.GetFullPath(dataDirectory);
        }

        public string DataDirectory { get; }

        public string RulesetsDirectory => Path.Combine(DataDirectory, "rulesets");

        public string RulesetAssembly => Path.Combine(RulesetsDirectory, RULESET_FILENAME);

        /// <summary>everything omamori keeps, the loader, the mods and its state files</summary>
        public string Root => Path.Combine(DataDirectory, "omamori");

        public string CoreDirectory => Path.Combine(Root, "core");

        public string CoreAssembly => Path.Combine(CoreDirectory, CORE_FILENAME);

        public string ModsDirectory => Path.Combine(Root, "mods");

        /// <summary>which mods the player turned off</summary>
        public string SettingsFile => Path.Combine(Root, "settings.json");

        /// <summary>which mods crashed in which session</summary>
        public string FaultsFile => Path.Combine(Root, "faults.json");

        /// <summary>lazer only loads omamori with both halves there, the bootstrap and the core it starts</summary>
        public bool IsInstalled => File.Exists(RulesetAssembly) && File.Exists(CoreAssembly);

        /// <summary>the installed release, null when it isn't installed or the core doesn't say</summary>
        public string? ReadVersion()
        {
            if (!IsInstalled)
                return null;

            try
            {
                // the build stamps the release version here, followed by +commit
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

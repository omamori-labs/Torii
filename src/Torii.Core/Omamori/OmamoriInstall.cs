using System;
using System.Diagnostics;
using System.IO;

namespace Torii.Core.Omamori
{
    // Mirrors Layout in Omamori
    public sealed class OmamoriInstall
    {
        public const string RULESET_FILENAME = "osu.Game.Rulesets.Omamori.dll";

        public const string CORE_FILENAME = "Omamori.Core.dll";

        public OmamoriInstall(string dataDirectory)
        {
            DataDirectory = Path.GetFullPath(dataDirectory);
        }

        public string DataDirectory { get; }

        public string RulesetsDirectory => Path.Combine(DataDirectory, "rulesets");

        public string RulesetAssembly => Path.Combine(RulesetsDirectory, RULESET_FILENAME);

        public string Root => Path.Combine(DataDirectory, "omamori");

        public string CoreDirectory => Path.Combine(Root, "core");

        public string CoreAssembly => Path.Combine(CoreDirectory, CORE_FILENAME);

        public string ApiAssembly => Path.Combine(CoreDirectory, "Omamori.API.dll");

        public string ModsDirectory => Path.Combine(Root, "mods");

        public string SettingsFile => Path.Combine(Root, "settings.json");

        public string FaultsFile => Path.Combine(Root, "faults.json");

        public bool IsInstalled => File.Exists(RulesetAssembly) && File.Exists(CoreAssembly);

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

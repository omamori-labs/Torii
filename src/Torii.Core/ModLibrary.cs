using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Torii.Core
{
    /// <summary>The mods in one install, as Omamori will see them next time osu! starts</summary>
    public sealed class ModLibrary
    {
        // FaultHistory.STRIKES_TO_SKIP in Omamori
        private const int strikes_to_skip = 2;

        private readonly OmamoriInstall install;

        public ModLibrary(OmamoriInstall install)
        {
            this.install = install;
        }

        /// <summary>In the order Omamori discovers them. Folders without a mod.json aren't mods</summary>
        public IReadOnlyList<InstalledMod> List()
        {
            var mods = new List<InstalledMod>();

            if (!Directory.Exists(install.ModsDirectory))
                return mods;

            HashSet<string> disabled = readDisabled();
            JsonObject faults = readFaults();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (string directory in Directory.GetDirectories(install.ModsDirectory).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string manifestPath = Path.Combine(directory, ModManifestReader.FILENAME);

                if (!File.Exists(manifestPath))
                    continue;

                mods.Add(describe(directory, manifestPath, disabled, faults, seen));
            }

            return mods;
        }

        private static InstalledMod describe(string directory, string manifestPath, HashSet<string> disabled, JsonObject faults, HashSet<string> seen)
        {
            ModInfo manifest;

            try
            {
                manifest = ModManifestReader.Read(manifestPath);
            }
            catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                return new InstalledMod(directory, null, ModState.Problem, e is InvalidDataException ? e.Message : $"{ModManifestReader.FILENAME} can't be read");
            }

            // Omamori keeps the first one it finds
            if (!seen.Add(manifest.Id))
                return new InstalledMod(directory, manifest, ModState.Problem, "Another mod with the same id is installed");

            if (!File.Exists(Path.Combine(directory, manifest.Entry)))
                return new InstalledMod(directory, manifest, ModState.Problem, $"{manifest.Entry} is missing");

            if (disabled.Contains(manifest.Id))
                return new InstalledMod(directory, manifest, ModState.Off, null);

            if (crash(faults, manifest) is string crashed)
                return new InstalledMod(directory, manifest, ModState.Crashed, crashed);

            return new InstalledMod(directory, manifest, ModState.On, null);
        }

        private static string? crash(JsonObject faults, ModInfo manifest)
        {
            if (faults[manifest.Id] is not JsonObject record)
                return null;

            // A record for another version is dropped by Omamori on the next launch
            if (text(record["version"]) != manifest.Version)
                return null;

            int strikes = record["strikes"] is JsonValue value && value.TryGetValue(out int count) ? count : 0;
            if (strikes < strikes_to_skip)
                return null;

            string error = text(record["lastError"]);
            return $"Crashed in {strikes} sessions in a row{(error.Length > 0 ? $" ({error})" : string.Empty)}";
        }

        private HashSet<string> readDisabled()
        {
            var disabled = new HashSet<string>(StringComparer.Ordinal);

            if (readState(install.SettingsFile)["disabledMods"] is JsonArray ids)
            {
                foreach (JsonNode? id in ids)
                    disabled.Add(text(id));
            }

            return disabled;
        }

        private JsonObject readFaults() => readState(install.FaultsFile)["mods"] as JsonObject ?? [];

        // Omamori starts fresh when a state file is missing or unreadable, so the same goes here
        private static JsonObject readState(string path)
        {
            try
            {
                if (File.Exists(path))
                    return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? [];
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
            {
            }

            return [];
        }

        private static string text(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? s) ? s : string.Empty;
    }
}

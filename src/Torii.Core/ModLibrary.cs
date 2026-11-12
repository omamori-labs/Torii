using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Torii.Core
{
    /// <summary>The mods in one install, as Omamori will see them next time osu! starts</summary>
    public sealed class ModLibrary
    {
        // FaultHistory.STRIKES_TO_SKIP in Omamori
        private const int strikes_to_skip = 2;

        // Same output as Omamori's JsonFile
        private static readonly JsonSerializerOptions json_options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private readonly OmamoriInstall install;
        private readonly Func<bool> osuRunning;

        public ModLibrary(OmamoriInstall install, Func<bool>? osuRunning = null)
        {
            this.install = install;
            this.osuRunning = osuRunning ?? (() => OsuProcess.IsRunningIn(install.DataDirectory));
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

        /// <summary>Takes effect the next time osu! starts</summary>
        /// <exception cref="InvalidOperationException">osu! is running and would overwrite the change</exception>
        public void SetEnabled(string id, bool enabled)
        {
            requireOsuClosed();

            JsonObject settings = readState(install.SettingsFile);

            if (settings["disabledMods"] is not JsonArray disabled)
                settings["disabledMods"] = disabled = [];

            List<JsonNode?> matches = [.. disabled.Where(d => text(d) == id)];

            if (enabled == (matches.Count == 0))
                return;

            foreach (JsonNode? match in matches)
                disabled.Remove(match);

            if (!enabled)
                disabled.Add(id);

            writeState(install.SettingsFile, settings);
        }

        /// <summary>Clears a mod's crash history so Omamori loads it again</summary>
        /// <exception cref="InvalidOperationException">osu! is running and would overwrite the change</exception>
        public void Retry(string id)
        {
            requireOsuClosed();

            JsonObject faults = readState(install.FaultsFile);

            if (faults["mods"] is JsonObject mods && mods.Remove(id))
                writeState(install.FaultsFile, faults);
        }

        /// <summary>Deletes the mod's folder. Its settings and data under omamori/ are kept</summary>
        /// <exception cref="InvalidOperationException">osu! is running and has the mod loaded</exception>
        public void Remove(InstalledMod mod)
        {
            requireOsuClosed();

            if (!Paths.IsInside(install.ModsDirectory, Path.GetFullPath(mod.Directory)))
                throw new ArgumentException("Not a mod of this install.", nameof(mod));

            if (Directory.Exists(mod.Directory))
                Directory.Delete(mod.Directory, recursive: true);

            // So installing it again later doesn't start turned off
            if (mod.Manifest != null && !List().Any(m => m.Manifest?.Id == mod.Manifest.Id))
                SetEnabled(mod.Manifest.Id, true);
        }

        /// <summary>Installs or updates a mod from a zip with a mod.json at its root or in its only top folder</summary>
        /// <exception cref="InvalidDataException">Not an Omamori mod. Nothing was changed</exception>
        /// <exception cref="InvalidOperationException">osu! is running and may have the old version loaded</exception>
        public InstalledMod InstallFromZip(string zipPath)
        {
            requireOsuClosed();

            // Not in mods/, a leftover there would show up as a mod
            string staging = Path.Combine(install.Root, $".torii-{Guid.NewGuid():N}");

            try
            {
                extractMod(zipPath, staging);

                ModInfo manifest = ModManifestReader.Read(Path.Combine(staging, ModManifestReader.FILENAME));

                if (!File.Exists(Path.Combine(staging, manifest.Entry)))
                    throw new InvalidDataException($"This isn't an Omamori mod, {manifest.Entry} is missing from the zip.");

                foreach (InstalledMod old in List().Where(m => m.Manifest?.Id == manifest.Id))
                    Directory.Delete(old.Directory, recursive: true);

                string target = Path.Combine(install.ModsDirectory, manifest.Id);

                if (Directory.Exists(target))
                    throw new IOException($"The folder {manifest.Id} in the mods folder is in use by something else.");

                Directory.CreateDirectory(install.ModsDirectory);
                Directory.Move(staging, target);

                return List().First(m => Paths.Same(m.Directory, target));
            }
            finally
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
            }
        }

        private static void extractMod(string zipPath, string staging)
        {
            using ZipArchive archive = Zips.Open(zipPath);

            List<(ZipArchiveEntry Entry, string Name)> files =
            [
                .. archive.Entries.Select(e => (Entry: e, Name: Paths.EntryName(e.FullName))).Where(f => !f.Name.EndsWith('/'))
            ];

            string prefix = modFolder([.. files.Select(f => f.Name)])
                            ?? throw new InvalidDataException($"This isn't an Omamori mod, it has no {ModManifestReader.FILENAME}.");

            foreach ((ZipArchiveEntry entry, string name) in files)
            {
                string path = Path.GetFullPath(Path.Combine(staging, name[prefix.Length..]));

                if (!Paths.IsInside(staging, path))
                    throw new InvalidDataException($"This isn't an Omamori mod, it has {name} in it.");

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                entry.ExtractToFile(path);
            }
        }

        // Omamori's build packs mods as <id>/mod.json, a hand-made zip may have it at the root
        private static string? modFolder(List<string> names)
        {
            if (names.Contains(ModManifestReader.FILENAME))
                return string.Empty;

            List<string> folders = [.. names.Select(n => n.Split('/')[0]).Distinct()];

            return folders.Count == 1 && names.Contains($"{folders[0]}/{ModManifestReader.FILENAME}") ? $"{folders[0]}/" : null;
        }

        private void requireOsuClosed()
        {
            if (osuRunning())
                throw new InvalidOperationException("Close osu! first, it would overwrite the change.");
        }

        private static void writeState(string path, JsonObject state)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            string temporary = path + ".tmp";
            File.WriteAllText(temporary, state.ToJsonString(json_options));
            File.Move(temporary, path, overwrite: true);
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

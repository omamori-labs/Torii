using System;
using System.Collections.Generic;
using System.Linq;

namespace Torii.Core.Mods
{
    public sealed record PlannedInstall(IndexedMod Mod, IndexedModVersion Version);

    public sealed record ModUpdate(InstalledMod Installed, IndexedMod Mod, IndexedModVersion Version);

    public static class ModIndexPlanner
    {
        public static IReadOnlyList<PlannedInstall> Plan(IReadOnlyList<IndexedMod> index, IReadOnlyList<InstalledMod> installed, IndexedMod mod, IndexedModVersion version, string? api)
        {
            if (api != null && ModApi.Problem(version.ApiVersion, api) is { } incompatible)
                throw new InvalidOperationException($"{mod.Name} {version.Version} is {incompatible}.");

            var plan = new List<PlannedInstall>();
            add(index, installed, mod, version, api, plan, []);

            return plan;
        }

        public static IndexedModVersion? Newest(IndexedMod mod, string? api) => mod.Versions.FirstOrDefault(v => compatible(v, api));

        public static IReadOnlyList<ModUpdate> Updates(IReadOnlyList<IndexedMod> index, IReadOnlyList<InstalledMod> installed, string? api)
        {
            var updates = new List<ModUpdate>();

            foreach (InstalledMod mod in installed)
            {
                if (mod.Manifest == null || index.FirstOrDefault(m => m.Id == mod.Manifest.Id) is not IndexedMod listed)
                    continue;

                IndexedModVersion? latest = listed.Versions.FirstOrDefault(v => compatible(v, api));

                if (latest != null && ModVersion.TryParse(mod.Manifest.Version, out ModVersion? have) && ModVersion.TryParse(latest.Version, out ModVersion? newest) && newest.CompareTo(have) > 0)
                    updates.Add(new ModUpdate(mod, listed, latest));
            }

            return updates;
        }

        private static void add(IReadOnlyList<IndexedMod> index, IReadOnlyList<InstalledMod> installed, IndexedMod mod, IndexedModVersion version, string? api, List<PlannedInstall> plan, HashSet<string> seen)
        {
            seen.Add(mod.Id);

            foreach (ModDependency dependency in version.Dependencies.Where(d => !d.Optional))
            {
                if (seen.Contains(dependency.Id) || installed.Any(m => m.Manifest?.Id == dependency.Id && atLeast(m.Manifest.Version, dependency.MinVersion)))
                    continue;

                IndexedMod target = index.FirstOrDefault(m => m.Id == dependency.Id)
                                    ?? throw new InvalidOperationException($"{mod.Name} needs {dependency.Id}, which isn't installed and isn't in the mod index.");

                IndexedModVersion chosen = target.Versions.FirstOrDefault(v => compatible(v, api) && atLeast(v.Version, dependency.MinVersion))
                                           ?? throw new InvalidOperationException($"{mod.Name} needs {dependency.Id}{(dependency.MinVersion.Length > 0 ? $" {dependency.MinVersion} or newer" : string.Empty)}, and the index has no version of it this Omamori can run.");

                add(index, installed, target, chosen, api, plan, seen);
            }

            plan.Add(new PlannedInstall(mod, version));
        }

        private static bool compatible(IndexedModVersion version, string? api) => api == null || ModApi.Problem(version.ApiVersion, api) == null;

        private static bool atLeast(string version, string minimum)
        {
            if (minimum.Length == 0)
                return true;

            return ModVersion.TryParse(minimum, out ModVersion? min) && ModVersion.TryParse(version, out ModVersion? actual) && actual.CompareTo(min) >= 0;
        }
    }
}

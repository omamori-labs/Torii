using System.Collections.Generic;
using System.Linq;

namespace Torii.Core.Mods
{
    internal static class ModDependencies
    {
        // Messages are the ones from Omamori's DependencyResolver
        public static List<InstalledMod> Apply(IReadOnlyList<InstalledMod> mods)
        {
            Dictionary<string, InstalledMod> candidates = mods.Where(m => m.State == ModState.On && m.Manifest != null).ToDictionary(m => m.Manifest!.Id);
            HashSet<string> off = [.. mods.Where(m => m.State == ModState.Off && m.Manifest != null).Select(m => m.Manifest!.Id)];

            var accepted = new HashSet<string>();
            var problems = new Dictionary<string, string>();
            bool progress;

            do
            {
                progress = false;

                foreach ((string id, InstalledMod mod) in candidates)
                {
                    if (accepted.Contains(id) || problems.ContainsKey(id))
                        continue;

                    string? problem = null;
                    bool waiting = false;

                    foreach (ModDependency dependency in mod.Manifest!.Dependencies.Where(d => !d.Optional))
                    {
                        if (off.Contains(dependency.Id))
                            problem = $"needs {dependency.Id}, which is turned off";
                        else if (!candidates.TryGetValue(dependency.Id, out InstalledMod? target))
                            problem = $"needs {dependency.Id}, which isn't installed or can't load";
                        else if (problems.ContainsKey(dependency.Id))
                            problem = $"needs {dependency.Id}, which was skipped";
                        else if (!accepted.Contains(dependency.Id))
                        {
                            waiting = true;
                            continue;
                        }
                        else
                            problem = versionProblem(dependency, target.Manifest!);

                        if (problem != null)
                            break;
                    }

                    if (problem != null)
                        problems[id] = problem;
                    else if (!waiting)
                        accepted.Add(id);
                    else
                        continue;

                    progress = true;
                }
            }
            while (progress);

            return
            [
                .. mods.Select(m => m.Manifest != null && candidates.ContainsKey(m.Manifest.Id) && !accepted.Contains(m.Manifest.Id)
                    ? m with { Problem = problems.GetValueOrDefault(m.Manifest.Id, "dependency cycle") }
                    : m)
            ];
        }

        private static string? versionProblem(ModDependency dependency, ModInfo target)
        {
            if (dependency.MinVersion.Length == 0)
                return null;

            if (!ModVersion.TryParse(dependency.MinVersion, out ModVersion? min))
                return $"{dependency.Id} has an invalid minVersion";

            if (!ModVersion.TryParse(target.Version, out ModVersion? actual))
                return $"needs {dependency.Id} {dependency.MinVersion} or newer, installed version \"{target.Version}\" can't be read";

            return actual.CompareTo(min) >= 0 ? null : $"needs {dependency.Id} {dependency.MinVersion} or newer, {target.Version} is installed";
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Torii.Core.Mods;

namespace Torii.Tests
{
    [TestFixture]
    public class ModIndexPlannerTests
    {
        private static readonly Uri download = new Uri("https://example.com/mod.zip");

        [Test]
        public void ModWithoutDependenciesIsPlannedAlone()
        {
            IndexedMod mod = listed("a.mod", version("1.0.0"));

            Assert.That(ids(ModIndexPlanner.Plan([mod], [], mod, mod.Latest, "1.0")), Is.EqualTo("a.mod"));
        }

        [Test]
        public void DependenciesComeBeforeTheModThatNeedsThem()
        {
            IndexedMod library = listed("a.library", version("1.0.0"));
            IndexedMod mod = listed("a.mod", version("1.0.0", new ModDependency("a.library", string.Empty, false)));

            Assert.That(ids(ModIndexPlanner.Plan([mod, library], [], mod, mod.Latest, "1.0")), Is.EqualTo("a.library, a.mod"));
        }

        [Test]
        public void DependenciesOfDependenciesAreIncluded()
        {
            IndexedMod core = listed("a.core", version("1.0.0"));
            IndexedMod library = listed("a.library", version("1.0.0", new ModDependency("a.core", string.Empty, false)));
            IndexedMod mod = listed("a.mod", version("1.0.0", new ModDependency("a.library", string.Empty, false)));

            Assert.That(ids(ModIndexPlanner.Plan([mod, library, core], [], mod, mod.Latest, "1.0")), Is.EqualTo("a.core, a.library, a.mod"));
        }

        [Test]
        public void DependencyNeededTwiceIsPlannedOnce()
        {
            IndexedMod core = listed("a.core", version("1.0.0"));
            IndexedMod library = listed("a.library", version("1.0.0", new ModDependency("a.core", string.Empty, false)));
            IndexedMod mod = listed("a.mod", version("1.0.0", new ModDependency("a.library", string.Empty, false), new ModDependency("a.core", string.Empty, false)));

            Assert.That(ids(ModIndexPlanner.Plan([mod, library, core], [], mod, mod.Latest, "1.0")), Is.EqualTo("a.core, a.library, a.mod"));
        }

        [Test]
        public void InstalledDependencyIsNotPlannedAgain()
        {
            IndexedMod library = listed("a.library", version("1.0.0"));
            IndexedMod mod = listed("a.mod", version("1.0.0", new ModDependency("a.library", "1.0.0", false)));

            Assert.That(ids(ModIndexPlanner.Plan([mod, library], [installed("a.library", "1.2.0")], mod, mod.Latest, "1.0")), Is.EqualTo("a.mod"));
        }

        [Test]
        public void InstalledDependencyThatIsTooOldIsPlannedForAnUpdate()
        {
            IndexedMod library = listed("a.library", version("1.0.0"), version("2.0.0"));
            IndexedMod mod = listed("a.mod", version("1.0.0", new ModDependency("a.library", "2.0.0", false)));

            PlannedInstall[] plan = [.. ModIndexPlanner.Plan([mod, library], [installed("a.library", "1.0.0")], mod, mod.Latest, "1.0")];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(string.Join(", ", plan.Select(p => p.Mod.Id)), Is.EqualTo("a.library, a.mod"));
                Assert.That(plan[0].Version.Version, Is.EqualTo("2.0.0"));
            }
        }

        [Test]
        public void NewestVersionThatThisOmamoriCanRunIsChosenForADependency()
        {
            IndexedMod library = listed("a.library", version("1.0.0", api: "1.0"), version("2.0.0", api: "1.5"));
            IndexedMod mod = listed("a.mod", version("1.0.0", new ModDependency("a.library", string.Empty, false)));

            Assert.That(ModIndexPlanner.Plan([mod, library], [], mod, mod.Latest, "1.0")[0].Version.Version, Is.EqualTo("1.0.0"));
        }

        [Test]
        public void OptionalDependenciesAreLeftOut()
        {
            IndexedMod mod = listed("a.mod", version("1.0.0", new ModDependency("a.extra", string.Empty, true)));

            Assert.That(ids(ModIndexPlanner.Plan([mod], [], mod, mod.Latest, "1.0")), Is.EqualTo("a.mod"));
        }

        [Test]
        public void DependencyThatIsNowhereIsRefused()
        {
            IndexedMod mod = listed("a.mod", version("1.0.0", new ModDependency("a.missing", string.Empty, false)));

            Assert.That(() => ModIndexPlanner.Plan([mod], [], mod, mod.Latest, "1.0"), Throws.InvalidOperationException.With.Message.Contains("a.missing"));
        }

        [Test]
        public void DependencyWithNoVersionNewEnoughIsRefused()
        {
            IndexedMod library = listed("a.library", version("1.0.0"));
            IndexedMod mod = listed("a.mod", version("1.0.0", new ModDependency("a.library", "2.0.0", false)));

            Assert.That(() => ModIndexPlanner.Plan([mod, library], [], mod, mod.Latest, "1.0"), Throws.InvalidOperationException.With.Message.Contains("2.0.0"));
        }

        [Test]
        public void ModBuiltForANewerApiIsRefused()
        {
            IndexedMod mod = listed("a.mod", version("1.0.0", api: "1.5"));

            Assert.That(() => ModIndexPlanner.Plan([mod], [], mod, mod.Latest, "1.0"), Throws.InvalidOperationException);
        }

        [Test]
        public void DependencyCycleStillEnds()
        {
            IndexedMod a = listed("a.one", version("1.0.0", new ModDependency("a.two", string.Empty, false)));
            IndexedMod b = listed("a.two", version("1.0.0", new ModDependency("a.one", string.Empty, false)));

            Assert.That(ids(ModIndexPlanner.Plan([a, b], [], a, a.Latest, "1.0")), Is.EqualTo("a.two, a.one"));
        }

        [Test]
        public void NewerVersionInTheIndexIsAnUpdate()
        {
            IndexedMod mod = listed("a.mod", version("1.0.0"), version("1.1.0"));

            ModUpdate update = ModIndexPlanner.Updates([mod], [installed("a.mod", "1.0.0")], "1.0").Single();

            Assert.That(update.Version.Version, Is.EqualTo("1.1.0"));
        }

        [Test]
        public void ModThatIsUpToDateHasNoUpdate()
        {
            IndexedMod mod = listed("a.mod", version("1.1.0"));

            Assert.That(ModIndexPlanner.Updates([mod], [installed("a.mod", "1.1.0")], "1.0"), Is.Empty);
        }

        [Test]
        public void ModNewerThanTheIndexHasNoUpdate()
        {
            IndexedMod mod = listed("a.mod", version("1.0.0"));

            Assert.That(ModIndexPlanner.Updates([mod], [installed("a.mod", "2.0.0")], "1.0"), Is.Empty);
        }

        [Test]
        public void UpdateSkipsVersionsThisOmamoriCantRun()
        {
            IndexedMod mod = listed("a.mod", version("1.0.0"), version("2.0.0", api: "1.5"));

            Assert.That(ModIndexPlanner.Updates([mod], [installed("a.mod", "1.0.0")], "1.0"), Is.Empty);
        }

        [Test]
        public void ModsNotInTheIndexAreLeftAlone()
        {
            IndexedMod mod = listed("a.mod", version("1.1.0"));

            Assert.That(ModIndexPlanner.Updates([mod], [installed("other.mod", "1.0.0"), new InstalledMod("broken", null, ModState.Problem, null)], "1.0"), Is.Empty);
        }

        private static string ids(IReadOnlyList<PlannedInstall> plan) => string.Join(", ", plan.Select(p => p.Mod.Id));

        private static IndexedMod listed(string id, params IndexedModVersion[] versions) =>
            new IndexedMod(id, id, string.Empty, string.Empty, null, [.. versions.OrderByDescending(v => Version.Parse(v.Version))]);

        private static IndexedModVersion version(string number, params ModDependency[] dependencies) => version(number, "1.0", dependencies);

        private static IndexedModVersion version(string number, string api, params ModDependency[] dependencies) =>
            new IndexedModVersion(number, api, download, new string('a', 64), 0, dependencies);

        private static InstalledMod installed(string id, string number) =>
            new InstalledMod(id, new ModInfo(id, id, number, string.Empty, string.Empty, "Mod.dll", "1.0", [], []), ModState.On, null);
    }
}

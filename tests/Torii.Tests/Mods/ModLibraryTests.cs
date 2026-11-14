using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Torii.Core.Mods;
using Torii.Core.Omamori;

namespace Torii.Tests
{
    [TestFixture]
    public class ModLibraryTests
    {
        private static readonly string crash_time = ModLibrary.FormatCrashTime(new DateTimeOffset(2026, 11, 10, 20, 0, 0, TimeSpan.Zero));

        private string directory = null!;
        private OmamoriInstall install = null!;
        private ModLibrary library = null!;
        private bool osuRunning;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            install = new OmamoriInstall(directory);
            osuRunning = false;
            library = new ModLibrary(install, () => osuRunning);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, recursive: true);

        [Test]
        public void WithoutAModsFolderThereAreNoMods()
        {
            Assert.That(library.List(), Is.Empty);
        }

        [Test]
        public void FolderWithoutAManifestIsNotAMod()
        {
            Directory.CreateDirectory(Path.Combine(install.ModsDirectory, "notes"));

            Assert.That(library.List(), Is.Empty);
        }

        [Test]
        public void ModIsListedWithWhatItsManifestSays()
        {
            string folder = mod("coolmod", manifest("someone.coolmod", name: "Cool Mod", version: "1.2.0", author: "someone", description: "does cool things"));

            InstalledMod listed = library.List().Single();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(listed.Directory, Is.EqualTo(folder));
                Assert.That(listed.Label, Is.EqualTo("Cool Mod"));
                Assert.That(listed.Manifest!.Id, Is.EqualTo("someone.coolmod"));
                Assert.That(listed.Manifest.Version, Is.EqualTo("1.2.0"));
                Assert.That(listed.Manifest.Author, Is.EqualTo("someone"));
                Assert.That(listed.Manifest.Description, Is.EqualTo("does cool things"));
                Assert.That(listed.State, Is.EqualTo(ModState.On));
                Assert.That(listed.Problem, Is.Null);
            }
        }

        [Test]
        public void ModsAreListedByFolderIgnoringCase()
        {
            mod("b-second", manifest("mod.b"));
            mod("C-third", manifest("mod.c"));
            mod("A-first", manifest("mod.a"));

            string[] expected = ["mod.a", "mod.b", "mod.c"];

            Assert.That(library.List().Select(m => m.Manifest!.Id), Is.EqualTo(expected));
        }

        [Test]
        public void ModThatPatchesOsuIsMarked()
        {
            mod("patcher", manifest("mod.patcher", capabilities: "\"Cosmetic\", \"Patches\""));
            mod("plain", manifest("mod.plain"));

            IReadOnlyList<InstalledMod> mods = library.List();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(mods[0].Manifest!.Patches, Is.True);
                Assert.That(mods[1].Manifest!.Patches, Is.False);
            }
        }

        [Test]
        public void ModTurnedOffInSettingsIsOff()
        {
            mod("coolmod", manifest("someone.coolmod"));
            File.WriteAllText(install.SettingsFile, """{ "disabledMods": ["someone.coolmod"] }""");

            Assert.That(library.List().Single().State, Is.EqualTo(ModState.Off));
        }

        [TestCase("not json")]
        [TestCase("[]")]
        [TestCase("""{ "disabledMods": "someone.coolmod" }""")]
        public void UnreadableSettingsLeaveEveryModOn(string settings)
        {
            mod("coolmod", manifest("someone.coolmod"));
            File.WriteAllText(install.SettingsFile, settings);

            Assert.That(library.List().Single().State, Is.EqualTo(ModState.On));
        }

        [Test]
        public void ModThatCrashedTwoSessionsInARowIsSkipped()
        {
            mod("coolmod", manifest("someone.coolmod", version: "1.0.0"));
            faults("someone.coolmod", "1.0.0", strikes: 2);

            InstalledMod listed = library.List().Single();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(listed.State, Is.EqualTo(ModState.Crashed));
                Assert.That(listed.Problem, Is.EqualTo($"Crashed in 2 sessions in a row, {crash_time} (NullReferenceException: boom)"));
            }
        }

        [Test]
        public void ModThatCrashedOnceInTheLastLaunchStaysOnAndSaysSo()
        {
            mod("coolmod", manifest("someone.coolmod", version: "1.0.0"));
            faults("someone.coolmod", "1.0.0", strikes: 1);

            InstalledMod listed = library.List().Single();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(listed.State, Is.EqualTo(ModState.On));
                Assert.That(listed.Problem, Is.EqualTo($"Crashed the last time osu! ran, {crash_time} (NullReferenceException: boom)"));
            }
        }

        [Test]
        public void ModThatCrashedOnceBeforeACleanLaunchHasNoProblem()
        {
            mod("coolmod", manifest("someone.coolmod", version: "1.0.0"));
            faults("someone.coolmod", "1.0.0", strikes: 1, lastSession: 6);

            InstalledMod listed = library.List().Single();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(listed.State, Is.EqualTo(ModState.On));
                Assert.That(listed.Problem, Is.Null);
            }
        }

        [TestCase("\"\"")]
        [TestCase("\"not a time\"")]
        [TestCase("\"0001-01-01T00:00:00+00:00\"")]
        [TestCase("5")]
        public void CrashWithoutAUsableTimeLeavesTheTimeOut(string lastFault)
        {
            mod("coolmod", manifest("someone.coolmod", version: "1.0.0"));
            faults("someone.coolmod", "1.0.0", strikes: 1, lastFault: lastFault);

            Assert.That(library.List().Single().Problem, Is.EqualTo("Crashed the last time osu! ran (NullReferenceException: boom)"));
        }

        [Test]
        public void HistoryWithoutALaunchCounterReportsNoOneOffCrash()
        {
            mod("coolmod", manifest("someone.coolmod", version: "1.0.0"));
            Directory.CreateDirectory(install.Root);
            File.WriteAllText(install.FaultsFile, """{ "mods": { "someone.coolmod": { "version": "1.0.0", "strikes": 1, "lastSession": 0, "lastError": "boom" } } }""");

            Assert.That(library.List().Single().Problem, Is.Null);
        }

        [Test]
        public void CrashesOfAnotherVersionDoNotCount()
        {
            mod("coolmod", manifest("someone.coolmod", version: "1.1.0"));
            faults("someone.coolmod", "1.0.0", strikes: 5);

            Assert.That(library.List().Single().State, Is.EqualTo(ModState.On));
        }

        [Test]
        public void ModThatIsTurnedOffIsOffEvenIfItCrashed()
        {
            mod("coolmod", manifest("someone.coolmod", version: "1.0.0"));
            faults("someone.coolmod", "1.0.0", strikes: 2);
            File.WriteAllText(install.SettingsFile, """{ "disabledMods": ["someone.coolmod"] }""");

            Assert.That(library.List().Single().State, Is.EqualTo(ModState.Off));
        }

        [TestCase("not json", "mod.json isn't valid JSON")]
        [TestCase("[]", "mod.json isn't a mod manifest")]
        [TestCase("""{ "name": "Cool", "version": "1.0.0", "entry": "Mod.dll" }""", "mod.json has no id")]
        [TestCase("""{ "id": "cool", "version": "1.0.0", "entry": "Mod.dll" }""", "mod.json has no name")]
        [TestCase("""{ "id": "cool", "name": "Cool", "entry": "Mod.dll" }""", "mod.json has no version")]
        [TestCase("""{ "id": "cool", "name": "Cool", "version": "1.0.0" }""", "mod.json has no entry")]
        [TestCase("""{ "id": "Not An Id", "name": "Cool", "version": "1.0.0", "entry": "Mod.dll" }""", "mod.json has an invalid id")]
        [TestCase("""{ "id": "cool", "name": "Cool", "version": "1.0.0", "entry": "../Mod.dll" }""", "mod.json has an invalid entry")]
        public void ModWithABrokenManifestIsListedWithTheProblem(string json, string problem)
        {
            mod("broken", json);

            InstalledMod listed = library.List().Single();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(listed.State, Is.EqualTo(ModState.Problem));
                Assert.That(listed.Problem, Is.EqualTo(problem));
                Assert.That(listed.Manifest, Is.Null);
                Assert.That(listed.Label, Is.EqualTo("broken"));
            }
        }

        [Test]
        public void ModWithoutItsAssemblyHasAProblem()
        {
            string folder = mod("coolmod", manifest("someone.coolmod"));
            File.Delete(Path.Combine(folder, "Mod.dll"));

            InstalledMod listed = library.List().Single();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(listed.State, Is.EqualTo(ModState.Problem));
                Assert.That(listed.Problem, Is.EqualTo("Mod.dll is missing"));
                Assert.That(listed.Label, Is.EqualTo("Cool Mod"));
            }
        }

        [Test]
        public void SecondModWithTheSameIdHasAProblem()
        {
            mod("a-original", manifest("someone.coolmod"));
            mod("b-copy", manifest("someone.coolmod"));

            IReadOnlyList<InstalledMod> mods = library.List();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(mods[0].State, Is.EqualTo(ModState.On));
                Assert.That(mods[1].State, Is.EqualTo(ModState.Problem));
                Assert.That(mods[1].Problem, Is.EqualTo("Another mod with the same id is installed"));
            }
        }

        [Test]
        public void TurningAModOffAndOnAgain()
        {
            mod("coolmod", manifest("someone.coolmod"));

            library.SetEnabled("someone.coolmod", false);
            ModState off = library.List().Single().State;

            library.SetEnabled("someone.coolmod", true);
            ModState on = library.List().Single().State;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(off, Is.EqualTo(ModState.Off));
                Assert.That(on, Is.EqualTo(ModState.On));
            }
        }

        [Test]
        public void TurningAModOffWritesWhatOmamoriReads()
        {
            library.SetEnabled("someone.coolmod", false);

            using JsonDocument settings = JsonDocument.Parse(File.ReadAllText(install.SettingsFile));
            string[] expected = ["someone.coolmod"];

            Assert.That(settings.RootElement.GetProperty("disabledMods").EnumerateArray().Select(e => e.GetString()), Is.EqualTo(expected));
        }

        [Test]
        public void TurningAModOffTwiceListsItOnce()
        {
            library.SetEnabled("someone.coolmod", false);
            library.SetEnabled("someone.coolmod", false);

            Assert.That(File.ReadAllText(install.SettingsFile).Split("someone.coolmod"), Has.Length.EqualTo(2));
        }

        [Test]
        public void TurningAModOnRemovesEveryCopyOfItsId()
        {
            mod("coolmod", manifest("someone.coolmod"));
            File.WriteAllText(install.SettingsFile, """{ "disabledMods": ["someone.coolmod", "other.mod", "someone.coolmod"] }""");

            library.SetEnabled("someone.coolmod", true);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(library.List().Single().State, Is.EqualTo(ModState.On));
                Assert.That(File.ReadAllText(install.SettingsFile), Does.Contain("other.mod").And.Not.Contain("someone.coolmod"));
            }
        }

        [Test]
        public void TogglingKeepsSettingsToriiDoesNotKnow()
        {
            Directory.CreateDirectory(install.Root);
            File.WriteAllText(install.SettingsFile, """{ "somethingNew": { "kept": true }, "disabledMods": [] }""");

            library.SetEnabled("someone.coolmod", false);

            using JsonDocument settings = JsonDocument.Parse(File.ReadAllText(install.SettingsFile));

            Assert.That(settings.RootElement.GetProperty("somethingNew").GetProperty("kept").GetBoolean(), Is.True);
        }

        [Test]
        public void TogglingWithoutAChangeDoesNotWrite()
        {
            library.SetEnabled("someone.coolmod", true);

            Assert.That(File.Exists(install.SettingsFile), Is.False);
        }

        [Test]
        public void TogglingReplacesUnreadableSettings()
        {
            mod("coolmod", manifest("someone.coolmod"));
            File.WriteAllText(install.SettingsFile, "not json");

            library.SetEnabled("someone.coolmod", false);

            Assert.That(library.List().Single().State, Is.EqualTo(ModState.Off));
        }

        [Test]
        public void TogglingLeavesNoTemporaryFile()
        {
            library.SetEnabled("someone.coolmod", false);

            Assert.That(Directory.EnumerateFiles(install.Root).Select(Path.GetFileName).Single(), Is.EqualTo("settings.json"));
        }

        [Test]
        public void TogglingWhileOsuRunsIsRefused()
        {
            osuRunning = true;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(() => library.SetEnabled("someone.coolmod", false), Throws.InvalidOperationException);
                Assert.That(File.Exists(install.SettingsFile), Is.False);
            }
        }

        [Test]
        public void RetryingACrashedModTurnsItBackOn()
        {
            mod("coolmod", manifest("someone.coolmod", version: "1.0.0"));
            faults("someone.coolmod", "1.0.0", strikes: 2);

            library.Retry("someone.coolmod");

            Assert.That(library.List().Single().State, Is.EqualTo(ModState.On));
        }

        [Test]
        public void RetryingKeepsTheSessionCountAndOtherMods()
        {
            faults("someone.coolmod", "1.0.0", strikes: 2, alsoCrashed: "other.mod");

            library.Retry("someone.coolmod");

            using JsonDocument history = JsonDocument.Parse(File.ReadAllText(install.FaultsFile));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(history.RootElement.GetProperty("session").GetInt32(), Is.EqualTo(7));
                Assert.That(history.RootElement.GetProperty("mods").TryGetProperty("other.mod", out _), Is.True);
                Assert.That(history.RootElement.GetProperty("mods").TryGetProperty("someone.coolmod", out _), Is.False);
            }
        }

        [Test]
        public void RetryingAModWithoutCrashesDoesNotWrite()
        {
            library.Retry("someone.coolmod");

            Assert.That(File.Exists(install.FaultsFile), Is.False);
        }

        [Test]
        public void RetryingWhileOsuRunsIsRefused()
        {
            faults("someone.coolmod", "1.0.0", strikes: 2);
            string before = File.ReadAllText(install.FaultsFile);
            osuRunning = true;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(() => library.Retry("someone.coolmod"), Throws.InvalidOperationException);
                Assert.That(File.ReadAllText(install.FaultsFile), Is.EqualTo(before));
            }
        }

        [Test]
        public void ModNeedingAnInstalledModIsFine()
        {
            mod("base", manifest("mod.base", version: "1.2.0"));
            mod("addon", manifest("mod.addon", dependencies: """{ "id": "mod.base", "minVersion": "1.0" }"""));

            Assert.That(library.List().Select(m => m.Problem), Is.All.Null);
        }

        [Test]
        public void ModNeedingAMissingModHasAProblem()
        {
            mod("addon", manifest("mod.addon", dependencies: """{ "id": "mod.base" }"""));

            InstalledMod listed = library.List().Single();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(listed.Problem, Is.EqualTo("needs mod.base, which isn't installed or can't load"));
                Assert.That(listed.State, Is.EqualTo(ModState.On));
            }
        }

        [Test]
        public void ModNeedingAModThatIsTurnedOffHasAProblem()
        {
            mod("base", manifest("mod.base"));
            mod("addon", manifest("mod.addon", dependencies: """{ "id": "mod.base" }"""));
            File.WriteAllText(install.SettingsFile, """{ "disabledMods": ["mod.base"] }""");

            Assert.That(library.List().Single(m => m.Manifest!.Id == "mod.addon").Problem, Is.EqualTo("needs mod.base, which is turned off"));
        }

        [Test]
        public void ModNeedingANewerVersionHasAProblem()
        {
            mod("base", manifest("mod.base", version: "1.0.0"));
            mod("addon", manifest("mod.addon", dependencies: """{ "id": "mod.base", "minVersion": "1.2.0" }"""));

            Assert.That(library.List().Single(m => m.Manifest!.Id == "mod.addon").Problem, Is.EqualTo("needs mod.base 1.2.0 or newer, 1.0.0 is installed"));
        }

        [TestCase("1.0.0-beta", "1.0.0", false)]
        [TestCase("1.0.0", "1.0.0-beta", true)]
        [TestCase("v1.2", "1.2.0", true)]
        [TestCase("1.10.0", "1.9.0", true)]
        [TestCase("1.0.0-beta.2", "1.0.0-beta.11", false)]
        public void MinVersionFollowsSemverPrecedence(string installed, string minimum, bool satisfied)
        {
            mod("base", manifest("mod.base", version: installed));
            mod("addon", manifest("mod.addon", dependencies: $$"""{ "id": "mod.base", "minVersion": "{{minimum}}" }"""));

            Assert.That(library.List().Single(m => m.Manifest!.Id == "mod.addon").Problem == null, Is.EqualTo(satisfied));
        }

        [Test]
        public void OptionalDependencyThatIsMissingIsFine()
        {
            mod("addon", manifest("mod.addon", dependencies: """{ "id": "mod.base", "optional": true }"""));

            Assert.That(library.List().Single().Problem, Is.Null);
        }

        [Test]
        public void ModNeedingASkippedModIsSkippedToo()
        {
            mod("a-top", manifest("mod.top", dependencies: """{ "id": "mod.middle" }"""));
            mod("b-middle", manifest("mod.middle", dependencies: """{ "id": "mod.missing" }"""));

            Assert.That(library.List().Single(m => m.Manifest!.Id == "mod.top").Problem, Is.EqualTo("needs mod.middle, which was skipped"));
        }

        [Test]
        public void ModsNeedingEachOtherAreInACycle()
        {
            mod("a", manifest("mod.a", dependencies: """{ "id": "mod.b" }"""));
            mod("b", manifest("mod.b", dependencies: """{ "id": "mod.a" }"""));

            Assert.That(library.List().Select(m => m.Problem), Is.All.EqualTo("dependency cycle"));
        }

        private static string manifest(string id, string name = "Cool Mod", string version = "1.0.0", string author = "", string description = "", string capabilities = "\"Cosmetic\"", string dependencies = "") => $$"""
            {
              "id": "{{id}}",
              "name": "{{name}}",
              "version": "{{version}}",
              "author": "{{author}}",
              "description": "{{description}}",
              "entry": "Mod.dll",
              "apiVersion": "0.2",
              "capabilities": [{{capabilities}}],
              "dependencies": [{{dependencies}}]
            }
            """;

        private string mod(string folder, string manifestJson)
        {
            string path = Path.Combine(install.ModsDirectory, folder);
            Directory.CreateDirectory(path);

            File.WriteAllText(Path.Combine(path, "mod.json"), manifestJson);
            File.WriteAllText(Path.Combine(path, "Mod.dll"), string.Empty);

            return path;
        }

        // What Omamori's FaultHistory writes
        private void faults(string id, string version, int strikes, string? alsoCrashed = null, int lastSession = 7, string lastFault = "\"2026-11-10T20:00:00+00:00\"")
        {
            Directory.CreateDirectory(install.Root);

            string record = $$"""
                {
                      "version": "{{version}}",
                      "strikes": {{strikes}},
                      "lastSession": {{lastSession}},
                      "lastError": "NullReferenceException: boom",
                      "lastFault": {{lastFault}}
                    }
                """;

            string other = alsoCrashed == null ? string.Empty : $", \"{alsoCrashed}\": {record}";

            File.WriteAllText(install.FaultsFile, $$"""
                {
                  "session": 7,
                  "mods": {
                    "{{id}}": {{record}}{{other}}
                  }
                }
                """);
        }
    }
}

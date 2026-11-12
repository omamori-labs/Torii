using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Torii.Core;

namespace Torii.Tests
{
    [TestFixture]
    public class ModLibraryTests
    {
        private string directory = null!;
        private OmamoriInstall install = null!;
        private ModLibrary library = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            install = new OmamoriInstall(directory);
            library = new ModLibrary(install);
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
                Assert.That(listed.Problem, Is.EqualTo("Crashed in 2 sessions in a row (NullReferenceException: boom)"));
            }
        }

        [Test]
        public void ModThatCrashedOnceStaysOn()
        {
            mod("coolmod", manifest("someone.coolmod", version: "1.0.0"));
            faults("someone.coolmod", "1.0.0", strikes: 1);

            Assert.That(library.List().Single().State, Is.EqualTo(ModState.On));
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

        private static string manifest(string id, string name = "Cool Mod", string version = "1.0.0", string author = "", string description = "", string capabilities = "\"Cosmetic\"") => $$"""
            {
              "id": "{{id}}",
              "name": "{{name}}",
              "version": "{{version}}",
              "author": "{{author}}",
              "description": "{{description}}",
              "entry": "Mod.dll",
              "apiVersion": "0.2",
              "capabilities": [{{capabilities}}]
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

        // Same shape Omamori's FaultHistory writes
        private void faults(string id, string version, int strikes)
        {
            Directory.CreateDirectory(install.Root);

            File.WriteAllText(install.FaultsFile, $$"""
                {
                  "session": 7,
                  "mods": {
                    "{{id}}": {
                      "version": "{{version}}",
                      "strikes": {{strikes}},
                      "lastSession": 7,
                      "lastError": "NullReferenceException: boom",
                      "lastFault": "2026-11-10T20:00:00+00:00"
                    }
                  }
                }
                """);
        }
    }
}

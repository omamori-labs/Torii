using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Torii.Core;

namespace Torii.Tests
{
    [TestFixture]
    public class ModInstallTests
    {
        private string directory = null!;
        private OmamoriInstall install = null!;
        private ModLibrary library = null!;
        private bool osuRunning;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            install = new OmamoriInstall(Path.Combine(directory, "data"));
            Directory.CreateDirectory(install.DataDirectory);

            osuRunning = false;
            library = new ModLibrary(install, () => osuRunning);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, recursive: true);

        [Test]
        public void ZipPackedByOmamorisBuildInstalls()
        {
            InstalledMod installed = library.InstallFromZip(zip(("someone.coolmod/mod.json", manifest("someone.coolmod")), ("someone.coolmod/Mod.dll", "dll"), ("someone.coolmod/assets/icon.png", "png")));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(installed.State, Is.EqualTo(ModState.On));
                Assert.That(installed.Manifest!.Id, Is.EqualTo("someone.coolmod"));
                Assert.That(installed.Directory, Is.EqualTo(Path.Combine(install.ModsDirectory, "someone.coolmod")));
                Assert.That(File.Exists(Path.Combine(installed.Directory, "assets", "icon.png")), Is.True);
                Assert.That(library.List(), Has.Count.EqualTo(1));
            }
        }

        [Test]
        public void ZipWithTheManifestAtItsRootInstalls()
        {
            InstalledMod installed = library.InstallFromZip(zip(("mod.json", manifest("someone.coolmod")), ("Mod.dll", "dll")));

            Assert.That(installed.Directory, Is.EqualTo(Path.Combine(install.ModsDirectory, "someone.coolmod")));
        }

        [Test]
        public void FolderIsNamedAfterTheIdNotTheZip()
        {
            InstalledMod installed = library.InstallFromZip(zip(("CoolMod-main/mod.json", manifest("someone.coolmod")), ("CoolMod-main/Mod.dll", "dll")));

            Assert.That(Path.GetFileName(installed.Directory), Is.EqualTo("someone.coolmod"));
        }

        [Test]
        public void InstallingAgainReplacesTheOldVersion()
        {
            library.InstallFromZip(zip(("mod.json", manifest("someone.coolmod", "1.0.0")), ("Mod.dll", "dll"), ("Old.dll", "dll")));

            InstalledMod updated = library.InstallFromZip(zip(("mod.json", manifest("someone.coolmod", "2.0.0")), ("Mod.dll", "dll")));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(updated.Manifest!.Version, Is.EqualTo("2.0.0"));
                Assert.That(File.Exists(Path.Combine(updated.Directory, "Old.dll")), Is.False);
                Assert.That(library.List(), Has.Count.EqualTo(1));
            }
        }

        [Test]
        public void UpdatingReplacesACopyInADifferentlyNamedFolder()
        {
            string manual = Path.Combine(install.ModsDirectory, "extracted by hand");
            Directory.CreateDirectory(manual);
            File.WriteAllText(Path.Combine(manual, "mod.json"), manifest("someone.coolmod", "1.0.0"));
            File.WriteAllText(Path.Combine(manual, "Mod.dll"), "dll");

            library.InstallFromZip(zip(("mod.json", manifest("someone.coolmod", "2.0.0")), ("Mod.dll", "dll")));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Directory.Exists(manual), Is.False);
                Assert.That(library.List().Single().Manifest!.Version, Is.EqualTo("2.0.0"));
            }
        }

        [Test]
        public void UpdatingAModThatIsOffKeepsItOff()
        {
            library.InstallFromZip(zip(("mod.json", manifest("someone.coolmod", "1.0.0")), ("Mod.dll", "dll")));
            library.SetEnabled("someone.coolmod", false);

            InstalledMod updated = library.InstallFromZip(zip(("mod.json", manifest("someone.coolmod", "2.0.0")), ("Mod.dll", "dll")));

            Assert.That(updated.State, Is.EqualTo(ModState.Off));
        }

        [Test]
        public void OtherModsAreLeftAlone()
        {
            library.InstallFromZip(zip(("mod.json", manifest("other.mod")), ("Mod.dll", "dll")));
            library.InstallFromZip(zip(("mod.json", manifest("someone.coolmod")), ("Mod.dll", "dll")));

            Assert.That(library.List(), Has.Count.EqualTo(2));
        }

        [Test]
        public void ZipWithoutAManifestIsRejected()
        {
            string path = zip(("Mod.dll", "dll"), ("readme.txt", "hi"));

            assertRejected(path);
        }

        [Test]
        public void ZipWithSeveralTopFoldersIsRejected()
        {
            string path = zip(("one/mod.json", manifest("mod.one")), ("one/Mod.dll", "dll"), ("two/mod.json", manifest("mod.two")), ("two/Mod.dll", "dll"));

            assertRejected(path);
        }

        [Test]
        public void ZipWithoutTheModsAssemblyIsRejected()
        {
            string path = zip(("mod.json", manifest("someone.coolmod")));

            assertRejected(path);
        }

        [Test]
        public void ZipWithABrokenManifestIsRejected()
        {
            string path = zip(("mod.json", "not json"), ("Mod.dll", "dll"));

            assertRejected(path);
        }

        [TestCase("../outside.txt")]
        [TestCase("assets/../../outside.txt")]
        public void ZipReachingOutsideTheModIsRejected(string entry)
        {
            string path = zip(("mod.json", manifest("someone.coolmod")), ("Mod.dll", "dll"), (entry, "x"));

            using (Assert.EnterMultipleScope())
            {
                assertRejected(path);
                Assert.That(Directory.EnumerateFiles(directory, "outside.txt", SearchOption.AllDirectories), Is.Empty);
            }
        }

        [Test]
        public void ReleaseZipIsNotAMod()
        {
            string path = zip(("rulesets/osu.Game.Rulesets.Omamori.dll", "dll"), ("omamori/core/Omamori.Core.dll", "dll"));

            assertRejected(path);
        }

        [Test]
        public void RejectedUpdateKeepsTheInstalledVersion()
        {
            library.InstallFromZip(zip(("mod.json", manifest("someone.coolmod", "1.0.0")), ("Mod.dll", "dll")));

            assertRejected(zip(("mod.json", manifest("someone.coolmod", "2.0.0"))));

            Assert.That(library.List().Single().Manifest!.Version, Is.EqualTo("1.0.0"));
        }

        [Test]
        public void InstallingWhileOsuRunsIsRefused()
        {
            string path = zip(("mod.json", manifest("someone.coolmod")), ("Mod.dll", "dll"));
            osuRunning = true;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(() => library.InstallFromZip(path), Throws.InvalidOperationException);
                Assert.That(Directory.Exists(install.Root), Is.False);
            }
        }

        [Test]
        public void RemovingAModDeletesItsFolder()
        {
            InstalledMod installed = library.InstallFromZip(zip(("mod.json", manifest("someone.coolmod")), ("Mod.dll", "dll")));

            library.Remove(installed);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Directory.Exists(installed.Directory), Is.False);
                Assert.That(library.List(), Is.Empty);
            }
        }

        [Test]
        public void RemovingAModKeepsItsSettingsAndData()
        {
            InstalledMod installed = library.InstallFromZip(zip(("mod.json", manifest("someone.coolmod")), ("Mod.dll", "dll")));
            string config = Path.Combine(install.Root, "config", "someone.coolmod.json");
            string data = Path.Combine(install.Root, "data", "someone.coolmod", "save.dat");
            Directory.CreateDirectory(Path.GetDirectoryName(config)!);
            Directory.CreateDirectory(Path.GetDirectoryName(data)!);
            File.WriteAllText(config, "{}");
            File.WriteAllText(data, "x");

            library.Remove(installed);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(File.Exists(config), Is.True);
                Assert.That(File.Exists(data), Is.True);
            }
        }

        [Test]
        public void ModRemovedWhileOffStartsOnWhenInstalledAgain()
        {
            string path = zip(("mod.json", manifest("someone.coolmod")), ("Mod.dll", "dll"));
            InstalledMod installed = library.InstallFromZip(path);
            library.SetEnabled("someone.coolmod", false);

            library.Remove(installed);

            Assert.That(library.InstallFromZip(path).State, Is.EqualTo(ModState.On));
        }

        [Test]
        public void RemovingABrokenModWorks()
        {
            string broken = Path.Combine(install.ModsDirectory, "broken");
            Directory.CreateDirectory(broken);
            File.WriteAllText(Path.Combine(broken, "mod.json"), "not json");

            library.Remove(library.List().Single());

            Assert.That(Directory.Exists(broken), Is.False);
        }

        [Test]
        public void RemovingAFolderOutsideTheModsFolderIsRefused()
        {
            var elsewhere = new InstalledMod(install.DataDirectory, null, ModState.Problem, null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(() => library.Remove(elsewhere), Throws.ArgumentException);
                Assert.That(Directory.Exists(install.DataDirectory), Is.True);
            }
        }

        [Test]
        public void RemovingWhileOsuRunsIsRefused()
        {
            InstalledMod installed = library.InstallFromZip(zip(("mod.json", manifest("someone.coolmod")), ("Mod.dll", "dll")));
            osuRunning = true;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(() => library.Remove(installed), Throws.InvalidOperationException);
                Assert.That(Directory.Exists(installed.Directory), Is.True);
            }
        }

        private void assertRejected(string zipPath)
        {
            Assert.That(() => library.InstallFromZip(zipPath), Throws.InstanceOf<InvalidDataException>());

            if (Directory.Exists(install.Root))
                Assert.That(Directory.EnumerateDirectories(install.Root).Select(Path.GetFileName), Has.None.Not.EqualTo("mods"));
        }

        private static string manifest(string id, string version = "1.0.0") => $$"""
            {
              "id": "{{id}}",
              "name": "Cool Mod",
              "version": "{{version}}",
              "entry": "Mod.dll",
              "apiVersion": "0.2",
              "capabilities": ["Cosmetic"]
            }
            """;

        private string zip(params (string Name, string Content)[] entries) =>
            Fixtures.ZipFile(directory, "zip", [.. entries.Select(e => (e.Name, Encoding.UTF8.GetBytes(e.Content)))]);
    }
}

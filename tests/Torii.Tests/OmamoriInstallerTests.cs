using System;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;
using Torii.Core;

namespace Torii.Tests
{
    [TestFixture]
    public class OmamoriInstallerTests
    {
        private const string ruleset_entry = "rulesets/osu.Game.Rulesets.Omamori.dll";
        private const string core_entry = "omamori/core/Omamori.Core.dll";

        private string directory = null!;
        private OmamoriInstall install = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            install = new OmamoriInstall(Path.Combine(directory, "data"));
            Directory.CreateDirectory(install.DataDirectory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, recursive: true);

        [Test]
        public void ReleaseZipInstallsTheLoader()
        {
            OmamoriInstaller.InstallFromZip(install, zip(ruleset_entry, core_entry, "omamori/core/0Harmony.dll"));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(install.IsInstalled, Is.True);
                Assert.That(File.ReadAllText(install.CoreAssembly), Is.EqualTo(core_entry));
                Assert.That(File.Exists(Path.Combine(install.CoreDirectory, "0Harmony.dll")), Is.True);
            }
        }

        [Test]
        public void ZipWithWindowsSeparatorsInstalls()
        {
            OmamoriInstaller.InstallFromZip(install, zip(ruleset_entry.Replace('/', '\\'), core_entry.Replace('/', '\\')));

            Assert.That(install.IsInstalled, Is.True);
        }

        [Test]
        public void FolderEntriesAreSkipped()
        {
            OmamoriInstaller.InstallFromZip(install, zip("rulesets/", "omamori/", "omamori/core/", ruleset_entry, core_entry));

            Assert.That(install.IsInstalled, Is.True);
        }

        [Test]
        public void UpdatingDropsFilesTheNewReleaseNoLongerShips()
        {
            OmamoriInstaller.InstallFromZip(install, zip(ruleset_entry, core_entry, "omamori/core/Old.dll"));
            OmamoriInstaller.InstallFromZip(install, zip(ruleset_entry, core_entry));

            Assert.That(File.Exists(Path.Combine(install.CoreDirectory, "Old.dll")), Is.False);
        }

        [Test]
        public void UpdatingKeepsModsAndState()
        {
            string mod = placePlayerFiles();

            OmamoriInstaller.InstallFromZip(install, zip(ruleset_entry, core_entry));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(File.Exists(mod), Is.True);
                Assert.That(File.Exists(install.SettingsFile), Is.True);
                Assert.That(File.Exists(install.FaultsFile), Is.True);
            }
        }

        [TestCase(ruleset_entry)]
        [TestCase(core_entry)]
        [TestCase("readme.txt")]
        public void ZipWithoutTheWholeLoaderIsRejected(string onlyEntry)
        {
            string path = zip(onlyEntry);

            Assert.That(() => OmamoriInstaller.InstallFromZip(install, path), Throws.InstanceOf<InvalidDataException>());
        }

        [TestCase("../outside.txt")]
        [TestCase("rulesets/../../outside.txt")]
        [TestCase("files/somewhere.txt")]
        [TestCase("client.realm")]
        [TestCase("omamori/mods/someone.mod/Mod.dll")]
        [TestCase("omamori/settings.json")]
        [TestCase("omamori/faults.json")]
        [TestCase("omamori/config/someone.mod.json")]
        [TestCase("omamori/data/someone.mod/save.dat")]
        [TestCase("omamori/readme.txt")]
        public void ZipReachingOutsideTheLoaderIsRejected(string entry)
        {
            string path = zip(ruleset_entry, core_entry, entry);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(() => OmamoriInstaller.InstallFromZip(install, path), Throws.InstanceOf<InvalidDataException>());
                Assert.That(File.Exists(Path.Combine(directory, "outside.txt")), Is.False);
            }
        }

        [Test]
        public void RejectedZipLeavesTheCurrentInstallAlone()
        {
            OmamoriInstaller.InstallFromZip(install, zip(ruleset_entry, core_entry, "omamori/core/Kept.dll"));
            string bad = zip(ruleset_entry, core_entry, "../outside.txt");

            Assert.That(() => OmamoriInstaller.InstallFromZip(install, bad), Throws.InstanceOf<InvalidDataException>());
            Assert.That(File.Exists(Path.Combine(install.CoreDirectory, "Kept.dll")), Is.True);
        }

        [Test]
        public void FileThatIsNotAZipIsRejected()
        {
            string path = Path.Combine(directory, "not-a.zip");
            File.WriteAllText(path, "hello");

            Assert.That(() => OmamoriInstaller.InstallFromZip(install, path), Throws.InstanceOf<InvalidDataException>());
        }

        [Test]
        public void UninstallingRemovesTheLoaderAndKeepsModsAndState()
        {
            OmamoriInstaller.InstallFromZip(install, zip(ruleset_entry, core_entry));
            string mod = placePlayerFiles();

            OmamoriInstaller.Uninstall(install);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(install.IsInstalled, Is.False);
                Assert.That(File.Exists(install.RulesetAssembly), Is.False);
                Assert.That(Directory.Exists(install.CoreDirectory), Is.False);
                Assert.That(File.Exists(mod), Is.True);
                Assert.That(File.Exists(install.SettingsFile), Is.True);
            }
        }

        [Test]
        public void UninstallingWithModsRemovesEverythingOmamoriKeeps()
        {
            OmamoriInstaller.InstallFromZip(install, zip(ruleset_entry, core_entry));
            placePlayerFiles();

            OmamoriInstaller.Uninstall(install, removeMods: true);

            Assert.That(Directory.Exists(install.Root), Is.False);
        }

        [Test]
        public void UninstallingLeavesNoEmptyFolderBehind()
        {
            OmamoriInstaller.InstallFromZip(install, zip(ruleset_entry, core_entry));

            OmamoriInstaller.Uninstall(install);

            Assert.That(Directory.Exists(install.Root), Is.False);
        }

        [Test]
        public void UninstallingRemovesTheBootstrapsSymbols()
        {
            OmamoriInstaller.InstallFromZip(install, zip(ruleset_entry, core_entry, "rulesets/osu.Game.Rulesets.Omamori.pdb"));

            OmamoriInstaller.Uninstall(install);

            Assert.That(Directory.EnumerateFiles(install.RulesetsDirectory), Is.Empty);
        }

        [Test]
        public void UninstallingLeavesOtherRulesetsAlone()
        {
            OmamoriInstaller.InstallFromZip(install, zip(ruleset_entry, core_entry));
            string other = Path.Combine(install.RulesetsDirectory, "osu.Game.Rulesets.OmamoriExtras.dll");
            File.WriteAllText(other, string.Empty);

            OmamoriInstaller.Uninstall(install);

            Assert.That(File.Exists(other), Is.True);
        }

        [Test]
        public void UninstallingWhenNothingIsInstalledDoesNothing()
        {
            Assert.That(() => OmamoriInstaller.Uninstall(install), Throws.Nothing);
        }

        private string placePlayerFiles()
        {
            string mod = Path.Combine(install.ModsDirectory, "someone.mod", "mod.json");
            Directory.CreateDirectory(Path.GetDirectoryName(mod)!);

            File.WriteAllText(mod, "{}");
            File.WriteAllText(install.SettingsFile, "{}");
            File.WriteAllText(install.FaultsFile, "{}");

            return mod;
        }

        private string zip(params string[] entries)
        {
            string path = Path.Combine(directory, $"{Guid.NewGuid():N}.zip");

            using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);

            foreach (string name in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(name);

                if (name.EndsWith('/'))
                    continue;

                using var writer = new StreamWriter(entry.Open());
                writer.Write(name);
            }

            return path;
        }
    }
}

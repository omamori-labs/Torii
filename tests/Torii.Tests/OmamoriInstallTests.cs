using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Torii.Core;

namespace Torii.Tests
{
    [TestFixture]
    public class OmamoriInstallTests
    {
        private string directory = null!;
        private OmamoriInstall install = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            install = new OmamoriInstall(directory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, recursive: true);

        [Test]
        public void FilesGoWhereOmamoriLooksForThem()
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(install.RulesetAssembly, Is.EqualTo(Path.Combine(directory, "rulesets", "osu.Game.Rulesets.Omamori.dll")));
                Assert.That(install.CoreAssembly, Is.EqualTo(Path.Combine(directory, "omamori", "core", "Omamori.Core.dll")));
                Assert.That(install.ModsDirectory, Is.EqualTo(Path.Combine(directory, "omamori", "mods")));
                Assert.That(install.SettingsFile, Is.EqualTo(Path.Combine(directory, "omamori", "settings.json")));
                Assert.That(install.FaultsFile, Is.EqualTo(Path.Combine(directory, "omamori", "faults.json")));
            }
        }

        [Test]
        public void RelativeDataFolderIsMadeAbsolute()
        {
            Assert.That(Path.IsPathFullyQualified(new OmamoriInstall("osu").DataDirectory), Is.True);
        }

        [Test]
        public void EmptyDataFolderHasNoInstall()
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(install.IsInstalled, Is.False);
                Assert.That(install.ReadVersion(), Is.Null);
            }
        }

        [Test]
        public void BootstrapWithoutCoreIsNotAnInstall()
        {
            place(install.RulesetAssembly);

            Assert.That(install.IsInstalled, Is.False);
        }

        [Test]
        public void CoreWithoutBootstrapIsNotAnInstall()
        {
            place(install.CoreAssembly);

            Assert.That(install.IsInstalled, Is.False);
        }

        [Test]
        public void BootstrapAndCoreTogetherAreAnInstall()
        {
            place(install.RulesetAssembly);
            place(install.CoreAssembly);

            Assert.That(install.IsInstalled, Is.True);
        }

        [Test]
        public void VersionComesFromTheCoreWithoutTheCommit()
        {
            // any real assembly does, this one is stamped the same way omamori's core is
            Assembly stamped = typeof(OmamoriInstall).Assembly;
            string informational = stamped.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

            place(install.RulesetAssembly);
            Directory.CreateDirectory(install.CoreDirectory);
            File.Copy(stamped.Location, install.CoreAssembly);

            string? version = install.ReadVersion();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(version, Is.EqualTo(informational.Split('+')[0]));
                Assert.That(version, Does.Not.Contain("+"));
            }
        }

        [Test]
        public void CoreThatIsNotAnAssemblyHasNoVersion()
        {
            place(install.RulesetAssembly);
            place(install.CoreAssembly);

            Assert.That(install.ReadVersion(), Is.Null);
        }

        private static void place(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "not an assembly");
        }
    }
}

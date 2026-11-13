using System;
using System.IO;
using NUnit.Framework;
using Torii.Core.Library;

namespace Torii.Tests
{
    [TestFixture]
    public class RealOsuTests
    {
        private string directory = null!;
        private string applicationData = null!;
        private string localApplicationData = null!;
        private string home = null!;
        private string applications = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            applicationData = Path.Combine(directory, "roaming");
            localApplicationData = Path.Combine(directory, "local");
            home = Path.Combine(directory, "home");
            applications = Path.Combine(directory, "applications");

            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, recursive: true);

        [Test]
        public void DataIsFoundInTheRoamingFolder() => assertFoundAt(Path.Combine(applicationData, "osu"));

        [Test]
        public void DataIsFoundInTheLocalFolder() => assertFoundAt(Path.Combine(localApplicationData, "osu"));

        [Test]
        public void DataIsFoundWhereMacOsKeepsIt() => assertFoundAt(Path.Combine(home, "Library", "Application Support", "osu"));

        [Test]
        public void FolderWithoutADatabaseIsNotTheLibrary()
        {
            Directory.CreateDirectory(Path.Combine(applicationData, "osu", "files"));

            Assert.That(find(), Is.Null);
        }

        [Test]
        public void NothingInstalledMeansNothingFound()
        {
            Assert.That(find(), Is.Null);
        }

        [Test]
        public void MovedDataIsFollowed()
        {
            string moved = Path.Combine(directory, "games", "osu-data");
            Directory.CreateDirectory(moved);
            File.WriteAllText(Path.Combine(moved, "client.realm"), string.Empty);

            string folder = Path.Combine(applicationData, "osu");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "storage.ini"), $"FullPath = {moved}{Environment.NewLine}");

            Assert.That(find(), Is.EqualTo(moved));
        }

        [Test]
        public void MovedDataThatIsGoneIsNotFoundAtTheOldPlace()
        {
            string folder = Path.Combine(applicationData, "osu");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "client.realm"), string.Empty);
            File.WriteAllText(Path.Combine(folder, "storage.ini"), $"FullPath = {Path.Combine(directory, "gone")}{Environment.NewLine}");

            Assert.That(find(), Is.Null);
        }

        [Test]
        public void VersionIsReadFromTheInstalledGame()
        {
            writeVersion(Path.Combine(localApplicationData, "osulazer", "current"), "2026.1005.0-lazer");

            Assert.That(RealOsu.ReadVersion(localApplicationData, applications), Is.EqualTo("2026.1005.0-lazer"));
        }

        [Test]
        public void VersionIsReadFromTheMacOsApp()
        {
            writeVersion(Path.Combine(applications, "osu!.app", "Contents", "Resources"), "2026.1105.0-lazer");

            Assert.That(RealOsu.ReadVersion(localApplicationData, applications), Is.EqualTo("2026.1105.0-lazer"));
        }

        [Test]
        public void VersionIsUnknownWithoutAnInstall()
        {
            Assert.That(RealOsu.ReadVersion(localApplicationData, applications), Is.Null);
        }

        private string? find() => RealOsu.FindDataDirectory(applicationData, localApplicationData, home);

        private void assertFoundAt(string folder)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "client.realm"), string.Empty);

            Assert.That(find(), Is.EqualTo(folder));
        }

        private static void writeVersion(string folder, string version)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "sq.version"), $"<package><metadata><version>{version}</version></metadata></package>");
        }
    }
}

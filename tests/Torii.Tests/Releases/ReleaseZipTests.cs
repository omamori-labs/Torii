using System;
using System.IO;
using NUnit.Framework;
using Torii.Core.Releases;

namespace Torii.Tests
{
    [TestFixture]
    public class ReleaseZipTests
    {
        private string directory = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, recursive: true);

        [Test]
        public void LazerVersionIsReadFromTheCore()
        {
            string path = zip(("omamori/core/Omamori.Core.dll", Fixtures.Assembly("LazerBuild", "2026.1005.0-lazer")));

            Assert.That(ReleaseZip.LazerVersion(path), Is.EqualTo("2026.1005.0-lazer"));
        }

        [Test]
        public void ZipWithWindowsSeparatorsIsRead()
        {
            string path = zip(("omamori\\core\\Omamori.Core.dll", Fixtures.Assembly("LazerBuild", "2026.1005.0-lazer")));

            Assert.That(ReleaseZip.LazerVersion(path), Is.EqualTo("2026.1005.0-lazer"));
        }

        [Test]
        public void OtherMetadataIsNotTheLazerVersion()
        {
            string path = zip(("omamori/core/Omamori.Core.dll", Fixtures.Assembly("RepositoryUrl", "https://example.com")));

            Assert.That(ReleaseZip.LazerVersion(path), Is.Null);
        }

        [Test]
        public void CoreBuiltWithoutKnowingItsLazerHasNoVersion()
        {
            string path = zip(("omamori/core/Omamori.Core.dll", Fixtures.Assembly("LazerBuild", "")));

            Assert.That(ReleaseZip.LazerVersion(path), Is.Null);
        }

        [Test]
        public void ZipWithoutACoreIsRejected()
        {
            string path = zip(("rulesets/osu.Game.Rulesets.Omamori.dll", Fixtures.Assembly("LazerBuild", "2026.1005.0-lazer")));

            Assert.That(() => ReleaseZip.LazerVersion(path), Throws.InstanceOf<InvalidDataException>());
        }

        [Test]
        public void CoreThatIsNotAnAssemblyIsRejected()
        {
            string path = zip(("omamori/core/Omamori.Core.dll", "not an assembly"u8.ToArray()));

            Assert.That(() => ReleaseZip.LazerVersion(path), Throws.InstanceOf<InvalidDataException>());
        }

        [Test]
        public void FileThatIsNotAZipIsRejected()
        {
            string path = Path.Combine(directory, "not-a.zip");
            File.WriteAllText(path, "hello");

            Assert.That(() => ReleaseZip.LazerVersion(path), Throws.InstanceOf<InvalidDataException>());
        }


        private string zip(params (string Name, byte[] Content)[] entries) => Fixtures.ZipFile(directory, "zip", entries);
    }
}

using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Emit;
using NUnit.Framework;
using Torii.Core;

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
            string path = zip(("omamori/core/Omamori.Core.dll", core("LazerBuild", "2026.1005.0-lazer")));

            Assert.That(ReleaseZip.LazerVersion(path), Is.EqualTo("2026.1005.0-lazer"));
        }

        [Test]
        public void ZipWithWindowsSeparatorsIsRead()
        {
            string path = zip(("omamori\\core\\Omamori.Core.dll", core("LazerBuild", "2026.1005.0-lazer")));

            Assert.That(ReleaseZip.LazerVersion(path), Is.EqualTo("2026.1005.0-lazer"));
        }

        [Test]
        public void OtherMetadataIsNotTheLazerVersion()
        {
            string path = zip(("omamori/core/Omamori.Core.dll", core("RepositoryUrl", "https://example.com")));

            Assert.That(ReleaseZip.LazerVersion(path), Is.Null);
        }

        [Test]
        public void CoreBuiltWithoutKnowingItsLazerHasNoVersion()
        {
            string path = zip(("omamori/core/Omamori.Core.dll", core("LazerBuild", "")));

            Assert.That(ReleaseZip.LazerVersion(path), Is.Null);
        }

        [Test]
        public void ZipWithoutACoreIsRejected()
        {
            string path = zip(("rulesets/osu.Game.Rulesets.Omamori.dll", core("LazerBuild", "2026.1005.0-lazer")));

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

        // A real assembly carrying one AssemblyMetadata attribute, the way Omamori's build stamps its core
        private static byte[] core(string key, string value)
        {
            var builder = new PersistedAssemblyBuilder(new AssemblyName("Omamori.Core"), typeof(object).Assembly);
            ConstructorInfo constructor = typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!;

            builder.SetCustomAttribute(new CustomAttributeBuilder(constructor, [key, value]));
            builder.DefineDynamicModule("Omamori.Core");

            using var stream = new MemoryStream();
            builder.Save(stream);

            return stream.ToArray();
        }

        private string zip(params (string Name, byte[] Content)[] entries)
        {
            string path = Path.Combine(directory, $"{Guid.NewGuid():N}.zip");

            using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);

            foreach ((string name, byte[] content) in entries)
            {
                using Stream stream = archive.CreateEntry(name).Open();
                stream.Write(content);
            }

            return path;
        }
    }
}

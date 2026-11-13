using System;
using System.IO;
using NUnit.Framework;
using Torii.Core.Lazer;
using Torii.Core.Library;

namespace Torii.Tests
{
    [TestFixture]
    public class SharedLibraryVersionTests
    {
        private string directory = null!;
        private SharedLibrary library = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            library = new SharedLibrary(Path.Combine(directory, "library"), (_, _) => true, () => false);
            Directory.CreateDirectory(library.Root);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, recursive: true);

        [Test]
        public void NewLibraryHasNoVersion()
        {
            Assert.That(library.ReadVersion(), Is.Null);
        }

        [Test]
        public void LibraryRemembersTheFirstVersionThatUsesIt()
        {
            library.Use(instance("a", "2026.1005.0-lazer"));

            Assert.That(library.ReadVersion(), Is.EqualTo("2026.1005.0-lazer"));
        }

        [Test]
        public void NewerVersionTakesOver()
        {
            library.Use(instance("a", "2026.1005.0-lazer"));
            library.Use(instance("b", "2026.1105.0-lazer"));

            Assert.That(library.ReadVersion(), Is.EqualTo("2026.1105.0-lazer"));
        }

        [Test]
        public void OlderVersionIsRefusedAndTheLibraryKeepsItsVersion()
        {
            library.Use(instance("a", "2026.1105.0-lazer"));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(() => library.Use(instance("b", "2026.1005.0-lazer")), Throws.InvalidOperationException);
                Assert.That(library.ReadVersion(), Is.EqualTo("2026.1105.0-lazer"));
            }
        }

        [Test]
        public void SameVersionIsFine()
        {
            library.Use(instance("a", "2026.1005.0-lazer"));

            Assert.That(() => library.Use(instance("b", "2026.1005.0-lazer")), Throws.Nothing);
        }

        [Test]
        public void InstanceWithoutAVersionIsRefused()
        {
            var unknown = new LazerInstance(Path.Combine(directory, "unknown"));
            Directory.CreateDirectory(unknown.Directory);

            Assert.That(() => library.Use(unknown), Throws.InvalidOperationException);
        }

        private LazerInstance instance(string name, string version)
        {
            var created = new LazerInstance(Path.Combine(directory, name));
            Directory.CreateDirectory(created.Directory);
            File.WriteAllText(Path.Combine(created.Directory, "sq.version"), $"<package><metadata><version>{version}</version></metadata></package>");

            return created;
        }
    }
}

using System;
using System.IO;
using NUnit.Framework;
using Torii.Core.Lazer;
using Torii.Core.Library;

namespace Torii.Tests
{
    [TestFixture]
    public class ElevatedLinkTests
    {
        private string directory = null!;
        private LazerInstance instance = null!;
        private string libraryDirectory = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            instance = new LazerInstance(Path.Combine(directory, "instance"));
            libraryDirectory = Path.Combine(directory, "library");
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, recursive: true);

        [TestCase]
        [TestCase("--something")]
        [TestCase("--other", "a", "b")]
        public void OtherArgumentsAreNotForTheHelper(params string[] args)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(ElevatedLink.TryRun(args, out int exitCode), Is.False);
                Assert.That(exitCode, Is.Zero);
            }
        }

        [Test]
        public void HelperRefusesTheWrongNumberOfArguments()
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(ElevatedLink.TryRun([ElevatedLink.ARGUMENT, instance.Directory], out int exitCode), Is.True);
                Assert.That(exitCode, Is.EqualTo(1));
            }
        }

        [Test]
        public void HelperRefusesRelativePaths()
        {
            Directory.CreateDirectory(instance.Directory);
            File.WriteAllText(instance.PortableMarker, string.Empty);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ElevatedLink.TryRun([ElevatedLink.ARGUMENT, "instance", "library"], out int exitCode), Is.True);
                Assert.That(exitCode, Is.EqualTo(1));
            }
        }

        [Test]
        public void HelperRefusesAFolderThatIsNotAnInstance()
        {
            Directory.CreateDirectory(instance.Directory);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ElevatedLink.TryRun([ElevatedLink.ARGUMENT, instance.Directory, libraryDirectory], out int exitCode), Is.True);
                Assert.That(exitCode, Is.EqualTo(1));
                Assert.That(Directory.Exists(libraryDirectory), Is.False);
            }
        }

        [Test]
        public void HelperRefusesALibraryFolderHoldingOtherThings()
        {
            Directory.CreateDirectory(instance.Directory);
            File.WriteAllText(instance.PortableMarker, string.Empty);
            Directory.CreateDirectory(libraryDirectory);
            File.WriteAllText(Path.Combine(libraryDirectory, "something.txt"), string.Empty);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ElevatedLink.TryRun([ElevatedLink.ARGUMENT, instance.Directory, libraryDirectory], out int exitCode), Is.True);
                Assert.That(exitCode, Is.EqualTo(1));
                Assert.That(Directory.Exists(Path.Combine(libraryDirectory, "files")), Is.False);
            }
        }

        [Test]
        public void ElevationIsRequestedWhenLinksCantBeMade()
        {
            LazerInstance? asked = null;
            string? askedLibrary = null;

            var library = new SharedLibrary(libraryDirectory, (i, l) =>
            {
                asked = i;
                askedLibrary = l;
                return true;
            }, () => false);

            library.Attach(instance);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(asked, Is.SameAs(instance));
                Assert.That(askedLibrary, Is.EqualTo(library.Root));
            }
        }

        [Test]
        public void RefusedElevationLeavesTheInstanceAlone()
        {
            Directory.CreateDirectory(instance.FilesDirectory);
            File.WriteAllText(Path.Combine(instance.FilesDirectory, "hash"), "map");
            var library = new SharedLibrary(libraryDirectory, (_, _) => false, () => false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(() => library.Attach(instance), Throws.InvalidOperationException);
                Assert.That(File.ReadAllText(Path.Combine(instance.FilesDirectory, "hash")), Is.EqualTo("map"));
                Assert.That(Directory.Exists(library.FilesDirectory), Is.False);
            }
        }

        [Test]
        public void WithoutElevationMissingPermissionIsReported()
        {
            var library = new SharedLibrary(libraryDirectory, null, () => false);

            Assert.That(() => library.Attach(instance), Throws.InvalidOperationException);
        }
    }
}

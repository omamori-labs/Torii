using System;
using System.IO;
using NUnit.Framework;
using Torii.Core.Lazer;
using Torii.Core.Library;

namespace Torii.Tests
{
    [TestFixture]
    public class SharedLibraryTests
    {
        private string directory = null!;
        private SharedLibrary library = null!;
        private LazerInstance instance = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            try
            {
                File.CreateSymbolicLink(Path.Combine(directory, "probe"), directory);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Assert.Ignore("Symbolic links can't be made here.");
            }

            library = new SharedLibrary(Path.Combine(directory, "library"));
            instance = new LazerInstance(Path.Combine(directory, "instance"));
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, recursive: true);

        [Test]
        public void NewInstanceGetsItsFilesFolderFromTheLibrary()
        {
            library.Attach(instance);

            File.WriteAllText(Path.Combine(instance.FilesDirectory, "hash"), "map");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(new DirectoryInfo(instance.FilesDirectory).LinkTarget, Is.EqualTo(library.FilesDirectory));
                Assert.That(File.ReadAllText(Path.Combine(library.FilesDirectory, "hash")), Is.EqualTo("map"));
            }
        }

        [Test]
        public void NoDatabaseIsLinkedBeforeOneExists()
        {
            library.Attach(instance);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(File.Exists(instance.DatabaseFile), Is.False);
                Assert.That(new FileInfo(instance.DatabaseFile).LinkTarget, Is.Null);
                Assert.That(File.Exists(library.DatabaseFile), Is.False);
            }
        }

        [Test]
        public void LibraryDataIsLinkedIntoANewInstance()
        {
            Directory.CreateDirectory(library.FilesDirectory);
            File.WriteAllText(Path.Combine(library.FilesDirectory, "hash"), "map");
            File.WriteAllText(library.DatabaseFile, "db");

            library.Attach(instance);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(File.ReadAllText(Path.Combine(instance.FilesDirectory, "hash")), Is.EqualTo("map"));
                Assert.That(File.ReadAllText(instance.DatabaseFile), Is.EqualTo("db"));
                Assert.That(new FileInfo(instance.DatabaseFile).LinkTarget, Is.EqualTo(library.DatabaseFile));
            }
        }

        [Test]
        public void DataAlreadyInAnInstanceMovesIntoTheLibrary()
        {
            Directory.CreateDirectory(instance.FilesDirectory);
            File.WriteAllText(Path.Combine(instance.FilesDirectory, "hash"), "map");
            File.WriteAllText(instance.DatabaseFile, "db");

            library.Attach(instance);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(File.ReadAllText(Path.Combine(library.FilesDirectory, "hash")), Is.EqualTo("map"));
                Assert.That(File.ReadAllText(library.DatabaseFile), Is.EqualTo("db"));
                Assert.That(new DirectoryInfo(instance.FilesDirectory).LinkTarget, Is.EqualTo(library.FilesDirectory));
                Assert.That(new FileInfo(instance.DatabaseFile).LinkTarget, Is.EqualTo(library.DatabaseFile));
            }
        }

        [Test]
        public void AttachingAgainChangesNothing()
        {
            library.Attach(instance);
            File.WriteAllText(Path.Combine(instance.FilesDirectory, "hash"), "map");

            library.Attach(instance);

            Assert.That(File.ReadAllText(Path.Combine(library.FilesDirectory, "hash")), Is.EqualTo("map"));
        }

        [Test]
        public void InstanceAndLibraryThatBothHaveTheirOwnFilesAreRefused()
        {
            Directory.CreateDirectory(instance.FilesDirectory);
            Directory.CreateDirectory(library.FilesDirectory);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(() => library.Attach(instance), Throws.InvalidOperationException);
                Assert.That(new DirectoryInfo(instance.FilesDirectory).LinkTarget, Is.Null);
            }
        }

        [Test]
        public void InstanceLinkedToAnotherLibraryIsRefused()
        {
            new SharedLibrary(Path.Combine(directory, "elsewhere")).Attach(instance);

            Assert.That(() => library.Attach(instance), Throws.InvalidOperationException);
        }

        [Test]
        public void DeletingAnInstanceLeavesTheLibrary()
        {
            library.Attach(instance);
            File.WriteAllText(Path.Combine(instance.FilesDirectory, "hash"), "map");
            File.WriteAllText(instance.DatabaseFile, "db");
            library.Attach(instance);

            Directory.Delete(instance.Directory, recursive: true);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(File.ReadAllText(Path.Combine(library.FilesDirectory, "hash")), Is.EqualTo("map"));
                Assert.That(File.ReadAllText(library.DatabaseFile), Is.EqualTo("db"));
            }
        }

        [Test]
        public void LinkedInstanceNeedsNoPermission()
        {
            library.Attach(instance);
            bool asked = false;
            var restricted = new SharedLibrary(library.Root, (_, _) => asked = true, () => false);

            restricted.Attach(instance);

            Assert.That(asked, Is.False);
        }

        [Test]
        public void ElevationIsNotRequestedWhenLinksCanBeMade()
        {
            bool asked = false;
            var elevating = new SharedLibrary(library.Root, (_, _) => asked = true);

            elevating.Attach(instance);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(asked, Is.False);
                Assert.That(new DirectoryInfo(instance.FilesDirectory).LinkTarget, Is.EqualTo(library.FilesDirectory));
            }
        }

        [Test]
        public void HelperLinksAnInstanceToTheLibrary()
        {
            Directory.CreateDirectory(instance.Directory);
            File.WriteAllText(instance.PortableMarker, string.Empty);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ElevatedLink.TryRun([ElevatedLink.ARGUMENT, instance.Directory, library.Root], out int exitCode), Is.True);
                Assert.That(exitCode, Is.Zero);
                Assert.That(new DirectoryInfo(instance.FilesDirectory).LinkTarget, Is.EqualTo(library.FilesDirectory));
            }
        }

        [Test]
        public void InstancesShareWhatTheLibraryHolds()
        {
            var other = new LazerInstance(Path.Combine(directory, "other-instance"));
            library.Attach(instance);
            library.Attach(other);

            File.WriteAllText(Path.Combine(instance.FilesDirectory, "hash"), "map");

            Assert.That(File.ReadAllText(Path.Combine(other.FilesDirectory, "hash")), Is.EqualTo("map"));
        }
    }
}

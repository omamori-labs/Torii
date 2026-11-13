using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Torii.Core.Library;

namespace Torii.Tests
{
    [TestFixture]
    public class LibrarySeedTests
    {
        private string directory = null!;
        private string source = null!;
        private SharedLibrary library = null!;
        private bool osuRunning;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            source = Path.Combine(directory, "source");

            Directory.CreateDirectory(Path.Combine(source, "files", "a", "ab"));
            Directory.CreateDirectory(Path.Combine(source, "files", "b", "bc"));
            File.WriteAllText(Path.Combine(source, "files", "a", "ab", "hash1"), "map1");
            File.WriteAllText(Path.Combine(source, "files", "b", "bc", "hash2"), "map2");
            File.WriteAllText(Path.Combine(source, "client.realm"), "db");

            library = new SharedLibrary(Path.Combine(directory, "library"), (_, _) => true, () => false);
            osuRunning = false;
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, recursive: true);

        [Test]
        public void NewLibraryIsEmpty()
        {
            Assert.That(LibrarySeed.IsEmpty(library), Is.True);
        }

        [Test]
        public async Task ImportBringsTheDatabaseAndFilesIntoAnEmptyLibrary()
        {
            await seed().ImportAsync(library, source, null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(File.ReadAllText(library.DatabaseFile), Is.EqualTo("db"));
                Assert.That(File.ReadAllText(Path.Combine(library.FilesDirectory, "a", "ab", "hash1")), Is.EqualTo("map1"));
                Assert.That(File.ReadAllText(Path.Combine(library.FilesDirectory, "b", "bc", "hash2")), Is.EqualTo("map2"));
                Assert.That(LibrarySeed.IsEmpty(library), Is.False);
                Assert.That(library.IsUsable, Is.True);
            }
        }

        [Test]
        public async Task ImportLeavesTheSourceAsItWas()
        {
            await seed().ImportAsync(library, source, null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(File.ReadAllText(Path.Combine(source, "client.realm")), Is.EqualTo("db"));
                Assert.That(File.ReadAllText(Path.Combine(source, "files", "a", "ab", "hash1")), Is.EqualTo("map1"));
                Assert.That(File.ReadAllText(Path.Combine(source, "files", "b", "bc", "hash2")), Is.EqualTo("map2"));
            }
        }

        [Test]
        public async Task FilesAreLinkedNotCopied()
        {
            await seed().ImportAsync(library, source, null);

            File.AppendAllText(Path.Combine(library.FilesDirectory, "a", "ab", "hash1"), "!");

            Assert.That(File.ReadAllText(Path.Combine(source, "files", "a", "ab", "hash1")), Is.EqualTo("map1!"));
        }

        [Test]
        public async Task DatabaseIsCopiedNotLinked()
        {
            await seed().ImportAsync(library, source, null);

            File.AppendAllText(library.DatabaseFile, "!");

            Assert.That(File.ReadAllText(Path.Combine(source, "client.realm")), Is.EqualTo("db"));
        }

        [Test]
        public async Task ProgressEndsAtOne()
        {
            var progress = new RecordingProgress<double>();

            await seed().ImportAsync(library, source, null, progress);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(progress.Reported, Is.Not.Empty);
                Assert.That(progress.Reported.Last(), Is.EqualTo(1));
                Assert.That(progress.Reported, Is.Ordered);
            }
        }

        [Test]
        public async Task EmptyFilesFolderInTheLibraryIsFine()
        {
            Directory.CreateDirectory(library.FilesDirectory);

            await seed().ImportAsync(library, source, null);

            Assert.That(File.ReadAllText(Path.Combine(library.FilesDirectory, "a", "ab", "hash1")), Is.EqualTo("map1"));
        }

        [Test]
        public async Task KnownVersionIsRememberedByTheLibrary()
        {
            await seed().ImportAsync(library, source, "2026.1105.0-lazer");

            Assert.That(library.ReadVersion(), Is.EqualTo("2026.1105.0-lazer"));
        }

        [Test]
        public async Task UnknownVersionLeavesTheLibraryWithoutOne()
        {
            await seed().ImportAsync(library, source, null);

            Assert.That(library.ReadVersion(), Is.Null);
        }

        [Test]
        public void OsuRunningRefusesTheImportAndLeavesNothingBehind()
        {
            osuRunning = true;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(async () => await seed().ImportAsync(library, source, null), Throws.InvalidOperationException);
                Assert.That(LibrarySeed.IsEmpty(library), Is.True);
                Assert.That(stagingFolders(), Is.Empty);
            }
        }

        [Test]
        public void LibraryWithADatabaseIsRefused()
        {
            Directory.CreateDirectory(library.Root);
            File.WriteAllText(library.DatabaseFile, "mine");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(async () => await seed().ImportAsync(library, source, null), Throws.InvalidOperationException);
                Assert.That(File.ReadAllText(library.DatabaseFile), Is.EqualTo("mine"));
            }
        }

        [Test]
        public void LibraryWithFilesIsRefused()
        {
            Directory.CreateDirectory(Path.Combine(library.FilesDirectory, "c", "cd"));
            File.WriteAllText(Path.Combine(library.FilesDirectory, "c", "cd", "mine"), "map");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(async () => await seed().ImportAsync(library, source, null), Throws.InvalidOperationException);
                Assert.That(File.Exists(library.DatabaseFile), Is.False);
            }
        }

        [Test]
        public void SourceWithoutADatabaseIsRefused()
        {
            File.Delete(Path.Combine(source, "client.realm"));

            Assert.That(async () => await seed().ImportAsync(library, source, null), Throws.InvalidOperationException);
        }

        [Test]
        public void CancellingLeavesNothingBehindAndTheSourceUntouched()
        {
            using var cancellation = new CancellationTokenSource();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(async () => await seed().ImportAsync(library, source, null, new CancelOnReport(cancellation), cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
                Assert.That(LibrarySeed.IsEmpty(library), Is.True);
                Assert.That(stagingFolders(), Is.Empty);
                Assert.That(File.Exists(Path.Combine(source, "files", "a", "ab", "hash1")), Is.True);
                Assert.That(File.Exists(Path.Combine(source, "files", "b", "bc", "hash2")), Is.True);
            }
        }

        private LibrarySeed seed() => new LibrarySeed(() => osuRunning);

        private string[] stagingFolders() => Directory.Exists(library.Root) ? Directory.GetDirectories(library.Root, "import-*") : [];

        private sealed class CancelOnReport : IProgress<double>
        {
            private readonly CancellationTokenSource cancellation;

            public CancelOnReport(CancellationTokenSource cancellation)
            {
                this.cancellation = cancellation;
            }

            public void Report(double value) => cancellation.Cancel();
        }
    }
}

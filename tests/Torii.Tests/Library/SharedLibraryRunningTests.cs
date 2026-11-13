using System;
using System.IO;
using NUnit.Framework;
using Torii.Core.Lazer;
using Torii.Core.Library;

namespace Torii.Tests
{
    [TestFixture]
    public class SharedLibraryRunningTests
    {
        private string directory = null!;
        private LazerInstance instance = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            instance = new LazerInstance(Path.Combine(directory, "instance"));
            Directory.CreateDirectory(instance.Directory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, recursive: true);

        [Test]
        public void NoGameRunningMeansNothingIsFound()
        {
            var library = new SharedLibrary(Path.Combine(directory, "library"), null, () => false, _ => null);

            Assert.That(library.FindRunningInstance(), Is.Null);
        }

        [Test]
        public void GameWithItsOwnDataIsNotUsingTheLibrary()
        {
            Directory.CreateDirectory(instance.FilesDirectory);
            var library = new SharedLibrary(Path.Combine(directory, "library"), null, () => false, uses => uses(instance.Directory) ? instance.Directory : null);

            Assert.That(library.FindRunningInstance(), Is.Null);
        }

        [Test]
        public void GameWithoutAFilesFolderIsNotUsingTheLibrary()
        {
            var library = new SharedLibrary(Path.Combine(directory, "library"), null, () => false, uses => uses(instance.Directory) ? instance.Directory : null);

            Assert.That(library.FindRunningInstance(), Is.Null);
        }
    }
}

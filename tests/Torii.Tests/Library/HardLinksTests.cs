using System;
using System.IO;
using NUnit.Framework;
using Torii.Core.Library;

namespace Torii.Tests
{
    [TestFixture]
    public class HardLinksTests
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
        public void LinkSharesTheFile()
        {
            string existing = Path.Combine(directory, "existing");
            string link = Path.Combine(directory, "link");
            File.WriteAllText(existing, "map");

            bool created = HardLinks.TryCreate(link, existing);
            File.AppendAllText(link, "!");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(created, Is.True);
                Assert.That(File.ReadAllText(existing), Is.EqualTo("map!"));
            }
        }

        [Test]
        public void LinkIntoAFolderThatDoesNotExistFails()
        {
            string existing = Path.Combine(directory, "existing");
            File.WriteAllText(existing, "map");

            Assert.That(HardLinks.TryCreate(Path.Combine(directory, "missing", "link"), existing), Is.False);
        }

        [Test]
        public void LinkToAFileThatDoesNotExistFails()
        {
            Assert.That(HardLinks.TryCreate(Path.Combine(directory, "link"), Path.Combine(directory, "missing")), Is.False);
        }
    }
}

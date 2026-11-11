using System;
using System.IO;
using NUnit.Framework;
using Torii.Core;

namespace Torii.Tests
{
    [TestFixture]
    public class OsuDataTests
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
        public void DefaultFolderIsNamedOsu()
        {
            Assert.That(Path.GetFileName(OsuData.DefaultDirectory()), Is.EqualTo("osu"));
        }

        [Test]
        public void WithoutStorageConfigTheDefaultFolderIsUsed()
        {
            Assert.That(OsuData.Resolve(directory), Is.EqualTo(directory));
        }

        [Test]
        public void MovedDataIsFollowed()
        {
            string moved = Path.Combine(directory, "elsewhere");
            writeStorageConfig($"FullPath = {moved}");

            Assert.That(OsuData.Resolve(directory), Is.EqualTo(moved));
        }

        [Test]
        public void OtherStorageConfigLinesAreIgnored()
        {
            string moved = Path.Combine(directory, "elsewhere");
            writeStorageConfig("", "SomethingElse = 1", $"FullPath={moved}");

            Assert.That(OsuData.Resolve(directory), Is.EqualTo(moved));
        }

        [TestCase("FullPath = ")]
        [TestCase("FullPathButNot = D:\\osu")]
        [TestCase("not an ini file")]
        public void StorageConfigWithoutAPathFallsBackToTheDefaultFolder(string line)
        {
            writeStorageConfig(line);

            Assert.That(OsuData.Resolve(directory), Is.EqualTo(directory));
        }

        [Test]
        public void EmptyFolderDoesNotLookLikeData()
        {
            Assert.That(OsuData.LooksLikeData(directory), Is.False);
        }

        [Test]
        public void FolderWithADatabaseLooksLikeData()
        {
            File.WriteAllText(Path.Combine(directory, "client.realm"), string.Empty);

            Assert.That(OsuData.LooksLikeData(directory), Is.True);
        }

        [Test]
        public void FolderWithAFileStoreLooksLikeData()
        {
            Directory.CreateDirectory(Path.Combine(directory, "files"));

            Assert.That(OsuData.LooksLikeData(directory), Is.True);
        }

        private void writeStorageConfig(params string[] lines) => File.WriteAllLines(Path.Combine(directory, OsuData.STORAGE_CONFIG), lines);
    }
}

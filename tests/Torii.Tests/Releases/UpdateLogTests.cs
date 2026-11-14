using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Torii.Core.Releases;

namespace Torii.Tests
{
    [TestFixture]
    public class UpdateLogTests
    {
        private string directory = null!;
        private UpdateLog log = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            log = new UpdateLog(Path.Combine(directory, "deeper", "update.log"));
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, recursive: true);

        [Test]
        public void LinesAreAppendedWithATimestampAndTheFolderIsCreated()
        {
            log.Write("first");
            log.Write("second");

            string[] lines = File.ReadAllLines(log.FilePath);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(lines, Has.Length.EqualTo(2));
                Assert.That(lines[0], Does.EndWith(" first"));
                Assert.That(lines[1], Does.EndWith(" second"));
                Assert.That(lines.All(line => DateTime.TryParse(line[..19], out _)), Is.True);
            }
        }

        [Test]
        public void AFileOverTheLimitStartsAgain()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(log.FilePath)!);
            File.WriteAllText(log.FilePath, new string('x', 70 * 1024));

            log.Write("fresh");

            string[] lines = File.ReadAllLines(log.FilePath);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(lines, Has.Length.EqualTo(1));
                Assert.That(lines[0], Does.EndWith(" fresh"));
            }
        }

        [Test]
        public void FailingToWriteIsNotAnError()
        {
            Directory.CreateDirectory(log.FilePath);

            Assert.That(() => log.Write("nowhere to go"), Throws.Nothing);
        }

        [Test]
        public void DefaultFileIsInTheToriiFolderNotTheAppFolder()
        {
            Assert.That(UpdateLog.DefaultFile(), Does.EndWith(Path.Combine("Torii", "update.log")));
        }
    }
}

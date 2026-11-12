using System;
using System.IO;
using NUnit.Framework;
using Torii.Core;

namespace Torii.Tests
{
    [TestFixture]
    public class OsuProcessTests
    {
        private string directory = null!;
        private string executable = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            executable = Path.Combine(directory, "osu!.exe");
            File.WriteAllText(executable, string.Empty);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, recursive: true);

        [Test]
        public void ExecutableNextToTheGameLibraryIsLazer()
        {
            File.WriteAllText(Path.Combine(directory, "osu.Game.dll"), string.Empty);

            Assert.That(OsuProcess.IsLazer(executable), Is.True);
        }

        [Test]
        [Platform("Win")]
        public void ExecutableWithoutTheGameLibraryIsStable()
        {
            Assert.That(OsuProcess.IsLazer(executable), Is.False);
        }

        [Test]
        [Platform(Exclude = "Win")]
        public void OutsideWindowsThereIsOnlyLazer()
        {
            Assert.That(OsuProcess.IsLazer(executable), Is.True);
        }

        [Test]
        public void ProcessThatCannotBeInspectedCountsAsLazer()
        {
            Assert.That(OsuProcess.IsLazer(null), Is.True);
        }

        [Test]
        public void LookingForOsuNeverThrows()
        {
            Assert.That(() => OsuProcess.IsRunning(), Throws.Nothing);
        }
    }
}

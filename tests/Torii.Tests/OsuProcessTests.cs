using System;
using System.IO;
using NUnit.Framework;
using Torii.Core;

namespace Torii.Tests
{
    [TestFixture]
    public class OsuProcessTests
    {
        private static readonly string instance = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}", "instance");

        [Test]
        public void GameInTheFolderIsRunningThere()
        {
            Assert.That(OsuProcess.IsIn(Path.Combine(instance, "osu!.exe"), instance), Is.True);
        }

        [Test]
        public void GameSomewhereElseIsNot()
        {
            Assert.That(OsuProcess.IsIn(Path.Combine(Path.GetTempPath(), "osulazer", "current", "osu!.exe"), instance), Is.False);
        }

        [Test]
        public void FolderWithTheSamePrefixIsNotTheFolder()
        {
            Assert.That(OsuProcess.IsIn(Path.Combine(instance + "-other", "osu!.exe"), instance), Is.False);
        }

        [Test]
        public void ProcessThatCannotBeInspectedCountsAsRunningThere()
        {
            Assert.That(OsuProcess.IsIn(null, instance), Is.True);
        }

        [Test]
        public void NothingIsFoundWhenNoRunningGameQualifies()
        {
            Assert.That(OsuProcess.FindRunning(_ => false), Is.Null);
        }

        [Test]
        public void LookingForOsuNeverThrows()
        {
            Assert.That(() => OsuProcess.IsRunningIn(instance), Throws.Nothing);
        }
    }
}

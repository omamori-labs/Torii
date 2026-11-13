using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Torii.Core;
using Torii.Core.Lazer;

namespace Torii.Tests
{
    [TestFixture]
    public class GameLauncherTests
    {
        private string directory = null!;
        private LazerInstance instance = null!;
        private List<ProcessStartInfo> started = null!;
        private GameLauncher launcher = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            instance = new LazerInstance(directory);
            started = [];
            launcher = new GameLauncher(started.Add);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, recursive: true);

        [Test]
        public void SetUpInstanceStartsItsOwnGameInItsOwnFolder()
        {
            place(instance.Executable, Path.Combine(directory, "osu.Game.dll"), instance.PortableMarker);

            launcher.Launch(instance);

            ProcessStartInfo info = started.Single();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(info.FileName, Is.EqualTo(instance.Executable));
                Assert.That(info.WorkingDirectory, Is.EqualTo(instance.Directory));
                Assert.That(info.UseShellExecute, Is.False);
            }
        }

        [Test]
        public void EmptyFolderIsNotStarted()
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(() => launcher.Launch(instance), Throws.InvalidOperationException);
                Assert.That(started, Is.Empty);
            }
        }

        [Test]
        public void GameThatWouldOpenThePlayersRealDataIsNotStarted()
        {
            place(instance.Executable, Path.Combine(directory, "osu.Game.dll"));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(() => launcher.Launch(instance), Throws.InvalidOperationException);
                Assert.That(started, Is.Empty);
            }
        }

        [Test]
        public void InstanceWithoutItsExecutableIsNotStarted()
        {
            place(Path.Combine(directory, "osu.Game.dll"), instance.PortableMarker);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(() => launcher.Launch(instance), Throws.InvalidOperationException);
                Assert.That(started, Is.Empty);
            }
        }

        private static void place(params string[] files)
        {
            foreach (string file in files)
                File.WriteAllText(file, string.Empty);
        }
    }
}

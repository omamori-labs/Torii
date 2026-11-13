using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Torii.Core.Lazer;

namespace Torii.Tests
{
    [TestFixture]
    public class GameCacheTests
    {
        private const string version = "2026.1005.0-lazer";

        private static readonly string[] no_versions = [];
        private static readonly string[] only_newer = ["2026.1100.0-lazer"];
        private static readonly string[] game_files = ["osu.Game.dll", "native.dll"];

        private string directory = null!;
        private List<(string Link, string Existing)> links = null!;
        private bool linksWork;
        private GameCache cache = null!;
        private int fills;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            links = [];
            linksWork = true;
            fills = 0;
            cache = new GameCache(Path.Combine(directory, "games"), (link, existing) =>
            {
                links.Add((link, existing));

                if (!linksWork)
                    return false;

                File.Copy(existing, link);
                return true;
            });
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(directory, recursive: true);
        }

        [Test]
        public async Task FirstInstanceFillsTheCacheAndGetsTheGame()
        {
            LazerInstance instance = newInstance("alpha");

            await cache.InstallAsync(version, instance, fill("game"));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(fills, Is.EqualTo(1));
                Assert.That(cache.Has(version), Is.True);
                Assert.That(File.ReadAllText(Path.Combine(instance.Directory, "osu.Game.dll")), Is.EqualTo("game"));
                Assert.That(File.ReadAllText(Path.Combine(instance.Directory, "runtimes", "native.dll")), Is.EqualTo("game"));
                Assert.That(File.Exists(instance.PortableMarker), Is.True);
                Assert.That(File.ReadAllLines(instance.GameFilesList), Has.Length.EqualTo(2));
                Assert.That(instance.IsInstalled, Is.True);
            }
        }

        [Test]
        public async Task SecondInstanceOfTheSameVersionDoesNotDownloadAgain()
        {
            await cache.InstallAsync(version, newInstance("alpha"), fill("game"));

            LazerInstance second = newInstance("beta");
            await cache.InstallAsync(version, second, fill("game"));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(fills, Is.EqualTo(1));
                Assert.That(second.IsInstalled, Is.True);
            }
        }

        [Test]
        public async Task InstancesAreLinkedToTheCachedFiles()
        {
            LazerInstance instance = newInstance("alpha");

            await cache.InstallAsync(version, instance, fill("game"));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(links.Select(l => Path.GetFileName(l.Link)), Is.EquivalentTo(game_files));
                Assert.That(links.All(l => l.Existing.StartsWith(Path.Combine(directory, "games", version), StringComparison.Ordinal)), Is.True);
            }
        }

        [Test]
        public async Task FilesAreCopiedWhenLinkingFailsAndLinkingIsNotTriedAgain()
        {
            linksWork = false;
            LazerInstance instance = newInstance("alpha");

            await cache.InstallAsync(version, instance, fill("game"));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(links, Has.Count.EqualTo(1));
                Assert.That(File.ReadAllText(Path.Combine(instance.Directory, "osu.Game.dll")), Is.EqualTo("game"));
                Assert.That(File.ReadAllText(Path.Combine(instance.Directory, "runtimes", "native.dll")), Is.EqualTo("game"));
            }
        }

        [Test]
        public async Task ChangingVersionReplacesTheGameButKeepsTheData()
        {
            LazerInstance instance = newInstance("alpha");
            await cache.InstallAsync(version, instance, fill("old", "only-in-old.dll"));
            File.WriteAllText(instance.DatabaseFile, "player data");

            await cache.InstallAsync("2026.1100.0-lazer", instance, fill("new"));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(File.ReadAllText(Path.Combine(instance.Directory, "osu.Game.dll")), Is.EqualTo("new"));
                Assert.That(File.Exists(Path.Combine(instance.Directory, "only-in-old.dll")), Is.False);
                Assert.That(File.ReadAllText(instance.DatabaseFile), Is.EqualTo("player data"));
            }
        }

        [Test]
        public async Task CacheEntryWithAMissingFileIsDownloadedAgain()
        {
            await cache.InstallAsync(version, newInstance("alpha"), fill("game"));
            File.Delete(Path.Combine(directory, "games", version, "osu.Game.dll"));

            await cache.InstallAsync(version, newInstance("beta"), fill("game"));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(fills, Is.EqualTo(2));
                Assert.That(cache.Has(version), Is.True);
            }
        }

        [Test]
        public async Task FailedDownloadLeavesNothingInTheCache()
        {
            await Assert.ThatAsync(() => cache.InstallAsync(version, newInstance("alpha"), _ => throw new InvalidDataException("bad package")), Throws.InstanceOf<InvalidDataException>());

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cache.Has(version), Is.False);
                Assert.That(Directory.EnumerateFileSystemEntries(cache.Root), Is.Empty);
            }
        }

        [Test]
        public async Task PackageThatLeavesNoGameFilesIsRefused()
        {
            await Assert.ThatAsync(() => cache.InstallAsync(version, newInstance("alpha"), _ => Task.CompletedTask), Throws.InstanceOf<InvalidDataException>());
        }

        [Test]
        public async Task PruneRemovesVersionsNoInstanceUses()
        {
            await cache.InstallAsync(version, newInstance("alpha"), fill("old"));
            await cache.InstallAsync("2026.1100.0-lazer", newInstance("beta"), fill("new"));

            cache.Prune(only_newer);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cache.Has(version), Is.False);
                Assert.That(cache.Has("2026.1100.0-lazer"), Is.True);
            }
        }

        [Test]
        public async Task PruningTheCacheLeavesInstancesPlayable()
        {
            LazerInstance instance = newInstance("alpha");
            var real = new GameCache(Path.Combine(directory, "real-games"));
            await real.InstallAsync(version, instance, fill("game"));

            real.Prune(no_versions);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(real.Has(version), Is.False);
                Assert.That(File.ReadAllText(Path.Combine(instance.Directory, "osu.Game.dll")), Is.EqualTo("game"));
                Assert.That(instance.IsInstalled, Is.True);
            }
        }

        [Test]
        public async Task PruneLeavesADownloadInProgressAlone()
        {
            var started = new TaskCompletionSource();
            var finish = new TaskCompletionSource();

            Task install = cache.InstallAsync(version, newInstance("alpha"), async staging =>
            {
                await fill("game")(staging);
                started.SetResult();
                await finish.Task;
            });

            await started.Task;
            cache.Prune(no_versions);
            finish.SetResult();
            await install;

            Assert.That(cache.Has(version), Is.True);
        }

        [TestCase("")]
        [TestCase("..")]
        [TestCase("a/b")]
        [TestCase("staging-1")]
        public void VersionsThatAreNotFolderNamesAreRefused(string bad)
        {
            Assert.That(() => cache.Has(bad), Throws.InstanceOf<ArgumentException>());
        }

        private LazerInstance newInstance(string name) => new LazerInstance(Path.Combine(directory, "instances", name));

        private Func<string, Task> fill(string content, params string[] extra) => staging =>
        {
            fills++;

            var files = new List<string> { "osu.Game.dll", Path.Combine("runtimes", "native.dll") };
            files.AddRange(extra);

            foreach (string relative in files)
            {
                string path = Path.Combine(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, content);
            }

            File.WriteAllLines(new LazerInstance(staging).GameFilesList, files);
            return Task.CompletedTask;
        };
    }
}

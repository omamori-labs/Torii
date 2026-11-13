using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Torii.Core.Instances;
using Torii.Core.Lazer;

namespace Torii.Tests
{
    [TestFixture]
    public class InstanceStoreTests
    {
        private const string cached_version = "2026.1100.0-lazer";

        private static readonly string[] sorted_names = ["alpha", "Beta", "gamma"];

        private static readonly string[] only_cosmetic = ["Cosmetic mods"];

        private static readonly string[] new_then_old = ["new", "old-instance"];

        private static readonly string[] only_after = ["after"];

        private static readonly string[] only_renamed = ["renamed"];

        private static readonly string[] alpha_and_copy = ["alpha", "alpha copy"];

        private static readonly string[] only_game_dll = ["osu.Game.dll"];

        private static readonly string[] only_alpha = ["alpha"];

        private static readonly string[] game_files =["osu.Game.dll", "sq.version"];

        private static readonly string[] alpha_gamma_zzz = ["alpha", "gamma", "zzz"];

        private string directory = null!;
        private string legacy = null!;
        private bool running;
        private InstanceStore store = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            legacy = Path.Combine(directory, "old-instance");
            Directory.CreateDirectory(directory);

            running = false;
            store = new InstanceStore(Path.Combine(directory, "instances"), legacy, _ => running);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, recursive: true);

        [Test]
        public async Task GameVersionStaysCachedUntilTheLastInstanceUsingItIsDeleted()
        {
            var cache = new GameCache(Path.Combine(directory, "games"));
            var cached = new InstanceStore(Path.Combine(directory, "instances"), legacy, _ => false, cache);
            LazerInstance first = cached.Create("first");
            LazerInstance second = cached.Create("second");
            await cache.InstallAsync(cached_version, first, fillGame(cached_version));
            await cache.InstallAsync(cached_version, second, fillGame(cached_version));

            cached.Delete(first);
            bool afterFirst = cache.Has(cached_version);

            cached.Delete(second);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(afterFirst, Is.True);
                Assert.That(cache.Has(cached_version), Is.False);
            }
        }

        [Test]
        public async Task DeletingAnInstanceKeepsTheVersionAnotherInstanceUses()
        {
            var cache = new GameCache(Path.Combine(directory, "games"));
            var cached = new InstanceStore(Path.Combine(directory, "instances"), legacy, _ => false, cache);
            LazerInstance old = cached.Create("old");
            LazerInstance current = cached.Create("current");
            await cache.InstallAsync("2026.1005.0-lazer", old, fillGame("2026.1005.0-lazer"));
            await cache.InstallAsync(cached_version, current, fillGame(cached_version));

            cached.Delete(old);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cache.Has("2026.1005.0-lazer"), Is.False);
                Assert.That(cache.Has(cached_version), Is.True);
            }
        }

        [Test]
        public void DuplicateCopiesModsAndSettingsButLeavesTheSharedDataBehind()
        {
            LazerInstance source = sourceInstance("alpha");

            LazerInstance copy = store.Duplicate(source, "alpha copy");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(copy.Name, Is.EqualTo("alpha copy"));
                Assert.That(store.List().Select(i => i.Name), Is.EqualTo(alpha_and_copy));
                Assert.That(File.ReadAllText(Path.Combine(copy.Directory, "omamori", "mods", "m", "mod.json")), Is.EqualTo("manifest"));
                Assert.That(File.ReadAllText(copy.Omamori.SettingsFile), Is.EqualTo("settings"));
                Assert.That(File.ReadAllText(Path.Combine(copy.Directory, "rulesets", "ruleset.dll")), Is.EqualTo("ruleset"));
                Assert.That(File.Exists(copy.PortableMarker), Is.True);
                Assert.That(File.Exists(Path.Combine(copy.Directory, "osu.Game.dll")), Is.True);
                Assert.That(Directory.Exists(copy.FilesDirectory), Is.False);
                Assert.That(File.Exists(copy.DatabaseFile), Is.False);
                Assert.That(File.Exists(Path.Combine(copy.Directory, "client.realm.lock")), Is.False);
                Assert.That(Directory.Exists(Path.Combine(copy.Directory, "logs")), Is.False);
                Assert.That(File.Exists(Path.Combine(source.Directory, "client.realm")), Is.True);
            }
        }

        [Test]
        public void DuplicateLinksTheGameFilesAndCopiesTheRest()
        {
            var links = new System.Collections.Generic.List<string>();
            store = new InstanceStore(Path.Combine(directory, "instances"), legacy, _ => running, link: (destination, existing) =>
            {
                links.Add(Path.GetFileName(destination));
                File.Copy(existing, destination);
                return true;
            });

            LazerInstance copy = store.Duplicate(sourceInstance("alpha"), "linked");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(links, Is.EqualTo(only_game_dll));
                Assert.That(File.ReadAllText(Path.Combine(copy.Directory, "osu.Game.dll")), Is.EqualTo("game"));
            }
        }

        [Test]
        public void DuplicateCopiesGameFilesWhenLinkingFails()
        {
            int tries = 0;
            store = new InstanceStore(Path.Combine(directory, "instances"), legacy, _ => running, link: (_, _) =>
            {
                tries++;
                return false;
            });

            LazerInstance copy = store.Duplicate(sourceInstance("alpha"), "copied");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(tries, Is.EqualTo(1));
                Assert.That(File.ReadAllText(Path.Combine(copy.Directory, "osu.Game.dll")), Is.EqualTo("game"));
            }
        }

        [Test]
        public void DuplicateIsRefusedWhileTheSourceRuns()
        {
            LazerInstance source = sourceInstance("alpha");
            running = true;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(() => store.Duplicate(source, "copy"), Throws.InvalidOperationException);
                Assert.That(store.List(), Has.Count.EqualTo(1));
            }
        }

        [Test]
        public void DuplicateNeedsAFreeValidName()
        {
            LazerInstance source = sourceInstance("alpha");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(() => store.Duplicate(source, "ALPHA"), Throws.InvalidOperationException);
                Assert.That(() => store.Duplicate(source, "  "), Throws.InvalidOperationException);
                Assert.That(store.List(), Has.Count.EqualTo(1));
            }
        }

        [Test]
        public void FailedDuplicateLeavesNoHalfCopyBehind()
        {
            store = new InstanceStore(Path.Combine(directory, "instances"), legacy, _ => running, link: (_, _) => throw new IOException("disk full"));
            LazerInstance source = sourceInstance("alpha");

            Assert.That(() => store.Duplicate(source, "broken"), Throws.InstanceOf<IOException>());

            Assert.That(store.List().Select(i => i.Name), Is.EqualTo(only_alpha));
        }

        [Test]
        public void CopyNameIsSuggestedAndMadeUnique()
        {
            LazerInstance source = sourceInstance("alpha");
            string first = store.SuggestCopyName(source);

            store.Create(first);
            string second = store.SuggestCopyName(source);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(first, Is.EqualTo("alpha copy"));
                Assert.That(second, Is.EqualTo("alpha copy 2"));
            }
        }

        [Test]
        public void ThereAreNoInstancesToStartWith()
        {
            Assert.That(store.List(), Is.Empty);
        }

        [Test]
        public void CreatedInstanceIsListedWithItsName()
        {
            LazerInstance created = store.Create("Cosmetic mods");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(created.Name, Is.EqualTo("Cosmetic mods"));
                Assert.That(Directory.Exists(created.Directory), Is.True);
                Assert.That(store.List().Select(i => i.Name), Is.EqualTo(only_cosmetic));
            }
        }

        [Test]
        public void NameIsTrimmed()
        {
            Assert.That(store.Create("  Tidy  ").Name, Is.EqualTo("Tidy"));
        }

        [Test]
        public void InstancesAreListedByNameWhateverTheCase()
        {
            store.Create("gamma");
            store.Create("alpha");
            store.Create("Beta");

            Assert.That(store.List().Select(i => i.Name), Is.EqualTo(sorted_names));
        }

        [Test]
        public void OldDefaultInstanceIsListedToo()
        {
            Directory.CreateDirectory(legacy);
            store.Create("new");

            Assert.That(store.List().Select(i => i.Name), Is.EqualTo(new_then_old));
        }

        [Test]
        public void OldDefaultInstanceIsNotListedWhenItIsNotThere()
        {
            Assert.That(store.List(), Is.Empty);
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("a/b")]
        [TestCase("a\\b")]
        [TestCase("a:b")]
        [TestCase("what?")]
        [TestCase("name.")]
        [TestCase("con")]
        [TestCase("CON.txt")]
        [TestCase("Lpt3")]
        [TestCase("12345678901234567890123456789012345678901234567890x")]
        public void BadNamesAreRefused(string name)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(store.Validate(name), Is.Not.Null);
                Assert.That(() => store.Create(name), Throws.InvalidOperationException);
                Assert.That(store.List(), Is.Empty);
            }
        }

        [TestCase("Cosmetic mods")]
        [TestCase("My instance 2")]
        [TestCase("a.b")]
        [TestCase("12345678901234567890123456789012345678901234567890")]
        public void GoodNamesAreAccepted(string name)
        {
            Assert.That(store.Validate(name), Is.Null);
        }

        [Test]
        public void NameAlreadyTakenIsRefusedWhateverTheCase()
        {
            store.Create("Foo");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(store.Validate("foo"), Is.Not.Null);
                Assert.That(() => store.Create("FOO"), Throws.InvalidOperationException);
            }
        }

        [Test]
        public void NameOfTheOldDefaultInstanceIsTaken()
        {
            Directory.CreateDirectory(legacy);

            Assert.That(store.Validate("Old-Instance"), Is.Not.Null);
        }

        [Test]
        public void DeletingRemovesTheInstanceAndEverythingInIt()
        {
            LazerInstance created = store.Create("doomed");
            Directory.CreateDirectory(Path.Combine(created.Directory, "omamori", "mods", "a"));
            File.WriteAllText(Path.Combine(created.Directory, "omamori", "mods", "a", "mod.json"), "{}");

            store.Delete(created);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Directory.Exists(created.Directory), Is.False);
                Assert.That(store.List(), Is.Empty);
            }
        }

        [Test]
        public void DeletingLeavesTheOtherInstancesAlone()
        {
            LazerInstance doomed = store.Create("doomed");
            LazerInstance kept = store.Create("kept");

            store.Delete(doomed);

            Assert.That(store.List().Single().Name, Is.EqualTo(kept.Name));
        }

        [Test]
        public void OldDefaultInstanceCanBeDeleted()
        {
            Directory.CreateDirectory(legacy);

            store.Delete(new LazerInstance(legacy));

            Assert.That(Directory.Exists(legacy), Is.False);
        }

        [Test]
        public void InstanceWhoseGameRunsIsNotDeleted()
        {
            LazerInstance created = store.Create("busy");
            running = true;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(() => store.Delete(created), Throws.InvalidOperationException);
                Assert.That(Directory.Exists(created.Directory), Is.True);
            }
        }

        [Test]
        public void FolderTheStoreDoesNotManageIsNotDeleted()
        {
            string elsewhere = Path.Combine(directory, "elsewhere");
            Directory.CreateDirectory(elsewhere);
            File.WriteAllText(Path.Combine(elsewhere, "keep.txt"), string.Empty);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(() => store.Delete(new LazerInstance(elsewhere)), Throws.InvalidOperationException);
                Assert.That(File.Exists(Path.Combine(elsewhere, "keep.txt")), Is.True);
            }
        }

        [Test]
        public void RenamingMovesTheInstanceWithEverythingInIt()
        {
            LazerInstance created = store.Create("before");
            Directory.CreateDirectory(Path.Combine(created.Directory, "omamori", "mods", "a"));
            File.WriteAllText(Path.Combine(created.Directory, "omamori", "mods", "a", "mod.json"), "{}");

            LazerInstance renamed = store.Rename(created, "after");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(renamed.Name, Is.EqualTo("after"));
                Assert.That(Directory.Exists(created.Directory), Is.False);
                Assert.That(File.Exists(Path.Combine(renamed.Directory, "omamori", "mods", "a", "mod.json")), Is.True);
                Assert.That(store.List().Select(i => i.Name), Is.EqualTo(only_after));
            }
        }

        [Test]
        public void RenamedInstanceTakesItsNewPlaceInTheList()
        {
            store.Create("alpha");
            LazerInstance middle = store.Create("beta");
            store.Create("gamma");

            store.Rename(middle, "zzz");

            Assert.That(store.List().Select(i => i.Name), Is.EqualTo(alpha_gamma_zzz));
        }

        [Test]
        public void OnlyTheCaseOfTheNameCanChange()
        {
            LazerInstance created = store.Create("Foo");

            LazerInstance renamed = store.Rename(created, "foo");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(renamed.Name, Is.EqualTo("foo"));
                Assert.That(store.List().Single().Name, Is.EqualTo("foo"));
            }
        }

        [Test]
        public void RenamingToTheSameNameChangesNothing()
        {
            LazerInstance created = store.Create("same");

            LazerInstance renamed = store.Rename(created, "same");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(renamed, Is.SameAs(created));
                Assert.That(Directory.Exists(created.Directory), Is.True);
            }
        }

        [Test]
        public void NameOfAnotherInstanceIsRefusedWhenRenaming()
        {
            store.Create("a");
            LazerInstance second = store.Create("b");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(() => store.Rename(second, "A"), Throws.InvalidOperationException);
                Assert.That(Directory.Exists(second.Directory), Is.True);
            }
        }

        [Test]
        public void InstanceMayKeepItsOwnNameButNobodyElseMayTakeIt()
        {
            LazerInstance created = store.Create("alpha");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(store.Validate("alpha", created), Is.Null);
                Assert.That(store.Validate("alpha"), Is.Not.Null);
            }
        }

        [Test]
        public void BadNameIsRefusedWhenRenaming()
        {
            LazerInstance created = store.Create("fine");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(() => store.Rename(created, "a/b"), Throws.InvalidOperationException);
                Assert.That(Directory.Exists(created.Directory), Is.True);
            }
        }

        [Test]
        public void InstanceWhoseGameRunsIsNotRenamed()
        {
            LazerInstance created = store.Create("busy");
            running = true;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(() => store.Rename(created, "other"), Throws.InvalidOperationException);
                Assert.That(Directory.Exists(created.Directory), Is.True);
            }
        }

        [Test]
        public void FolderTheStoreDoesNotManageIsNotRenamed()
        {
            string elsewhere = Path.Combine(directory, "elsewhere");
            Directory.CreateDirectory(elsewhere);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(() => store.Rename(new LazerInstance(elsewhere), "mine"), Throws.InvalidOperationException);
                Assert.That(Directory.Exists(elsewhere), Is.True);
            }
        }

        [Test]
        public void OldDefaultInstanceMovesIntoTheInstancesFolderWhenRenamed()
        {
            Directory.CreateDirectory(legacy);

            LazerInstance renamed = store.Rename(new LazerInstance(legacy), "renamed");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Directory.Exists(legacy), Is.False);
                Assert.That(Directory.Exists(renamed.Directory), Is.True);
                Assert.That(store.List().Select(i => i.Name), Is.EqualTo(only_renamed));
            }
        }

        [Test]
        public void TheInstancesFolderItselfIsNotDeleted()
        {
            store.Create("one");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(() => store.Delete(new LazerInstance(store.Root)), Throws.InvalidOperationException);
                Assert.That(store.List(), Has.Count.EqualTo(1));
            }
        }

        private LazerInstance sourceInstance(string name)
        {
            LazerInstance source = store.Create(name);

            write(source, "osu.Game.dll", "game");
            write(source, "framework.ini", string.Empty);
            write(source, "torii-game-files.txt", $"osu.Game.dll{Environment.NewLine}");
            write(source, Path.Combine("omamori", "mods", "m", "mod.json"), "manifest");
            write(source, Path.Combine("omamori", "settings.json"), "settings");
            write(source, Path.Combine("rulesets", "ruleset.dll"), "ruleset");
            write(source, Path.Combine("files", "ab", "abc"), "map");
            write(source, "client.realm", "database");
            write(source, "client.realm.lock", string.Empty);
            write(source, Path.Combine("logs", "runtime.log"), "log");

            return source;
        }

        private static void write(LazerInstance instance, string relative, string content)
        {
            string path = Path.Combine(instance.Directory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        private static Func<string, Task> fillGame(string version) => staging =>
        {
            File.WriteAllText(Path.Combine(staging, "osu.Game.dll"), string.Empty);
            File.WriteAllText(Path.Combine(staging, "sq.version"), $"<package><metadata><version>{version}</version></metadata></package>");
            File.WriteAllLines(new LazerInstance(staging).GameFilesList, game_files);

            return Task.CompletedTask;
        };
    }
}

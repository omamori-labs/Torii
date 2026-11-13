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

        private static readonly string[] game_files = ["osu.Game.dll", "sq.version"];

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

        private static Func<string, Task> fillGame(string version) => staging =>
        {
            File.WriteAllText(Path.Combine(staging, "osu.Game.dll"), string.Empty);
            File.WriteAllText(Path.Combine(staging, "sq.version"), $"<package><metadata><version>{version}</version></metadata></package>");
            File.WriteAllLines(new LazerInstance(staging).GameFilesList, game_files);

            return Task.CompletedTask;
        };
    }
}

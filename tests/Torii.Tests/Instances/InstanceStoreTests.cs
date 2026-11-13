using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Torii.Core.Instances;
using Torii.Core.Lazer;

namespace Torii.Tests
{
    [TestFixture]
    public class InstanceStoreTests
    {
        private static readonly string[] sorted_names = ["alpha", "Beta", "gamma"];

        private static readonly string[] only_cosmetic = ["Cosmetic mods"];

        private static readonly string[] new_then_old = ["new", "old-instance"];

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
        public void TheInstancesFolderItselfIsNotDeleted()
        {
            store.Create("one");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(() => store.Delete(new LazerInstance(store.Root)), Throws.InvalidOperationException);
                Assert.That(store.List(), Has.Count.EqualTo(1));
            }
        }
    }
}

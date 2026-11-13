using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Torii.Core.Mods;

namespace Torii.Tests
{
    [TestFixture]
    public class ModOrderTests
    {
        private static readonly string[] b_then_a = ["b.two", "a.one"];
        private static readonly string[] b_a_c = ["b.two", "a.one", "c.three"];
        private static readonly string[] a_b_c_d = ["a.one", "b.two", "c.three", "d.four"];
        private static readonly string[] d_then_unlisted = ["d.four", "a.one", "b.two", "c.three"];
        private static readonly string[] only_d = ["d.four"];
        private static readonly string[] upper_b = ["B.TWO"];

        private string directory = null!;
        private string file = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            file = Path.Combine(directory, "torii-mod-order.txt");
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, recursive: true);

        [Test]
        public void MissingFileMeansNoOrder()
        {
            Assert.That(ModOrder.Load(file), Is.Null);
        }

        [Test]
        public void SavedNamesComeBackInOrder()
        {
            ModOrder.Save(file, b_then_a);

            Assert.That(ModOrder.Load(file), Is.EqualTo(b_then_a));
        }

        [Test]
        public void BlankLinesAndSpacesAreIgnoredAndAnEmptyFileMeansNoOrder()
        {
            File.WriteAllText(file, $"  b.two  {Environment.NewLine}{Environment.NewLine}   {Environment.NewLine}a.one{Environment.NewLine}");
            string[]? loaded = ModOrder.Load(file)?.ToArray();

            File.WriteAllText(file, $"{Environment.NewLine}  {Environment.NewLine}");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(loaded, Is.EqualTo(b_then_a));
                Assert.That(ModOrder.Load(file), Is.Null);
            }
        }

        [Test]
        public void SavingLeavesNoTemporaryFileAndReplacesWhatWasThere()
        {
            ModOrder.Save(file, b_then_a);
            ModOrder.Save(file, b_a_c);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ModOrder.Load(file), Is.EqualTo(b_a_c));
                Assert.That(File.Exists(file + ".tmp"), Is.False);
            }
        }

        [Test]
        public void ModsFollowTheSavedOrderAndUnlistedOnesComeLastInTheirOwnOrder()
        {
            InstalledMod[] mods = [.. a_b_c_d.Select(installed)];

            var ordered = ModOrder.Apply(mods, only_d);

            Assert.That(ordered.Select(m => Path.GetFileName(m.Directory)), Is.EqualTo(d_then_unlisted));
        }

        [Test]
        public void WithoutASavedOrderTheListIsUntouched()
        {
            InstalledMod[] mods = [.. a_b_c_d.Select(installed)];

            Assert.That(ModOrder.Apply(mods, null), Is.SameAs(mods));
        }

        [Test]
        public void FolderNamesAreMatchedIgnoringCase()
        {
            InstalledMod[] mods = [installed("a.one"), installed("b.two")];

            var ordered = ModOrder.Apply(mods, upper_b);

            Assert.That(ordered.Select(m => Path.GetFileName(m.Directory)), Is.EqualTo(b_then_a));
        }

        private InstalledMod installed(string folder) => new InstalledMod(Path.Combine(directory, folder), null, ModState.On, null);
    }
}

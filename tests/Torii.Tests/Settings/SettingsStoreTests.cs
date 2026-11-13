using System;
using System.IO;
using NUnit.Framework;
using Torii.Core.Settings;

namespace Torii.Tests
{
    [TestFixture]
    public class SettingsStoreTests
    {
        private string directory = null!;
        private string file = null!;
        private SettingsStore store = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            file = Path.Combine(directory, "settings.json");
            store = new SettingsStore(file);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, recursive: true);

        [Test]
        public void MissingFileGivesDefaults()
        {
            Assert.That(store.Load(), Is.EqualTo(new AppSettings()));
        }

        [Test]
        public void SavedSettingsComeBack()
        {
            var settings = new AppSettings(Path.Combine(directory, "instances", "alpha"), 900, 600.5);

            bool saved = store.Save(settings);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(saved, Is.True);
                Assert.That(store.Load(), Is.EqualTo(settings));
            }
        }

        [Test]
        public void OnlyWhatWasSetIsWritten()
        {
            store.Save(new AppSettings("somewhere"));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(store.Load(), Is.EqualTo(new AppSettings("somewhere")));
                Assert.That(File.ReadAllText(file), Does.Not.Contain("windowWidth"));
            }
        }

        [TestCase("not json at all")]
        [TestCase("")]
        [TestCase("[1, 2, 3]")]
        [TestCase("\"just a string\"")]
        [TestCase("{ \"selectedInstance\": ")]
        public void UnreadableFileGivesDefaults(string content)
        {
            File.WriteAllText(file, content);

            Assert.That(store.Load(), Is.EqualTo(new AppSettings()));
        }

        [Test]
        public void ValuesOfTheWrongTypeOrSizeAreIgnored()
        {
            File.WriteAllText(file, """{ "selectedInstance": 5, "windowWidth": "wide", "windowHeight": -3 }""");

            Assert.That(store.Load(), Is.EqualTo(new AppSettings()));
        }

        [Test]
        public void EmptyInstanceAndZeroSizeAreIgnored()
        {
            File.WriteAllText(file, """{ "selectedInstance": "", "windowWidth": 0, "windowHeight": 480 }""");

            Assert.That(store.Load(), Is.EqualTo(new AppSettings(null, null, 480)));
        }

        [Test]
        public void GoodValuesSurviveBadNeighbours()
        {
            File.WriteAllText(file, """{ "selectedInstance": "kept", "windowWidth": "wide", "somethingElse": [1, 2] }""");

            Assert.That(store.Load(), Is.EqualTo(new AppSettings("kept")));
        }

        [Test]
        public void SavingCreatesTheFolderAndLeavesNoTemporaryFile()
        {
            var nested = new SettingsStore(Path.Combine(directory, "deeper", "still", "settings.json"));

            bool saved = nested.Save(new AppSettings("x"));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(saved, Is.True);
                Assert.That(File.Exists(nested.FilePath), Is.True);
                Assert.That(File.Exists(nested.FilePath + ".tmp"), Is.False);
            }
        }

        [Test]
        public void SavingOverwritesWhatWasThere()
        {
            store.Save(new AppSettings("first", 800, 500));
            store.Save(new AppSettings("second"));

            Assert.That(store.Load(), Is.EqualTo(new AppSettings("second")));
        }

        [Test]
        public void FailingToSaveIsReportedNotThrown()
        {
            Directory.CreateDirectory(file);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(store.Save(new AppSettings("x")), Is.False);
                Assert.That(store.Load(), Is.EqualTo(new AppSettings()));
            }
        }

        [Test]
        public void DefaultFileIsInTheToriiFolder()
        {
            Assert.That(SettingsStore.DefaultFile(), Does.EndWith(Path.Combine("Torii", "settings.json")));
        }
    }
}

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Torii.Core.Lazer;
using Torii.Core.Mods;
using Torii.ViewModels;

namespace Torii.Tests
{
    [TestFixture]
    public class ModListViewModelTests
    {
        private static readonly string[] expected_labels = ["Patcher", "Plain"];

        private string directory = null!;
        private LazerInstance instance = null!;
        private bool osuRunning;
        private string? status;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            instance = new LazerInstance(Path.Combine(directory, "instance"));
            osuRunning = false;
            status = null;
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(directory, recursive: true);
        }

        [Test]
        public void ModsAreListed()
        {
            mod("b.plain", "Plain", author: "", capabilities: "\"Cosmetic\"");
            mod("a.patcher", "Patcher", author: "someone", capabilities: "\"Patches\"");

            ModListViewModel list = create();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(list.HasMods, Is.True);
                Assert.That(list.Mods.Select(m => m.Label), Is.EqualTo(expected_labels));
                Assert.That(list.Mods[0].Byline, Is.EqualTo("by someone"));
                Assert.That(list.Mods[0].Patches, Is.True);
                Assert.That(list.Mods[1].Byline, Is.Empty);
                Assert.That(list.Mods[1].Patches, Is.False);
                Assert.That(list.Mods.Select(m => m.IsOn), Has.All.True);
            }
        }

        [Test]
        public void SwitchingAModOffAndOn()
        {
            mod("a.mod", "Cool Mod");
            ModListViewModel list = create();
            ModItemViewModel row = list.Mods[0];

            row.IsOn = false;
            bool off = row.IsOn;
            string? offStatus = status;

            row.IsOn = true;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(off, Is.False);
                Assert.That(offStatus, Is.EqualTo("Cool Mod is off the next time osu! starts."));
                Assert.That(row.IsOn, Is.True);
                Assert.That(new ModLibrary(instance.Omamori, () => false).List()[0].State, Is.EqualTo(ModState.On));
                Assert.That(list.Mods[0], Is.SameAs(row));
            }
        }

        [Test]
        public void SwitchingAModWhileTheInstanceRunsIsRefusedAndTheSwitchGoesBack()
        {
            mod("a.mod", "Cool Mod");
            ModListViewModel list = create();
            ModItemViewModel row = list.Mods[0];
            bool reverted = false;
            row.PropertyChanged += (_, e) => reverted |= e.PropertyName == nameof(ModItemViewModel.IsOn);

            osuRunning = true;
            row.IsOn = false;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(row.IsOn, Is.True);
                Assert.That(reverted, Is.True);
                Assert.That(status, Does.StartWith("Couldn't turn Cool Mod off."));
                Assert.That(File.Exists(instance.Omamori.SettingsFile), Is.False);
            }
        }

        [Test]
        public void BrokenModCannotBeSwitched()
        {
            string folder = Path.Combine(instance.Omamori.ModsDirectory, "broken");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "mod.json"), "not json");

            ModItemViewModel row = create().Mods[0];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(row.CanToggle, Is.False);
                Assert.That(row.IsOn, Is.False);
                Assert.That(row.Label, Is.EqualTo("broken"));
                Assert.That(row.Problem, Is.Not.Empty);
            }
        }

        [Test]
        public void ModsAddedByHandShowUpOnRefresh()
        {
            ModListViewModel list = create();

            mod("a.mod", "Cool Mod");
            list.Refresh();

            Assert.That(list.Mods, Has.Count.EqualTo(1));
        }

        [Test]
        public void RemovingAModTakesItOffTheList()
        {
            mod("a.mod", "Cool Mod");
            ModListViewModel list = create();

            list.Mods[0].RemoveCommand.Execute(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(list.Mods, Is.Empty);
                Assert.That(Directory.Exists(Path.Combine(instance.Omamori.ModsDirectory, "a.mod")), Is.False);
                Assert.That(status, Is.EqualTo("Cool Mod is removed. Its settings were kept."));
            }
        }

        [Test]
        public void RemovingAModWhileTheInstanceRunsIsRefused()
        {
            mod("a.mod", "Cool Mod");
            ModListViewModel list = create();

            osuRunning = true;
            list.Mods[0].RemoveCommand.Execute(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(list.Mods, Has.Count.EqualTo(1));
                Assert.That(status, Does.StartWith("Couldn't remove Cool Mod."));
            }
        }

        [Test]
        public void FailuresGoToTheProblemCallbackWhenThereIsOne()
        {
            mod("a.mod", "Cool Mod");
            string? problem = null;
            var list = new ModListViewModel(new ModLibrary(instance.Omamori, () => osuRunning), message => status = message, message => problem = message);

            osuRunning = true;
            list.Mods[0].RemoveCommand.Execute(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(problem, Does.StartWith("Couldn't remove Cool Mod."));
                Assert.That(status, Is.Null);
            }
        }

        [Test]
        public void OnlyACrashedModCanBeTriedAgain()
        {
            mod("a.crashed", "Crashed");
            mod("b.fine", "Fine");
            crash("a.crashed");

            ModListViewModel list = create();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(list.Mods[0].CanRetry, Is.True);
                Assert.That(list.Mods[0].Problem, Does.StartWith("Crashed in 2 sessions"));
                Assert.That(list.Mods[1].CanRetry, Is.False);
            }
        }

        [Test]
        public void TryingACrashedModAgainClearsItsProblem()
        {
            mod("a.crashed", "Crashed");
            crash("a.crashed");
            ModListViewModel list = create();

            list.Mods[0].RetryCommand.Execute(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(list.Mods[0].CanRetry, Is.False);
                Assert.That(list.Mods[0].Problem, Is.Empty);
                Assert.That(status, Is.EqualTo("Crashed is tried again the next time osu! starts."));
            }
        }

        private ModListViewModel create() => new ModListViewModel(new ModLibrary(instance.Omamori, () => osuRunning), message => status = message);

        private void crash(string id)
        {
            File.WriteAllText(instance.Omamori.FaultsFile, $$"""
                { "session": 7, "mods": { "{{id}}": { "version": "1.0.0", "strikes": 2, "lastSession": 7, "lastError": "boom" } } }
                """);
        }

        private void mod(string id, string name, string author = "", string capabilities = "\"Cosmetic\"")
        {
            string folder = Path.Combine(instance.Omamori.ModsDirectory, id);
            Directory.CreateDirectory(folder);

            File.WriteAllText(Path.Combine(folder, "Mod.dll"), string.Empty);
            File.WriteAllText(Path.Combine(folder, "mod.json"), $$"""
                { "id": "{{id}}", "name": "{{name}}", "version": "1.0.0", "author": "{{author}}", "entry": "Mod.dll", "apiVersion": "0.2", "capabilities": [{{capabilities}}] }
                """);
        }
    }
}

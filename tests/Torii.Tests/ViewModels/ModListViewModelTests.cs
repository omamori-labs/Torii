using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        private static readonly string[] only_wobbly = ["Wobbly"];
        private static readonly string[] fine_and_wobbly = ["Fine", "Wobbly"];
        private static readonly string[] three_one_two = ["Three", "One", "Two"];
        private static readonly string[] folders_c_a_b = ["c.three", "a.one", "b.two"];
        private static readonly string[] two_one_newest = ["Two", "One", "Newest"];
        private static readonly string[] folders_b_a_new = ["b.two", "a.one", "0.new"];
        private static readonly string[] only_folder_a = ["a.one"];
        private static readonly string[] one_two = ["One", "Two"];
        private static readonly string[] two_one = ["Two", "One"];
        private static readonly string[] running_only = ["Running"];
        private static readonly string[] stopped_only = ["Stopped"];
        private static readonly string[] crashed_only = ["Crashed"];
        private static readonly string[] snowman_only = ["Snowman"];
        private static readonly string[] snow_only = ["Snowfall"];
        private static readonly string[] trail_only = ["Cursor Trail"];
        private static readonly string[] two_snow = ["Snowfall", "Snowman"];

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
        public void SeveralZipsAreInstalledTogether()
        {
            ModListViewModel list = create();

            list.AddFromZips([modZip("a.one", "One"), modZip("b.two", "Two")]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(list.Mods, Has.Count.EqualTo(2));
                Assert.That(status, Is.Null);
            }
        }

        [Test]
        public void OneZipIsInstalledWithoutAMessage()
        {
            ModListViewModel list = create();

            list.AddFromZips([modZip("a.one", "One")]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(list.Mods.Single().Label, Is.EqualTo("One"));
                Assert.That(status, Is.Null);
            }
        }

        [Test]
        public void BadZipInABatchIsReportedAlongsideTheGoodOnes()
        {
            var problems = new List<string>();
            var list = new ModListViewModel(new ModLibrary(instance.Omamori, () => osuRunning), message => status = message, problems.Add);

            list.AddFromZips([modZip("a.one", "One"), notAMod("bad.zip")]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(list.Mods.Single().Label, Is.EqualTo("One"));
                Assert.That(status, Is.Null);
                Assert.That(problems, Has.Count.EqualTo(1));
                Assert.That(problems[0], Does.StartWith("Couldn't add 1 of 2 mods.").And.Contain("bad.zip"));
            }
        }

        [Test]
        public void ABatchOfBadZipsIsReportedInOneMessage()
        {
            var problems = new List<string>();
            var list = new ModListViewModel(new ModLibrary(instance.Omamori, () => osuRunning), message => status = message, problems.Add);

            list.AddFromZips([notAMod("one.zip"), notAMod("two.zip")]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(list.Mods, Is.Empty);
                Assert.That(status, Is.Null);
                Assert.That(problems, Has.Count.EqualTo(1));
                Assert.That(problems[0], Does.StartWith("Couldn't add 2 of 2 mods.").And.Contain("one.zip").And.Contain("two.zip"));
            }
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
        public void MovingAModReordersItAndWritesTheOrderFile()
        {
            mod("a.one", "One");
            mod("b.two", "Two");
            mod("c.three", "Three");
            ModListViewModel list = createOrdered();

            list.Move(list.Mods[2], 0);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(list.Mods.Select(m => m.Label), Is.EqualTo(three_one_two));
                Assert.That(list.Visible.Select(m => m.Label), Is.EqualTo(three_one_two));
                Assert.That(File.ReadAllLines(order_file()), Is.EqualTo(folders_c_a_b));
            }
        }

        [Test]
        public void NoOrderFileIsWrittenUntilAModIsMoved()
        {
            mod("a.one", "One");
            mod("b.two", "Two");

            createOrdered();

            Assert.That(File.Exists(order_file()), Is.False);
        }

        [Test]
        public void SavedOrderIsAppliedWhenTheListOpens()
        {
            mod("a.one", "One");
            mod("b.two", "Two");
            mod("c.three", "Three");
            File.WriteAllLines(order_file(), folders_c_a_b);

            ModListViewModel list = createOrdered();

            Assert.That(list.Mods.Select(m => m.Label), Is.EqualTo(three_one_two));
        }

        [Test]
        public void NewModsGoLastOnceThereIsAnOrderAndAreSaved()
        {
            mod("a.one", "One");
            mod("b.two", "Two");
            ModListViewModel list = createOrdered();
            list.Move(list.Mods[1], 0);

            mod("0.new", "Newest");
            list.Refresh();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(list.Mods.Select(m => m.Label), Is.EqualTo(two_one_newest));
                Assert.That(File.ReadAllLines(order_file()), Is.EqualTo(folders_b_a_new));
            }
        }

        [Test]
        public void RemovedModsLeaveTheOrderFile()
        {
            mod("a.one", "One");
            mod("b.two", "Two");
            ModListViewModel list = createOrdered();
            list.Move(list.Mods[1], 0);

            list.Mods[0].RemoveCommand.Execute(null);

            Assert.That(File.ReadAllLines(order_file()), Is.EqualTo(only_folder_a));
        }

        [Test]
        public void ReorderingIsRefusedWhileSearchingOrFiltering()
        {
            mod("a.one", "One");
            mod("b.two", "Two");
            ModListViewModel list = createOrdered();

            list.Search = "o";
            bool whileSearching = list.CanReorder;
            list.Move(list.Mods[1], 0);
            list.Search = string.Empty;

            list.Filter = ModFilter.On;
            bool whileFiltering = list.CanReorder;
            list.Move(list.Mods[1], 0);
            list.Filter = ModFilter.All;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(whileSearching, Is.False);
                Assert.That(whileFiltering, Is.False);
                Assert.That(list.CanReorder, Is.True);
                Assert.That(list.Mods.Select(m => m.Label), Is.EqualTo(one_two));
                Assert.That(File.Exists(order_file()), Is.False);
            }
        }

        [Test]
        public void MovingPastTheEndsStaysInsideTheList()
        {
            mod("a.one", "One");
            mod("b.two", "Two");
            ModListViewModel list = createOrdered();

            list.Move(list.Mods[0], 99);

            Assert.That(list.Mods.Select(m => m.Label), Is.EqualTo(two_one));
        }

        [Test]
        public void FailingToSaveTheOrderIsReportedAndTheListStillMoves()
        {
            mod("a.one", "One");
            mod("b.two", "Two");
            var problems = new List<string>();
            var list = new ModListViewModel(new ModLibrary(instance.Omamori, () => osuRunning), message => status = message, problems.Add, orderFile: Path.Combine(directory, "missing", "order.txt"));

            list.Move(list.Mods[1], 0);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(problems.Single(), Does.StartWith("Couldn't save the mod order."));
                Assert.That(list.Mods.Select(m => m.Label), Is.EqualTo(two_one));
            }
        }

        [Test]
        public void SearchKeepsOnlyMatchingMods()
        {
            mod("a.snow", "Snowfall", author: "Mika");
            mod("b.trail", "Cursor Trail", author: "Jun");
            ModListViewModel list = create();

            list.Search = "snow";
            string[] byName = [.. list.Visible.Select(m => m.Label)];

            list.Search = "jun";
            string[] byAuthor = [.. list.Visible.Select(m => m.Label)];

            list.Search = "B.TRAIL";
            string[] byId = [.. list.Visible.Select(m => m.Label)];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(byName, Is.EqualTo(snow_only));
                Assert.That(byAuthor, Is.EqualTo(trail_only));
                Assert.That(byId, Is.EqualTo(trail_only));
                Assert.That(list.Mods, Has.Count.EqualTo(2));
            }
        }

        [Test]
        public void StateFilterShowsOnlyThatState()
        {
            mod("a.on", "Running");
            mod("b.off", "Stopped");
            mod("c.crashed", "Crashed");
            crash("c.crashed");
            ModListViewModel list = create();
            list.Mods.Single(m => m.Label == "Stopped").IsOn = false;

            list.Filter = ModFilter.On;
            string[] on = [.. list.Visible.Select(m => m.Label)];

            list.Filter = ModFilter.Off;
            string[] off = [.. list.Visible.Select(m => m.Label)];

            list.Filter = ModFilter.Problems;
            string[] problems = [.. list.Visible.Select(m => m.Label)];

            list.Filter = ModFilter.All;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(on, Is.EqualTo(running_only));
                Assert.That(off, Is.EqualTo(stopped_only));
                Assert.That(problems, Is.EqualTo(crashed_only));
                Assert.That(list.Visible, Has.Count.EqualTo(3));
            }
        }

        [Test]
        public void ModThatCrashedOnceShowsAWarningStaysOnAndCountsAsAProblem()
        {
            mod("a.fine", "Fine");
            mod("b.wobbly", "Wobbly");
            crash("b.wobbly", strikes: 1);
            ModListViewModel list = create();
            ModItemViewModel wobbly = list.Mods[1];

            list.Filter = ModFilter.Problems;
            string[] problems = [.. list.Visible.Select(m => m.Label)];

            list.Filter = ModFilter.On;
            string[] on = [.. list.Visible.Select(m => m.Label)];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(wobbly.Problem, Does.StartWith("Crashed the last time osu! ran"));
                Assert.That(wobbly.IsOn, Is.True);
                Assert.That(wobbly.CanToggle, Is.True);
                Assert.That(wobbly.CanRetry, Is.False);
                Assert.That(problems, Is.EqualTo(only_wobbly));
                Assert.That(on, Is.EqualTo(fine_and_wobbly));
            }
        }

        [Test]
        public void StateFilterAndSearchCombine()
        {
            mod("a.snow", "Snowfall");
            mod("b.snowman", "Snowman");
            ModListViewModel list = create();
            list.Mods.Single(m => m.Label == "Snowman").IsOn = false;

            list.Filter = ModFilter.Off;
            list.Search = "snow";

            Assert.That(list.Visible.Select(m => m.Label), Is.EqualTo(snowman_only));
        }

        [Test]
        public void SwitchingAModOffTakesItOutOfTheOnFilter()
        {
            mod("a.snow", "Snowfall");
            ModListViewModel list = create();
            list.Filter = ModFilter.On;

            list.Mods[0].IsOn = false;

            Assert.That(list.NoMatches, Is.True);
        }

        [Test]
        public void ClearingTheSearchShowsEveryMod()
        {
            mod("a.snow", "Snowfall");
            mod("b.trail", "Cursor Trail");
            ModListViewModel list = create();

            list.Search = "snow";
            list.Search = "  ";

            Assert.That(list.Visible, Has.Count.EqualTo(2));
        }

        [Test]
        public void SearchWithNoMatchSaysSo()
        {
            mod("a.snow", "Snowfall");
            ModListViewModel list = create();

            list.Search = "zzz";

            using (Assert.EnterMultipleScope())
            {
                Assert.That(list.Visible, Is.Empty);
                Assert.That(list.NoMatches, Is.True);
                Assert.That(list.HasMods, Is.True);
            }
        }

        [Test]
        public void ModsAddedWhileSearchingAreFiltered()
        {
            mod("a.snow", "Snowfall");
            ModListViewModel list = create();
            list.Search = "snow";

            mod("b.snowman", "Snowman");
            mod("c.trail", "Cursor Trail");
            list.Refresh();

            Assert.That(list.Visible.Select(m => m.Label), Is.EqualTo(two_snow));
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
            bool offOnDisk = new ModLibrary(instance.Omamori, () => false).List()[0].State == ModState.Off;

            row.IsOn = true;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(off, Is.False);
                Assert.That(offOnDisk, Is.True);
                Assert.That(offStatus, Is.Null);
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
                Assert.That(status, Is.Null);
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
                Assert.That(status, Is.Null);
            }
        }

        [Test]
        public void ModWithAnUpdateShowsTheNewVersion()
        {
            mod("a.one", "One");
            mod("a.two", "Two");
            ModListViewModel list = create();

            list.SetUpdates(new Dictionary<string, string> { ["a.one"] = "2.0.0" });

            using (Assert.EnterMultipleScope())
            {
                Assert.That(list.Mods[0].HasUpdate, Is.True);
                Assert.That(list.Mods[0].UpdateLabel, Is.EqualTo("Update to 2.0.0"));
                Assert.That(list.Mods[1].HasUpdate, Is.False);
            }
        }

        [Test]
        public void UpdatesSurviveARefresh()
        {
            mod("a.one", "One");
            ModListViewModel list = create();
            list.SetUpdates(new Dictionary<string, string> { ["a.one"] = "2.0.0" });

            list.Refresh();

            Assert.That(list.Mods[0].HasUpdate, Is.True);
        }

        [Test]
        public void ClearingTheUpdatesRemovesThem()
        {
            mod("a.one", "One");
            ModListViewModel list = create();
            list.SetUpdates(new Dictionary<string, string> { ["a.one"] = "2.0.0" });

            list.SetUpdates(new Dictionary<string, string>());

            Assert.That(list.Mods[0].HasUpdate, Is.False);
        }

        [Test]
        public async Task UpdatingAsksForTheModsId()
        {
            mod("a.one", "One");
            ModListViewModel list = create();
            string? requested = null;
            list.RequestUpdate = id =>
            {
                requested = id;
                return Task.CompletedTask;
            };

            await list.Mods[0].UpdateCommand.ExecuteAsync(null);

            Assert.That(requested, Is.EqualTo("a.one"));
        }

        private string modZip(string id, string name)
        {
            string path = Path.Combine(directory, $"{id}.zip");

            File.WriteAllBytes(path, Fixtures.Zip(
                ($"{id}/Mod.dll", []),
                ($"{id}/mod.json", Encoding.UTF8.GetBytes($$"""
                    { "id": "{{id}}", "name": "{{name}}", "version": "1.0.0", "entry": "Mod.dll", "apiVersion": "0.2", "capabilities": ["Cosmetic"] }
                    """))));

            return path;
        }

        private string notAMod(string fileName)
        {
            string path = Path.Combine(directory, fileName);
            File.WriteAllBytes(path, Fixtures.Zip(("readme.txt", "hi"u8.ToArray())));

            return path;
        }

        private string order_file() => Path.Combine(directory, "torii-mod-order.txt");

        private ModListViewModel createOrdered() =>
            new ModListViewModel(new ModLibrary(instance.Omamori, () => osuRunning), message => status = message, orderFile: order_file());

        private ModListViewModel create() => new ModListViewModel(new ModLibrary(instance.Omamori, () => osuRunning), message => status = message);

        private void crash(string id, int strikes = 2)
        {
            File.WriteAllText(instance.Omamori.FaultsFile, $$"""
                { "session": 7, "mods": { "{{id}}": { "version": "1.0.0", "strikes": {{strikes}}, "lastSession": 7, "lastError": "boom" } } }
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

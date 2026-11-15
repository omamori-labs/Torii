using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Torii.Core;
using Torii.Core.Instances;
using Torii.Core.Lazer;
using Torii.Core.Library;
using Torii.Core.Releases;
using Torii.ViewModels;

namespace Torii.Tests
{
    [TestFixture]
    public class MainViewModelTests
    {
        private static readonly string[] alpha_beta = ["alpha", "beta"];

        private static readonly string[] alpha_gamma = ["alpha", "gamma"];

        private static readonly string[] beta_zzz = ["beta", "zzz"];

        private static readonly string[] alpha_renamed = ["alpha", "renamed"];

        private static readonly string[] only_alpha = ["alpha"];

        private static readonly string[] alpha_mid_zed = ["alpha", "mid", "zed"];

        private static readonly string[] alpha_and_copy = ["alpha", "alpha copy"];

        private static readonly string[] gamma_alpha_beta_delta = ["gamma", "alpha", "beta", "delta"];

        private static readonly string[] gamma_alpha_beta = ["gamma", "alpha", "beta"];

        private static readonly string[] beta_alpha = ["beta", "alpha"];

        private static readonly string[] beta_alpha_aaa = ["beta", "alpha", "aaa"];

        private static readonly string[] zzz_alpha = ["zzz", "alpha"];

        private static readonly bool[] off_then_on = [false, true];

        private string directory = null!;
        private InstanceStore store = null!;
        private RouteHandler handler = null!;
        private HttpClient http = null!;
        private ReleaseFeed releases = null!;
        private SharedLibrary library = null!;
        private bool storeSeesGameRunning;
        private bool gameRunning;
        private List<string> problems = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            storeSeesGameRunning = false;
            gameRunning = false;
            problems = [];
            store = new InstanceStore(Path.Combine(directory, "instances"), Path.Combine(directory, "old-instance"), _ => storeSeesGameRunning);
            handler = new RouteHandler();
            http = new HttpClient(handler);
            releases = new ReleaseFeed(http);
            library = new SharedLibrary(Path.Combine(directory, "library"), (_, _) => true, () => false, _ => null);
        }

        [TearDown]
        public void TearDown()
        {
            http.Dispose();
            handler.Dispose();
            Directory.Delete(directory, recursive: true);
        }

        [Test]
        public void StartsWithItsInstancesInOrderAndSelectsTheFirst()
        {
            store.Create("beta");
            store.Create("alpha");

            MainViewModel model = create();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.Instances.Select(i => i.Name), Is.EqualTo(alpha_beta));
                Assert.That(model.Selected!.Name, Is.EqualTo("alpha"));
                Assert.That(model.HasInstances, Is.True);
            }
        }

        [Test]
        public void SavedOrderIsAppliedAndUnlistedInstancesComeLastByName()
        {
            LazerInstance alpha = store.Create("alpha");
            store.Create("beta");
            LazerInstance gamma = store.Create("gamma");
            store.Create("delta");

            MainViewModel model = create(order: [gamma.Directory, alpha.Directory]);

            Assert.That(model.Instances.Select(i => i.Name), Is.EqualTo(gamma_alpha_beta_delta));
        }

        [Test]
        public void OrderEntriesForInstancesThatAreGoneAreIgnored()
        {
            LazerInstance alpha = store.Create("alpha");
            store.Create("beta");

            MainViewModel model = create(order: [Path.Combine(directory, "instances", "gone"), alpha.Directory]);

            Assert.That(model.Instances.Select(i => i.Name), Is.EqualTo(alpha_beta));
        }

        [Test]
        public void MovingAnInstanceReordersItAndSavesTheOrder()
        {
            store.Create("alpha");
            store.Create("beta");
            store.Create("gamma");
            MainViewModel model = create();
            IReadOnlyList<string>? saved = null;
            model.RememberOrder = order => saved = order;

            model.Move(model.Instances[2], 0);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.Instances.Select(i => i.Name), Is.EqualTo(gamma_alpha_beta));
                Assert.That(saved!.Select(Path.GetFileName), Is.EqualTo(gamma_alpha_beta));
            }
        }

        [Test]
        public void MovingPastTheEndsStaysInsideTheList()
        {
            store.Create("alpha");
            store.Create("beta");
            MainViewModel model = create();

            model.Move(model.Instances[0], 99);

            Assert.That(model.Instances.Select(i => i.Name), Is.EqualTo(beta_alpha));
        }

        [Test]
        public void NothingIsSavedUntilTheOrderIsChanged()
        {
            store.Create("alpha");
            MainViewModel model = create();
            int saves = 0;
            model.RememberOrder = _ => saves++;
            model.AskName = _ => Task.FromResult<string?>("beta");

            model.NewInstanceCommand.Execute(null);

            Assert.That(saves, Is.Zero);
        }

        [Test]
        public async Task NewInstanceGoesLastOnceThereIsAnOrder()
        {
            LazerInstance alpha = store.Create("alpha");
            LazerInstance beta = store.Create("beta");
            MainViewModel model = create(order: [beta.Directory, alpha.Directory]);
            IReadOnlyList<string>? saved = null;
            model.RememberOrder = order => saved = order;
            model.AskName = _ => Task.FromResult<string?>("aaa");

            await model.NewInstanceCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.Instances.Select(i => i.Name), Is.EqualTo(beta_alpha_aaa));
                Assert.That(saved!.Select(Path.GetFileName), Is.EqualTo(beta_alpha_aaa));
            }
        }

        [Test]
        public async Task RenamingKeepsThePositionInACustomOrder()
        {
            LazerInstance alpha = store.Create("alpha");
            LazerInstance beta = store.Create("beta");
            MainViewModel model = create(order: [beta.Directory, alpha.Directory]);
            IReadOnlyList<string>? saved = null;
            model.RememberOrder = order => saved = order;
            model.AskName = _ => Task.FromResult<string?>("zzz");

            await model.Instances[0].RenameCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.Instances.Select(i => i.Name), Is.EqualTo(zzz_alpha));
                Assert.That(saved!.Select(Path.GetFileName), Is.EqualTo(zzz_alpha));
            }
        }

        [Test]
        public async Task DeletingDropsTheInstanceFromTheSavedOrder()
        {
            LazerInstance alpha = store.Create("alpha");
            LazerInstance beta = store.Create("beta");
            MainViewModel model = create(order: [beta.Directory, alpha.Directory]);
            IReadOnlyList<string>? saved = null;
            model.RememberOrder = order => saved = order;

            await model.Instances[0].DeleteCommand.ExecuteAsync(null);

            Assert.That(saved!.Select(Path.GetFileName), Is.EqualTo(only_alpha));
        }

        [Test]
        public void WithoutInstancesNothingIsSelected()
        {
            MainViewModel model = create();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.HasInstances, Is.False);
                Assert.That(model.Selected, Is.Null);
            }
        }

        [Test]
        public void FolderFromTheCommandLineIsListedSelectedAndNotDeletable()
        {
            store.Create("alpha");
            var given = new LazerInstance(Path.Combine(directory, "somewhere", "testing"));

            MainViewModel model = create(given);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.Instances, Has.Count.EqualTo(2));
                Assert.That(model.Selected!.Instance.Directory, Is.EqualTo(given.Directory));
                Assert.That(model.Selected.DeleteCommand.CanExecute(null), Is.False);
                Assert.That(model.Selected.RenameCommand.CanExecute(null), Is.False);
            }
        }

        [Test]
        public void FolderFromTheCommandLineThatIsAlreadyListedIsNotAddedTwice()
        {
            store.Create("alpha");
            LazerInstance second = store.Create("beta");

            MainViewModel model = create(new LazerInstance(second.Directory));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.Instances, Has.Count.EqualTo(2));
                Assert.That(model.Selected!.Name, Is.EqualTo("beta"));
            }
        }

        [Test]
        public async Task NewInstanceIsNamedCreatedListedInOrderAndSelected()
        {
            store.Create("alpha");
            store.Create("zed");
            MainViewModel model = create();
            model.AskName = _ => Task.FromResult<string?>("mid");

            await model.NewInstanceCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.Instances.Select(i => i.Name), Is.EqualTo(alpha_mid_zed));
                Assert.That(model.Selected!.Name, Is.EqualTo("mid"));
                Assert.That(Directory.Exists(model.Selected.Instance.Directory), Is.True);
                Assert.That(problems, Is.Empty);
            }
        }

        [Test]
        public async Task DialogIsGivenTheStoresNamingRules()
        {
            store.Create("alpha");
            MainViewModel model = create();
            var verdicts = new List<string?>();
            NamePrompt? asked = null;
            model.AskName = prompt =>
            {
                asked = prompt;
                verdicts.Add(prompt.Validate("a/b"));
                verdicts.Add(prompt.Validate("ALPHA"));
                verdicts.Add(prompt.Validate("fine"));
                return Task.FromResult<string?>(null);
            };

            await model.NewInstanceCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(asked!.Title, Is.EqualTo("New instance"));
                Assert.That(asked.Confirm, Is.EqualTo("Create"));
                Assert.That(asked.Initial, Is.Empty);
                Assert.That(verdicts[0], Is.Not.Null);
                Assert.That(verdicts[1], Does.Contain("already"));
                Assert.That(verdicts[2], Is.Null);
            }
        }

        [Test]
        public async Task CancellingTheDialogChangesNothing()
        {
            MainViewModel model = create();
            model.AskName = _ => Task.FromResult<string?>(null);

            await model.NewInstanceCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.Instances, Is.Empty);
                Assert.That(store.List(), Is.Empty);
                Assert.That(problems, Is.Empty);
            }
        }

        [Test]
        public async Task WithoutADialogNothingHappens()
        {
            MainViewModel model = create();

            await model.NewInstanceCommand.ExecuteAsync(null);

            Assert.That(model.Instances, Is.Empty);
        }

        [Test]
        public async Task NameTheStoreRefusesIsReportedAndNothingIsCreated()
        {
            MainViewModel model = create();
            model.AskName = _ => Task.FromResult<string?>("con");

            await model.NewInstanceCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(problems.Single(), Does.StartWith("Couldn't create con."));
                Assert.That(model.Instances, Is.Empty);
                Assert.That(store.List(), Is.Empty);
            }
        }

        [Test]
        public async Task DeletingRemovesTheInstanceAtOnceAndSelectsTheNextOne()
        {
            store.Create("alpha");
            LazerInstance middle = store.Create("beta");
            store.Create("gamma");
            MainViewModel model = create();
            model.Selected = model.Instances[1];

            await model.Selected!.DeleteCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Directory.Exists(middle.Directory), Is.False);
                Assert.That(model.Instances.Select(i => i.Name), Is.EqualTo(alpha_gamma));
                Assert.That(model.Selected!.Name, Is.EqualTo("gamma"));
                Assert.That(problems, Is.Empty);
            }
        }

        [Test]
        public async Task DeletingTheLastInstanceLeavesNothingSelected()
        {
            store.Create("only");
            MainViewModel model = create();

            await model.Selected!.DeleteCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.Selected, Is.Null);
                Assert.That(model.HasInstances, Is.False);
            }
        }

        [Test]
        public void DeleteIsNotOfferedWhileTheGameRuns()
        {
            store.Create("alpha");
            gameRunning = true;

            InstanceViewModel card = create().Selected!;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(card.DeleteCommand.CanExecute(null), Is.False);
                Assert.That(card.RenameCommand.CanExecute(null), Is.False);
                Assert.That(card.OpenFolderCommand.CanExecute(null), Is.True);
            }
        }

        [Test]
        public async Task DeleteThatTheStoreRefusesSaysWhyAndKeepsTheInstance()
        {
            store.Create("alpha");
            MainViewModel model = create();
            storeSeesGameRunning = true;

            await model.Selected!.DeleteCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(problems.Single(), Does.StartWith("Couldn't delete alpha.").And.Contains("Close osu!"));
                Assert.That(model.Instances, Has.Count.EqualTo(1));
            }
        }

        [Test]
        public async Task RenamingMovesTheInstanceAndKeepsItSelected()
        {
            store.Create("alpha");
            store.Create("beta");
            MainViewModel model = create();
            NamePrompt? asked = null;
            model.AskName = prompt =>
            {
                asked = prompt;
                return Task.FromResult<string?>("zzz");
            };

            await model.Selected!.RenameCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(asked!.Title, Is.EqualTo("Rename instance"));
                Assert.That(asked.Confirm, Is.EqualTo("Rename"));
                Assert.That(asked.Initial, Is.EqualTo("alpha"));
                Assert.That(asked.Validate("alpha"), Is.Null);
                Assert.That(asked.Validate("beta"), Is.Not.Null);
                Assert.That(model.Instances.Select(i => i.Name), Is.EqualTo(beta_zzz));
                Assert.That(model.Selected!.Name, Is.EqualTo("zzz"));
                Assert.That(problems, Is.Empty);
            }
        }

        [Test]
        public async Task RenamingAnotherCardLeavesTheSelectionAlone()
        {
            store.Create("alpha");
            store.Create("beta");
            MainViewModel model = create();
            InstanceViewModel other = model.Instances[1];
            model.AskName = _ => Task.FromResult<string?>("renamed");

            await other.RenameCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.Selected!.Name, Is.EqualTo("alpha"));
                Assert.That(model.Instances.Select(i => i.Name), Is.EqualTo(alpha_renamed));
            }
        }

        [Test]
        public async Task DuplicatingAsksForANameCopiesTheInstanceAndSelectsTheCopy()
        {
            LazerInstance alpha = store.Create("alpha");
            UiFixture.PlaceGame(alpha);
            UiFixture.AddMod(alpha, "one");
            MainViewModel model = create();
            NamePrompt? asked = null;
            model.AskName = prompt =>
            {
                asked = prompt;
                return Task.FromResult<string?>("alpha copy");
            };

            await model.Selected!.DuplicateCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(asked!.Title, Is.EqualTo("Duplicate instance"));
                Assert.That(asked.Confirm, Is.EqualTo("Duplicate"));
                Assert.That(asked.Initial, Is.EqualTo("alpha copy"));
                Assert.That(asked.Validate("alpha"), Is.Not.Null);
                Assert.That(model.Instances.Select(i => i.Name), Is.EqualTo(alpha_and_copy));
                Assert.That(model.Selected!.Name, Is.EqualTo("alpha copy"));
                Assert.That(model.Selected.ModList.Mods, Has.Count.EqualTo(1));
                Assert.That(model.Selected.IsSetUp, Is.True);
                Assert.That(problems, Is.Empty);
            }
        }

        [Test]
        public void DuplicateIsNotOfferedBeforeTheInstanceIsSetUp()
        {
            store.Create("alpha");
            MainViewModel model = create();

            Assert.That(model.Selected!.DuplicateCommand.CanExecute(null), Is.False);
        }

        [Test]
        public async Task CancellingTheDuplicateChangesNothing()
        {
            UiFixture.PlaceGame(store.Create("alpha"));
            MainViewModel model = create();
            model.AskName = _ => Task.FromResult<string?>(null);

            await model.Selected!.DuplicateCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.Instances.Select(i => i.Name), Is.EqualTo(only_alpha));
                Assert.That(problems, Is.Empty);
            }
        }

        [Test]
        public async Task DuplicateTheStoreRefusesIsReportedAndAddsNothing()
        {
            UiFixture.PlaceGame(store.Create("alpha"));
            MainViewModel model = create();
            model.AskName = _ => Task.FromResult<string?>("con");

            await model.Selected!.DuplicateCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(problems.Single(), Does.StartWith("Couldn't duplicate alpha."));
                Assert.That(model.Instances.Select(i => i.Name), Is.EqualTo(only_alpha));
            }
        }

        [Test]
        public async Task CancellingTheRenameChangesNothing()
        {
            LazerInstance created = store.Create("alpha");
            MainViewModel model = create();
            model.AskName = _ => Task.FromResult<string?>(null);

            await model.Selected!.RenameCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.Instances.Select(i => i.Name), Is.EqualTo(only_alpha));
                Assert.That(Directory.Exists(created.Directory), Is.True);
                Assert.That(problems, Is.Empty);
            }
        }

        [Test]
        public async Task RenameTheStoreRefusesIsReportedAndKeepsTheInstance()
        {
            store.Create("alpha");
            MainViewModel model = create();
            model.AskName = _ => Task.FromResult<string?>("con");

            await model.Selected!.RenameCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(problems.Single(), Does.StartWith("Couldn't rename alpha."));
                Assert.That(model.Instances.Select(i => i.Name), Is.EqualTo(only_alpha));
            }
        }

        [Test]
        public async Task DeletingAnotherCardLeavesTheSelectionAlone()
        {
            store.Create("alpha");
            store.Create("beta");
            MainViewModel model = create();
            InstanceViewModel other = model.Instances[1];

            await other.DeleteCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.Instances.Select(i => i.Name), Is.EqualTo(only_alpha));
                Assert.That(model.Selected!.Name, Is.EqualTo("alpha"));
            }
        }

        [Test]
        public async Task OpenFolderReachesTheInstanceFolder()
        {
            LazerInstance created = store.Create("alpha");
            MainViewModel model = create();
            string? opened = null;
            model.OpenFolder = path =>
            {
                opened = path;
                return Task.CompletedTask;
            };

            await model.Selected!.OpenFolderCommand.ExecuteAsync(null);

            Assert.That(opened, Is.EqualTo(created.Directory));
        }

        [Test]
        public async Task OpenFolderForAModReachesTheModFolder()
        {
            LazerInstance created = store.Create("alpha");
            mod(created, "cool");
            MainViewModel model = create();
            string? opened = null;
            model.OpenFolder = path =>
            {
                opened = path;
                return Task.CompletedTask;
            };

            await model.Selected!.ModList.Mods[0].OpenFolderCommand.ExecuteAsync(null);

            Assert.That(opened, Is.EqualTo(Path.Combine(created.Omamori.ModsDirectory, "cool")));
        }

        [Test]
        public void RememberedInstanceIsSelectedWhenItStillExists()
        {
            store.Create("alpha");
            LazerInstance beta = store.Create("beta");

            MainViewModel model = create(null, beta.Directory);

            Assert.That(model.Selected!.Name, Is.EqualTo("beta"));
        }

        [Test]
        public void InstanceThatIsGoneFallsBackToTheFirst()
        {
            store.Create("alpha");

            MainViewModel model = create(null, Path.Combine(directory, "instances", "deleted"));

            Assert.That(model.Selected!.Name, Is.EqualTo("alpha"));
        }

        [Test]
        public void CommandLineFolderBeatsTheRememberedOne()
        {
            store.Create("alpha");
            LazerInstance beta = store.Create("beta");
            var given = new LazerInstance(Path.Combine(directory, "somewhere", "testing"));

            MainViewModel model = create(given, beta.Directory);

            Assert.That(model.Selected!.Instance.Directory, Is.EqualTo(given.Directory));
        }

        [Test]
        public void ChoosingAnInstanceIsReportedButStartingUpIsNot()
        {
            store.Create("alpha");
            LazerInstance beta = store.Create("beta");
            var reported = new List<string?>();
            MainViewModel model = create(null, beta.Directory);
            model.Remember = reported.Add;
            int atStart = reported.Count;

            model.Selected = model.Instances[0];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(atStart, Is.Zero);
                Assert.That(reported, Has.Count.EqualTo(1));
                Assert.That(reported[0], Is.EqualTo(model.Instances[0].Instance.Directory));
            }
        }

        [Test]
        public async Task DeletingTheLastInstanceReportsNothingSelected()
        {
            store.Create("only");
            MainViewModel model = create();
            var reported = new List<string?>();
            model.Remember = reported.Add;

            await model.Selected!.DeleteCommand.ExecuteAsync(null);

            Assert.That(reported, Has.Count.EqualTo(1).And.All.Null);
        }

        [Test]
        public async Task RenamingReportsTheNewFolder()
        {
            store.Create("alpha");
            MainViewModel model = create();
            var reported = new List<string?>();
            model.Remember = reported.Add;
            model.AskName = _ => Task.FromResult<string?>("zzz");

            await model.Selected!.RenameCommand.ExecuteAsync(null);

            Assert.That(reported.Last(), Is.EqualTo(Path.Combine(directory, "instances", "zzz")));
        }

        [Test]
        public void ReleasesAreFetchedOnceForAllInstances()
        {
            handler.Add(ReleaseFeed.GitHub, "[]"u8.ToArray());
            store.Create("alpha");
            store.Create("beta");
            MainViewModel model = create();
            Assert.That(() => handler.Requested.Count, Is.EqualTo(1).After(2000, 10));

            model.Selected = model.Instances[1];
            model.Selected = model.Instances[0];
            model.Selected = model.Instances[1];

            Assert.That(handler.Requested, Has.Count.EqualTo(1));
        }

        [Test]
        public void EveryInstanceLoadsItsReleasesWithoutBeingSelected()
        {
            store.Create("alpha");
            store.Create("beta");
            store.Create("gamma");

            MainViewModel model = create();

            Assert.That(() => model.Instances.All(i => i.ReleasesReady), Is.True.After(2000, 10));
        }

        [Test]
        public async Task PickersReachEveryInstanceEvenOnesMadeLater()
        {
            MainViewModel model = create();
            model.PickModZip = () => Task.FromResult<string?>("mod.zip");
            model.PickOsuFolder = () => Task.FromResult<string?>("osu");

            model.AskName = _ => Task.FromResult<string?>("later");
            await model.NewInstanceCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(await model.Selected!.PickModZip!(), Is.EqualTo("mod.zip"));
                Assert.That(await model.Selected.PickOsuFolder!(), Is.EqualTo("osu"));
            }
        }

        [TestCase(0, "0 mods")]
        [TestCase(1, "1 mod")]
        [TestCase(2, "2 mods")]
        public void CardSaysHowManyModsTheInstanceHas(int count, string label)
        {
            LazerInstance instance = store.Create("alpha");

            for (int i = 0; i < count; i++)
                mod(instance, $"mod{i}");

            Assert.That(create().Selected!.ModsLabel, Is.EqualTo(label));
        }

        [Test]
        public void ModCountFollowsModsAddedByHand()
        {
            LazerInstance instance = store.Create("alpha");
            MainViewModel model = create();

            mod(instance, "late");
            model.RefreshMods();

            Assert.That(model.Selected!.ModsLabel, Is.EqualTo("1 mod"));
        }

        [Test]
        public async Task RestartingIntoAnUpdateIsOffWhileAnInstanceIsBusy()
        {
            store.Create("alpha");
            gameRunning = true;
            MainViewModel model = create();
            var update = new UpdateViewModel(new FakeUpdateClient { Offer = "0.1.0-alpha.3" }, busy: () => model.IsBusy);
            model.Update = update;
            await update.CheckAsync();
            await update.UpdateCommand.ExecuteAsync(null);

            var offered = new List<bool>();
            update.RestartCommand.CanExecuteChanged += (_, _) => offered.Add(update.RestartCommand.CanExecute(null));

            gameRunning = false;
            await model.RefreshAsync();

            Assert.That(offered, Is.EqualTo(off_then_on));
        }

        private static void mod(LazerInstance instance, string id)
        {
            string folder = Path.Combine(instance.Omamori.ModsDirectory, id);
            Directory.CreateDirectory(folder);

            File.WriteAllText(Path.Combine(folder, "Mod.dll"), string.Empty);
            File.WriteAllText(Path.Combine(folder, "mod.json"), $$"""
                { "id": "{{id}}", "name": "{{id}}", "version": "1.0.0", "author": "", "entry": "Mod.dll", "apiVersion": "0.2", "capabilities": ["Cosmetic"] }
                """);
        }

        private MainViewModel create(LazerInstance? given = null, string? remembered = null, IReadOnlyList<string>? order = null) => new MainViewModel(store, makeInstance, given, remembered, order) { ShowProblem = showProblem };

        private Task showProblem(string message)
        {
            problems.Add(message);
            return Task.CompletedTask;
        }

        private InstanceViewModel makeInstance(LazerInstance instance) => new InstanceViewModel(
            instance,
            releases,
            new InstanceSetup(http, directory, _ => false),
            library,
            new LibrarySeed(() => false, () => null, () => null),
            () => gameRunning,
            new GameLauncher(_ => { }));
    }
}

using System;
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

        private static readonly string[] alpha_mid_zed = ["alpha", "mid", "zed"];

        private string directory = null!;
        private InstanceStore store = null!;
        private RouteHandler handler = null!;
        private HttpClient http = null!;
        private SharedLibrary library = null!;
        private bool storeSeesGameRunning;
        private bool gameRunning;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            storeSeesGameRunning = false;
            gameRunning = false;
            store = new InstanceStore(Path.Combine(directory, "instances"), Path.Combine(directory, "old-instance"), _ => storeSeesGameRunning);
            handler = new RouteHandler();
            http = new HttpClient(handler);
            library = new SharedLibrary(Path.Combine(directory, "library"), (_, _) => true, () => false);
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
        public void WithoutInstancesNothingIsSelected()
        {
            MainViewModel model = create();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.HasInstances, Is.False);
                Assert.That(model.Selected, Is.Null);
                Assert.That(model.AskDeleteCommand.CanExecute(null), Is.False);
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
                Assert.That(model.AskDeleteCommand.CanExecute(null), Is.False);
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
        public void NewInstanceIsNamedCreatedListedInOrderAndSelected()
        {
            store.Create("alpha");
            store.Create("zed");
            MainViewModel model = create();

            model.NewInstanceCommand.Execute(null);
            bool naming = model.IsNaming;
            model.NewName = "mid";
            bool canCreate = model.CreateInstanceCommand.CanExecute(null);
            model.CreateInstanceCommand.Execute(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(naming, Is.True);
                Assert.That(canCreate, Is.True);
                Assert.That(model.Instances.Select(i => i.Name), Is.EqualTo(alpha_mid_zed));
                Assert.That(model.Selected!.Name, Is.EqualTo("mid"));
                Assert.That(model.IsNaming, Is.False);
                Assert.That(Directory.Exists(model.Selected.Instance.Directory), Is.True);
            }
        }

        [Test]
        public void EmptyNameCannotBeCreated()
        {
            MainViewModel model = create();
            model.NewInstanceCommand.Execute(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.CreateInstanceCommand.CanExecute(null), Is.False);
                Assert.That(model.NewNameProblem, Is.Null);
            }
        }

        [TestCase("a/b")]
        [TestCase("con")]
        [TestCase("name.")]
        public void BadNameShowsAProblemAndCannotBeCreated(string name)
        {
            MainViewModel model = create();
            model.NewInstanceCommand.Execute(null);

            model.NewName = name;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.NewNameProblem, Is.Not.Null);
                Assert.That(model.CreateInstanceCommand.CanExecute(null), Is.False);
            }
        }

        [Test]
        public void NameAlreadyTakenShowsAProblem()
        {
            store.Create("alpha");
            MainViewModel model = create();
            model.NewInstanceCommand.Execute(null);

            model.NewName = "ALPHA";

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.NewNameProblem, Does.Contain("already"));
                Assert.That(model.CreateInstanceCommand.CanExecute(null), Is.False);
            }
        }

        [Test]
        public void CancellingNamingChangesNothing()
        {
            MainViewModel model = create();
            model.NewInstanceCommand.Execute(null);
            model.NewName = "draft";

            model.CancelNewCommand.Execute(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.IsNaming, Is.False);
                Assert.That(model.NewName, Is.Empty);
                Assert.That(model.Instances, Is.Empty);
                Assert.That(store.List(), Is.Empty);
            }
        }

        [Test]
        public void DeletingAsksFirstAndCanBeCancelled()
        {
            store.Create("alpha");
            MainViewModel model = create();

            model.AskDeleteCommand.Execute(null);
            bool asked = model.ConfirmingDelete;
            model.CancelDeleteCommand.Execute(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(asked, Is.True);
                Assert.That(model.ConfirmingDelete, Is.False);
                Assert.That(model.Instances, Has.Count.EqualTo(1));
                Assert.That(Directory.Exists(model.Selected!.Instance.Directory), Is.True);
            }
        }

        [Test]
        public void ConfirmedDeleteRemovesTheInstanceAndSelectsTheNextOne()
        {
            store.Create("alpha");
            LazerInstance middle = store.Create("beta");
            store.Create("gamma");
            MainViewModel model = create();
            model.Selected = model.Instances[1];

            model.AskDeleteCommand.Execute(null);
            model.ConfirmDeleteCommand.Execute(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Directory.Exists(middle.Directory), Is.False);
                Assert.That(model.Instances.Select(i => i.Name), Is.EqualTo(alpha_gamma));
                Assert.That(model.Selected!.Name, Is.EqualTo("gamma"));
                Assert.That(model.ConfirmingDelete, Is.False);
            }
        }

        [Test]
        public void DeletingTheLastInstanceLeavesNothingSelected()
        {
            store.Create("only");
            MainViewModel model = create();

            model.AskDeleteCommand.Execute(null);
            model.ConfirmDeleteCommand.Execute(null);

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

            Assert.That(create().AskDeleteCommand.CanExecute(null), Is.False);
        }

        [Test]
        public void DeleteThatTheStoreRefusesSaysWhyAndKeepsTheInstance()
        {
            store.Create("alpha");
            MainViewModel model = create();
            storeSeesGameRunning = true;

            model.AskDeleteCommand.Execute(null);
            model.ConfirmDeleteCommand.Execute(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.Problem, Does.StartWith("Couldn't delete alpha.").And.Contains("Close osu!"));
                Assert.That(model.ConfirmingDelete, Is.False);
                Assert.That(model.Instances, Has.Count.EqualTo(1));
            }
        }

        [Test]
        public void SelectingAnInstanceLoadsItsReleasesOnce()
        {
            store.Create("alpha");
            store.Create("beta");
            MainViewModel model = create();
            Assert.That(() => handler.Requested.Count, Is.EqualTo(1).After(2000, 10));

            model.Selected = model.Instances[1];
            Assert.That(() => handler.Requested.Count, Is.EqualTo(2).After(2000, 10));

            model.Selected = model.Instances[0];
            model.Selected = model.Instances[1];

            Assert.That(handler.Requested, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task PickersReachEveryInstanceEvenOnesMadeLater()
        {
            MainViewModel model = create();
            model.PickModZip = () => Task.FromResult<string?>("mod.zip");
            model.PickOsuFolder = () => Task.FromResult<string?>("osu");

            model.NewInstanceCommand.Execute(null);
            model.NewName = "later";
            model.CreateInstanceCommand.Execute(null);

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

        private static void mod(LazerInstance instance, string id)
        {
            string folder = Path.Combine(instance.Omamori.ModsDirectory, id);
            Directory.CreateDirectory(folder);

            File.WriteAllText(Path.Combine(folder, "Mod.dll"), string.Empty);
            File.WriteAllText(Path.Combine(folder, "mod.json"), $$"""
                { "id": "{{id}}", "name": "{{id}}", "version": "1.0.0", "author": "", "entry": "Mod.dll", "apiVersion": "0.2", "capabilities": ["Cosmetic"] }
                """);
        }

        private MainViewModel create(LazerInstance? given = null) => new MainViewModel(store, makeInstance, given);

        private InstanceViewModel makeInstance(LazerInstance instance) => new InstanceViewModel(
            instance,
            new ReleaseFeed(http),
            new InstanceSetup(http, directory, _ => false),
            library,
            new LibrarySeed(() => false, () => null, () => null),
            () => gameRunning,
            new GameLauncher(_ => { }));
    }
}

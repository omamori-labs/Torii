using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Torii.Core;
using Torii.Core.Lazer;
using Torii.Core.Library;
using Torii.Core.Releases;
using Torii.ViewModels;

namespace Torii.Tests
{
    [TestFixture]
    public class InstanceViewModelTests
    {
        private const string lazer = "2026.1005.0-lazer";

        private static readonly string[] link_then_start = ["link", "start"];

        private static readonly string[] link_only = ["link"];

        private string directory = null!;
        private LazerInstance instance = null!;
        private RouteHandler handler = null!;
        private HttpClient http = null!;
        private bool osuRunning;
        private List<ProcessStartInfo> started = null!;
        private List<string> events = null!;
        private bool linkAllowed;
        private string? realOsuData;
        private bool realOsuRunning;
        private string? otherRunningIn;
        private List<string> problems = null!;
        private List<bool?> indeterminateAtLink = null!;
        private InstanceViewModel? current;
        private Action? launched;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            instance = new LazerInstance(Path.Combine(directory, "instance"));
            handler = new RouteHandler();
            http = new HttpClient(handler);
            osuRunning = false;
            started = [];
            events = [];
            linkAllowed = true;
            realOsuData = null;
            realOsuRunning = false;
            otherRunningIn = null;
            problems = [];
            indeterminateAtLink = [];
            launched = null;
        }

        [TearDown]
        public void TearDown()
        {
            http.Dispose();
            handler.Dispose();
            Directory.Delete(directory, recursive: true);
        }

        [Test]
        public void WithoutAnInstanceThereIsSomethingToSetUp()
        {
            InstanceViewModel model = create();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.Summary, Is.EqualTo("Not set up yet"));
                Assert.That(model.InstallLabel, Is.EqualTo("Set up"));
                Assert.That(model.ModList.HasMods, Is.False);
            }
        }

        [Test]
        public async Task LoadingReleasesSelectsTheNewest()
        {
            serveReleases("2.0.0", "1.0.0");
            InstanceViewModel model = create();

            await model.LoadReleasesCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.Releases, Has.Count.EqualTo(2));
                Assert.That(model.SelectedRelease!.Version, Is.EqualTo("2.0.0"));
                Assert.That(model.Status, Is.Null);
                Assert.That(model.InstallCommand.CanExecute(null), Is.True);
            }
        }

        [Test]
        public async Task FailingToLoadReleasesSaysSoAndLeavesNothingToInstall()
        {
            InstanceViewModel model = create();

            await model.LoadReleasesCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.Status, Does.StartWith("Couldn't check for releases."));
                Assert.That(model.InstallCommand.CanExecute(null), Is.False);
            }
        }

        [Test]
        [Platform("Win,Linux")]
        public async Task SettingUpInstallsTheGameAndOmamori()
        {
            serveReleases("2.0.0");
            serveLazer();
            InstanceViewModel model = create();
            await model.LoadReleasesCommand.ExecuteAsync(null);

            await model.InstallCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(instance.IsInstalled, Is.True);
                Assert.That(instance.Omamori.IsInstalled, Is.True);
                Assert.That(model.Summary, Does.EndWith($"on osu! {lazer}"));
                Assert.That(model.Status, Is.EqualTo("Omamori 2.0.0 is ready."));
                Assert.That(model.IsBusy, Is.False);
                Assert.That(model.InstallLabel, Is.Not.EqualTo("Set up"));
            }
        }

        [Test]
        public async Task FailedSetupSaysWhy()
        {
            serveReleases("2.0.0");
            InstanceViewModel model = create();
            await model.LoadReleasesCommand.ExecuteAsync(null);

            await model.InstallCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(instance.IsInstalled, Is.False);
                Assert.That(problems.Single(), Does.StartWith("Couldn't set up Omamori 2.0.0."));
                Assert.That(model.IsBusy, Is.False);
                Assert.That(model.Summary, Is.EqualTo("Not set up yet"));
            }
        }

        [Test]
        public async Task NothingCanBeChangedWhileTheInstanceRuns()
        {
            serveReleases("2.0.0");
            InstanceViewModel model = create();
            await model.LoadReleasesCommand.ExecuteAsync(null);

            osuRunning = true;
            await model.RefreshOsuRunningAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.OsuRunning, Is.True);
                Assert.That(model.CanEdit, Is.False);
                Assert.That(model.InstallCommand.CanExecute(null), Is.False);
            }
        }

        [Test]
        public void ThereIsNothingToPlayBeforeSetup()
        {
            InstanceViewModel model = create();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.IsSetUp, Is.False);
                Assert.That(model.PlayCommand.CanExecute(null), Is.False);
            }
        }

        [Test]
        public async Task PlayingStartsTheInstancesGame()
        {
            placeGame();
            InstanceViewModel model = create();

            await model.PlayCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(started.Single().FileName, Is.EqualTo(instance.Executable));
                Assert.That(model.Status, Is.Null);
                Assert.That(model.IsBusy, Is.False);
            }
        }

        [Test]
        public async Task GameIsNotStartedWhileAnotherInstanceUsesTheLibrary()
        {
            placeGame();
            otherRunningIn = Path.Combine(directory, "instances", "meow");
            InstanceViewModel model = create();

            await model.PlayCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(started, Is.Empty);
                Assert.That(events, Is.Empty);
                Assert.That(problems.Single(), Does.StartWith("Couldn't start osu!.").And.Contains("meow").And.Contains("Close it first"));
                Assert.That(model.Status, Is.Null);
                Assert.That(model.IsBusy, Is.False);
            }
        }

        [Test]
        public async Task BarIsIndeterminateWhileTheLibraryIsLinkedAndNotAfter()
        {
            placeGame();
            InstanceViewModel model = create();

            await model.PlayCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(indeterminateAtLink, Has.Count.EqualTo(1));
                Assert.That(indeterminateAtLink, Has.All.EqualTo(true));
                Assert.That(model.IsIndeterminate, Is.False);
                Assert.That(model.IsBusy, Is.False);
            }
        }

        [Test]
        public async Task BarIsNotLeftSpinningWhenPlayFails()
        {
            placeGame();
            otherRunningIn = Path.Combine(directory, "instances", "meow");
            InstanceViewModel model = create();

            await model.PlayCommand.ExecuteAsync(null);

            Assert.That(model.IsIndeterminate, Is.False);
        }

        [Test]
        public async Task PlayingLinksTheLibraryBeforeStarting()
        {
            placeGame();
            InstanceViewModel model = create();

            await model.PlayCommand.ExecuteAsync(null);

            Assert.That(events, Is.EqualTo(link_then_start));
        }

        [Test]
        public async Task GameIsNotStartedWhenTheLibraryCantBeLinked()
        {
            placeGame();
            InstanceViewModel model = create();
            linkAllowed = false;

            await model.PlayCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(started, Is.Empty);
                Assert.That(problems.Single(), Does.StartWith("Couldn't start osu!."));
                Assert.That(model.IsBusy, Is.False);
            }
        }

        [Test]
        public async Task LibraryIsLinkedAgainWhenTheGameCloses()
        {
            placeGame();
            InstanceViewModel model = create();
            launched = () => osuRunning = true;
            await model.PlayCommand.ExecuteAsync(null);
            events.Clear();
            indeterminateAtLink.Clear();

            osuRunning = false;
            await model.RefreshOsuRunningAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(events, Is.EqualTo(link_only));
                Assert.That(indeterminateAtLink, Has.All.EqualTo(true));
                Assert.That(model.IsIndeterminate, Is.False);
                Assert.That(model.OsuRunning, Is.False);
                Assert.That(model.IsBusy, Is.False);
            }
        }

        [Test]
        public async Task GameThatClosesWithoutTheLibraryBeingLinkedSaysSo()
        {
            placeGame();
            InstanceViewModel model = create();
            launched = () => osuRunning = true;
            await model.PlayCommand.ExecuteAsync(null);

            osuRunning = false;
            linkAllowed = false;
            await model.RefreshOsuRunningAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.Status, Does.StartWith("Couldn't move the game's data into the library."));
                Assert.That(model.IsBusy, Is.False);
            }
        }

        [Test]
        public async Task GameIsNotStartedWhenTheLibraryIsNewerThanTheInstance()
        {
            placeGame();
            Directory.CreateDirectory(Path.Combine(directory, "library"));
            File.WriteAllText(Path.Combine(directory, "library", "torii-library.txt"), "2099.101.0-lazer");
            InstanceViewModel model = create();

            await model.PlayCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(started, Is.Empty);
                Assert.That(problems.Single(), Does.StartWith("Couldn't start osu!.").And.Contains("update this instance"));
            }
        }

        [Test]
        public void ImportIsOfferedWhenThereIsAnOsuLibraryToTakeIn()
        {
            placeRealOsu();
            InstanceViewModel model = create();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.ImportAvailable, Is.True);
                Assert.That(model.ImportCommand.CanExecute(null), Is.True);
            }
        }

        [Test]
        public void ImportIsStillOfferedWhenNoOsuLibraryWasFound()
        {
            Assert.That(create().ImportAvailable, Is.True);
        }

        [Test]
        public async Task FolderIsAskedForWhenNoOsuLibraryWasFound()
        {
            string picked = Path.Combine(directory, "picked-osu");
            placeOsuLibrary(picked);
            InstanceViewModel model = create();
            model.PickOsuFolder = () => Task.FromResult<string?>(picked);

            await model.ImportCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(File.ReadAllText(Path.Combine(directory, "library", "client.realm")), Is.EqualTo("db"));
                Assert.That(model.ImportAvailable, Is.False);
            }
        }

        [Test]
        public async Task FolderIsNotAskedForWhenOneWasFound()
        {
            placeRealOsu();
            InstanceViewModel model = create();
            bool asked = false;
            model.PickOsuFolder = () =>
            {
                asked = true;
                return Task.FromResult<string?>(null);
            };

            await model.ImportCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(asked, Is.False);
                Assert.That(File.Exists(Path.Combine(directory, "library", "client.realm")), Is.True);
            }
        }

        [Test]
        public async Task CancellingTheFolderPickerImportsNothing()
        {
            InstanceViewModel model = create();
            model.PickOsuFolder = () => Task.FromResult<string?>(null);

            await model.ImportCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(File.Exists(Path.Combine(directory, "library", "client.realm")), Is.False);
                Assert.That(model.Status, Is.Null);
                Assert.That(model.ImportAvailable, Is.True);
                Assert.That(model.IsBusy, Is.False);
            }
        }

        [Test]
        public async Task FolderWithoutAnOsuLibraryIsRefused()
        {
            string empty = Path.Combine(directory, "not-osu");
            Directory.CreateDirectory(empty);
            InstanceViewModel model = create();
            model.PickOsuFolder = () => Task.FromResult<string?>(empty);

            await model.ImportCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(problems.Single(), Does.StartWith("Couldn't import from osu!.").And.Contains("client.realm"));
                Assert.That(model.ImportAvailable, Is.True);
            }
        }

        [Test]
        public void ImportIsNotOfferedWhenTheInstanceHasItsOwnData()
        {
            placeRealOsu();
            Directory.CreateDirectory(instance.Directory);
            File.WriteAllText(instance.DatabaseFile, "mine");

            Assert.That(create().ImportAvailable, Is.False);
        }

        [Test]
        public async Task ImportingFillsTheLibraryAndHidesTheButton()
        {
            placeRealOsu();
            InstanceViewModel model = create();

            await model.ImportCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(File.ReadAllText(Path.Combine(directory, "library", "client.realm")), Is.EqualTo("db"));
                Assert.That(File.ReadAllText(Path.Combine(directory, "library", "files", "a", "ab", "hash")), Is.EqualTo("map"));
                Assert.That(File.ReadAllText(Path.Combine(directory, "library", "torii-library.txt")).Trim(), Is.EqualTo(lazer));
                Assert.That(model.Status, Is.EqualTo("Your osu! maps and skins are imported."));
                Assert.That(model.ImportAvailable, Is.False);
                Assert.That(model.IsBusy, Is.False);
            }
        }

        [Test]
        public async Task FailedImportSaysWhyAndKeepsTheButton()
        {
            placeRealOsu();
            realOsuRunning = true;
            InstanceViewModel model = create();

            await model.ImportCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(problems.Single(), Does.StartWith("Couldn't import from osu!."));
                Assert.That(model.ImportAvailable, Is.True);
                Assert.That(model.IsBusy, Is.False);
            }
        }

        [Test]
        public async Task OnceTheGameRunsItCannotBeStartedAgain()
        {
            placeGame();
            InstanceViewModel model = create();
            launched = () => osuRunning = true;

            await model.PlayCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.OsuRunning, Is.True);
                Assert.That(model.PlayCommand.CanExecute(null), Is.False);
                Assert.That(model.CanEdit, Is.False);
            }
        }

        [Test]
        public async Task GameThatFailsToStartSaysWhy()
        {
            placeGame();
            InstanceViewModel model = create();
            launched = () => throw new Win32Exception("blocked");

            await model.PlayCommand.ExecuteAsync(null);

            Assert.That(problems.Single(), Is.EqualTo("Couldn't start osu!. blocked"));
        }

        [Test]
        public void NothingSaysThereAreNoModsBeforeSetup()
        {
            InstanceViewModel model = create();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.NoMods, Is.False);
                Assert.That(model.AddModCommand.CanExecute(null), Is.False);
            }
        }

        [Test]
        public void SetUpInstanceWithoutModsSaysSo()
        {
            placeGame();

            Assert.That(create().NoMods, Is.True);
        }

        [Test]
        public async Task AddingAModFromAZipListsIt()
        {
            placeGame();
            InstanceViewModel model = create();
            model.PickModZip = () => Task.FromResult<string?>(modZip("someone.coolmod", "Cool Mod"));

            await model.AddModCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.ModList.Mods.Single().Label, Is.EqualTo("Cool Mod"));
                Assert.That(model.Status, Is.EqualTo("Cool Mod 1.0.0 is installed."));
                Assert.That(model.NoMods, Is.False);
            }
        }

        [Test]
        public async Task CancellingThePickerChangesNothing()
        {
            placeGame();
            InstanceViewModel model = create();
            model.PickModZip = () => Task.FromResult<string?>(null);

            await model.AddModCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.ModList.Mods, Is.Empty);
                Assert.That(model.Status, Is.Null);
            }
        }

        [Test]
        public async Task AddingSomethingThatIsNotAModSaysWhy()
        {
            placeGame();
            InstanceViewModel model = create();
            string notAMod = Path.Combine(directory, "not-a-mod.zip");
            File.WriteAllBytes(notAMod, Fixtures.Zip(("readme.txt", "hi"u8.ToArray())));
            model.PickModZip = () => Task.FromResult<string?>(notAMod);

            await model.AddModCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.ModList.Mods, Is.Empty);
                Assert.That(problems.Single(), Does.StartWith("Couldn't add the mod."));
            }
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

        private void placeGame()
        {
            Directory.CreateDirectory(instance.Directory);

            foreach (string file in new[] { instance.Executable, Path.Combine(instance.Directory, "osu.Game.dll"), instance.PortableMarker })
                File.WriteAllText(file, string.Empty);

            File.WriteAllText(Path.Combine(instance.Directory, "sq.version"), $"<package><metadata><version>{lazer}</version></metadata></package>");
        }

        private Task showProblem(string message)
        {
            problems.Add(message);
            return Task.CompletedTask;
        }

        private void placeRealOsu()
        {
            realOsuData = Path.Combine(directory, "real-osu");
            placeOsuLibrary(realOsuData);
        }

        private static void placeOsuLibrary(string folder)
        {
            Directory.CreateDirectory(Path.Combine(folder, "files", "a", "ab"));
            File.WriteAllText(Path.Combine(folder, "files", "a", "ab", "hash"), "map");
            File.WriteAllText(Path.Combine(folder, "client.realm"), "db");
        }

        private InstanceViewModel create()
        {
            var launcher = new GameLauncher(info =>
            {
                launched?.Invoke();
                started.Add(info);
                events.Add("start");
            });

            var library = new SharedLibrary(Path.Combine(directory, "library"), (_, _) =>
            {
                events.Add("link");
                indeterminateAtLink.Add(current?.IsIndeterminate);
                return linkAllowed;
            }, () => false, _ => otherRunningIn);

            return current = new InstanceViewModel(instance, new ReleaseFeed(http), new InstanceSetup(http, directory, _ => osuRunning), library, new LibrarySeed(() => realOsuRunning, () => realOsuData, () => lazer), () => osuRunning, launcher) { ShowProblem = showProblem };
        }

        private void serveReleases(params string[] versions)
        {
            byte[] zip = Fixtures.OmamoriZip(lazer);

            var releases = versions.Select(version =>
            {
                var download = new Uri($"https://example.com/omamori-{version}.zip");
                handler.Add(download, zip);

                return $$"""
                    { "tag_name": "v{{version}}", "assets": [{ "name": "omamori-{{version}}.zip", "size": {{zip.Length}}, "digest": "sha256:{{Fixtures.Sha256(zip)}}", "browser_download_url": "{{download}}" }] }
                    """;
            });

            handler.Add(ReleaseFeed.GitHub, Encoding.UTF8.GetBytes($"[{string.Join(',', releases)}]"));
        }

        private void serveLazer()
        {
            byte[] package = Fixtures.LazerPackage(lazer);
            var download = new Uri("https://github.com/ppy/osu/releases/download/package.nupkg");

            handler.Add(download, package);
            handler.Add(LazerFeed.ReleaseUri(lazer), Fixtures.LazerRelease(lazer, package, download));
        }
    }
}

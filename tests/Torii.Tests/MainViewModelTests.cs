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
using Torii.Core.Mods;
using Torii.Core.Releases;

namespace Torii.Tests
{
    [TestFixture]
    public class MainViewModelTests
    {
        private const string lazer = "2026.1005.0-lazer";

        private static readonly string[] expected_labels = ["Patcher", "Plain"];

        private string directory = null!;
        private LazerInstance instance = null!;
        private RouteHandler handler = null!;
        private HttpClient http = null!;
        private bool osuRunning;
        private List<ProcessStartInfo> started = null!;
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
            MainViewModel model = create();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.Summary, Is.EqualTo("Not set up yet"));
                Assert.That(model.InstallLabel, Is.EqualTo("Set up"));
                Assert.That(model.HasMods, Is.False);
            }
        }

        [Test]
        public async Task LoadingReleasesSelectsTheNewest()
        {
            serveReleases("2.0.0", "1.0.0");
            MainViewModel model = create();

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
            MainViewModel model = create();

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
            MainViewModel model = create();
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
            MainViewModel model = create();
            await model.LoadReleasesCommand.ExecuteAsync(null);

            await model.InstallCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(instance.IsInstalled, Is.False);
                Assert.That(model.Status, Does.StartWith("Couldn't set up Omamori 2.0.0."));
                Assert.That(model.IsBusy, Is.False);
                Assert.That(model.Summary, Is.EqualTo("Not set up yet"));
            }
        }

        [Test]
        public async Task NothingCanBeChangedWhileTheInstanceRuns()
        {
            serveReleases("2.0.0");
            MainViewModel model = create();
            await model.LoadReleasesCommand.ExecuteAsync(null);

            osuRunning = true;
            model.RefreshOsuRunning();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.OsuRunning, Is.True);
                Assert.That(model.CanEdit, Is.False);
                Assert.That(model.InstallCommand.CanExecute(null), Is.False);
            }
        }

        [Test]
        public void ModsAreListed()
        {
            mod("b.plain", "Plain", author: "", capabilities: "\"Cosmetic\"");
            mod("a.patcher", "Patcher", author: "someone", capabilities: "\"Patches\"");

            MainViewModel model = create();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.HasMods, Is.True);
                Assert.That(model.Mods.Select(m => m.Label), Is.EqualTo(expected_labels));
                Assert.That(model.Mods[0].Byline, Is.EqualTo("by someone"));
                Assert.That(model.Mods[0].Patches, Is.True);
                Assert.That(model.Mods[1].Byline, Is.Empty);
                Assert.That(model.Mods[1].Patches, Is.False);
                Assert.That(model.Mods.Select(m => m.IsOn), Has.All.True);
            }
        }

        [Test]
        public void SwitchingAModOffAndOn()
        {
            mod("a.mod", "Cool Mod");
            MainViewModel model = create();
            ModItemViewModel row = model.Mods[0];

            row.IsOn = false;
            bool off = row.IsOn;
            string? offStatus = model.Status;

            row.IsOn = true;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(off, Is.False);
                Assert.That(offStatus, Is.EqualTo("Cool Mod is off the next time osu! starts."));
                Assert.That(row.IsOn, Is.True);
                Assert.That(new ModLibrary(instance.Omamori, () => false).List()[0].State, Is.EqualTo(ModState.On));
                Assert.That(model.Mods[0], Is.SameAs(row));
            }
        }

        [Test]
        public void SwitchingAModWhileTheInstanceRunsIsRefusedAndTheSwitchGoesBack()
        {
            mod("a.mod", "Cool Mod");
            MainViewModel model = create();
            ModItemViewModel row = model.Mods[0];
            bool reverted = false;
            row.PropertyChanged += (_, e) => reverted |= e.PropertyName == nameof(ModItemViewModel.IsOn);

            osuRunning = true;
            row.IsOn = false;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(row.IsOn, Is.True);
                Assert.That(reverted, Is.True);
                Assert.That(model.Status, Does.StartWith("Couldn't turn Cool Mod off."));
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
            MainViewModel model = create();

            mod("a.mod", "Cool Mod");
            model.RefreshMods();

            Assert.That(model.Mods, Has.Count.EqualTo(1));
        }

        [Test]
        public void ThereIsNothingToPlayBeforeSetup()
        {
            MainViewModel model = create();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.IsSetUp, Is.False);
                Assert.That(model.PlayCommand.CanExecute(null), Is.False);
            }
        }

        [Test]
        public void PlayingStartsTheInstancesGame()
        {
            placeGame();
            MainViewModel model = create();

            model.PlayCommand.Execute(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(started.Single().FileName, Is.EqualTo(instance.Executable));
                Assert.That(model.Status, Is.EqualTo("Starting osu!..."));
            }
        }

        [Test]
        public void OnceTheGameRunsItCannotBeStartedAgain()
        {
            placeGame();
            MainViewModel model = create();
            launched = () => osuRunning = true;

            model.PlayCommand.Execute(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.OsuRunning, Is.True);
                Assert.That(model.PlayCommand.CanExecute(null), Is.False);
                Assert.That(model.CanEdit, Is.False);
            }
        }

        [Test]
        public void GameThatFailsToStartSaysWhy()
        {
            placeGame();
            MainViewModel model = create();
            launched = () => throw new Win32Exception("blocked");

            model.PlayCommand.Execute(null);

            Assert.That(model.Status, Is.EqualTo("Couldn't start osu!. blocked"));
        }

        [Test]
        public void NothingSaysThereAreNoModsBeforeSetup()
        {
            MainViewModel model = create();

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
            MainViewModel model = create();
            model.PickModZip = () => Task.FromResult<string?>(modZip("someone.coolmod", "Cool Mod"));

            await model.AddModCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.Mods.Single().Label, Is.EqualTo("Cool Mod"));
                Assert.That(model.Status, Is.EqualTo("Cool Mod 1.0.0 is installed."));
                Assert.That(model.NoMods, Is.False);
            }
        }

        [Test]
        public async Task CancellingThePickerChangesNothing()
        {
            placeGame();
            MainViewModel model = create();
            model.PickModZip = () => Task.FromResult<string?>(null);

            await model.AddModCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.Mods, Is.Empty);
                Assert.That(model.Status, Is.Null);
            }
        }

        [Test]
        public async Task AddingSomethingThatIsNotAModSaysWhy()
        {
            placeGame();
            MainViewModel model = create();
            string notAMod = Path.Combine(directory, "not-a-mod.zip");
            File.WriteAllBytes(notAMod, Fixtures.Zip(("readme.txt", "hi"u8.ToArray())));
            model.PickModZip = () => Task.FromResult<string?>(notAMod);

            await model.AddModCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.Mods, Is.Empty);
                Assert.That(model.Status, Does.StartWith("Couldn't add the mod."));
            }
        }

        [Test]
        public void RemovingAModTakesItOffTheList()
        {
            mod("a.mod", "Cool Mod");
            MainViewModel model = create();

            model.Mods[0].RemoveCommand.Execute(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.Mods, Is.Empty);
                Assert.That(Directory.Exists(Path.Combine(instance.Omamori.ModsDirectory, "a.mod")), Is.False);
                Assert.That(model.Status, Is.EqualTo("Cool Mod is removed. Its settings were kept."));
            }
        }

        [Test]
        public void RemovingAModWhileTheInstanceRunsIsRefused()
        {
            mod("a.mod", "Cool Mod");
            MainViewModel model = create();

            osuRunning = true;
            model.Mods[0].RemoveCommand.Execute(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.Mods, Has.Count.EqualTo(1));
                Assert.That(model.Status, Does.StartWith("Couldn't remove Cool Mod."));
            }
        }

        [Test]
        public void OnlyACrashedModCanBeTriedAgain()
        {
            mod("a.crashed", "Crashed");
            mod("b.fine", "Fine");
            crash("a.crashed");

            MainViewModel model = create();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.Mods[0].CanRetry, Is.True);
                Assert.That(model.Mods[0].Problem, Does.StartWith("Crashed in 2 sessions"));
                Assert.That(model.Mods[1].CanRetry, Is.False);
            }
        }

        [Test]
        public void TryingACrashedModAgainClearsItsProblem()
        {
            mod("a.crashed", "Crashed");
            crash("a.crashed");
            MainViewModel model = create();

            model.Mods[0].RetryCommand.Execute(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.Mods[0].CanRetry, Is.False);
                Assert.That(model.Mods[0].Problem, Is.Empty);
                Assert.That(model.Status, Is.EqualTo("Crashed is tried again the next time osu! starts."));
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

        private void crash(string id)
        {
            File.WriteAllText(instance.Omamori.FaultsFile, $$"""
                { "session": 7, "mods": { "{{id}}": { "version": "1.0.0", "strikes": 2, "lastSession": 7, "lastError": "boom" } } }
                """);
        }

        private void placeGame()
        {
            Directory.CreateDirectory(instance.Directory);

            foreach (string file in new[] { instance.Executable, Path.Combine(instance.Directory, "osu.Game.dll"), instance.PortableMarker })
                File.WriteAllText(file, string.Empty);
        }

        private MainViewModel create()
        {
            var launcher = new GameLauncher(info =>
            {
                launched?.Invoke();
                started.Add(info);
            });

            return new MainViewModel(instance, new ReleaseFeed(http), new InstanceSetup(http, directory, _ => osuRunning), () => osuRunning, launcher);
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

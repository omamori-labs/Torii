using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Torii.Core;

namespace Torii.Tests
{
    [TestFixture]
    public class MainViewModelTests
    {
        private string directory = null!;
        private OmamoriInstall install = null!;
        private bool osuRunning;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");

            install = new OmamoriInstall(Path.Combine(directory, "data"));
            Directory.CreateDirectory(install.DataDirectory);

            osuRunning = false;
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, recursive: true);

        [Test]
        public void EmptyDataFolderIsNotInstalledAndWarns()
        {
            using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, []));
            MainViewModel model = create(http);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.Summary, Is.EqualTo("Not installed"));
                Assert.That(model.Warning, Is.Not.Null);
                Assert.That(model.InstallLabel, Is.EqualTo("Install"));
                Assert.That(model.UninstallCommand.CanExecute(null), Is.False);
            }
        }

        [Test]
        public async Task LoadingReleasesSelectsTheNewest()
        {
            using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, feed("2.0.0", "1.0.0")));
            MainViewModel model = create(http);

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
            using var http = new HttpClient(new StubHandler(HttpStatusCode.Forbidden, []));
            MainViewModel model = create(http);

            await model.LoadReleasesCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.Status, Does.StartWith("Couldn't check for releases."));
                Assert.That(model.InstallCommand.CanExecute(null), Is.False);
            }
        }

        [Test]
        public async Task InstallingUpdatesTheSummary()
        {
            byte[] zip = releaseZip();
            using var http = new HttpClient(new RoutingHandler(feedWithHash(sha256(zip), "2.0.0"), zip));
            MainViewModel model = create(http);
            await model.LoadReleasesCommand.ExecuteAsync(null);

            await model.InstallCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(install.IsInstalled, Is.True);
                Assert.That(model.Summary, Does.StartWith("Installed"));
                Assert.That(model.Status, Is.EqualTo("Omamori 2.0.0 is installed."));
                Assert.That(model.IsBusy, Is.False);
                Assert.That(model.UninstallCommand.CanExecute(null), Is.True);
            }
        }

        [Test]
        public async Task FailedInstallSaysWhy()
        {
            using var http = new HttpClient(new RoutingHandler(feedWithHash(new string('0', 64), "2.0.0"), releaseZip()));
            MainViewModel model = create(http);
            await model.LoadReleasesCommand.ExecuteAsync(null);

            await model.InstallCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(install.IsInstalled, Is.False);
                Assert.That(model.Status, Does.StartWith("Couldn't install Omamori 2.0.0."));
                Assert.That(model.IsBusy, Is.False);
            }
        }

        [Test]
        public async Task NothingCanBeChangedWhileOsuRuns()
        {
            byte[] zip = releaseZip();
            using var http = new HttpClient(new RoutingHandler(feedWithHash(sha256(zip), "2.0.0"), zip));
            MainViewModel model = create(http);
            await model.LoadReleasesCommand.ExecuteAsync(null);
            await model.InstallCommand.ExecuteAsync(null);

            osuRunning = true;
            model.RefreshOsuRunning();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.OsuRunning, Is.True);
                Assert.That(model.InstallCommand.CanExecute(null), Is.False);
                Assert.That(model.UninstallCommand.CanExecute(null), Is.False);
            }
        }

        [Test]
        public async Task UninstallingUpdatesTheSummary()
        {
            byte[] zip = releaseZip();
            using var http = new HttpClient(new RoutingHandler(feedWithHash(sha256(zip), "2.0.0"), zip));
            MainViewModel model = create(http);
            await model.LoadReleasesCommand.ExecuteAsync(null);
            await model.InstallCommand.ExecuteAsync(null);

            model.UninstallCommand.Execute(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(install.IsInstalled, Is.False);
                Assert.That(model.Summary, Is.EqualTo("Not installed"));
                Assert.That(model.UninstallCommand.CanExecute(null), Is.False);
            }
        }

        [Test]
        public void ModsAreListed()
        {
            mod("b.plain", "Plain", author: "", capabilities: "\"Cosmetic\"");
            mod("a.patcher", "Patcher", author: "someone", capabilities: "\"Patches\"");
            using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, []));

            MainViewModel model = create(http);

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
        public void WithoutModsTheListIsEmpty()
        {
            using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, []));

            Assert.That(create(http).HasMods, Is.False);
        }

        [Test]
        public void SwitchingAModOffAndOn()
        {
            mod("a.mod", "Cool Mod");
            using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, []));
            MainViewModel model = create(http);
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
                Assert.That(new ModLibrary(install, () => false).List()[0].State, Is.EqualTo(ModState.On));
                Assert.That(model.Mods[0], Is.SameAs(row));
            }
        }

        [Test]
        public void SwitchingAModWhileOsuRunsIsRefusedAndTheSwitchGoesBack()
        {
            mod("a.mod", "Cool Mod");
            using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, []));
            MainViewModel model = create(http);
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
                Assert.That(File.Exists(install.SettingsFile), Is.False);
            }
        }

        [Test]
        public void BrokenModCannotBeSwitched()
        {
            string folder = Path.Combine(install.ModsDirectory, "broken");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "mod.json"), "not json");
            using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, []));

            ModItemViewModel row = create(http).Mods[0];

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
            using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, []));
            MainViewModel model = create(http);

            mod("a.mod", "Cool Mod");
            model.RefreshMods();

            Assert.That(model.Mods, Has.Count.EqualTo(1));
        }

        [Test]
        public void EditingIsOffWhileOsuRuns()
        {
            using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, []));
            MainViewModel model = create(http);

            osuRunning = true;
            model.RefreshOsuRunning();

            Assert.That(model.CanEdit, Is.False);
        }

        private static readonly string[] expected_labels = ["Patcher", "Plain"];

        private void mod(string id, string name, string author = "", string capabilities = "\"Cosmetic\"")
        {
            string folder = Path.Combine(install.ModsDirectory, id);
            Directory.CreateDirectory(folder);

            File.WriteAllText(Path.Combine(folder, "Mod.dll"), string.Empty);
            File.WriteAllText(Path.Combine(folder, "mod.json"), $$"""
                { "id": "{{id}}", "name": "{{name}}", "version": "1.0.0", "author": "{{author}}", "entry": "Mod.dll", "apiVersion": "0.2", "capabilities": [{{capabilities}}] }
                """);
        }

        private MainViewModel create(HttpClient http) =>
            new MainViewModel(install, new ReleaseFeed(http), new ReleaseInstaller(http, directory), () => osuRunning);

        private static byte[] feed(params string[] versions) => feedWithHash(new string('0', 64), versions);

        private static byte[] feedWithHash(string hash, params string[] versions)
        {
            var releases = versions.Select(version => $$"""
                { "tag_name": "v{{version}}", "assets": [{ "name": "omamori-{{version}}.zip", "digest": "sha256:{{hash}}", "browser_download_url": "https://example.com/omamori-{{version}}.zip" }] }
                """);

            return Encoding.UTF8.GetBytes($"[{string.Join(',', releases)}]");
        }

        private static string sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

        private static byte[] releaseZip()
        {
            using var stream = new MemoryStream();

            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (string name in new[] { "rulesets/osu.Game.Rulesets.Omamori.dll", "omamori/core/Omamori.Core.dll" })
                {
                    using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                    writer.Write(name);
                }
            }

            return stream.ToArray();
        }

        // The feed for the api, the zip for everything else
        private sealed class RoutingHandler : HttpMessageHandler
        {
            private readonly byte[] feed;
            private readonly byte[] zip;

            public RoutingHandler(byte[] feed, byte[] zip)
            {
                this.feed = feed;
                this.zip = zip;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            {
                byte[] body = request.RequestUri!.Host == "api.github.com" ? feed : zip;

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
            }
        }
    }
}

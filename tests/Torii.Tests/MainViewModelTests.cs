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

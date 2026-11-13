using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Torii.Core;
using Torii.Core.Lazer;
using Torii.Core.Omamori;

namespace Torii.Tests
{
    [TestFixture]
    [Platform("Win,Linux")]
    public class InstanceSetupTests
    {
        private const string old_lazer = "2026.1005.0-lazer";
        private const string new_lazer = "2026.1101.0-lazer";

        private string directory = null!;
        private string downloads = null!;
        private LazerInstance instance = null!;
        private RouteHandler handler = null!;
        private HttpClient http = null!;
        private bool running;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            downloads = Path.Combine(directory, "downloads");
            Directory.CreateDirectory(downloads);

            instance = new LazerInstance(Path.Combine(directory, "instance"));
            handler = new RouteHandler();
            http = new HttpClient(handler);
            running = false;

            serveLazer(old_lazer);
            serveLazer(new_lazer);
        }

        [TearDown]
        public void TearDown()
        {
            http.Dispose();
            handler.Dispose();
            Directory.Delete(directory, recursive: true);
        }

        [Test]
        public async Task SettingUpBringsInTheGameTheReleaseWasBuiltFor()
        {
            await setup().SetUpAsync(instance, serveOmamori("1.0.0", old_lazer));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(instance.IsInstalled, Is.True);
                Assert.That(instance.ReadVersion(), Is.EqualTo(old_lazer));
                Assert.That(instance.Omamori.IsInstalled, Is.True);
                Assert.That(Directory.EnumerateFileSystemEntries(downloads), Is.Empty);
            }
        }

        [Test]
        public async Task ReleaseForTheSameGameDoesNotDownloadItAgain()
        {
            await setup().SetUpAsync(instance, serveOmamori("1.0.0", old_lazer));
            handler.Requested.Clear();

            await setup().SetUpAsync(instance, serveOmamori("1.1.0", old_lazer));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(handler.Requested, Has.None.Matches<Uri>(u => u.AbsoluteUri.Contains("ppy/osu", StringComparison.Ordinal)));
                Assert.That(instance.Omamori.IsInstalled, Is.True);
            }
        }

        [Test]
        public async Task ReleaseForANewerGameUpdatesItAndKeepsTheData()
        {
            await setup().SetUpAsync(instance, serveOmamori("1.0.0", old_lazer));
            string realm = Path.Combine(instance.Directory, "client.realm");
            string mod = Path.Combine(instance.Omamori.ModsDirectory, "someone.mod", "mod.json");
            Directory.CreateDirectory(Path.GetDirectoryName(mod)!);
            File.WriteAllText(realm, "scores");
            File.WriteAllText(mod, "{}");

            await setup().SetUpAsync(instance, serveOmamori("2.0.0", new_lazer));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(instance.ReadVersion(), Is.EqualTo(new_lazer));
                Assert.That(File.ReadAllText(realm), Is.EqualTo("scores"));
                Assert.That(File.Exists(mod), Is.True);
            }
        }

        [Test]
        public async Task ReleaseForAnOlderGameIsRefused()
        {
            await setup().SetUpAsync(instance, serveOmamori("2.0.0", new_lazer));
            OmamoriRelease older = serveOmamori("1.0.0", old_lazer);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(async () => await setup().SetUpAsync(instance, older), Throws.InvalidOperationException);
                Assert.That(instance.ReadVersion(), Is.EqualTo(new_lazer));
                Assert.That(Directory.EnumerateFileSystemEntries(downloads), Is.Empty);
            }
        }

        [Test]
        public void ReleaseThatDoesNotSayWhichGameItNeedsIsRefused()
        {
            OmamoriRelease release = serveOmamori("1.0.0", null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(async () => await setup().SetUpAsync(instance, release), Throws.InstanceOf<InvalidDataException>());
                Assert.That(Directory.Exists(instance.Directory), Is.False);
                Assert.That(Directory.EnumerateFileSystemEntries(downloads), Is.Empty);
            }
        }

        [Test]
        public void ReleaseForAGameThatCannotBeDownloadedIsRefused()
        {
            OmamoriRelease release = serveOmamori("1.0.0", "1999.101.0-lazer");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(async () => await setup().SetUpAsync(instance, release), Throws.InstanceOf<InvalidDataException>());
                Assert.That(instance.IsInstalled, Is.False);
            }
        }

        [Test]
        public void TamperedReleaseIsRefusedBeforeAnythingIsInstalled()
        {
            OmamoriRelease release = serveOmamori("1.0.0", old_lazer) with { Sha256 = new string('0', 64) };

            using (Assert.EnterMultipleScope())
            {
                Assert.That(async () => await setup().SetUpAsync(instance, release), Throws.InstanceOf<InvalidDataException>());
                Assert.That(Directory.Exists(instance.Directory), Is.False);
            }
        }

        [Test]
        public void RunningInstanceIsLeftAlone()
        {
            OmamoriRelease release = serveOmamori("1.0.0", old_lazer);
            running = true;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(async () => await setup().SetUpAsync(instance, release), Throws.InvalidOperationException);
                Assert.That(handler.Requested, Is.Empty);
            }
        }

        [Test]
        public async Task ProgressGoesThroughTheStagesInOrder()
        {
            var progress = new RecordingProgress<SetupProgress>();

            await setup().SetUpAsync(instance, serveOmamori("1.0.0", old_lazer), progress);

            SetupStage[] stages = [.. progress.Reported.Select(p => p.Stage).Distinct()];
            SetupStage[] expected = [SetupStage.DownloadingOmamori, SetupStage.DownloadingOsu, SetupStage.Installing];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(stages, Is.EqualTo(expected));
                Assert.That(progress.Reported.Select(p => p.Fraction), Has.All.InRange(0, 1));
                Assert.That(progress.Reported[^1], Is.EqualTo(new SetupProgress(SetupStage.Installing, 1)));
            }
        }

        [Test]
        public void DefaultInstanceIsInToriisOwnFolder()
        {
            Assert.That(LazerInstance.DefaultDirectory(), Does.EndWith(Path.Combine("Torii", "instance")));
        }

        private InstanceSetup setup() => new InstanceSetup(http, downloads, _ => running);

        private OmamoriRelease serveOmamori(string version, string? lazerVersion)
        {
            byte[] zip = Fixtures.OmamoriZip(lazerVersion);
            var download = new Uri($"https://example.com/omamori-{version}.zip");
            handler.Add(download, zip);

            return new OmamoriRelease(version, false, download, Fixtures.Sha256(zip), zip.Length, null);
        }

        private void serveLazer(string version)
        {
            byte[] package = Fixtures.LazerPackage(version);
            var download = new Uri($"https://github.com/ppy/osu/releases/download/{version}/package.nupkg");

            handler.Add(download, package);
            handler.Add(LazerFeed.ReleaseUri(version), Fixtures.LazerRelease(version, package, download));
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Torii.Core.Mods;
using Torii.Core.Omamori;

namespace Torii.Tests
{
    [TestFixture]
    public class IndexedModInstallerTests
    {
        private static readonly Uri download = new Uri("https://example.com/someone.coolmod.zip");

        private string directory = null!;
        private string downloads = null!;
        private OmamoriInstall install = null!;
        private ModLibrary library = null!;
        private bool osuRunning;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            downloads = Path.Combine(directory, "downloads");
            Directory.CreateDirectory(downloads);

            install = new OmamoriInstall(Path.Combine(directory, "data"));
            Directory.CreateDirectory(install.DataDirectory);

            osuRunning = false;
            library = new ModLibrary(install, () => osuRunning);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, recursive: true);

        [Test]
        public async Task DownloadThatMatchesItsChecksumIsInstalled()
        {
            byte[] zip = modZip("someone.coolmod", "1.0.0");
            using var handler = new StubHandler(HttpStatusCode.OK, zip);
            using var http = new HttpClient(handler);

            InstalledMod installed = await new IndexedModInstaller(http, downloads).InstallAsync(library, mod("someone.coolmod"), version("1.0.0", Fixtures.Sha256(zip)));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(installed.Manifest!.Id, Is.EqualTo("someone.coolmod"));
                Assert.That(library.List(), Has.Count.EqualTo(1));
                Assert.That(handler.Requested, Is.EqualTo(download));
                Assert.That(Directory.EnumerateFileSystemEntries(downloads), Is.Empty);
            }
        }

        [Test]
        public async Task ProgressEndsAtOne()
        {
            byte[] zip = modZip("someone.coolmod", "1.0.0");
            using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, zip));
            var progress = new RecordingProgress<double>();

            await new IndexedModInstaller(http, downloads).InstallAsync(library, mod("someone.coolmod"), version("1.0.0", Fixtures.Sha256(zip)), progress);

            Assert.That(progress.Reported[^1], Is.EqualTo(1));
        }

        [Test]
        public async Task InstallingANewerVersionReplacesTheOldOne()
        {
            byte[] old = modZip("someone.coolmod", "1.0.0");
            byte[] updated = modZip("someone.coolmod", "2.0.0");

            using var first = new HttpClient(new StubHandler(HttpStatusCode.OK, old));
            await new IndexedModInstaller(first, downloads).InstallAsync(library, mod("someone.coolmod"), version("1.0.0", Fixtures.Sha256(old)));

            using var second = new HttpClient(new StubHandler(HttpStatusCode.OK, updated));
            await new IndexedModInstaller(second, downloads).InstallAsync(library, mod("someone.coolmod"), version("2.0.0", Fixtures.Sha256(updated)));

            Assert.That(library.List().Single().Manifest!.Version, Is.EqualTo("2.0.0"));
        }

        [Test]
        public void DownloadThatDoesNotMatchItsChecksumInstallsNothing()
        {
            using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, modZip("someone.coolmod", "1.0.0")));
            var installer = new IndexedModInstaller(http, downloads);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(async () => await installer.InstallAsync(library, mod("someone.coolmod"), version("1.0.0", new string('0', 64))), Throws.InstanceOf<InvalidDataException>());
                Assert.That(library.List(), Is.Empty);
                Assert.That(Directory.EnumerateFileSystemEntries(downloads), Is.Empty);
            }
        }

        [Test]
        public void ZipOfAnotherModIsRefused()
        {
            byte[] zip = modZip("someone.othermod", "1.0.0");
            using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, zip));
            var installer = new IndexedModInstaller(http, downloads);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(async () => await installer.InstallAsync(library, mod("someone.coolmod"), version("1.0.0", Fixtures.Sha256(zip))), Throws.InstanceOf<InvalidDataException>());
                Assert.That(library.List(), Is.Empty);
                Assert.That(Directory.EnumerateFileSystemEntries(downloads), Is.Empty);
            }
        }

        [Test]
        public void ZipOfAnotherVersionIsRefusedAndKeepsTheInstalledOne()
        {
            library.InstallFromZip(Fixtures.ZipFile(directory, "zip", modEntries("someone.coolmod", "1.0.0")));
            byte[] zip = modZip("someone.coolmod", "1.5.0");
            using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, zip));
            var installer = new IndexedModInstaller(http, downloads);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(async () => await installer.InstallAsync(library, mod("someone.coolmod"), version("2.0.0", Fixtures.Sha256(zip))), Throws.InstanceOf<InvalidDataException>());
                Assert.That(library.List().Single().Manifest!.Version, Is.EqualTo("1.0.0"));
            }
        }

        [Test]
        public void FailedDownloadLeavesNothingBehind()
        {
            using var http = new HttpClient(new StubHandler(HttpStatusCode.NotFound, []));
            var installer = new IndexedModInstaller(http, downloads);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(async () => await installer.InstallAsync(library, mod("someone.coolmod"), version("1.0.0", new string('0', 64))), Throws.InstanceOf<HttpRequestException>());
                Assert.That(Directory.EnumerateFileSystemEntries(downloads), Is.Empty);
            }
        }

        [Test]
        public void InstallingWhileOsuRunsIsRefusedAndLeavesNoFile()
        {
            byte[] zip = modZip("someone.coolmod", "1.0.0");
            using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, zip));
            var installer = new IndexedModInstaller(http, downloads);
            osuRunning = true;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(async () => await installer.InstallAsync(library, mod("someone.coolmod"), version("1.0.0", Fixtures.Sha256(zip))), Throws.InvalidOperationException);
                Assert.That(Directory.EnumerateFileSystemEntries(downloads), Is.Empty);
            }
        }

        private static IndexedMod mod(string id) => new IndexedMod(id, "Cool Mod", "someone", string.Empty, null, []);

        private static IndexedModVersion version(string number, string hash) => new IndexedModVersion(number, "0.2", download, hash, 0, []);

        private static (string Name, byte[] Content)[] modEntries(string id, string version) =>
        [
            ("mod.json", Encoding.UTF8.GetBytes($$"""{ "id": "{{id}}", "name": "Cool Mod", "version": "{{version}}", "entry": "Mod.dll", "apiVersion": "0.2" }""")),
            ("Mod.dll", Encoding.UTF8.GetBytes("dll")),
        ];

        private static byte[] modZip(string id, string version) => Fixtures.Zip(modEntries(id, version));
    }
}

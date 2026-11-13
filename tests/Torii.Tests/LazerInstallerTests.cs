using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using Torii.Core;

namespace Torii.Tests
{
    [TestFixture]
    public class LazerInstallerTests
    {
        private static readonly Uri download = new Uri("https://example.com/osulazer.nupkg");

        private string directory = null!;
        private string downloads = null!;
        private LazerInstance instance = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            downloads = Path.Combine(directory, "downloads");
            Directory.CreateDirectory(downloads);

            instance = new LazerInstance(Path.Combine(directory, "instance"));
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, recursive: true);

        [Test]
        public void EmptyFolderIsNotAnInstance()
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(instance.IsInstalled, Is.False);
                Assert.That(instance.ReadVersion(), Is.Null);
            }
        }

        [Test]
        public void PackageInstallsTheGameAndKeepsItsDataInTheFolder()
        {
            LazerInstaller.InstallFromPackage(instance, package("2026.1005.0-lazer"));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(instance.IsInstalled, Is.True);
                Assert.That(File.Exists(Path.Combine(instance.Directory, "osu!.exe")), Is.True);
                Assert.That(File.Exists(instance.PortableMarker), Is.True);
                Assert.That(instance.ReadVersion(), Is.EqualTo("2026.1005.0-lazer"));
                Assert.That(File.Exists(Path.Combine(instance.Directory, "runtimes", "win-x64", "native", "bass.dll")), Is.True);
            }
        }

        [Test]
        public void OnlyTheAppPartOfThePackageIsExtracted()
        {
            LazerInstaller.InstallFromPackage(instance, package("2026.1005.0-lazer"));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(File.Exists(Path.Combine(instance.Directory, "osulazer.nuspec")), Is.False);
                Assert.That(Directory.Exists(Path.Combine(instance.Directory, "lib")), Is.False);
            }
        }

        [Test]
        public void OmamoriInstallsIntoTheInstance()
        {
            Assert.That(instance.Omamori.DataDirectory, Is.EqualTo(instance.Directory));
        }

        [Test]
        public void UpdatingRemovesFilesTheNewVersionDropped()
        {
            LazerInstaller.InstallFromPackage(instance, package("2026.1005.0-lazer", "lib/app/Old.dll"));
            LazerInstaller.InstallFromPackage(instance, package("2026.1101.0-lazer"));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(File.Exists(Path.Combine(instance.Directory, "Old.dll")), Is.False);
                Assert.That(instance.ReadVersion(), Is.EqualTo("2026.1101.0-lazer"));
            }
        }

        [Test]
        public void UpdatingKeepsThePlayersData()
        {
            LazerInstaller.InstallFromPackage(instance, package("2026.1005.0-lazer"));
            string realm = Path.Combine(instance.Directory, "client.realm");
            string mod = Path.Combine(instance.Omamori.ModsDirectory, "someone.mod", "mod.json");
            string settings = Path.Combine(instance.Directory, "framework.ini");
            Directory.CreateDirectory(Path.GetDirectoryName(mod)!);
            File.WriteAllText(realm, "scores");
            File.WriteAllText(mod, "{}");
            File.WriteAllText(settings, "VolumeUniversal = 0.5");

            LazerInstaller.InstallFromPackage(instance, package("2026.1101.0-lazer"));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(File.ReadAllText(realm), Is.EqualTo("scores"));
                Assert.That(File.Exists(mod), Is.True);
                Assert.That(File.ReadAllText(settings), Is.EqualTo("VolumeUniversal = 0.5"));
            }
        }

        [Test]
        public void PackageWithoutTheGameIsRejected()
        {
            string path = zip("lib/app/readme.txt", "osulazer.nuspec");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(() => LazerInstaller.InstallFromPackage(instance, path), Throws.InstanceOf<InvalidDataException>());
                Assert.That(Directory.Exists(instance.Directory), Is.False);
            }
        }

        [TestCase("lib/app/../../../outside.txt")]
        [TestCase("lib/app/..%2F..%2Foutside.txt")]
        public void PackageReachingOutsideTheInstanceIsRejected(string entry)
        {
            string path = package("2026.1005.0-lazer", entry);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(() => LazerInstaller.InstallFromPackage(instance, path), Throws.InstanceOf<InvalidDataException>());
                Assert.That(Directory.EnumerateFiles(directory, "outside.txt", SearchOption.AllDirectories), Is.Empty);
                Assert.That(instance.IsInstalled, Is.False);
            }
        }

        [Test]
        public async Task DownloadThatMatchesItsChecksumIsInstalled()
        {
            byte[] bytes = File.ReadAllBytes(package("2026.1005.0-lazer"));
            using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, bytes));

            await new LazerInstaller(http, downloads).InstallAsync(instance, new LazerPackage("2026.1005.0-lazer", download, sha256(bytes), bytes.Length));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(instance.IsInstalled, Is.True);
                Assert.That(Directory.EnumerateFileSystemEntries(downloads), Is.Empty);
            }
        }

        [Test]
        public void DownloadThatDoesNotMatchItsChecksumIsNotInstalled()
        {
            byte[] bytes = File.ReadAllBytes(package("2026.1005.0-lazer"));
            using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, bytes));
            var installer = new LazerInstaller(http, downloads);
            var wrong = new LazerPackage("2026.1005.0-lazer", download, new string('0', 64), bytes.Length);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(async () => await installer.InstallAsync(instance, wrong), Throws.InstanceOf<InvalidDataException>());
                Assert.That(instance.IsInstalled, Is.False);
                Assert.That(Directory.EnumerateFileSystemEntries(downloads), Is.Empty);
            }
        }

        [Test]
        public void OlderGameIsNotInstalledOverANewerOne()
        {
            LazerInstaller.InstallFromPackage(instance, package("2026.1101.0-lazer"));
            using var handler = new StubHandler(HttpStatusCode.OK, []);
            using var http = new HttpClient(handler);
            var installer = new LazerInstaller(http, downloads);
            var older = new LazerPackage("2026.1005.0-lazer", download, new string('0', 64), 0);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(async () => await installer.InstallAsync(instance, older), Throws.InvalidOperationException);
                Assert.That(handler.Requested, Is.Null);
                Assert.That(instance.ReadVersion(), Is.EqualTo("2026.1101.0-lazer"));
            }
        }

        [TestCase("2026.1005.0-lazer", "2026.1005.0-lazer", 0)]
        [TestCase("2026.1005.1-lazer", "2026.1005.0-lazer", 1)]
        [TestCase("2026.921.1-lazer", "2026.1005.0-lazer", -1)]
        [TestCase("2027.101.0-lazer", "2026.1231.0-lazer", 1)]
        [TestCase("2026.1005-lazer", "2026.1005.0-lazer", 0)]
        [TestCase("nightly", "2026.1005.0-lazer", 0)]
        public void VersionsAreOrderedByTheirNumbers(string a, string b, int expected)
        {
            Assert.That(Math.Sign(LazerVersions.Compare(a, b)), Is.EqualTo(expected));
        }

        private static string sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

        // Same layout as a Velopack package
        private string package(string version, params string[] extraEntries)
        {
            string path = Path.Combine(directory, $"{Guid.NewGuid():N}.nupkg");

            using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);

            write(archive, "osulazer.nuspec", "metadata");
            write(archive, "lib/app/osu%21.exe", "exe");
            write(archive, "lib/app/osu.Game.dll", "dll");
            write(archive, "lib/app/runtimes/win-x64/native/bass.dll", "dll");
            write(archive, "lib/app/sq.version", $"""
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
                  <metadata>
                    <id>osulazer</id>
                    <version>{version}</version>
                  </metadata>
                </package>
                """);

            foreach (string entry in extraEntries)
                write(archive, entry, "extra");

            return path;
        }

        private string zip(params string[] entries)
        {
            string path = Path.Combine(directory, $"{Guid.NewGuid():N}.nupkg");

            using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);

            foreach (string entry in entries)
                write(archive, entry, "x");

            return path;
        }

        private static void write(ZipArchive archive, string name, string content)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(content);
        }
    }
}

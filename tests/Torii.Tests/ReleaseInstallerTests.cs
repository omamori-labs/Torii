using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Torii.Core;

namespace Torii.Tests
{
    [TestFixture]
    public class ReleaseInstallerTests
    {
        private static readonly Uri download = new Uri("https://example.com/omamori-1.0.0.zip");

        private string directory = null!;
        private string downloads = null!;
        private OmamoriInstall install = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            downloads = Path.Combine(directory, "downloads");
            Directory.CreateDirectory(downloads);

            install = new OmamoriInstall(Path.Combine(directory, "data"));
            Directory.CreateDirectory(install.DataDirectory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, recursive: true);

        [Test]
        public async Task DownloadThatMatchesItsChecksumIsInstalled()
        {
            byte[] zip = releaseZip();
            using var handler = new StubHandler(HttpStatusCode.OK, zip);
            using var http = new HttpClient(handler);

            await new ReleaseInstaller(http, downloads).InstallAsync(install, release(sha256(zip)));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(install.IsInstalled, Is.True);
                Assert.That(handler.Requested, Is.EqualTo(download));
                Assert.That(handler.UserAgent, Is.EqualTo("Torii"));
            }
        }

        [Test]
        public async Task ProgressEndsAtOneAndNeverGoesBack()
        {
            byte[] zip = releaseZip();
            using var handler = new StubHandler(HttpStatusCode.OK, zip);
            using var http = new HttpClient(handler);
            var progress = new RecordingProgress();

            await new ReleaseInstaller(http, downloads).InstallAsync(install, release(sha256(zip)), progress);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(progress.Reported, Is.Ordered.Ascending);
                Assert.That(progress.Reported, Has.All.InRange(0, 1));
                Assert.That(progress.Reported[^1], Is.EqualTo(1));
            }
        }

        [Test]
        public void DownloadThatDoesNotMatchItsChecksumIsNotInstalled()
        {
            using var handler = new StubHandler(HttpStatusCode.OK, releaseZip());
            using var http = new HttpClient(handler);
            var installer = new ReleaseInstaller(http, downloads);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(async () => await installer.InstallAsync(install, release(new string('0', 64))), Throws.InstanceOf<InvalidDataException>());
                Assert.That(install.IsInstalled, Is.False);
            }
        }

        [Test]
        public void ReleaseWithoutAChecksumIsNotDownloaded()
        {
            using var handler = new StubHandler(HttpStatusCode.OK, releaseZip());
            using var http = new HttpClient(handler);
            var installer = new ReleaseInstaller(http, downloads);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(async () => await installer.InstallAsync(install, release(null)), Throws.InstanceOf<InvalidDataException>());
                Assert.That(handler.Requested, Is.Null);
                Assert.That(install.IsInstalled, Is.False);
            }
        }

        [Test]
        public void FailedDownloadIsNotInstalled()
        {
            using var handler = new StubHandler(HttpStatusCode.NotFound, []);
            using var http = new HttpClient(handler);
            var installer = new ReleaseInstaller(http, downloads);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(async () => await installer.InstallAsync(install, release(new string('0', 64))), Throws.InstanceOf<HttpRequestException>());
                Assert.That(install.IsInstalled, Is.False);
            }
        }

        [Test]
        public void CancelledDownloadIsNotInstalled()
        {
            byte[] zip = releaseZip();
            using var handler = new StubHandler(HttpStatusCode.OK, zip);
            using var http = new HttpClient(handler);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(async () => await new ReleaseInstaller(http, downloads).InstallAsync(install, release(sha256(zip)), null, cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
                Assert.That(install.IsInstalled, Is.False);
            }
        }

        [Test]
        public async Task DownloadIsDeletedAfterInstalling()
        {
            byte[] zip = releaseZip();
            using var handler = new StubHandler(HttpStatusCode.OK, zip);
            using var http = new HttpClient(handler);

            await new ReleaseInstaller(http, downloads).InstallAsync(install, release(sha256(zip)));

            Assert.That(Directory.EnumerateFileSystemEntries(downloads), Is.Empty);
        }

        [Test]
        public void DownloadIsDeletedAfterFailing()
        {
            using var handler = new StubHandler(HttpStatusCode.OK, releaseZip());
            using var http = new HttpClient(handler);
            var installer = new ReleaseInstaller(http, downloads);

            Assert.That(async () => await installer.InstallAsync(install, release(new string('0', 64))), Throws.InstanceOf<InvalidDataException>());
            Assert.That(Directory.EnumerateFileSystemEntries(downloads), Is.Empty);
        }

        private static OmamoriRelease release(string? hash) => new OmamoriRelease("1.0.0", false, download, hash, 0, null);

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

        // Progress<T> reports on another thread and loses the order
        private sealed class RecordingProgress : IProgress<double>
        {
            public List<double> Reported { get; } = [];

            public void Report(double value) => Reported.Add(value);
        }
    }
}

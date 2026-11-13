using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
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
        private static readonly byte[] zip = Fixtures.OmamoriZip("2026.1005.0-lazer");

        private string downloads = null!;

        [SetUp]
        public void SetUp()
        {
            downloads = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(downloads);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(downloads, recursive: true);

        [Test]
        public async Task DownloadThatMatchesItsChecksumIsKept()
        {
            using var handler = new StubHandler(HttpStatusCode.OK, zip);
            using var http = new HttpClient(handler);

            string path = await new ReleaseInstaller(http, downloads).DownloadAsync(release(Fixtures.Sha256(zip)));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(zip));
                Assert.That(Path.GetDirectoryName(path), Is.EqualTo(downloads));
                Assert.That(handler.Requested, Is.EqualTo(download));
                Assert.That(handler.UserAgent, Is.EqualTo("Torii"));
            }
        }

        [Test]
        public async Task ProgressEndsAtOneAndNeverGoesBack()
        {
            using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, zip));
            var progress = new RecordingProgress();

            await new ReleaseInstaller(http, downloads).DownloadAsync(release(Fixtures.Sha256(zip)), progress);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(progress.Reported, Is.Ordered.Ascending);
                Assert.That(progress.Reported, Has.All.InRange(0, 1));
                Assert.That(progress.Reported[^1], Is.EqualTo(1));
            }
        }

        [Test]
        public void DownloadThatDoesNotMatchItsChecksumIsDeleted()
        {
            using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, zip));
            var installer = new ReleaseInstaller(http, downloads);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(async () => await installer.DownloadAsync(release(new string('0', 64))), Throws.InstanceOf<InvalidDataException>());
                Assert.That(Directory.EnumerateFileSystemEntries(downloads), Is.Empty);
            }
        }

        [Test]
        public void ReleaseWithoutAChecksumIsNotDownloaded()
        {
            using var handler = new StubHandler(HttpStatusCode.OK, zip);
            using var http = new HttpClient(handler);
            var installer = new ReleaseInstaller(http, downloads);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(async () => await installer.DownloadAsync(release(null)), Throws.InstanceOf<InvalidDataException>());
                Assert.That(handler.Requested, Is.Null);
            }
        }

        [Test]
        public void FailedDownloadLeavesNothingBehind()
        {
            using var http = new HttpClient(new StubHandler(HttpStatusCode.NotFound, []));
            var installer = new ReleaseInstaller(http, downloads);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(async () => await installer.DownloadAsync(release(new string('0', 64))), Throws.InstanceOf<HttpRequestException>());
                Assert.That(Directory.EnumerateFileSystemEntries(downloads), Is.Empty);
            }
        }

        [Test]
        public void CancelledDownloadLeavesNothingBehind()
        {
            using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, zip));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var installer = new ReleaseInstaller(http, downloads);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(async () => await installer.DownloadAsync(release(Fixtures.Sha256(zip)), null, cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
                Assert.That(Directory.EnumerateFileSystemEntries(downloads), Is.Empty);
            }
        }

        private static OmamoriRelease release(string? hash) => new OmamoriRelease("1.0.0", false, download, hash, 0, null);

        // Progress<T> loses the order
        private sealed class RecordingProgress : IProgress<double>
        {
            public List<double> Reported { get; } = [];

            public void Report(double value) => Reported.Add(value);
        }
    }
}

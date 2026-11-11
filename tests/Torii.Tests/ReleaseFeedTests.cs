using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Torii.Core;

namespace Torii.Tests
{
    [TestFixture]
    public class ReleaseFeedTests
    {
        private static string sample => File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Samples", "releases.json"));

        [Test]
        public void ReleasesWithAZipAreListedNewestFirst()
        {
            IReadOnlyList<OmamoriRelease> releases = ReleaseFeed.Parse(sample);
            string[] expected = ["0.1.0", "0.1.0-alpha.5", "0.1.0-alpha.3"];

            Assert.That(releases.Select(r => r.Version), Is.EqualTo(expected));
        }

        [Test]
        public void TheZipIsPickedOutOfTheOtherAssets()
        {
            OmamoriRelease release = ReleaseFeed.Parse(sample)[0];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(release.Download, Is.EqualTo(new Uri("https://github.com/omamori-labs/Omamori/releases/download/v0.1.0/omamori-0.1.0.zip")));
                Assert.That(release.Size, Is.EqualTo(345678));
                Assert.That(release.Published, Is.EqualTo(new DateTimeOffset(2026, 11, 20, 18, 30, 0, TimeSpan.Zero)));
            }
        }

        [Test]
        public void PrereleasesAreMarked()
        {
            IReadOnlyList<OmamoriRelease> releases = ReleaseFeed.Parse(sample);
            bool[] expected = [false, true, true];

            Assert.That(releases.Select(r => r.Prerelease), Is.EqualTo(expected));
        }

        [Test]
        public void HashIsLowercaseHexWithoutThePrefix()
        {
            Assert.That(ReleaseFeed.Parse(sample)[0].Sha256, Is.EqualTo("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789"));
        }

        [Test]
        public void ReleaseWithoutADigestHasNoHash()
        {
            Assert.That(ReleaseFeed.Parse(sample)[2].Sha256, Is.Null);
        }

        [TestCase("sha256:tooshort")]
        [TestCase("sha512:996e59d331d551b034820442567b2b7636317185359d38a40bb5cc957b7bacbf")]
        [TestCase("sha256:zz6e59d331d551b034820442567b2b7636317185359d38a40bb5cc957b7bacbf")]
        public void DigestThatIsNotASha256HasNoHash(string digest)
        {
            string json = $$"""
                [{ "tag_name": "v1.0.0", "assets": [{ "name": "omamori-1.0.0.zip", "digest": "{{digest}}", "browser_download_url": "https://example.com/omamori-1.0.0.zip" }] }]
                """;

            Assert.That(ReleaseFeed.Parse(json).Single().Sha256, Is.Null);
        }

        [Test]
        public void NoReleasesIsAnEmptyList()
        {
            Assert.That(ReleaseFeed.Parse("[]"), Is.Empty);
        }

        [TestCase("{ \"message\": \"API rate limit exceeded\" }")]
        [TestCase("<html>not json</html>")]
        public void AnythingButAListOfReleasesIsRejected(string json)
        {
            Assert.That(() => ReleaseFeed.Parse(json), Throws.InstanceOf<JsonException>());
        }

        [Test]
        public async Task ListingAsksGitHubWithAUserAgent()
        {
            using var handler = new RecordingHandler(HttpStatusCode.OK, sample);
            using var http = new HttpClient(handler);

            IReadOnlyList<OmamoriRelease> releases = await new ReleaseFeed(http).ListAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(releases, Has.Count.EqualTo(3));
                Assert.That(handler.Requested, Is.EqualTo(ReleaseFeed.GitHub));
                Assert.That(handler.UserAgent, Is.EqualTo("Torii"));
            }
        }

        [Test]
        public void FailedRequestIsNotAnEmptyList()
        {
            using var handler = new RecordingHandler(HttpStatusCode.Forbidden, "{ \"message\": \"API rate limit exceeded\" }");
            using var http = new HttpClient(handler);

            Assert.That(async () => await new ReleaseFeed(http).ListAsync(), Throws.InstanceOf<HttpRequestException>());
        }

        // answers every request itself, so nothing here reaches the network
        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode status;
            private readonly string body;

            public RecordingHandler(HttpStatusCode status, string body)
            {
                this.status = status;
                this.body = body;
            }

            public Uri? Requested { get; private set; }

            public string? UserAgent { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requested = request.RequestUri;
                UserAgent = request.Headers.UserAgent.ToString();

                return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
            }
        }
    }
}

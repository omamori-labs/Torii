using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Torii.Core.Lazer;

namespace Torii.Tests
{
    [TestFixture]
    public class LazerFeedTests
    {
        private const string version = "2026.1005.0-lazer";
        private const string asset = "osulazer-2026.1005.0-lazer-full.nupkg";
        private const string hash = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

        [Test]
        [Platform("Win")]
        public void WindowsPackageIsTheFullNupkg()
        {
            Assert.That(LazerFeed.AssetName(version), Is.EqualTo(asset));
        }

        [Test]
        [Platform("Linux")]
        public void LinuxPackageIsTheLinuxNupkg()
        {
            Assert.That(LazerFeed.AssetName(version), Is.EqualTo("osulazer-2026.1005.0-lazer-linux-x64-full.nupkg"));
        }

        [Test]
        public void PackageIsPickedOutOfTheOtherAssets()
        {
            LazerPackage package = LazerFeed.Parse(release(("install.exe", $"sha256:{hash}"), (asset, $"sha256:{hash.ToUpperInvariant()}")), version, asset);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(package.Version, Is.EqualTo(version));
                Assert.That(package.Download, Is.EqualTo(new Uri($"https://github.com/ppy/osu/releases/download/{version}/{asset}")));
                Assert.That(package.Sha256, Is.EqualTo(hash));
                Assert.That(package.Size, Is.EqualTo(1234));
            }
        }

        [Test]
        public void ReleaseWithoutThePackageIsRejected()
        {
            string json = release(("install.exe", $"sha256:{hash}"));

            Assert.That(() => LazerFeed.Parse(json, version, asset), Throws.InstanceOf<InvalidDataException>());
        }

        [TestCase(null)]
        [TestCase("sha256:tooshort")]
        [TestCase("md5:abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abc")]
        public void PackageWithoutASha256IsRejected(string? digest)
        {
            string json = release((asset, digest));

            Assert.That(() => LazerFeed.Parse(json, version, asset), Throws.InstanceOf<InvalidDataException>());
        }

        [TestCase("not json")]
        [TestCase("[]")]
        [TestCase("{ \"message\": \"API rate limit exceeded\" }")]
        public void AnythingButAReleaseIsRejected(string json)
        {
            Assert.That(() => LazerFeed.Parse(json, version, asset), Throws.InstanceOf<InvalidDataException>());
        }

        [Test]
        [Platform("Win,Linux")]
        public async Task FindingAsksGitHubForThatTag()
        {
            using var handler = new StubHandler(HttpStatusCode.OK, Encoding.UTF8.GetBytes(release((LazerFeed.AssetName(version), $"sha256:{hash}"))));
            using var http = new HttpClient(handler);

            LazerPackage package = await new LazerFeed(http).FindAsync(version);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(package.Sha256, Is.EqualTo(hash));
                Assert.That(handler.Requested, Is.EqualTo(new Uri("https://api.github.com/repos/ppy/osu/releases/tags/2026.1005.0-lazer")));
                Assert.That(handler.UserAgent, Is.EqualTo("Torii"));
            }
        }

        [Test]
        public void VersionThatDoesNotExistIsRejected()
        {
            using var handler = new StubHandler(HttpStatusCode.NotFound, []);
            using var http = new HttpClient(handler);

            Assert.That(async () => await new LazerFeed(http).FindAsync(version), Throws.InstanceOf<InvalidDataException>());
        }

        [TestCase("../../other/repo")]
        [TestCase("2026.1005.0-lazer?x=1")]
        [TestCase("")]
        public void SomethingThatIsNotAVersionIsNeverRequested(string notAVersion)
        {
            using var handler = new StubHandler(HttpStatusCode.OK, []);
            using var http = new HttpClient(handler);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(async () => await new LazerFeed(http).FindAsync(notAVersion), Throws.InstanceOf<InvalidDataException>());
                Assert.That(handler.Requested, Is.Null);
            }
        }

        [Test]
        public void RateLimitIsNotMistakenForAMissingVersion()
        {
            using var handler = new StubHandler(HttpStatusCode.Forbidden, []);
            using var http = new HttpClient(handler);

            Assert.That(async () => await new LazerFeed(http).FindAsync(version), Throws.InstanceOf<HttpRequestException>());
        }

        private static string release(params (string Name, string? Digest)[] assets)
        {
            var json = new StringBuilder("{ \"tag_name\": \"2026.1005.0-lazer\", \"assets\": [");

            for (int i = 0; i < assets.Length; i++)
            {
                (string name, string? digest) = assets[i];
                string digestJson = digest == null ? "null" : $"\"{digest}\"";

                json.Append(i > 0 ? "," : string.Empty);
                json.Append("{ \"name\": \"").Append(name).Append("\", \"size\": 1234, \"digest\": ").Append(digestJson);
                json.Append(", \"browser_download_url\": \"https://github.com/ppy/osu/releases/download/2026.1005.0-lazer/").Append(name).Append("\" }");
            }

            return json.Append("] }").ToString();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Torii.Core.Mods;

namespace Torii.Tests
{
    [TestFixture]
    public class ModIndexFeedTests
    {
        private static string sample => File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Samples", "mod-index.json"));

        [Test]
        public void OnlyValidModsAreListed()
        {
            string[] expected = ["someone.hitcounter", "other.skinswitch"];

            Assert.That(ModIndexFeed.Parse(sample).Select(m => m.Id), Is.EqualTo(expected));
        }

        [Test]
        public void FirstEntryWinsWhenAnIdIsListedTwice()
        {
            Assert.That(ModIndexFeed.Parse(sample)[0].Name, Is.EqualTo("Hit counter"));
        }

        [Test]
        public void VersionsAreNewestFirst()
        {
            string[] expected = ["1.2.0", "1.1.0", "1.0.0"];

            Assert.That(ModIndexFeed.Parse(sample)[0].Versions.Select(v => v.Version), Is.EqualTo(expected));
        }

        [Test]
        public void LatestIsTheNewestVersion()
        {
            IndexedModVersion latest = ModIndexFeed.Parse(sample)[0].Latest;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(latest.Version, Is.EqualTo("1.2.0"));
                Assert.That(latest.ApiVersion, Is.EqualTo("1.1"));
                Assert.That(latest.Download, Is.EqualTo(new Uri("https://github.com/someone/hitcounter/releases/download/v1.2.0/someone.hitcounter-1.2.0.zip")));
                Assert.That(latest.Size, Is.EqualTo(2048));
            }
        }

        [Test]
        public void HashIsLowercase()
        {
            Assert.That(ModIndexFeed.Parse(sample)[0].Latest.Sha256, Is.EqualTo("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789"));
        }

        [Test]
        public void OptionalFieldsCanBeLeftOut()
        {
            IndexedMod mod = ModIndexFeed.Parse(sample)[1];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(mod.Author, Is.Empty);
                Assert.That(mod.Description, Is.Empty);
                Assert.That(mod.Repository, Is.Null);
                Assert.That(mod.Latest.Size, Is.Zero);
            }
        }

        [Test]
        public void DependenciesAreReadLikeInAManifest()
        {
            IReadOnlyList<ModDependency> dependencies = ModIndexFeed.Parse(sample)[0].Latest.Dependencies;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(dependencies, Has.Count.EqualTo(2));
                Assert.That(dependencies[0], Is.EqualTo(new ModDependency("other.skinswitch", "0.2.0", false)));
                Assert.That(dependencies[1], Is.EqualTo(new ModDependency("optional.extra", string.Empty, true)));
                Assert.That(ModIndexFeed.Parse(sample)[1].Latest.Dependencies, Is.Empty);
            }
        }

        [Test]
        public void RepositoryIsKept()
        {
            Assert.That(ModIndexFeed.Parse(sample)[0].Repository, Is.EqualTo(new Uri("https://github.com/someone/hitcounter")));
        }

        [Test]
        public void ModWhoseVersionsAreAllInvalidIsLeftOut()
        {
            string json = """
                { "mods": [{ "id": "a.b", "name": "A", "versions": [{ "version": "not a version", "apiVersion": "1.0", "download": "https://example.com/a.zip", "sha256": "1111111111111111111111111111111111111111111111111111111111111111" }] }] }
                """;

            Assert.That(ModIndexFeed.Parse(json), Is.Empty);
        }

        [Test]
        public void NoModsIsAnEmptyList()
        {
            Assert.That(ModIndexFeed.Parse("""{ "mods": [] }"""), Is.Empty);
        }

        [TestCase("[]")]
        [TestCase("""{ "message": "Not Found" }""")]
        [TestCase("404: Not Found")]
        public void AnythingButAnIndexIsRejected(string json)
        {
            Assert.That(() => ModIndexFeed.Parse(json), Throws.InstanceOf<JsonException>());
        }

        [Test]
        public async Task ListingFetchesTheDefaultIndex()
        {
            using var handler = new StubHandler(HttpStatusCode.OK, Encoding.UTF8.GetBytes(sample));
            using var http = new HttpClient(handler);

            IReadOnlyList<IndexedMod> mods = await new ModIndexFeed(http).ListAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(mods, Has.Count.EqualTo(2));
                Assert.That(handler.Requested, Is.EqualTo(ModIndexFeed.Default));
                Assert.That(handler.UserAgent, Is.EqualTo("Torii"));
            }
        }

        [Test]
        public void FailedRequestIsNotAnEmptyList()
        {
            using var handler = new StubHandler(HttpStatusCode.NotFound, Encoding.UTF8.GetBytes("404: Not Found"));
            using var http = new HttpClient(handler);

            Assert.That(async () => await new ModIndexFeed(http).ListAsync(), Throws.InstanceOf<HttpRequestException>());
        }
    }
}

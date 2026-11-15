using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Torii.Core.Omamori;
using Torii.Core.Releases;

namespace Torii.Tests
{
    [TestFixture]
    public class ReleaseChannelTests
    {
        [TestCase("0.1.0-alpha.1", true)]
        [TestCase("0.1.0-alpha.1+3f2a9c", true)]
        [TestCase("0.1.0-alpha.0.14+3f2a9c", true)]
        [TestCase("1.0.0-rc.1", true)]
        [TestCase("1.0.0", false)]
        [TestCase("1.0.0+3f2a9c", false)]
        [TestCase("1.2.3+build-5", false)]
        [TestCase("", false)]
        public void OnlyAVersionWithAPrereleaseTagIsAPrerelease(string version, bool expected)
        {
            Assert.That(ReleaseChannel.IsPrerelease(version), Is.EqualTo(expected));
        }

        [Test]
        public void ReleaseInstallIsOfferedTheNewestRelease()
        {
            Assert.That(ReleaseChannel.NewerThan(list("2.0.0", "1.5.0", "1.0.0"), "1.0.0"), Is.EqualTo("2.0.0"));
        }

        [Test]
        public void ReleaseInstallIsNotOfferedAPrerelease()
        {
            Assert.That(ReleaseChannel.NewerThan(list("2.0.0-alpha.1", "1.0.0"), "1.0.0"), Is.Null);
        }

        [Test]
        public void ReleaseInstallSkipsPrereleasesForTheNewestRelease()
        {
            Assert.That(ReleaseChannel.NewerThan(list("2.0.0-alpha.1", "1.5.0", "1.0.0"), "1.0.0"), Is.EqualTo("1.5.0"));
        }

        [Test]
        public void PrereleaseInstallIsOfferedTheNewestOfAnyKind()
        {
            Assert.That(ReleaseChannel.NewerThan(list("2.0.0-alpha.2", "2.0.0-alpha.1", "1.0.0-alpha.1"), "1.0.0-alpha.1"), Is.EqualTo("2.0.0-alpha.2"));
        }

        [Test]
        public void NewestInstallHasNothingNewer()
        {
            Assert.That(ReleaseChannel.NewerThan(list("2.0.0", "1.0.0"), "2.0.0"), Is.Null);
        }

        [Test]
        public void LocalBuildThatIsNotListedHasNothingToCompareWith()
        {
            Assert.That(ReleaseChannel.NewerThan(list("2.0.0", "1.0.0"), "9.9.9-local"), Is.Null);
        }

        [Test]
        public void NoReleasesMeansNothingNewer()
        {
            Assert.That(ReleaseChannel.NewerThan(list(), "1.0.0"), Is.Null);
        }

        private static IReadOnlyList<OmamoriRelease> list(params string[] versions) =>
            [.. versions.Select(version => new OmamoriRelease(version, ReleaseChannel.IsPrerelease(version), new Uri("https://example.com/omamori.zip"), null, 0, null))];
    }
}

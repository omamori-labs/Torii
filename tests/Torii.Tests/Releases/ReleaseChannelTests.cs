using NUnit.Framework;
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
    }
}

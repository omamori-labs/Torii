using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Torii.Core;

namespace Torii.Tests
{
    [TestFixture]
    public class LazerAppImageTests
    {
        private string directory = null!;
        private string work = null!;
        private LazerInstance instance = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            work = Path.Combine(directory, "work");
            Directory.CreateDirectory(work);

            instance = new LazerInstance(Path.Combine(directory, "instance"));
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, recursive: true);

        [Test]
        public void LinuxPackageInstallsTheGameFromItsAppImage()
        {
            LazerInstaller.InstallFromPackage(instance, package(), work, unpacking("2026.1005.0-lazer"));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(instance.IsInstalled, Is.True);
                Assert.That(instance.ReadVersion(), Is.EqualTo("2026.1005.0-lazer"));
                Assert.That(File.Exists(Path.Combine(instance.Directory, "osu!")), Is.True);
                Assert.That(File.Exists(Path.Combine(instance.Directory, "runtimes", "linux-x64", "native", "libbass.so")), Is.True);
                Assert.That(File.Exists(instance.PortableMarker), Is.True);
            }
        }

        [Test]
        public void OnlyTheGameFolderOfTheAppImageIsCopied()
        {
            LazerInstaller.InstallFromPackage(instance, package(), work, unpacking("2026.1005.0-lazer"));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(File.Exists(Path.Combine(instance.Directory, "AppRun")), Is.False);
                Assert.That(Directory.Exists(Path.Combine(instance.Directory, "squashfs-root")), Is.False);
                Assert.That(Directory.EnumerateFiles(instance.Directory, "*.AppImage"), Is.Empty);
            }
        }

        [Test]
        public void TheAppImageIsUnpackedWhereItWasExtracted()
        {
            string? unpacked = null;
            string? unpackedIn = null;

            LazerInstaller.InstallFromPackage(instance, package(), work, (appImage, folder) =>
            {
                unpacked = File.ReadAllText(appImage);
                unpackedIn = folder;
                unpacking("2026.1005.0-lazer")(appImage, folder);
            });

            using (Assert.EnterMultipleScope())
            {
                Assert.That(unpacked, Is.EqualTo("appimage"));
                Assert.That(Path.GetDirectoryName(unpackedIn), Is.EqualTo(work));
            }
        }

        [Test]
        public void NothingIsLeftInTheWorkFolder()
        {
            LazerInstaller.InstallFromPackage(instance, package(), work, unpacking("2026.1005.0-lazer"));

            Assert.That(Directory.EnumerateFileSystemEntries(work), Is.Empty);
        }

        [Test]
        public void UpdatingRemovesFilesTheNewVersionDroppedAndKeepsData()
        {
            LazerInstaller.InstallFromPackage(instance, package(), work, unpacking("2026.1005.0-lazer", "Old.dll"));
            string realm = Path.Combine(instance.Directory, "client.realm");
            File.WriteAllText(realm, "scores");

            LazerInstaller.InstallFromPackage(instance, package(), work, unpacking("2026.1101.0-lazer"));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(instance.ReadVersion(), Is.EqualTo("2026.1101.0-lazer"));
                Assert.That(File.Exists(Path.Combine(instance.Directory, "Old.dll")), Is.False);
                Assert.That(File.ReadAllText(realm), Is.EqualTo("scores"));
            }
        }

        [Test]
        public void AppImageWithoutTheGameIsRejected()
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(() => LazerInstaller.InstallFromPackage(instance, package(), work, (_, folder) => Directory.CreateDirectory(Path.Combine(folder, "squashfs-root"))),
                    Throws.InstanceOf<InvalidDataException>());
                Assert.That(instance.IsInstalled, Is.False);
                Assert.That(Directory.EnumerateFileSystemEntries(work), Is.Empty);
            }
        }

        [Test]
        public void FailedUnpackLeavesNoInstanceAndNoWorkFiles()
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(() => LazerInstaller.InstallFromPackage(instance, package(), work, (_, _) => throw new InvalidDataException("exit 1")),
                    Throws.InstanceOf<InvalidDataException>());
                Assert.That(instance.IsInstalled, Is.False);
                Assert.That(Directory.EnumerateFileSystemEntries(work), Is.Empty);
            }
        }

        [Test]
        [Platform("Win")]
        public void LinuxPackageCannotBeUnpackedOnWindows()
        {
            Assert.That(() => LazerInstaller.InstallFromPackage(instance, package(), work), Throws.InstanceOf<PlatformNotSupportedException>());
        }

        private string package()
        {
            string path = Path.Combine(directory, $"{Guid.NewGuid():N}.nupkg");

            File.WriteAllBytes(path, Fixtures.Zip(
                ("osulazer.nuspec", "metadata"u8.ToArray()),
                ("lib/app/osu.AppImage", "appimage"u8.ToArray())));

            return path;
        }

        // Stands in for running the AppImage with --appimage-extract
        private static Action<string, string> unpacking(string version, params string[] extraFiles) => (_, folder) =>
        {
            string root = Path.Combine(folder, "squashfs-root");
            string game = Path.Combine(root, "usr", "bin");
            Directory.CreateDirectory(Path.Combine(game, "runtimes", "linux-x64", "native"));

            File.WriteAllText(Path.Combine(root, "AppRun"), "run");
            File.WriteAllText(Path.Combine(game, "osu!"), "exe");
            File.WriteAllText(Path.Combine(game, "osu.Game.dll"), "dll");
            File.WriteAllText(Path.Combine(game, "runtimes", "linux-x64", "native", "libbass.so"), "so");
            File.WriteAllText(Path.Combine(game, "sq.version"), $"<package><metadata><version>{version}</version></metadata></package>");

            foreach (string extra in extraFiles.Select(f => Path.Combine(game, f)))
                File.WriteAllText(extra, "extra");
        };
    }
}

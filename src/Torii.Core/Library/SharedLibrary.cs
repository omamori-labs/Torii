using System;
using System.IO;
using System.Linq;
using Torii.Core.Lazer;

namespace Torii.Core.Library
{
    public sealed class SharedLibrary
    {
        private const int error_privilege_not_held = 1314;

        private readonly Func<LazerInstance, string, bool>? elevate;
        private readonly Func<bool> canLink;

        public SharedLibrary(string directory, Func<LazerInstance, string, bool>? elevate = null)
            : this(directory, elevate, null)
        {
        }

        internal SharedLibrary(string directory, Func<LazerInstance, string, bool>? elevate, Func<bool>? canLink)
        {
            Root = Path.GetFullPath(directory);
            this.elevate = elevate;
            this.canLink = canLink ?? canCreateLinks;
        }

        public string Root { get; }

        public static string DefaultDirectory() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Torii", "library");

        public string FilesDirectory => Path.Combine(Root, "files");

        public string DatabaseFile => Path.Combine(Root, "client.realm");

        internal bool IsUsable => !Directory.Exists(Root) || File.Exists(markerFile) || !Directory.EnumerateFileSystemEntries(Root).Any();

        private string markerFile => Path.Combine(Root, "torii-library.txt");

        public void Attach(LazerInstance instance)
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(instance.Directory);

            if (!File.Exists(markerFile))
                File.WriteAllText(markerFile, string.Empty);

            if (isDone(instance.FilesDirectory, FilesDirectory, isDirectory: true) && isDone(instance.DatabaseFile, DatabaseFile, isDirectory: false))
                return;

            if (!canLink())
            {
                if (elevate?.Invoke(instance, Root) != true)
                    throw new InvalidOperationException("Linking the library needs a permission Windows hasn't given: Developer Mode or administrator.");

                return;
            }

            attach(instance.FilesDirectory, FilesDirectory, isDirectory: true);
            attach(instance.DatabaseFile, DatabaseFile, isDirectory: false);
        }

        public string? ReadVersion()
        {
            if (!File.Exists(markerFile))
                return null;

            string version = File.ReadAllText(markerFile).Trim();
            return version.Length > 0 ? version : null;
        }

        public void Use(LazerInstance instance)
        {
            string version = instance.ReadVersion()
                             ?? throw new InvalidOperationException("Torii can't tell which osu! version this instance has, so it can't tell whether the library is safe to open.");
            string? newest = ReadVersion();

            if (newest != null && LazerVersions.Compare(newest, version) > 0)
                throw new InvalidOperationException($"The library was last opened by osu! {newest}. Opening it with the older {version} would reset it, so update this instance first.");

            if (newest == null || LazerVersions.Compare(version, newest) > 0)
                File.WriteAllText(markerFile, version);
        }

        private static string? target(string local, bool isDirectory)
        {
            FileSystemInfo info = isDirectory ? new DirectoryInfo(local) : new FileInfo(local);

            return info.LinkTarget is string current ? Path.GetFullPath(current, Path.GetDirectoryName(local)!) : null;
        }

        private static bool isDone(string local, string shared, bool isDirectory) =>
            target(local, isDirectory) is string resolved
                ? Paths.Same(resolved, shared)
                : !isDirectory && !File.Exists(local) && !File.Exists(shared);

        private static void attach(string local, string shared, bool isDirectory)
        {
            if (target(local, isDirectory) is string resolved)
            {
                if (!Paths.Same(resolved, shared))
                    throw new InvalidOperationException($"{local} is linked to {resolved}, which is a different library.");

                return;
            }

            bool hasLocal = isDirectory ? Directory.Exists(local) : File.Exists(local);
            bool hasShared = isDirectory ? Directory.Exists(shared) : File.Exists(shared);

            if (hasLocal && hasShared)
                throw new InvalidOperationException($"{local} and the library both have their own copy.");

            if (hasLocal)
            {
                if (isDirectory)
                    Directory.Move(local, shared);
                else
                    File.Move(local, shared);
            }
            else if (!hasShared)
            {
                if (!isDirectory)
                    return;

                Directory.CreateDirectory(shared);
            }

            if (isDirectory)
                Directory.CreateSymbolicLink(local, shared);
            else
                File.CreateSymbolicLink(local, shared);
        }

        private bool canCreateLinks()
        {
            string probe = Path.Combine(Root, $"probe-{Guid.NewGuid():N}");

            try
            {
                File.CreateSymbolicLink(probe, Root);
                return true;
            }
            // Symlinks need Developer Mode or admin
            catch (IOException e) when (OperatingSystem.IsWindows() && (e.HResult & 0xFFFF) == error_privilege_not_held)
            {
                return false;
            }
            finally
            {
                File.Delete(probe);
            }
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Torii.Core.Library
{
    public sealed class LibrarySeed
    {
        private readonly Func<bool> osuRunning;

        public LibrarySeed(Func<bool>? osuRunning = null)
        {
            this.osuRunning = osuRunning ?? anyOsuRunning;
        }

        public static bool IsEmpty(SharedLibrary library) =>
            !File.Exists(library.DatabaseFile) && !(Directory.Exists(library.FilesDirectory) && Directory.EnumerateFileSystemEntries(library.FilesDirectory).Any());

        public Task ImportAsync(SharedLibrary library, string source, string? version, IProgress<double>? progress = null, CancellationToken cancellation = default) =>
            Task.Run(() => import(library, source, version, progress, cancellation), cancellation);

        private static bool anyOsuRunning()
        {
            Process[] processes = Process.GetProcessesByName(OsuProcess.PROCESS_NAME);

            foreach (Process process in processes)
                process.Dispose();

            return processes.Length > 0;
        }

        private void import(SharedLibrary library, string source, string? version, IProgress<double>? progress, CancellationToken cancellation)
        {
            string sourceDatabase = Path.Combine(source, "client.realm");
            string sourceFiles = Path.Combine(source, "files");

            if (!File.Exists(sourceDatabase))
                throw new InvalidOperationException("There is no osu! library there to import.");

            if (!IsEmpty(library))
                throw new InvalidOperationException("The library already has maps in it, and Torii won't mix them with another library.");

            if (osuRunning())
                throw new InvalidOperationException("Close osu! first, it keeps its library locked while it runs.");

            library.Mark();

            string staging = Path.Combine(library.Root, $"import-{Guid.NewGuid():N}");

            try
            {
                string stagedFiles = Path.Combine(staging, "files");
                Directory.CreateDirectory(stagedFiles);

                string[] files = Directory.Exists(sourceFiles) ? Directory.GetFiles(sourceFiles, "*", SearchOption.AllDirectories) : [];
                bool canLink = true;

                for (int i = 0; i < files.Length; i++)
                {
                    cancellation.ThrowIfCancellationRequested();

                    string destination = Path.Combine(stagedFiles, Path.GetRelativePath(sourceFiles, files[i]));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                    if (!canLink || !HardLinks.TryCreate(destination, files[i]))
                    {
                        canLink = false;
                        File.Copy(files[i], destination);
                    }

                    progress?.Report((i + 1d) / (files.Length + 1));
                }

                cancellation.ThrowIfCancellationRequested();

                // The game rewrites the database in place, so it can't be a link
                string stagedDatabase = Path.Combine(staging, "client.realm");
                File.Copy(sourceDatabase, stagedDatabase);

                Directory.CreateDirectory(library.FilesDirectory);

                foreach (string entry in Directory.GetFileSystemEntries(stagedFiles))
                {
                    string destination = Path.Combine(library.FilesDirectory, Path.GetFileName(entry));

                    if (Directory.Exists(entry))
                        Directory.Move(entry, destination);
                    else
                        File.Move(entry, destination);
                }

                File.Move(stagedDatabase, library.DatabaseFile);
                progress?.Report(1);
            }
            finally
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
            }

            if (version != null)
                library.Claim(version);
        }
    }
}

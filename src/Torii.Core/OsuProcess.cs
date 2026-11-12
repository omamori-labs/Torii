using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Torii.Core
{
    /// <summary>While lazer runs it locks Omamori's files and rewrites the state files from memory</summary>
    public static class OsuProcess
    {
        public const string PROCESS_NAME = "osu!";

        public static bool IsRunning() => anyRunning(IsLazer);

        /// <summary>Whether the copy of the game in this folder is open. The player's own osu! doesn't count</summary>
        public static bool IsRunningIn(string directory) => anyRunning(executable => IsIn(executable, directory));

        /// <summary>Stable has the same process name on Windows. Lazer has osu.Game.dll next to it</summary>
        internal static bool IsLazer(string? executable)
        {
            if (executable == null || !OperatingSystem.IsWindows())
                return true;

            return File.Exists(Path.Combine(Path.GetDirectoryName(executable) ?? string.Empty, "osu.Game.dll"));
        }

        /// <summary>A process that can't be inspected counts as being in the folder</summary>
        internal static bool IsIn(string? executable, string directory) =>
            executable == null || Paths.IsInside(Path.GetFullPath(directory), Path.GetFullPath(executable));

        private static bool anyRunning(Func<string?, bool> matches)
        {
            Process[] processes = Process.GetProcessesByName(PROCESS_NAME);

            try
            {
                foreach (Process process in processes)
                {
                    if (matchesProcess(process, matches))
                        return true;
                }

                return false;
            }
            finally
            {
                foreach (Process process in processes)
                    process.Dispose();
            }
        }

        private static bool matchesProcess(Process process, Func<string?, bool> matches)
        {
            try
            {
                return matches(process.MainModule?.FileName);
            }
            catch (InvalidOperationException)
            {
                // Exited while being inspected
                return false;
            }
            catch (Win32Exception)
            {
                // Access denied, so it can't be ruled out
                return true;
            }
        }
    }
}

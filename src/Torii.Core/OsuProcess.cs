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

        /// <summary>Whether the copy of the game in this folder is open. The player's own osu! doesn't count</summary>
        public static bool IsRunningIn(string directory)
        {
            Process[] processes = Process.GetProcessesByName(PROCESS_NAME);

            try
            {
                foreach (Process process in processes)
                {
                    if (isIn(process, directory))
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

        /// <summary>A process that can't be inspected counts as being in the folder</summary>
        internal static bool IsIn(string? executable, string directory) =>
            executable == null || Paths.IsInside(Path.GetFullPath(directory), Path.GetFullPath(executable));

        private static bool isIn(Process process, string directory)
        {
            try
            {
                return IsIn(process.MainModule?.FileName, directory);
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

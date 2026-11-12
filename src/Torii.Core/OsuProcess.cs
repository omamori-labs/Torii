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

        public static bool IsRunning()
        {
            Process[] processes = Process.GetProcessesByName(PROCESS_NAME);

            try
            {
                foreach (Process process in processes)
                {
                    if (isLazer(process))
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

        /// <summary>Stable has the same process name on Windows. Lazer has osu.Game.dll next to it</summary>
        internal static bool IsLazer(string? executable)
        {
            if (executable == null || !OperatingSystem.IsWindows())
                return true;

            return File.Exists(Path.Combine(Path.GetDirectoryName(executable) ?? string.Empty, "osu.Game.dll"));
        }

        private static bool isLazer(Process process)
        {
            try
            {
                return IsLazer(process.MainModule?.FileName);
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

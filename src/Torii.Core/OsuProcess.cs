using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Torii.Core
{
    /// <summary>
    ///     whether osu!lazer is open
    ///     while it is, it has omamori's files loaded and overwrites omamori's state files with what it has in memory
    /// </summary>
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

        /// <summary>osu!stable runs under the same name on windows, lazer is the one with the game library next to it</summary>
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
                // it closed while we were looking
                return false;
            }
            catch (Win32Exception)
            {
                // not ours to inspect, so it can't be ruled out
                return true;
            }
        }
    }
}

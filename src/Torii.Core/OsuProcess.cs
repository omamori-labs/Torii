using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Torii.Core
{
    public static class OsuProcess
    {
        public const string PROCESS_NAME = "osu!";

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

        public static string? FindRunning(Func<string, bool> uses)
        {
            Process[] processes = Process.GetProcessesByName(PROCESS_NAME);

            try
            {
                foreach (Process process in processes)
                {
                    if (directoryOf(process) is string directory && uses(directory))
                        return directory;
                }

                return null;
            }
            finally
            {
                foreach (Process process in processes)
                    process.Dispose();
            }
        }

        internal static bool IsIn(string? executable, string directory) =>
            executable == null || Paths.IsInside(Path.GetFullPath(directory), Path.GetFullPath(executable));

        private static string? directoryOf(Process process)
        {
            try
            {
                return Path.GetDirectoryName(process.MainModule?.FileName);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            catch (Win32Exception)
            {
                return null;
            }
        }

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
                // Access denied, can't rule it out
                return true;
            }
        }
    }
}

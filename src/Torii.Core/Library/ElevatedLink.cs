using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Torii.Core.Lazer;

namespace Torii.Core.Library
{
    public static class ElevatedLink
    {
        public const string ARGUMENT = "--link";

        private const int error_cancelled = 1223;

        public static bool TryRun(string[] args, out int exitCode)
        {
            exitCode = 0;

            if (args.Length == 0 || args[0] != ARGUMENT)
                return false;

            exitCode = run(args) ? 0 : 1;
            return true;
        }

        public static bool Request(LazerInstance instance, string libraryDirectory)
        {
            var info = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                ArgumentList = { ARGUMENT, instance.Directory, libraryDirectory },
            };

            try
            {
                using Process? process = Process.Start(info);

                if (process == null)
                    return false;

                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch (Win32Exception e) when (e.NativeErrorCode == error_cancelled)
            {
                return false;
            }
        }

        private static bool run(string[] args)
        {
            if (args.Length != 3 || !Path.IsPathFullyQualified(args[1]) || !Path.IsPathFullyQualified(args[2]))
                return false;

            var instance = new LazerInstance(args[1]);
            var library = new SharedLibrary(args[2]);

            if (!File.Exists(instance.PortableMarker) || !library.IsUsable)
                return false;

            try
            {
                library.Attach(instance);
                return true;
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}

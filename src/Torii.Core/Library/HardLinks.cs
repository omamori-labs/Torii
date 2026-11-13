using System;
using System.Runtime.InteropServices;

namespace Torii.Core.Library
{
    internal static partial class HardLinks
    {
        public static bool TryCreate(string link, string existing)
        {
            if (OperatingSystem.IsWindows())
                return createHardLink(link, existing, IntPtr.Zero);

            return linkUnix(existing, link) == 0;
        }

        [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool createHardLink(string fileName, string existingFileName, IntPtr securityAttributes);

        [LibraryImport("libc", EntryPoint = "link", StringMarshalling = StringMarshalling.Utf8)]
        private static partial int linkUnix(string existing, string link);
    }
}

using System;
using System.IO;
using System.Reflection;
using Avalonia.Platform.Storage;

namespace Torii.Tests
{
    internal static class DroppedFile
    {
        // BclStorageFile is internal and IStorageFile can't be implemented
        public static IStorageItem At(string path)
        {
            Type type = typeof(IStorageItem).Assembly.GetType("Avalonia.Platform.Storage.FileIO.BclStorageFile")
                        ?? throw new InvalidOperationException("Avalonia no longer has BclStorageFile, so dropped files can't be faked.");

            return (IStorageItem)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [new FileInfo(path)], null)!;
        }
    }
}

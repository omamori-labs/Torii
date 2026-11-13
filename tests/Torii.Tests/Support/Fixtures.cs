using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using Torii.Core.Lazer;

namespace Torii.Tests
{
    internal static class Fixtures
    {
        public static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

        public static byte[] OmamoriZip(string? lazerVersion) => Zip(
            ("rulesets/osu.Game.Rulesets.Omamori.dll", "bootstrap"u8.ToArray()),
            ("omamori/core/Omamori.Core.dll", Core(lazerVersion)));

        public static byte[] Core(string? lazerVersion) => lazerVersion == null ? Assembly(null, null) : Assembly("LazerBuild", lazerVersion);

        public static byte[] Assembly(string? metadataKey, string? metadataValue)
        {
            var builder = new PersistedAssemblyBuilder(new AssemblyName("Omamori.Core"), typeof(object).Assembly);

            if (metadataKey != null)
            {
                ConstructorInfo constructor = typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!;
                builder.SetCustomAttribute(new CustomAttributeBuilder(constructor, [metadataKey, metadataValue]));
            }

            builder.DefineDynamicModule("Omamori.Core");

            using var stream = new MemoryStream();
            builder.Save(stream);

            return stream.ToArray();
        }

        // Same layout as a Velopack package
        public static byte[] LazerPackage(string version, params string[] extraEntries)
        {
            string nuspec = $"<package xmlns=\"http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd\"><metadata><id>osulazer</id><version>{version}</version></metadata></package>";

            List<(string, byte[])> entries =
            [
                ("osulazer.nuspec", "metadata"u8.ToArray()),
                ("lib/app/osu%21.exe", "exe"u8.ToArray()),
                ("lib/app/osu.Game.dll", "dll"u8.ToArray()),
                ("lib/app/runtimes/win-x64/native/bass.dll", "dll"u8.ToArray()),
                ("lib/app/sq.version", Encoding.UTF8.GetBytes(nuspec)),
                .. extraEntries.Select(name => (name, "extra"u8.ToArray())),
            ];

            return Zip([.. entries]);
        }

        public static string File(string directory, string extension, byte[] content)
        {
            string path = Path.Combine(directory, $"{Guid.NewGuid():N}.{extension}");
            System.IO.File.WriteAllBytes(path, content);

            return path;
        }

        public static string ZipFile(string directory, string extension, params (string Name, byte[] Content)[] entries) => File(directory, extension, Zip(entries));

        public static byte[] LazerRelease(string version, byte[] package, Uri download) => Encoding.UTF8.GetBytes($$"""
            { "tag_name": "{{version}}", "assets": [{ "name": "{{LazerFeed.AssetName(version)}}", "size": {{package.Length}}, "digest": "sha256:{{Sha256(package)}}", "browser_download_url": "{{download}}" }] }
            """);

        public static byte[] Zip(params (string Name, byte[] Content)[] entries)
        {
            using var stream = new MemoryStream();

            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach ((string name, byte[] content) in entries)
                {
                    using Stream entry = archive.CreateEntry(name).Open();
                    entry.Write(content);
                }
            }

            return stream.ToArray();
        }
    }
}

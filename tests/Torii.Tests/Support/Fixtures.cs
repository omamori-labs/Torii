using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using Torii.Core;

namespace Torii.Tests
{
    internal static class Fixtures
    {
        public static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

        public static byte[] OmamoriZip(string? lazerVersion) => Zip(
            ("rulesets/osu.Game.Rulesets.Omamori.dll", "bootstrap"u8.ToArray()),
            ("omamori/core/Omamori.Core.dll", Core(lazerVersion)));

        public static byte[] Core(string? lazerVersion)
        {
            var builder = new PersistedAssemblyBuilder(new AssemblyName("Omamori.Core"), typeof(object).Assembly);

            if (lazerVersion != null)
            {
                ConstructorInfo constructor = typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!;
                builder.SetCustomAttribute(new CustomAttributeBuilder(constructor, ["LazerBuild", lazerVersion]));
            }

            builder.DefineDynamicModule("Omamori.Core");

            using var stream = new MemoryStream();
            builder.Save(stream);

            return stream.ToArray();
        }

        public static byte[] LazerPackage(string version) => Zip(
            ("osulazer.nuspec", "metadata"u8.ToArray()),
            ("lib/app/osu%21.exe", "exe"u8.ToArray()),
            ("lib/app/osu.Game.dll", "dll"u8.ToArray()),
            ("lib/app/sq.version", Encoding.UTF8.GetBytes($"<package><metadata><id>osulazer</id><version>{version}</version></metadata></package>")));

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

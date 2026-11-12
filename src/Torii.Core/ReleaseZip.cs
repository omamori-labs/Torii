using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Torii.Core
{
    /// <summary>What an Omamori release zip says about itself, read without installing it</summary>
    public static class ReleaseZip
    {
        private const string core_entry = "omamori/core/" + OmamoriInstall.CORE_FILENAME;

        // Omamori's build stamps this on the core, see LazerBuild in its source
        private const string lazer_build_key = "LazerBuild";

        /// <summary>The osu!lazer version the release was built for, as ppy/osu tags it. Null when the core doesn't say</summary>
        /// <exception cref="InvalidDataException">Not an Omamori release</exception>
        public static string? LazerVersion(string zipPath)
        {
            using ZipArchive archive = Zips.Open(zipPath);

            ZipArchiveEntry entry = archive.Entries.FirstOrDefault(e => Paths.EntryName(e.FullName) == core_entry)
                                    ?? throw new InvalidDataException("This isn't an Omamori release, it has no loader in it.");

            // PEReader needs to seek, a zip entry can't
            using var assembly = new MemoryStream();
            using (Stream compressed = entry.Open())
                compressed.CopyTo(assembly);

            assembly.Position = 0;

            try
            {
                using var pe = new PEReader(assembly);

                if (!pe.HasMetadata)
                    throw new InvalidDataException("This isn't an Omamori release, its loader isn't a .NET assembly.");

                return assemblyMetadata(pe.GetMetadataReader(), lazer_build_key);
            }
            catch (BadImageFormatException e)
            {
                throw new InvalidDataException("This isn't an Omamori release, its loader isn't a .NET assembly.", e);
            }
        }

        private static string? assemblyMetadata(MetadataReader reader, string key)
        {
            foreach (CustomAttributeHandle handle in reader.GetAssemblyDefinition().GetCustomAttributes())
            {
                CustomAttribute attribute = reader.GetCustomAttribute(handle);

                if (attribute.Constructor.Kind != HandleKind.MemberReference)
                    continue;

                EntityHandle type = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;

                if (type.Kind != HandleKind.TypeReference || reader.GetString(reader.GetTypeReference((TypeReferenceHandle)type).Name) != "AssemblyMetadataAttribute")
                    continue;

                // ECMA-335 II.23.3: a 0x0001 prolog, then the constructor's two strings
                BlobReader value = reader.GetBlobReader(attribute.Value);

                if (value.ReadUInt16() != 1 || value.ReadSerializedString() != key)
                    continue;

                string? found = value.ReadSerializedString()?.Trim();
                return string.IsNullOrEmpty(found) ? null : found;
            }

            return null;
        }
    }
}

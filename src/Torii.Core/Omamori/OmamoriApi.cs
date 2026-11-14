using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Torii.Core.Omamori
{
    internal static class OmamoriApi
    {
        // ApiInfo.VERSION in Omamori.API, a const so it's only in the metadata
        public static string? Read(string assemblyPath)
        {
            try
            {
                using FileStream stream = File.OpenRead(assemblyPath);
                using var pe = new PEReader(stream);

                if (!pe.HasMetadata)
                    return null;

                MetadataReader reader = pe.GetMetadataReader();

                foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
                {
                    TypeDefinition type = reader.GetTypeDefinition(typeHandle);

                    if (reader.GetString(type.Name) != "ApiInfo")
                        continue;

                    foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
                    {
                        FieldDefinition field = reader.GetFieldDefinition(fieldHandle);

                        if (reader.GetString(field.Name) != "VERSION" || field.GetDefaultValue().IsNil)
                            continue;

                        Constant constant = reader.GetConstant(field.GetDefaultValue());

                        if (constant.TypeCode != ConstantTypeCode.String)
                            return null;

                        BlobReader value = reader.GetBlobReader(constant.Value);
                        string version = value.ReadUTF16(value.RemainingBytes).Trim();

                        return version.Length > 0 ? version : null;
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or BadImageFormatException)
            {
            }

            return null;
        }
    }
}

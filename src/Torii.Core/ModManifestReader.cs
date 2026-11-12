using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Torii.Core
{
    internal static partial class ModManifestReader
    {
        public const string FILENAME = "mod.json";

        /// <exception cref="InvalidDataException">The message says what is wrong with the mod.json</exception>
        public static ModInfo Read(string path)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                JsonElement root = document.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException($"{FILENAME} isn't a mod manifest");

                string id = required(root, "id");
                if (!idPattern().IsMatch(id))
                    throw new InvalidDataException($"{FILENAME} has an invalid id");

                string entry = required(root, "entry");
                if (!entryPattern().IsMatch(entry))
                    throw new InvalidDataException($"{FILENAME} has an invalid entry");

                return new ModInfo(id, required(root, "name"), required(root, "version"), optional(root, "author"), optional(root, "description"), entry, capabilities(root));
            }
            catch (JsonException)
            {
                throw new InvalidDataException($"{FILENAME} isn't valid JSON");
            }
        }

        private static string required(JsonElement root, string property)
        {
            string value = optional(root, property);
            return value.Length > 0 ? value : throw new InvalidDataException($"{FILENAME} has no {property}");
        }

        private static string optional(JsonElement root, string property) =>
            root.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : string.Empty;

        private static List<string> capabilities(JsonElement root)
        {
            var list = new List<string>();

            if (!root.TryGetProperty("capabilities", out JsonElement values) || values.ValueKind != JsonValueKind.Array)
                return list;

            foreach (JsonElement value in values.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.String)
                    list.Add(value.GetString()!);
            }

            return list;
        }

        // Both patterns are from Omamori's schemas/mod.schema.json
        [GeneratedRegex(@"^[a-z0-9]+([._-][a-z0-9]+)*$")]
        private static partial Regex idPattern();

        [GeneratedRegex(@"^(?![\\/])(?![A-Za-z]:)(?!.*\.\.).+\.dll$")]
        private static partial Regex entryPattern();
    }
}

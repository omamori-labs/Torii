using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Torii.Core.Settings
{
    public sealed class SettingsStore
    {
        private static readonly JsonSerializerOptions json_options = new JsonSerializerOptions { WriteIndented = true };

        public SettingsStore(string file)
        {
            FilePath = Path.GetFullPath(file);
        }

        public string FilePath { get; }

        public static string DefaultFile() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Torii", "settings.json");

        public AppSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath) || JsonNode.Parse(File.ReadAllText(FilePath)) is not JsonObject root)
                    return new AppSettings();

                return new AppSettings(text(root, "selectedInstance"), size(root, "windowWidth"), size(root, "windowHeight"), list(root, "instanceOrder"), text(root, "skippedUpdate"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                return new AppSettings();
            }
        }

        public bool Save(AppSettings settings)
        {
            var root = new JsonObject();

            if (settings.SelectedInstance != null)
                root["selectedInstance"] = settings.SelectedInstance;

            if (settings.WindowWidth is double width)
                root["windowWidth"] = width;

            if (settings.WindowHeight is double height)
                root["windowHeight"] = height;

            if (settings.SkippedUpdate != null)
                root["skippedUpdate"] = settings.SkippedUpdate;

            if (settings.InstanceOrder is { Count: > 0 } order)
                root["instanceOrder"] = new JsonArray([.. order.Select(path => JsonValue.Create(path))]);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

                string temporary = FilePath + ".tmp";
                File.WriteAllText(temporary, root.ToJsonString(json_options));
                File.Move(temporary, FilePath, overwrite: true);

                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static string? text(JsonObject root, string name) =>
            root[name] is JsonValue value && value.TryGetValue(out string? result) && !string.IsNullOrEmpty(result) ? result : null;

        private static string[]? list(JsonObject root, string name)
        {
            if (root[name] is not JsonArray array)
                return null;

            string[] items = [.. array.OfType<JsonValue>().Select(value => value.TryGetValue(out string? item) ? item : null).OfType<string>().Where(item => item.Length > 0)];

            return items.Length == 0 ? null : items;
        }

        private static double? size(JsonObject root, string name) =>
            root[name] is JsonValue value && value.TryGetValue(out double result) && double.IsFinite(result) && result > 0 ? result : null;
    }
}

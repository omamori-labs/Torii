using System;
using System.IO;
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

                return new AppSettings(text(root, "selectedInstance"), size(root, "windowWidth"), size(root, "windowHeight"));
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

        private static double? size(JsonObject root, string name) =>
            root[name] is JsonValue value && value.TryGetValue(out double result) && double.IsFinite(result) && result > 0 ? result : null;
    }
}

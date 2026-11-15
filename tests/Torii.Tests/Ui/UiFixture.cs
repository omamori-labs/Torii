using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using Torii.Core;
using Torii.Core.Instances;
using Torii.Core.Lazer;
using Torii.Core.Library;
using Torii.Core.Mods;
using Torii.Core.Releases;
using Torii.ViewModels;

namespace Torii.Tests
{
    internal sealed class UiFixture : IDisposable
    {
        private const string lazer = "2026.1005.0-lazer";

        private readonly RouteHandler handler = new RouteHandler();
        private readonly List<string> listed = [];
        private readonly HttpClient http;
        private readonly SharedLibrary library;

        public UiFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);

            http = new HttpClient(handler);
            Store = new InstanceStore(Path.Combine(Root, "instances"), Path.Combine(Root, "old-instance"), _ => false);
            library = new SharedLibrary(Path.Combine(Root, "library"), (_, _) => true, () => false, _ => null);
        }

        public string Root { get; }

        public InstanceStore Store { get; }

        public MainViewModel CreateShell(LazerInstance? given = null) => new MainViewModel(Store, makeInstance, given);

        public static void PlaceGame(LazerInstance instance)
        {
            Directory.CreateDirectory(instance.Directory);

            foreach (string file in new[] { instance.Executable, Path.Combine(instance.Directory, "osu.Game.dll"), instance.PortableMarker })
                File.WriteAllText(file, string.Empty);

            File.WriteAllText(Path.Combine(instance.Directory, "sq.version"), $"<package><metadata><version>{lazer}</version></metadata></package>");
        }

        public static void AddMod(LazerInstance instance, string id)
        {
            string folder = Path.Combine(instance.Omamori.ModsDirectory, id);
            Directory.CreateDirectory(folder);

            File.WriteAllText(Path.Combine(folder, "Mod.dll"), string.Empty);
            File.WriteAllText(Path.Combine(folder, "mod.json"), $$"""
                { "id": "{{id}}", "name": "{{id}}", "version": "1.0.0", "author": "", "entry": "Mod.dll", "apiVersion": "0.2", "capabilities": ["Cosmetic"] }
                """);
        }

        public void ServeMod(string id, string name, string description = "", string version = "1.0.0")
        {
            byte[] zip = Fixtures.Zip(
                ($"{id}/Mod.dll", []),
                ($"{id}/mod.json", Encoding.UTF8.GetBytes($$"""
                    { "id": "{{id}}", "name": "{{name}}", "version": "{{version}}", "entry": "Mod.dll", "apiVersion": "0.2", "capabilities": ["Cosmetic"] }
                    """)));

            handler.Add(new Uri($"https://example.com/{id}.zip"), zip);
            listed.Add($$"""
                { "id": "{{id}}", "name": "{{name}}", "description": "{{description}}", "versions": [{ "version": "{{version}}", "apiVersion": "0.2", "download": "https://example.com/{{id}}.zip", "sha256": "{{Fixtures.Sha256(zip)}}" }] }
                """);
            handler.Add(ModIndexFeed.Default, Encoding.UTF8.GetBytes($$"""{ "mods": [{{string.Join(",", listed)}}] }"""));
        }

        public string ModZip(string id, string name)
        {
            string path = Path.Combine(Root, $"{id}.zip");

            File.WriteAllBytes(path, Fixtures.Zip(
                ($"{id}/Mod.dll", []),
                ($"{id}/mod.json", Encoding.UTF8.GetBytes($$"""
                    { "id": "{{id}}", "name": "{{name}}", "version": "1.0.0", "entry": "Mod.dll", "apiVersion": "0.2", "capabilities": ["Cosmetic"] }
                    """))));

            return path;
        }

        public void Dispose()
        {
            http.Dispose();
            handler.Dispose();
            Directory.Delete(Root, recursive: true);
        }

        private InstanceViewModel makeInstance(LazerInstance instance) => new InstanceViewModel(
            instance,
            new ReleaseFeed(http),
            new InstanceSetup(http, Root, _ => false),
            library,
            new LibrarySeed(() => false, () => null, () => null),
            () => false,
            new GameLauncher(_ => { }),
            new ModIndexFeed(http),
            new IndexedModInstaller(http, Root));
    }
}

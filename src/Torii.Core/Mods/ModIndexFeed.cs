using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Torii.Core.Mods
{
    public sealed class ModIndexFeed
    {
        private readonly HttpClient http;
        private readonly Uri source;

        public ModIndexFeed(HttpClient http, Uri? source = null)
        {
            this.http = http;
            this.source = source ?? Default;
        }

        public static Uri Default { get; } = new Uri("https://raw.githubusercontent.com/omamori-labs/mod-index/main/mods.json");

        public async Task<IReadOnlyList<IndexedMod>> ListAsync(CancellationToken cancellation = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, source);
            request.Headers.UserAgent.ParseAdd("Torii");

            using HttpResponseMessage response = await http.SendAsync(request, cancellation).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            return Parse(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false));
        }

        public static IReadOnlyList<IndexedMod> Parse(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("mods", out JsonElement entries)
                || entries.ValueKind != JsonValueKind.Array)
                throw new JsonException("expected an object with a list of mods");

            var mods = new List<IndexedMod>();

            foreach (JsonElement entry in entries.EnumerateArray())
            {
                if (parseMod(entry) is IndexedMod mod && mods.All(m => m.Id != mod.Id))
                    mods.Add(mod);
            }

            return mods;
        }

        private static IndexedMod? parseMod(JsonElement entry)
        {
            if (entry.ValueKind != JsonValueKind.Object)
                return null;

            string? id = text(entry, "id");
            string? name = text(entry, "name");

            if (id == null || name == null || !ModManifestReader.IsId(id))
                return null;

            var versions = new List<(ModVersion Parsed, IndexedModVersion Version)>();

            if (entry.TryGetProperty("versions", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in list.EnumerateArray())
                {
                    if (parseVersion(item) is (ModVersion, IndexedModVersion) version)
                        versions.Add(version);
                }
            }

            if (versions.Count == 0)
                return null;

            versions.Sort((a, b) => b.Parsed.CompareTo(a.Parsed));

            Uri? repository = Uri.TryCreate(text(entry, "repository"), UriKind.Absolute, out Uri? url) && url.Scheme == Uri.UriSchemeHttps ? url : null;

            return new IndexedMod(id, name, text(entry, "author") ?? string.Empty, text(entry, "description") ?? string.Empty, repository, [.. versions.Select(v => v.Version)]);
        }

        private static (ModVersion, IndexedModVersion)? parseVersion(JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object)
                return null;

            string? version = text(item, "version");
            string? api = text(item, "apiVersion");
            string? hash = text(item, "sha256");

            if (version == null || api == null || hash == null
                || !ModVersion.TryParse(version, out ModVersion? parsed)
                || !ModVersion.TryParse(api, out _)
                || hash.Length != 64 || !hash.All(char.IsAsciiHexDigit)
                || !Uri.TryCreate(text(item, "download"), UriKind.Absolute, out Uri? download) || download.Scheme != Uri.UriSchemeHttps)
                return null;

            long size = item.TryGetProperty("size", out JsonElement bytes) && bytes.TryGetInt64(out long value) ? value : 0;

            return (parsed, new IndexedModVersion(version, api, download, hash.ToLowerInvariant(), size));
        }

        private static string? text(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.String)
                return null;

            string? result = value.GetString()?.Trim();
            return string.IsNullOrEmpty(result) ? null : result;
        }
    }
}

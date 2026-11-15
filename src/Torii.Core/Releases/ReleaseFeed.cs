using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Torii.Core.Omamori;

namespace Torii.Core.Releases
{
    public sealed class ReleaseFeed
    {
        private const string sha256_prefix = "sha256:";

        private const long cache_milliseconds = 10 * 60 * 1000;

        private readonly HttpClient http;
        private readonly Uri source;
        private readonly Func<long> ticks;
        private readonly Lock gate = new Lock();

        private Task<IReadOnlyList<OmamoriRelease>>? cached;
        private long cachedAt;

        public ReleaseFeed(HttpClient http, Uri? source = null, Func<long>? ticks = null)
        {
            this.http = http;
            this.source = source ?? GitHub;
            this.ticks = ticks ?? (() => Environment.TickCount64);
        }

        public static Uri GitHub { get; } = new Uri("https://api.github.com/repos/omamori-labs/Omamori/releases?per_page=30");

        public async Task<IReadOnlyList<OmamoriRelease>> ListAsync(CancellationToken cancellation = default)
        {
            Task<IReadOnlyList<OmamoriRelease>> pending;

            lock (gate)
            {
                if (cached == null || ticks() - cachedAt > cache_milliseconds)
                {
                    Task<IReadOnlyList<OmamoriRelease>> fetch = fetchAsync();
                    cached = fetch;
                    cachedAt = ticks();
                    pending = fetch;

                    _ = fetch.ContinueWith(failed =>
                    {
                        lock (gate)
                        {
                            if (ReferenceEquals(cached, failed))
                                cached = null;
                        }
                    }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
                else
                    pending = cached;
            }

            return await pending.WaitAsync(cancellation).ConfigureAwait(false);
        }

        private async Task<IReadOnlyList<OmamoriRelease>> fetchAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, source);

            // GitHub rejects requests without a user agent
            request.Headers.UserAgent.ParseAdd("Torii");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using HttpResponseMessage response = await http.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            return Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        }

        public static IReadOnlyList<OmamoriRelease> Parse(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new JsonException("expected a list of releases");

            var releases = new List<OmamoriRelease>();

            foreach (JsonElement release in document.RootElement.EnumerateArray())
            {
                if (parseRelease(release) is OmamoriRelease parsed)
                    releases.Add(parsed);
            }

            return releases;
        }

        private static OmamoriRelease? parseRelease(JsonElement release)
        {
            if (release.ValueKind != JsonValueKind.Object || flag(release, "draft"))
                return null;

            string? tag = text(release, "tag_name");
            if (tag == null || tag.Length < 2 || tag[0] != 'v')
                return null;

            string version = tag[1..];
            string zip = $"omamori-{version}.zip";

            if (!release.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
                return null;

            foreach (JsonElement asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object || !string.Equals(text(asset, "name"), zip, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!Uri.TryCreate(text(asset, "browser_download_url"), UriKind.Absolute, out Uri? download) || download.Scheme != Uri.UriSchemeHttps)
                    return null;

                long size = asset.TryGetProperty("size", out JsonElement bytes) && bytes.TryGetInt64(out long parsed) ? parsed : 0;
                DateTimeOffset? published = release.TryGetProperty("published_at", out JsonElement at) && at.TryGetDateTimeOffset(out DateTimeOffset date) ? date : null;

                return new OmamoriRelease(version, flag(release, "prerelease"), download, sha256(text(asset, "digest")), size, published);
            }

            return null;
        }

        private static string? sha256(string? digest)
        {
            if (digest == null || !digest.StartsWith(sha256_prefix, StringComparison.OrdinalIgnoreCase))
                return null;

            string hex = digest[sha256_prefix.Length..];
            return hex.Length == 64 && hex.All(char.IsAsciiHexDigit) ? hex.ToLowerInvariant() : null;
        }

        private static string? text(JsonElement element, string property) =>
            element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        private static bool flag(JsonElement element, string property) =>
            element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.True;
    }
}

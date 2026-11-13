using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Torii.Core
{
    public sealed partial class LazerFeed
    {
        private const string sha256_prefix = "sha256:";

        private readonly HttpClient http;

        public LazerFeed(HttpClient http)
        {
            this.http = http;
        }

        public static string AssetName(string version)
        {
            if (OperatingSystem.IsWindows())
                return $"osulazer-{version}-full.nupkg";

            if (OperatingSystem.IsLinux())
                return $"osulazer-{version}-linux-x64-full.nupkg";

            throw new PlatformNotSupportedException("Torii can only set up osu! on Windows and Linux for now.");
        }

        public static Uri ReleaseUri(string version) => new Uri($"https://api.github.com/repos/ppy/osu/releases/tags/{Uri.EscapeDataString(version)}");

        public async Task<LazerPackage> FindAsync(string version, CancellationToken cancellation = default)
        {
            // Goes into a url
            if (!versionPattern().IsMatch(version))
                throw new InvalidDataException($"{version} isn't an osu! version.");

            using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseUri(version));
            request.Headers.UserAgent.ParseAdd("Torii");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using HttpResponseMessage response = await http.SendAsync(request, cancellation).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new InvalidDataException($"osu! {version} isn't available to download.");

            response.EnsureSuccessStatusCode();

            return Parse(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false), version, AssetName(version));
        }

        public static LazerPackage Parse(string json, string version, string assetName)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);

                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("assets", out JsonElement assets)
                    || assets.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException($"osu! {version} isn't available to download.");

                foreach (JsonElement asset in assets.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.Object))
                {
                    if (text(asset, "name") != assetName)
                        continue;

                    string? digest = text(asset, "digest");

                    if (digest == null || !digest.StartsWith(sha256_prefix, StringComparison.OrdinalIgnoreCase) || digest.Length != sha256_prefix.Length + 64)
                        throw new InvalidDataException($"osu! {version} has no checksum to check the download against.");

                    if (!Uri.TryCreate(text(asset, "browser_download_url"), UriKind.Absolute, out Uri? download) || download.Scheme != Uri.UriSchemeHttps)
                        throw new InvalidDataException($"osu! {version} isn't available to download.");

                    long size = asset.TryGetProperty("size", out JsonElement bytes) && bytes.TryGetInt64(out long parsed) ? parsed : 0;

                    return new LazerPackage(version, download, digest[sha256_prefix.Length..].ToLowerInvariant(), size);
                }

                throw new InvalidDataException($"osu! {version} isn't available for this system.");
            }
            catch (JsonException e)
            {
                throw new InvalidDataException($"osu! {version} isn't available to download.", e);
            }
        }

        private static string? text(JsonElement element, string property) =>
            element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        [GeneratedRegex(@"^[0-9A-Za-z][0-9A-Za-z.\-]*$")]
        private static partial Regex versionPattern();
    }
}

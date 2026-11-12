using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Torii.Core
{
    internal static class Downloads
    {
        /// <summary>Downloads to a file and returns its sha256 as lowercase hex. Progress is 0 to 1</summary>
        public static async Task<string> ToFileAsync(HttpClient http, Uri source, string path, long expectedSize, IProgress<double>? progress, CancellationToken cancellation)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, source);
            request.Headers.UserAgent.ParseAdd("Torii");

            using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            long total = response.Content.Headers.ContentLength ?? expectedSize;

            using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[81920];
            long done = 0;

            Stream stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                FileStream target = File.Create(path);
                await using (target.ConfigureAwait(false))
                {
                    int read;

                    while ((read = await stream.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
                    {
                        sha256.AppendData(buffer, 0, read);
                        await target.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);

                        done += read;
                        if (total > 0)
                            progress?.Report(Math.Min(1, (double)done / total));
                    }
                }
            }

            progress?.Report(1);

            return Convert.ToHexStringLower(sha256.GetHashAndReset());
        }
    }
}

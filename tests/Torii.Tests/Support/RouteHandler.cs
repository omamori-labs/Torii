using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Torii.Tests
{
    // Serves a fixed set of urls and remembers what was asked for, so tests never hit the network
    internal sealed class RouteHandler : HttpMessageHandler
    {
        private readonly Dictionary<Uri, byte[]> routes = [];

        public List<Uri> Requested { get; } = [];

        public RouteHandler Add(Uri uri, byte[] body)
        {
            routes[uri] = body;
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested.Add(request.RequestUri!);

            return Task.FromResult(routes.TryGetValue(request.RequestUri!, out byte[]? body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}

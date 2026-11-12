using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Torii.Tests
{
    // Answers every request itself so tests never hit the network
    internal sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode status;
        private readonly byte[] body;

        public StubHandler(HttpStatusCode status, byte[] body)
        {
            this.status = status;
            this.body = body;
        }

        public Uri? Requested { get; private set; }

        public string? UserAgent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested = request.RequestUri;
            UserAgent = request.Headers.UserAgent.ToString();

            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
        }
    }
}

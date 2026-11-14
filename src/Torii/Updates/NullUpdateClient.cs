using System;
using System.Threading.Tasks;

namespace Torii.Updates
{
    public sealed class NullUpdateClient : IUpdateClient
    {
        public Task<string?> CheckAsync() => Task.FromResult<string?>(null);

        public Task DownloadAsync(IProgress<double> progress) => Task.CompletedTask;

        public void ApplyOnExit()
        {
        }

        public void ApplyAndRestart()
        {
        }
    }
}

using System;
using System.Threading.Tasks;

namespace Torii.Updates
{
    public interface IUpdateClient
    {
        Task<string?> CheckAsync();

        Task DownloadAsync(IProgress<double> progress);

        void ApplyOnExit();

        void ApplyAndRestart();
    }
}

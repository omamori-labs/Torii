using System;
using System.Threading.Tasks;
using Torii.Updates;

namespace Torii.Tests
{
    internal sealed class FakeUpdateClient : IUpdateClient
    {
        public string? Offer { get; set; }

        public Exception? CheckFails { get; set; }

        public Exception? DownloadFails { get; set; }

        public Exception? ApplyOnExitFails { get; set; }

        public Exception? RestartFails { get; set; }

        public double[] Reports { get; set; } = [];

        public TaskCompletionSource? Hold { get; set; }

        public int Downloads { get; private set; }

        public int AppliedOnExit { get; private set; }

        public int Restarts { get; private set; }

        public Task<string?> CheckAsync() => CheckFails != null ? Task.FromException<string?>(CheckFails) : Task.FromResult(Offer);

        public async Task DownloadAsync(IProgress<double> progress)
        {
            Downloads++;

            foreach (double report in Reports)
                progress.Report(report);

            if (Hold != null)
                await Hold.Task;

            if (DownloadFails != null)
                throw DownloadFails;
        }

        public void ApplyOnExit()
        {
            if (ApplyOnExitFails != null)
                throw ApplyOnExitFails;

            AppliedOnExit++;
        }

        public void ApplyAndRestart()
        {
            if (RestartFails != null)
                throw RestartFails;

            Restarts++;
        }
    }
}

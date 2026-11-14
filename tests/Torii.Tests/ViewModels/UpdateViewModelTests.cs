using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Torii.Core.Releases;
using Torii.Updates;
using Torii.ViewModels;

namespace Torii.Tests
{
    [TestFixture]
    public class UpdateViewModelTests
    {
        private static readonly string[] only_alpha_3 = ["0.1.0-alpha.3"];

        private string directory = null!;
        private FakeUpdateClient client = null!;
        private UpdateLog log = null!;
        private List<string> remembered = null!;
        private bool busy;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            client = new FakeUpdateClient();
            log = new UpdateLog(Path.Combine(directory, "update.log"));
            remembered = [];
            busy = false;
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, recursive: true);

        [Test]
        public async Task NothingShowsWhenThereIsNoUpdate()
        {
            UpdateViewModel model = create();

            await model.CheckAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.IsVisible, Is.False);
                Assert.That(model.State, Is.EqualTo(UpdateState.None));
            }
        }

        [Test]
        public async Task OfferedUpdateIsShownWithItsVersionAndNothingIsDownloaded()
        {
            client.Offer = "0.1.0-alpha.3";
            UpdateViewModel model = create();

            await model.CheckAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.IsVisible, Is.True);
                Assert.That(model.IsAvailable, Is.True);
                Assert.That(model.Version, Is.EqualTo("0.1.0-alpha.3"));
                Assert.That(model.Title, Is.EqualTo("Torii 0.1.0-alpha.3 is available"));
                Assert.That(client.Downloads, Is.Zero);
                Assert.That(client.AppliedOnExit, Is.Zero);
            }
        }

        [Test]
        public async Task UpdatingDownloadsThenIsReadyAndGetsAppliedWhenToriiCloses()
        {
            client.Offer = "0.1.0-alpha.3";
            UpdateViewModel model = create();
            await model.CheckAsync();

            await model.UpdateCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.State, Is.EqualTo(UpdateState.Ready));
                Assert.That(model.IsReady, Is.True);
                Assert.That(model.Title, Is.EqualTo("Torii 0.1.0-alpha.3 is ready"));
                Assert.That(model.Progress, Is.EqualTo(1));
                Assert.That(client.Downloads, Is.EqualTo(1));
                Assert.That(client.AppliedOnExit, Is.EqualTo(1));
                Assert.That(client.Restarts, Is.Zero);
            }
        }

        [Test]
        public async Task WhileDownloadingTheProgressShowsAndTheButtonsAreOff()
        {
            client.Offer = "0.1.0-alpha.3";
            client.Reports = [0.25];
            client.Hold = new TaskCompletionSource();
            UpdateViewModel model = create();
            await model.CheckAsync();

            Task running = model.UpdateCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.IsDownloading, Is.True);
                Assert.That(model.UpdateCommand.CanExecute(null), Is.False);
                Assert.That(model.SkipCommand.CanExecute(null), Is.False);
                Assert.That(model.RestartCommand.CanExecute(null), Is.False);
            }

            Assert.That(() => model.Progress, Is.EqualTo(0.25).After(2000, 10));

            client.Hold.SetResult();
            await running;

            Assert.That(model.State, Is.EqualTo(UpdateState.Ready));
        }

        [Test]
        public async Task FailedDownloadShowsTheReasonAndCanBeTriedAgain()
        {
            client.Offer = "0.1.0-alpha.3";
            client.DownloadFails = new InvalidOperationException("The network is gone.");
            UpdateViewModel model = create();
            await model.CheckAsync();

            await model.UpdateCommand.ExecuteAsync(null);
            UpdateState afterFailure = model.State;
            string problem = model.Problem;
            string title = model.Title;
            bool canTryAgain = model.UpdateCommand.CanExecute(null);

            client.DownloadFails = null;
            await model.UpdateCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(afterFailure, Is.EqualTo(UpdateState.Failed));
                Assert.That(problem, Is.EqualTo("The network is gone."));
                Assert.That(title, Is.EqualTo("Couldn't update to 0.1.0-alpha.3"));
                Assert.That(canTryAgain, Is.True);
                Assert.That(model.State, Is.EqualTo(UpdateState.Ready));
                Assert.That(model.Problem, Is.Empty);
            }
        }

        [Test]
        public async Task SkippingHidesTheCardAndRemembersTheVersion()
        {
            client.Offer = "0.1.0-alpha.3";
            UpdateViewModel model = create();
            await model.CheckAsync();

            model.SkipCommand.Execute(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.IsVisible, Is.False);
                Assert.That(remembered, Is.EqualTo(only_alpha_3));
                Assert.That(client.Downloads, Is.Zero);
            }
        }

        [Test]
        public async Task SkippedVersionStaysHiddenButANewerOneShows()
        {
            client.Offer = "0.1.0-alpha.3";
            UpdateViewModel model = create(skipped: "0.1.0-alpha.3");
            await model.CheckAsync();
            bool shownForSkipped = model.IsVisible;

            client.Offer = "0.1.0-alpha.4";
            await model.CheckAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(shownForSkipped, Is.False);
                Assert.That(model.IsVisible, Is.True);
                Assert.That(model.Version, Is.EqualTo("0.1.0-alpha.4"));
            }
        }

        [Test]
        public async Task SkippingInThisSessionAlsoHidesLaterChecksOfTheSameVersion()
        {
            client.Offer = "0.1.0-alpha.3";
            UpdateViewModel model = create();
            await model.CheckAsync();
            model.SkipCommand.Execute(null);

            await model.CheckAsync();

            Assert.That(model.IsVisible, Is.False);
        }

        [Test]
        public async Task DismissingHidesTheCardForNowWithoutRememberingAnything()
        {
            client.Offer = "0.1.0-alpha.3";
            UpdateViewModel model = create();
            await model.CheckAsync();

            model.DismissCommand.Execute(null);
            bool hidden = !model.IsVisible;
            await model.CheckAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(hidden, Is.True);
                Assert.That(remembered, Is.Empty);
                Assert.That(model.IsVisible, Is.True);
            }
        }

        [Test]
        public async Task RestartIsOnlyOfferedWhenTheUpdateIsReadyAndNothingIsBusy()
        {
            client.Offer = "0.1.0-alpha.3";
            UpdateViewModel model = create();
            await model.CheckAsync();
            bool beforeDownload = model.RestartCommand.CanExecute(null);

            await model.UpdateCommand.ExecuteAsync(null);
            bool whenReady = model.RestartCommand.CanExecute(null);

            busy = true;
            model.Refresh();
            bool whileBusy = model.RestartCommand.CanExecute(null);

            busy = false;
            model.Refresh();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(beforeDownload, Is.False);
                Assert.That(whenReady, Is.True);
                Assert.That(whileBusy, Is.False);
                Assert.That(model.RestartCommand.CanExecute(null), Is.True);
            }
        }

        [Test]
        public async Task RestartingAppliesTheUpdateAndRestarts()
        {
            client.Offer = "0.1.0-alpha.3";
            UpdateViewModel model = create();
            await model.CheckAsync();
            await model.UpdateCommand.ExecuteAsync(null);

            model.RestartCommand.Execute(null);

            Assert.That(client.Restarts, Is.EqualTo(1));
        }

        [Test]
        public async Task FailingToRestartShowsTheReason()
        {
            client.Offer = "0.1.0-alpha.3";
            client.RestartFails = new InvalidOperationException("Update.exe is missing.");
            UpdateViewModel model = create();
            await model.CheckAsync();
            await model.UpdateCommand.ExecuteAsync(null);

            model.RestartCommand.Execute(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.State, Is.EqualTo(UpdateState.Failed));
                Assert.That(model.Problem, Is.EqualTo("Update.exe is missing."));
            }
        }

        [Test]
        public async Task FailingToCheckIsQuietAndLeavesTheCardAlone()
        {
            client.CheckFails = new InvalidOperationException("offline");
            UpdateViewModel model = create();

            await model.CheckAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.IsVisible, Is.False);
                Assert.That(File.ReadAllText(log.FilePath), Does.Contain("update check failed: InvalidOperationException: offline"));
            }
        }

        [Test]
        public async Task ACheckWhileReadyChangesNothing()
        {
            client.Offer = "0.1.0-alpha.3";
            UpdateViewModel model = create();
            await model.CheckAsync();
            await model.UpdateCommand.ExecuteAsync(null);

            client.Offer = "0.1.0-alpha.4";
            await model.CheckAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.State, Is.EqualTo(UpdateState.Ready));
                Assert.That(model.Version, Is.EqualTo("0.1.0-alpha.3"));
            }
        }

        [Test]
        public async Task EveryStepIsWrittenToTheUpdateLog()
        {
            client.Offer = "0.1.0-alpha.3";
            UpdateViewModel model = create();

            await model.CheckAsync();
            await model.UpdateCommand.ExecuteAsync(null);

            string text = File.ReadAllText(log.FilePath);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(text, Does.Contain("0.1.0-alpha.3 is available"));
                Assert.That(text, Does.Contain("downloading 0.1.0-alpha.3"));
                Assert.That(text, Does.Contain("0.1.0-alpha.3 is downloaded and gets applied when Torii closes"));
            }
        }

        private UpdateViewModel create(string? skipped = null) => new UpdateViewModel(client, skipped, remembered.Add, log, () => busy);

        private sealed class FakeUpdateClient : IUpdateClient
        {
            public string? Offer { get; set; }

            public Exception? CheckFails { get; set; }

            public Exception? DownloadFails { get; set; }

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

            public void ApplyOnExit() => AppliedOnExit++;

            public void ApplyAndRestart()
            {
                if (RestartFails != null)
                    throw RestartFails;

                Restarts++;
            }
        }
    }
}

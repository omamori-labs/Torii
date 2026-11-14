using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NUnit.Framework;
using Torii.ViewModels;
using Torii.Views;

namespace Torii.Tests
{
    [TestFixture]
    public class UpdateCardTests
    {
        private static readonly string[] offered_buttons = ["Update", "Not now", "Skip this version"];
        private static readonly string[] ready_buttons = ["Restart now", "Not now"];
        private static readonly string[] failed_buttons = ["Try again", "Not now"];

        private UiFixture fixture = null!;
        private List<Window> windows = null!;
        private FakeUpdateClient client = null!;

        [SetUp]
        public void SetUp()
        {
            fixture = new UiFixture();
            windows = [];
            client = new FakeUpdateClient { Offer = "0.1.0-alpha.3" };
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Window window in windows)
                window.Close();

            fixture.Dispose();
        }

        [AvaloniaTest]
        public async Task NoCardWhenThereIsNoUpdate()
        {
            fixture.Store.Create("alpha");
            MainWindow window = show(fixture.CreateShell());
            await settle(window);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(host(window).Classes.Contains("shown"), Is.False);
                Assert.That(host(window).Bounds.Height, Is.LessThan(2));
                Assert.That(host(window).IsEnabled, Is.False);
            }
        }

        [AvaloniaTest]
        public async Task OfferedUpdateShowsAtTheBottomOfTheSidebarWithItsChoices()
        {
            fixture.Store.Create("alpha");
            (MainWindow window, UpdateViewModel update) = await showOffered();
            Border updateCard = card(window);
            ListBox list = instanceList(window);
            Rect cardBounds = boundsIn(window, updateCard);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(updateCard.IsEffectivelyVisible, Is.True);
                Assert.That(texts(updateCard), Does.Contain("Update available"));
                Assert.That(texts(updateCard), Does.Contain("Torii 0.1.0-alpha.3"));
                Assert.That(visibleButtons(updateCard), Is.EqualTo(offered_buttons));
                Assert.That(boundsIn(window, list).Bottom, Is.LessThanOrEqualTo(cardBounds.Top + 1));
                Assert.That(cardBounds.Bottom, Is.EqualTo(window.Bounds.Height - 12).Within(1));
                Assert.That(cardBounds.Width, Is.EqualTo(boundsIn(window, list).Width).Within(1));
                Assert.That(updateCard.GetVisualDescendants().OfType<ProgressBar>().Any(bar => bar.IsEffectivelyVisible), Is.False);
                Assert.That(button(updateCard, "Update").Command, Is.SameAs(update.UpdateCommand));
                Assert.That(button(updateCard, "Skip this version").Command, Is.SameAs(update.SkipCommand));
                Assert.That(button(updateCard, "Not now").Command, Is.SameAs(update.DismissCommand));
            }
        }

        [AvaloniaTest]
        public async Task DownloadingShowsTheProgressAndHidesTheChoices()
        {
            fixture.Store.Create("alpha");
            client.Hold = new TaskCompletionSource();
            client.Reports = [0.42];
            (MainWindow window, UpdateViewModel update) = await showOffered();

            Task running = update.UpdateCommand.ExecuteAsync(null);
            Assert.That(() =>
            {
                Dispatcher.UIThread.RunJobs();
                return update.Progress;
            }, Is.EqualTo(0.42).After(2000, 10));
            layout(window);
            Border updateCard = card(window);
            ProgressBar bar = updateCard.GetVisualDescendants().OfType<ProgressBar>().First();
            Rect inside = boundsIn(window, updateCard).Deflate(updateCard.Padding);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(texts(updateCard), Does.Contain("Downloading update"));
                Assert.That(bar.IsEffectivelyVisible, Is.True);
                Assert.That(bar.Value, Is.EqualTo(0.42).Within(0.001));
                Assert.That(boundsIn(window, bar).Left, Is.GreaterThanOrEqualTo(inside.Left - 0.5));
                Assert.That(boundsIn(window, bar).Right, Is.LessThanOrEqualTo(inside.Right + 0.5));
                Assert.That(texts(updateCard).Any(text => text.Contains("42", StringComparison.Ordinal) && text.Contains('%', StringComparison.Ordinal)), Is.True);
                Assert.That(visibleButtons(updateCard), Is.Empty);
            }

            client.Hold.SetResult();
            await running;
        }

        [AvaloniaTest]
        public async Task ReadyOffersARestartAndSaysItInstallsWhenToriiCloses()
        {
            fixture.Store.Create("alpha");
            (MainWindow window, UpdateViewModel update) = await showOffered();

            await update.UpdateCommand.ExecuteAsync(null);
            layout(window);
            Border updateCard = card(window);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(texts(updateCard), Does.Contain("Update ready"));
                Assert.That(texts(updateCard), Does.Contain("0.1.0-alpha.3 installs when Torii closes."));
                Assert.That(visibleButtons(updateCard), Is.EqualTo(ready_buttons));
                Assert.That(button(updateCard, "Restart now").Command, Is.SameAs(update.RestartCommand));
            }
        }

        [AvaloniaTest]
        public async Task FailedShowsTheReasonAndOffersToTryAgain()
        {
            fixture.Store.Create("alpha");
            client.DownloadFails = new InvalidOperationException("The network is gone.");
            (MainWindow window, UpdateViewModel update) = await showOffered();

            await update.UpdateCommand.ExecuteAsync(null);
            layout(window);
            Border updateCard = card(window);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(texts(updateCard), Does.Contain("Update failed"));
                Assert.That(texts(updateCard), Does.Contain("The network is gone."));
                Assert.That(visibleButtons(updateCard), Is.EqualTo(failed_buttons));
                Assert.That(button(updateCard, "Try again").Command, Is.SameAs(update.UpdateCommand));
            }
        }

        [AvaloniaTest]
        public async Task EveryButtonStaysInsideTheCardInEveryState()
        {
            fixture.Store.Create("alpha");
            (MainWindow window, UpdateViewModel update) = await showOffered();
            var problems = new List<string>();

            check(window, "available", problems);

            client.DownloadFails = new InvalidOperationException("A long reason that has to wrap over several lines inside the card.");
            await update.UpdateCommand.ExecuteAsync(null);
            layout(window);
            check(window, "failed", problems);

            client.DownloadFails = null;
            await update.UpdateCommand.ExecuteAsync(null);
            layout(window);
            check(window, "ready", problems);

            Assert.That(problems, Is.Empty);
        }

        [AvaloniaTest]
        public async Task NotNowCollapsesTheCardAndTheListSlidesBackDownToTheBottom()
        {
            fixture.Store.Create("alpha");
            (MainWindow window, UpdateViewModel _) = await showOffered();
            ListBox list = instanceList(window);
            double shorter = boundsIn(window, list).Bottom;

            button(card(window), "Not now").Command!.Execute(null);
            await settle(window);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(host(window).Classes.Contains("shown"), Is.False);
                Assert.That(host(window).Bounds.Height, Is.LessThan(2));
                Assert.That(host(window).IsEnabled, Is.False);
                Assert.That(shorter, Is.LessThan(window.Bounds.Height - 12 - 50));
                Assert.That(boundsIn(window, list).Bottom, Is.EqualTo(window.Bounds.Height - 12).Within(2));
                Assert.That(texts(card(window)), Does.Contain("Update available"));
                Assert.That(visibleButtons(card(window)), Is.EqualTo(offered_buttons));
            }
        }

        [AvaloniaTest]
        public async Task AnUpdateThatAppearsLaterExpandsTheCardAndPushesTheListUp()
        {
            fixture.Store.Create("alpha");
            MainViewModel shell = fixture.CreateShell();
            var update = new UpdateViewModel(client);
            shell.Update = update;
            MainWindow window = show(shell);
            await settle(window);
            ListBox list = instanceList(window);
            double before = boundsIn(window, list).Bottom;

            await update.CheckAsync();
            await settle(window);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(host(window).Classes.Contains("shown"), Is.True);
                Assert.That(host(window).Bounds.Height, Is.GreaterThan(100));
                Assert.That(boundsIn(window, list).Bottom, Is.LessThan(before - 100));
                Assert.That(boundsIn(window, card(window)).Bottom, Is.EqualTo(window.Bounds.Height - 12).Within(2));
            }
        }

        private static void check(MainWindow window, string state, List<string> problems)
        {
            Rect inside = boundsIn(window, card(window));

            foreach (Button candidate in card(window).GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible))
            {
                Rect bounds = boundsIn(window, candidate);

                if (bounds.Left < inside.Left - 0.5 || bounds.Right > inside.Right + 0.5 || bounds.Bottom > inside.Bottom + 0.5)
                    problems.Add($"{state}: '{candidate.Content}' is outside the card");
            }
        }

        private static ListBox instanceList(MainWindow window) => window.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "InstanceList");

        private static LayoutTransformControl host(MainWindow window) => window.GetVisualDescendants().OfType<LayoutTransformControl>().First(c => c.Name == "UpdateCardHost");

        private static async Task settle(MainWindow window)
        {
            for (int i = 0; i < 30; i++)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                await Task.Delay(15);
                Dispatcher.UIThread.RunJobs();
            }

            layout(window);
        }

        private static Border card(MainWindow window) => window.GetVisualDescendants().OfType<Border>().First(b => b.Name == "UpdateCard");

        private static Rect boundsIn(Visual root, Control control) => new Rect(control.TranslatePoint(new Point(0, 0), root) ?? default, control.Bounds.Size);

        private static string[] texts(Border updateCard) => [.. updateCard.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? string.Empty)];

        private static string[] visibleButtons(Border updateCard) => [.. updateCard.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).Select(b => (string)b.Content!)];

        private static Button button(Border updateCard, string content) => updateCard.GetVisualDescendants().OfType<Button>().First(b => (string?)b.Content == content);

        private static void layout(Window window)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }

        private MainWindow show(MainViewModel shell)
        {
            var window = new MainWindow { DataContext = shell };
            window.Show();
            windows.Add(window);
            layout(window);

            return window;
        }

        private async Task<(MainWindow Window, UpdateViewModel Update)> showOffered()
        {
            MainViewModel shell = fixture.CreateShell();
            var update = new UpdateViewModel(client);
            shell.Update = update;
            await update.CheckAsync();
            MainWindow window = show(shell);
            await settle(window);

            return (window, update);
        }
    }
}

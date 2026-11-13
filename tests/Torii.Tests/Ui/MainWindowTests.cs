using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NUnit.Framework;
using Torii.Core.Lazer;
using Torii.Views;

namespace Torii.Tests
{
    [TestFixture]
    public class MainWindowTests
    {
        private UiFixture fixture = null!;
        private List<Window> windows = null!;

        [SetUp]
        public void SetUp()
        {
            fixture = new UiFixture();
            windows = [];
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Window window in windows)
                window.Close();

            fixture.Dispose();
        }

        [AvaloniaTest]
        public void SidebarShowsEveryInstanceWithItsModCount()
        {
            LazerInstance alpha = fixture.Store.Create("alpha");
            UiFixture.AddMod(alpha, "one");
            UiFixture.AddMod(alpha, "two");
            fixture.Store.Create("beta");

            MainWindow window = show();
            string?[] texts = [.. window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(texts, Does.Contain("alpha"));
                Assert.That(texts, Does.Contain("beta"));
                Assert.That(texts, Does.Contain("2 mods"));
                Assert.That(texts, Does.Contain("0 mods"));
            }
        }

        [AvaloniaTest]
        public void NoTwoButtonsOverlapAtTheMinimumWidth()
        {
            MainWindow window = showSetUpInstanceAtTheMinimumWidth();
            List<Button> buttons = visibleButtons(window);

            Assert.That(buttons, Has.Count.GreaterThan(2));

            for (int i = 0; i < buttons.Count; i++)
            {
                for (int j = i + 1; j < buttons.Count; j++)
                    Assert.That(boundsIn(window, buttons[i]).Intersects(boundsIn(window, buttons[j])), Is.False, $"'{buttons[i].Content}' overlaps '{buttons[j].Content}'");
            }
        }

        [AvaloniaTest]
        public void EveryButtonStaysInsideTheWindowAtTheMinimumWidth()
        {
            MainWindow window = showSetUpInstanceAtTheMinimumWidth();

            foreach (Button button in visibleButtons(window))
            {
                Rect bounds = boundsIn(window, button);
                Assert.That(bounds.Right, Is.LessThanOrEqualTo(window.Bounds.Width + 0.5), $"'{button.Content}' runs off the right edge");
            }
        }

        [AvaloniaTest]
        public void CardMenuHostFillsTheWholeCard()
        {
            fixture.Store.Create("alpha");
            MainWindow window = show();

            ListBoxItem item = window.GetVisualDescendants().OfType<ListBoxItem>().First();
            Border host = cardHost(item);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(host.Bounds.Width, Is.EqualTo(item.Bounds.Width).Within(0.5));
                Assert.That(host.Bounds.Height, Is.EqualTo(item.Bounds.Height).Within(0.5));
            }
        }

        [AvaloniaTest]
        public void RightClickAnywhereOnACardOpensItsMenu()
        {
            fixture.Store.Create("alpha");
            MainWindow window = show();
            ListBoxItem item = window.GetVisualDescendants().OfType<ListBoxItem>().First();
            var flyout = (MenuFlyout)cardHost(item).ContextFlyout!;
            Rect card = boundsIn(window, item);

            Point[] points =
            [
                new Point(card.Left + 2, card.Top + 2),
                new Point(card.Right - 2, card.Top + 2),
                new Point(card.Left + 2, card.Bottom - 2),
                new Point(card.Right - 2, card.Bottom - 2),
                card.Center,
            ];

            foreach (Point point in points)
            {
                window.MouseDown(point, MouseButton.Right);
                window.MouseUp(point, MouseButton.Right);
                Dispatcher.UIThread.RunJobs();

                Assert.That(flyout.IsOpen, Is.True, $"no menu at {point}");

                flyout.Hide();
                Dispatcher.UIThread.RunJobs();
            }
        }

        [AvaloniaTest]
        public void CardMenuHasOpenRenameAndDelete()
        {
            fixture.Store.Create("alpha");
            MainWindow window = show();
            var flyout = (MenuFlyout)cardHost(window.GetVisualDescendants().OfType<ListBoxItem>().First()).ContextFlyout!;

            string?[] headers = [.. flyout.Items.OfType<MenuItem>().Select(i => i.Header as string)];

            Assert.That(headers, Is.EqualTo(card_menu));
        }

        [AvaloniaTest]
        public void MenusLetTheClosingClickReachWhatIsUnderThem()
        {
            LazerInstance alpha = fixture.Store.Create("alpha");
            UiFixture.AddMod(alpha, "one");
            MainWindow window = show();

            var cardMenu = (MenuFlyout)cardHost(window.GetVisualDescendants().OfType<ListBoxItem>().First()).ContextFlyout!;
            var rowMenu = (MenuFlyout)window.GetVisualDescendants().OfType<Grid>().First(g => g.ContextFlyout is MenuFlyout).ContextFlyout!;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cardMenu.OverlayDismissEventPassThrough, Is.True);
                Assert.That(rowMenu.OverlayDismissEventPassThrough, Is.True);
            }
        }

        [AvaloniaTest]
        public void ModRowMenuHasRemoveTryAgainAndOpenFolder()
        {
            LazerInstance alpha = fixture.Store.Create("alpha");
            UiFixture.AddMod(alpha, "one");
            MainWindow window = show();
            var rowMenu = (MenuFlyout)window.GetVisualDescendants().OfType<Grid>().First(g => g.ContextFlyout is MenuFlyout).ContextFlyout!;

            string?[] headers = [.. rowMenu.Items.OfType<MenuItem>().Select(i => i.Header as string)];

            Assert.That(headers, Is.EqualTo(row_menu));
        }

        private static readonly string?[] card_menu = ["Open folder", "Rename", "Delete"];

        private static readonly string?[] row_menu = ["Remove", "Try again", "Open mod folder"];

        private static Border cardHost(ListBoxItem item) => item.GetVisualDescendants().OfType<Border>().First(b => b.ContextFlyout != null);

        private static Rect boundsIn(Visual root, Control control) => new Rect(control.TranslatePoint(new Point(0, 0), root) ?? default, control.Bounds.Size);

        private static List<Button> visibleButtons(Window window) =>
            [.. window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Bounds.Width > 0)];

        private static void layout(Window window)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }

        private MainWindow show()
        {
            var window = new MainWindow { DataContext = fixture.CreateShell() };
            window.Show();
            windows.Add(window);
            layout(window);

            return window;
        }

        private MainWindow showSetUpInstanceAtTheMinimumWidth()
        {
            LazerInstance alpha = fixture.Store.Create("alpha");
            UiFixture.PlaceGame(alpha);

            MainWindow window = show();
            window.Width = window.MinWidth;
            layout(window);

            return window;
        }
    }
}

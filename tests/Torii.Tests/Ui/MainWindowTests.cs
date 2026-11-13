using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NUnit.Framework;
using Torii.Core.Lazer;
using Torii.ViewModels;
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
        public void DroppingAZipOnTheWindowAddsTheMod()
        {
            LazerInstance alpha = fixture.Store.Create("alpha");
            UiFixture.PlaceGame(alpha);
            MainWindow window = show();

            drop(window, fixture.ModZip("a.one", "One"));

            Assert.That(selected(window).ModList.Mods.Single().Label, Is.EqualTo("One"));
        }

        [AvaloniaTest]
        public void DroppingSeveralZipsAddsThemAll()
        {
            LazerInstance alpha = fixture.Store.Create("alpha");
            UiFixture.PlaceGame(alpha);
            MainWindow window = show();

            drop(window, fixture.ModZip("a.one", "One"), fixture.ModZip("b.two", "Two"));

            Assert.That(selected(window).ModList.Mods, Has.Count.EqualTo(2));
        }

        [AvaloniaTest]
        public void DroppingSomethingThatIsNotAZipChangesNothing()
        {
            LazerInstance alpha = fixture.Store.Create("alpha");
            UiFixture.PlaceGame(alpha);
            string notes = Path.Combine(fixture.Root, "notes.txt");
            File.WriteAllText(notes, "hello");
            MainWindow window = show();

            drop(window, notes);

            Assert.That(selected(window).ModList.Mods, Is.Empty);
        }

        [AvaloniaTest]
        public void DroppedZipIsRefusedUntilTheInstanceIsSetUp()
        {
            fixture.Store.Create("alpha");
            MainWindow window = show();

            drop(window, fixture.ModZip("a.one", "One"));

            Assert.That(selected(window).ModList.Mods, Is.Empty);
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
        public void CardMenuHasOpenRenameDuplicateAndDelete()
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
            MenuFlyout rowMenu = modMenu(window);

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
            MenuFlyout rowMenu = modMenu(window);

            string?[] headers = [.. rowMenu.Items.OfType<MenuItem>().Select(i => i.Header as string)];

            Assert.That(headers, Is.EqualTo(row_menu));
        }

        [AvaloniaTest]
        public void ModListScrollsWhenThereAreMoreModsThanFit()
        {
            LazerInstance alpha = fixture.Store.Create("alpha");

            for (int i = 0; i < 40; i++)
                UiFixture.AddMod(alpha, $"mod{i:00}");

            MainWindow window = show();
            ScrollViewer scroller = window.GetVisualDescendants().OfType<InstanceView>().First().GetVisualDescendants().OfType<ScrollViewer>().First(s => s.IsEffectivelyVisible && !s.GetVisualAncestors().OfType<TextBox>().Any());

            using (Assert.EnterMultipleScope())
            {
                Assert.That(scroller.Extent.Height, Is.GreaterThan(scroller.Viewport.Height));
                Assert.That(scroller.Viewport.Height, Is.LessThanOrEqualTo(window.Bounds.Height));
            }

            scroller.ScrollToEnd();
            layout(window);

            Assert.That(scroller.Offset.Y, Is.GreaterThan(0));
        }

        [AvaloniaTest]
        public void EveryModIsItsOwnCard()
        {
            LazerInstance alpha = fixture.Store.Create("alpha");
            UiFixture.AddMod(alpha, "one");
            UiFixture.AddMod(alpha, "two");
            MainWindow window = show();

            List<Border> cards = [.. window.GetVisualDescendants().OfType<InstanceView>().First().GetVisualDescendants().OfType<Border>().Where(b => b.ContextFlyout is MenuFlyout)];

            Assert.That(cards, Has.Count.EqualTo(2));
            Assert.That(boundsIn(window, cards[0]).Intersects(boundsIn(window, cards[1])), Is.False);
        }

        [AvaloniaTest]
        public void TypingInTheSearchBoxHidesOtherModCards()
        {
            LazerInstance alpha = fixture.Store.Create("alpha");
            UiFixture.AddMod(alpha, "snow");
            UiFixture.AddMod(alpha, "trail");
            MainWindow window = show();
            TextBox box = window.GetVisualDescendants().OfType<TextBox>().First(t => t.PlaceholderText == "Search mods");

            box.Text = "snow";
            layout(window);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(box.IsEffectivelyVisible, Is.True);
                Assert.That(window.GetVisualDescendants().OfType<InstanceView>().First().GetVisualDescendants().OfType<Border>().Count(b => b.ContextFlyout is MenuFlyout), Is.EqualTo(1));
            }
        }

        [AvaloniaTest]
        public void SkeletonsGiveWayToTheReleaseControlsOnceTheCheckFinishes()
        {
            fixture.Store.Create("alpha");
            MainWindow window = show();
            InstanceView view = window.GetVisualDescendants().OfType<InstanceView>().First();
            ComboBox releases = view.GetVisualDescendants().OfType<ComboBox>().First();

            Assert.That(() =>
            {
                Dispatcher.UIThread.RunJobs();
                return releases.IsVisible;
            }, Is.True.After(2000, 10));

            layout(window);

            Assert.That(view.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("skeleton") && b.IsVisible), Is.Empty);
        }

        [AvaloniaTest]
        public void SearchBoxIsHiddenWhenThereAreNoMods()
        {
            fixture.Store.Create("alpha");
            MainWindow window = show();

            TextBox box = window.GetVisualDescendants().OfType<TextBox>().First(t => t.PlaceholderText == "Search mods");

            Assert.That(box.IsEffectivelyVisible, Is.False);
        }

        private static MenuFlyout modMenu(MainWindow window) =>
            (MenuFlyout)window.GetVisualDescendants().OfType<InstanceView>().First().GetVisualDescendants().OfType<Border>().First(b => b.ContextFlyout is MenuFlyout).ContextFlyout!;

        private static readonly string?[] card_menu =["Open folder", "Rename", "Duplicate", "Delete"];

        private static readonly string?[] row_menu = ["Remove", "Try again", "Open mod folder"];

        private static Border cardHost(ListBoxItem item) => item.GetVisualDescendants().OfType<Border>().First(b => b.ContextFlyout != null);

        private static Rect boundsIn(Visual root, Control control) => new Rect(control.TranslatePoint(new Point(0, 0), root) ?? default, control.Bounds.Size);

        private static List<Button> visibleButtons(Window window) =>
            [.. window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Bounds.Width > 0)];

        private static InstanceViewModel selected(MainWindow window) => ((MainViewModel)window.DataContext!).Selected!;

        private static void drop(MainWindow window, params string[] paths)
        {
            var transfer = new DataTransfer();

            foreach (string path in paths)
                transfer.Add(DataTransferItem.Create(DataFormat.File, DroppedFile.At(path)));

            var point = new Point(400, 200);
            window.DragDrop(point, RawDragEventType.DragEnter, transfer, DragDropEffects.Copy);
            window.DragDrop(point, RawDragEventType.DragOver, transfer, DragDropEffects.Copy);
            window.DragDrop(point, RawDragEventType.Drop, transfer, DragDropEffects.Copy);
            Dispatcher.UIThread.RunJobs();
        }

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

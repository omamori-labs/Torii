using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        public async Task CardOfAnInstanceWithANewerOmamoriReleaseSaysUpdate()
        {
            LazerInstance alpha = fixture.Store.Create("alpha");
            LazerInstance beta = fixture.Store.Create("beta");
            string installed = UiFixture.PlaceOmamori(alpha);
            UiFixture.PlaceOmamori(beta);
            fixture.ServeReleases("99.0.0", installed);
            MainWindow window = show();
            var shell = (MainViewModel)window.DataContext!;

            foreach (InstanceViewModel instance in shell.Instances)
                await instance.LoadReleasesCommand.ExecuteAsync(null);

            layout(window);

            ListBox list = window.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "InstanceList");
            TextBlock[] updates = [.. list.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text == "Update" && t.IsEffectivelyVisible)];

            Assert.That(updates, Has.Length.EqualTo(2));

            foreach (TextBlock update in updates)
                Assert.That(boundsIn(window, list).Contains(boundsIn(window, update)), Is.True, "the Update label sticks out of the sidebar");
        }

        [AvaloniaTest]
        public async Task CardOfAnInstanceOnTheNewestOmamoriSaysNothing()
        {
            LazerInstance alpha = fixture.Store.Create("alpha");
            string installed = UiFixture.PlaceOmamori(alpha);
            fixture.ServeReleases(installed);
            MainWindow window = show();

            await selected(window).LoadReleasesCommand.ExecuteAsync(null);
            layout(window);

            ListBox list = window.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "InstanceList");

            Assert.That(list.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Update" && t.IsEffectivelyVisible), Is.False);
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
        public void EmptyStateShowsOnlyForASetUpInstanceWithoutMods()
        {
            LazerInstance alpha = fixture.Store.Create("alpha");
            MainWindow notSetUp = show();
            bool beforeSetUp = emptyHint(notSetUp).IsEffectivelyVisible;

            UiFixture.PlaceGame(alpha);
            MainWindow window = show();
            bool empty = emptyHint(window).IsEffectivelyVisible;
            Rect zone = boundsIn(window, emptyHint(window));

            UiFixture.AddMod(alpha, "one");
            selected(window).ModList.Refresh();
            layout(window);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(beforeSetUp, Is.False);
                Assert.That(empty, Is.True);
                Assert.That(zone.Width, Is.GreaterThan(0));
                Assert.That(emptyHint(window).IsEffectivelyVisible, Is.False);
            }
        }

        [AvaloniaTest]
        public void EmptyDropZoneFillsTheWholeModArea()
        {
            UiFixture.PlaceGame(fixture.Store.Create("alpha"));
            MainWindow window = show();
            Panel zone = window.GetVisualDescendants().OfType<Panel>().First(p => p.Name == "EmptyDropZone");
            Rect area = boundsIn(window, (Control)zone.GetVisualParent()!);
            Rect box = boundsIn(window, zone);
            Rect dashed = boundsIn(window, zone.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Rectangle>().First());

            using (Assert.EnterMultipleScope())
            {
                Assert.That(box.Width, Is.EqualTo(area.Width).Within(1));
                Assert.That(box.Height, Is.GreaterThan(area.Height - 12));
                Assert.That(box.Height, Is.GreaterThan(200));
                Assert.That(dashed.Width, Is.EqualTo(box.Width).Within(1));
                Assert.That(dashed.Height, Is.EqualTo(box.Height).Within(1));
                Assert.That(boundsIn(window, emptyHint(window)).Center.X, Is.EqualTo(box.Center.X).Within(2));
                Assert.That(boundsIn(window, emptyHint(window)).Center.Y, Is.GreaterThan(box.Center.Y).And.LessThan(box.Bottom));
            }
        }

        [AvaloniaTest]
        public void EmptyDropZoneHasABigIconAndBigText()
        {
            UiFixture.PlaceGame(fixture.Store.Create("alpha"));
            MainWindow window = show();
            Panel zone = window.GetVisualDescendants().OfType<Panel>().First(p => p.Name == "EmptyDropZone");
            Rect icon = boundsIn(window, zone.GetVisualDescendants().OfType<Optris.Icons.Avalonia.Icon>().First());
            TextBlock hint = emptyHint(window);
            Rect zoneBounds = boundsIn(window, zone);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(icon.Height, Is.GreaterThanOrEqualTo(64));
                Assert.That(hint.FontSize, Is.GreaterThanOrEqualTo(20));
                Assert.That(icon.Center.X, Is.EqualTo(zoneBounds.Center.X).Within(2));
                Assert.That(boundsIn(window, hint).Bottom, Is.LessThan(zoneBounds.Bottom));
                Assert.That(icon.Top, Is.GreaterThan(zoneBounds.Top));
            }
        }

        [AvaloniaTest]
        public void EmptyDropZoneEndsLevelWithTheBottomOfTheSidebar()
        {
            UiFixture.PlaceGame(fixture.Store.Create("alpha"));
            MainWindow window = show();
            Panel zone = window.GetVisualDescendants().OfType<Panel>().First(p => p.Name == "EmptyDropZone");
            ListBox sidebar = window.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "InstanceList");

            Assert.That(boundsIn(window, zone).Bottom, Is.EqualTo(boundsIn(window, sidebar).Bottom).Within(1));
        }

        [AvaloniaTest]
        public void SidebarPanelHasRoundedCornersThatClipItsCards()
        {
            fixture.Store.Create("alpha");
            MainWindow window = show();
            ListBox list = window.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "InstanceList");
            Border frame = list.GetVisualDescendants().OfType<Border>().First(b => b.Name == "border");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(list.CornerRadius, Is.EqualTo(new CornerRadius(4)));
                Assert.That(frame.CornerRadius, Is.EqualTo(new CornerRadius(4)));
                Assert.That(frame.ClipToBounds, Is.True);
            }
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

        private static readonly string?[] row_menu =["Remove", "Try again", "Open mod folder"];

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

            raise(window, transfer, new Point(400, 200), DragDropEffects.Copy, dropIt: true);
        }

        private static void raise(MainWindow window, DataTransfer transfer, Point point, DragDropEffects effects, bool dropIt)
        {
            window.DragDrop(point, RawDragEventType.DragEnter, transfer, effects);
            window.DragDrop(point, RawDragEventType.DragOver, transfer, effects);

            if (dropIt)
                window.DragDrop(point, RawDragEventType.Drop, transfer, effects);

            Dispatcher.UIThread.RunJobs();
        }

        private static TextBlock emptyHint(MainWindow window) => window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Drop a mod here");

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

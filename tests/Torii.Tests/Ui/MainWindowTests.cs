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
        public void DroppingACardBelowTheLastOneMovesItToTheEnd()
        {
            fixture.Store.Create("alpha");
            fixture.Store.Create("beta");
            fixture.Store.Create("gamma");
            MainWindow window = show();
            var shell = (MainViewModel)window.DataContext!;
            IReadOnlyList<string>? saved = null;
            shell.RememberOrder = order => saved = order;
            Rect last = boundsIn(window, cards(window)[2]);

            dragCard(window, shell.Instances[0], new Point(last.Center.X, last.Bottom - 2), dropIt: true);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(shell.Instances.Select(i => i.Name), Is.EqualTo(beta_gamma_alpha));
                Assert.That(saved!.Select(Path.GetFileName), Is.EqualTo(beta_gamma_alpha));
                Assert.That(dropLine(window).IsVisible, Is.False);
            }
        }

        [AvaloniaTest]
        public void DroppingACardAboveTheFirstOneMovesItToTheTop()
        {
            fixture.Store.Create("alpha");
            fixture.Store.Create("beta");
            fixture.Store.Create("gamma");
            MainWindow window = show();
            var shell = (MainViewModel)window.DataContext!;
            Rect first = boundsIn(window, cards(window)[0]);

            dragCard(window, shell.Instances[2], new Point(first.Center.X, first.Top + 2), dropIt: true);

            Assert.That(shell.Instances.Select(i => i.Name), Is.EqualTo(gamma_alpha_beta));
        }

        [AvaloniaTest]
        public void DroppingACardBetweenTwoOthersPutsItThere()
        {
            fixture.Store.Create("alpha");
            fixture.Store.Create("beta");
            fixture.Store.Create("gamma");
            MainWindow window = show();
            var shell = (MainViewModel)window.DataContext!;
            Rect second = boundsIn(window, cards(window)[1]);

            dragCard(window, shell.Instances[0], new Point(second.Center.X, second.Bottom - 2), dropIt: true);

            Assert.That(shell.Instances.Select(i => i.Name), Is.EqualTo(beta_alpha_gamma));
        }

        [AvaloniaTest]
        public void DroppingACardWhereItAlreadyIsSavesNothing()
        {
            fixture.Store.Create("alpha");
            fixture.Store.Create("beta");
            MainWindow window = show();
            var shell = (MainViewModel)window.DataContext!;
            int saves = 0;
            shell.RememberOrder = _ => saves++;
            Rect first = boundsIn(window, cards(window)[0]);

            dragCard(window, shell.Instances[0], first.Center, dropIt: true);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(saves, Is.Zero);
                Assert.That(shell.Instances.Select(i => i.Name), Is.EqualTo(alpha_beta));
            }
        }

        [AvaloniaTest]
        public void DraggingOverTheListShowsTheInsertionLineWhereTheCardWouldLand()
        {
            fixture.Store.Create("alpha");
            fixture.Store.Create("beta");
            fixture.Store.Create("gamma");
            MainWindow window = show();
            var shell = (MainViewModel)window.DataContext!;
            Rect second = boundsIn(window, cards(window)[1]);

            dragCard(window, shell.Instances[2], new Point(second.Center.X, second.Top + 2), dropIt: false);
            Border line = dropLine(window);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(line.IsVisible, Is.True);
                Assert.That(boundsIn(window, line).Center.Y, Is.EqualTo(second.Top).Within(2));
                Assert.That(shell.Instances.Select(i => i.Name), Is.EqualTo(alpha_beta_gamma));
            }
        }

        [AvaloniaTest]
        public void DroppingACardOutsideTheListChangesNothing()
        {
            fixture.Store.Create("alpha");
            fixture.Store.Create("beta");
            MainWindow window = show();
            var shell = (MainViewModel)window.DataContext!;

            dragCard(window, shell.Instances[0], new Point(window.Bounds.Width - 50, 300), dropIt: true);

            Assert.That(shell.Instances.Select(i => i.Name), Is.EqualTo(alpha_beta));
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
        public void DroppingAModCardBelowTheLastOneMovesItToTheEndAndSavesTheOrder()
        {
            LazerInstance alpha = threeMods();
            MainWindow window = show();
            InstanceViewModel instance = selected(window);
            Rect last = boundsIn(window, modCards(window)[2]);

            dragMod(window, instance.ModList.Mods[0], new Point(last.Center.X, last.Bottom - 2), dropIt: true);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(instance.ModList.Mods.Select(m => m.Label), Is.EqualTo(b2_c3_a1));
                Assert.That(File.ReadAllLines(alpha.ModOrderFile), Is.EqualTo(b2_c3_a1));
                Assert.That(modDropLine(window).IsVisible, Is.False);
            }
        }

        [AvaloniaTest]
        public void DroppingInTheEmptySpaceUnderTheModCardsMovesItToTheEnd()
        {
            threeMods();
            MainWindow window = show();
            InstanceViewModel instance = selected(window);
            Rect last = boundsIn(window, modCards(window)[2]);

            dragMod(window, instance.ModList.Mods[0], new Point(last.Center.X, last.Bottom + 40), dropIt: true);

            Assert.That(instance.ModList.Mods.Select(m => m.Label), Is.EqualTo(b2_c3_a1));
        }

        [AvaloniaTest]
        public void DraggingOverTheModsShowsTheInsertionLineWhereTheCardWouldLand()
        {
            threeMods();
            MainWindow window = show();
            InstanceViewModel instance = selected(window);
            Rect second = boundsIn(window, modCards(window)[1]);

            dragMod(window, instance.ModList.Mods[2], new Point(second.Center.X, second.Top + 2), dropIt: false);
            Border line = modDropLine(window);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(line.IsVisible, Is.True);
                Assert.That(boundsIn(window, line).Center.Y, Is.EqualTo(second.Top).Within(2));
                Assert.That(instance.ModList.Mods.Select(m => m.Label), Is.EqualTo(a1_b2_c3));
            }
        }

        [AvaloniaTest]
        public void ModsCannotBeReorderedWhileSearching()
        {
            LazerInstance alpha = threeMods();
            MainWindow window = show();
            InstanceViewModel instance = selected(window);
            instance.ModList.Search = "a1";
            layout(window);
            Rect only = boundsIn(window, modCards(window)[0]);

            dragMod(window, instance.ModList.Mods[0], new Point(only.Center.X, only.Bottom + 40), dropIt: true);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(instance.ModList.Mods.Select(m => m.Label), Is.EqualTo(a1_b2_c3));
                Assert.That(File.Exists(alpha.ModOrderFile), Is.False);
                Assert.That(modDropLine(window).IsVisible, Is.False);
            }
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

        private static readonly string[] alpha_beta = ["alpha", "beta"];

        private static readonly string[] alpha_beta_gamma = ["alpha", "beta", "gamma"];

        private static readonly string[] beta_gamma_alpha = ["beta", "gamma", "alpha"];

        private static readonly string[] gamma_alpha_beta = ["gamma", "alpha", "beta"];

        private static readonly string[] beta_alpha_gamma = ["beta", "alpha", "gamma"];

        private static readonly string[] a1_b2_c3 = ["a1", "b2", "c3"];

        private static readonly string[] b2_c3_a1 = ["b2", "c3", "a1"];

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

        private static void dragCard(MainWindow window, InstanceViewModel card, Point point, bool dropIt)
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(ReorderDrag.FORMAT, card));

            raise(window, transfer, point, DragDropEffects.Move, dropIt);
        }

        private LazerInstance threeMods()
        {
            LazerInstance alpha = fixture.Store.Create("alpha");
            UiFixture.AddMod(alpha, "a1");
            UiFixture.AddMod(alpha, "b2");
            UiFixture.AddMod(alpha, "c3");

            return alpha;
        }

        private static void dragMod(MainWindow window, ModItemViewModel mod, Point point, bool dropIt)
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(ReorderDrag.FORMAT, mod));

            raise(window, transfer, point, DragDropEffects.Move, dropIt);
        }

        private static List<Border> modCards(MainWindow window) =>
            [.. window.GetVisualDescendants().OfType<InstanceView>().First().GetVisualDescendants().OfType<Border>().Where(b => b.ContextFlyout is MenuFlyout)];

        private static Border modDropLine(MainWindow window) => window.GetVisualDescendants().OfType<Border>().First(b => b.Name == "ModDropLine");

        private static List<ListBoxItem> cards(MainWindow window) => [.. window.GetVisualDescendants().OfType<ListBoxItem>()];

        private static Border dropLine(MainWindow window) => window.GetVisualDescendants().OfType<Border>().First(b => b.Name == "InstanceDropLine");

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

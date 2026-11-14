using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NUnit.Framework;
using Torii.Core.Lazer;
using Torii.ViewModels;
using Torii.Views;

namespace Torii.Tests
{
    [TestFixture]
    public class ReorderDragTests
    {
        private static readonly string[] alpha_beta_gamma = ["alpha", "beta", "gamma"];
        private static readonly string[] beta_gamma_alpha = ["beta", "gamma", "alpha"];
        private static readonly string[] a1_b2_c3 = ["a1", "b2", "c3"];
        private static readonly string[] b2_c3_a1 = ["b2", "c3", "a1"];

        private UiFixture fixture = null!;
        private List<Window> windows = null!;

        [SetUp]
        public void SetUp()
        {
            ReorderDrag.Animate = false;
            fixture = new UiFixture();
            windows = [];
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Window window in windows)
                window.Close();

            fixture.Dispose();
            ReorderDrag.Animate = true;
        }

        [AvaloniaTest]
        public void CardLiftsAndFollowsThePointerWhileTheOthersMakeRoom()
        {
            threeInstances();
            MainWindow window = show();
            List<ListBoxItem> items = cards(window);
            Rect first = boundsIn(window, items[0]);
            Rect second = boundsIn(window, items[1]);
            Rect third = boundsIn(window, items[2]);
            double slot = second.Top - first.Top;
            Point start = first.Center;

            press(window, start);
            moveTo(window, new Point(start.X, start.Y + 10));
            moveTo(window, new Point(start.X, start.Y + slot * 1.4));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(items[0].Opacity, Is.LessThan(1));
                Assert.That(items[0].Effect, Is.Not.Null);
                Assert.That(items[0].ZIndex, Is.GreaterThan(0));
                Assert.That(boundsIn(window, items[0]).Center.Y, Is.EqualTo(start.Y + slot * 1.4).Within(2));
                Assert.That(boundsIn(window, items[1]).Top, Is.EqualTo(first.Top).Within(2));
                Assert.That(boundsIn(window, items[2]).Top, Is.EqualTo(third.Top).Within(2));
            }
        }

        [AvaloniaTest]
        public void ALiftedCardNeverGrowsPastItsOwnEdges()
        {
            threeInstances();
            MainWindow window = show();
            ListBoxItem item = cards(window)[0];
            Rect before = corners(window, item);
            Point start = before.Center;

            press(window, start);
            moveTo(window, new Point(start.X, start.Y + 10));
            moveTo(window, new Point(start.X, start.Y + 60));
            Rect during = corners(window, item);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(during.Width, Is.EqualTo(before.Width).Within(0.5));
                Assert.That(during.Height, Is.EqualTo(before.Height).Within(0.5));
                Assert.That(during.Left, Is.EqualTo(before.Left).Within(0.5));
            }

            release(window, new Point(start.X, start.Y + 60));
        }

        [AvaloniaTest]
        public void ALiftedModCardNeverGrowsPastItsOwnEdges()
        {
            threeMods();
            MainWindow window = show();
            Border card = modCards(window)[0];
            Rect before = corners(window, card);
            Point grab = new Point(before.Left + 20, before.Center.Y);

            press(window, grab);
            moveTo(window, new Point(grab.X, grab.Y + 10));
            moveTo(window, new Point(grab.X, grab.Y + 60));
            Rect during = corners(window, card);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(during.Width, Is.EqualTo(before.Width).Within(0.5));
                Assert.That(during.Height, Is.EqualTo(before.Height).Within(0.5));
                Assert.That(during.Left, Is.EqualTo(before.Left).Within(0.5));
            }

            release(window, new Point(grab.X, grab.Y + 60));
        }

        [AvaloniaTest]
        public void ReleasingDropsTheCardAndSavesTheOrder()
        {
            threeInstances();
            MainWindow window = show();
            MainViewModel shell = shellOf(window);
            IReadOnlyList<string>? saved = null;
            shell.RememberOrder = order => saved = order;
            Rect first = boundsIn(window, cards(window)[0]);

            drag(window, first.Center, new Point(first.Center.X, first.Center.Y + (first.Height + 8) * 2.2));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(shell.Instances.Select(i => i.Name), Is.EqualTo(beta_gamma_alpha));
                Assert.That(saved!.Select(Path.GetFileName), Is.EqualTo(beta_gamma_alpha));
                Assert.That(cards(window).All(card => card.Opacity == 1 && card.Effect == null && card.RenderTransform == null), Is.True);
            }
        }

        [AvaloniaTest]
        public void DraggingPastTheEndsPutsTheCardFirstOrLast()
        {
            threeInstances();
            MainWindow window = show();
            MainViewModel shell = shellOf(window);
            Rect first = topCard(window);

            drag(window, first.Center, new Point(first.Center.X, window.Bounds.Height - 4));
            layout(window);
            string[] afterDown = [.. shell.Instances.Select(i => i.Name)];

            Rect last = bottomCard(window);
            drag(window, last.Center, new Point(last.Center.X, first.Top - 40));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(afterDown, Is.EqualTo(beta_gamma_alpha));
                Assert.That(shell.Instances.Select(i => i.Name), Is.EqualTo(alpha_beta_gamma));
            }
        }

        [AvaloniaTest]
        public void MovingTheSelectedInstanceKeepsItSelected()
        {
            threeInstances();
            MainWindow window = show();
            MainViewModel shell = shellOf(window);
            InstanceViewModel alpha = shell.Selected!;
            Rect first = topCard(window);

            drag(window, first.Center, new Point(first.Center.X, window.Bounds.Height - 4));
            layout(window);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(shell.Instances.Select(i => i.Name), Is.EqualTo(beta_gamma_alpha));
                Assert.That(shell.Selected, Is.SameAs(alpha));
                Assert.That(window.GetVisualDescendants().OfType<InstanceView>().First().IsEffectivelyVisible, Is.True);
            }
        }

        [AvaloniaTest]
        public void DraggingBackToTheStartAndReleasingChangesNothing()
        {
            threeInstances();
            MainWindow window = show();
            MainViewModel shell = shellOf(window);
            int saves = 0;
            shell.RememberOrder = _ => saves++;
            Rect second = boundsIn(window, cards(window)[1]);

            press(window, second.Center);
            moveTo(window, new Point(second.Center.X, second.Center.Y + 10));
            moveTo(window, new Point(second.Center.X, second.Center.Y + 90));
            moveTo(window, second.Center);
            release(window, second.Center);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(saves, Is.Zero);
                Assert.That(shell.Instances.Select(i => i.Name), Is.EqualTo(alpha_beta_gamma));
            }
        }

        [AvaloniaTest]
        public void EscapeCancelsTheDragAndEverythingSpringsBack()
        {
            threeInstances();
            MainWindow window = show();
            MainViewModel shell = shellOf(window);
            List<ListBoxItem> items = cards(window);
            Rect first = boundsIn(window, items[0]);
            Rect second = boundsIn(window, items[1]);

            press(window, first.Center);
            moveTo(window, new Point(first.Center.X, first.Center.Y + 10));
            moveTo(window, new Point(first.Center.X, first.Center.Y + 120));
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            release(window, new Point(first.Center.X, first.Center.Y + 120));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(shell.Instances.Select(i => i.Name), Is.EqualTo(alpha_beta_gamma));
                Assert.That(items.All(card => card.Opacity == 1 && card.Effect == null && card.RenderTransform == null), Is.True);
                Assert.That(boundsIn(window, items[0]).Top, Is.EqualTo(first.Top).Within(1));
                Assert.That(boundsIn(window, items[1]).Top, Is.EqualTo(second.Top).Within(1));
            }
        }

        [AvaloniaTest]
        public void ClickingWithoutMovingSelectsTheCardAndDoesNotDrag()
        {
            threeInstances();
            MainWindow window = show();
            MainViewModel shell = shellOf(window);
            List<ListBoxItem> items = cards(window);
            Point point = boundsIn(window, items[2]).Center;

            press(window, point);
            moveTo(window, new Point(point.X + 2, point.Y + 2));
            release(window, point);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(shell.Selected!.Name, Is.EqualTo("gamma"));
                Assert.That(shell.Instances.Select(i => i.Name), Is.EqualTo(alpha_beta_gamma));
                Assert.That(items.All(card => card.Effect == null), Is.True);
            }
        }

        [AvaloniaTest]
        public void RightButtonNeverStartsADrag()
        {
            threeInstances();
            MainWindow window = show();
            List<ListBoxItem> items = cards(window);
            Point point = boundsIn(window, items[0]).Center;

            window.MouseDown(point, MouseButton.Right);
            window.MouseMove(new Point(point.X, point.Y + 10), RawInputModifiers.RightMouseButton);
            window.MouseMove(new Point(point.X, point.Y + 120), RawInputModifiers.RightMouseButton);
            Dispatcher.UIThread.RunJobs();

            Assert.That(items.All(card => card.Effect == null), Is.True);

            window.MouseUp(new Point(point.X, point.Y + 120), MouseButton.Right);
        }

        [AvaloniaTest]
        public void ModCardsReorderWithTheMouseAndTheOrderIsSaved()
        {
            LazerInstance alpha = threeMods();
            MainWindow window = show();
            InstanceViewModel instance = shellOf(window).Selected!;
            Rect first = boundsIn(window, modCards(window)[0]);
            Point grab = new Point(first.Left + 20, first.Center.Y);

            drag(window, grab, new Point(grab.X, grab.Y + (first.Height + 8) * 2.2));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(instance.ModList.Mods.Select(m => m.Label), Is.EqualTo(b2_c3_a1));
                Assert.That(File.ReadAllLines(alpha.ModOrderFile), Is.EqualTo(b2_c3_a1));
            }
        }

        [AvaloniaTest]
        public void ModsCannotBeDraggedWhileSearching()
        {
            LazerInstance alpha = threeMods();
            MainWindow window = show();
            InstanceViewModel instance = shellOf(window).Selected!;
            instance.ModList.Search = "a1";
            layout(window);
            Border only = modCards(window)[0];
            Rect bounds = boundsIn(window, only);
            Point grab = new Point(bounds.Left + 20, bounds.Center.Y);

            press(window, grab);
            moveTo(window, new Point(grab.X, grab.Y + 10));
            moveTo(window, new Point(grab.X, grab.Y + 150));
            bool lifted = only.GetVisualAncestors().OfType<ContentPresenter>().First().Effect != null;
            release(window, new Point(grab.X, grab.Y + 150));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(lifted, Is.False);
                Assert.That(instance.ModList.Mods.Select(m => m.Label), Is.EqualTo(a1_b2_c3));
                Assert.That(File.Exists(alpha.ModOrderFile), Is.False);
            }
        }

        [AvaloniaTest]
        public void PressingTheToggleDoesNotStartADrag()
        {
            threeMods();
            MainWindow window = show();
            ToggleSwitch toggle = window.GetVisualDescendants().OfType<ToggleSwitch>().First();
            Border card = toggle.GetVisualAncestors().OfType<Border>().First(b => b.ContextFlyout != null);
            Control container = card.GetVisualAncestors().OfType<ContentPresenter>().First();
            Point point = boundsIn(window, toggle).Center;

            press(window, point);
            moveTo(window, new Point(point.X, point.Y + 10));
            moveTo(window, new Point(point.X, point.Y + 150));

            Assert.That(container.Effect, Is.Null);

            release(window, new Point(point.X, point.Y + 150));
        }

        [AvaloniaTest]
        public async Task DraggingNearTheBottomEdgeScrollsALongModList()
        {
            LazerInstance alpha = fixture.Store.Create("alpha");

            for (int i = 0; i < 40; i++)
                UiFixture.AddMod(alpha, $"mod{i:00}");

            MainWindow window = show();
            ScrollViewer scroller = window.GetVisualDescendants().OfType<InstanceView>().First().GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "ModScroller");
            Rect first = boundsIn(window, modCards(window)[0]);
            Rect area = boundsIn(window, scroller);
            Point grab = new Point(first.Left + 20, first.Center.Y);

            press(window, grab);
            moveTo(window, new Point(grab.X, grab.Y + 10));
            moveTo(window, new Point(grab.X, area.Bottom - 6));

            for (int i = 0; i < 40 && scroller.Offset.Y <= 0; i++)
            {
                await Task.Delay(20);
                Dispatcher.UIThread.RunJobs();
            }

            double scrolled = scroller.Offset.Y;
            release(window, new Point(grab.X, area.Bottom - 6));

            Assert.That(scrolled, Is.GreaterThan(0));
        }

        private static void press(MainWindow window, Point point)
        {
            window.MouseDown(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        private static void moveTo(MainWindow window, Point point)
        {
            window.MouseMove(point, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
        }

        private static void release(MainWindow window, Point point)
        {
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        private static void drag(MainWindow window, Point from, Point to)
        {
            press(window, from);
            moveTo(window, new Point(from.X, from.Y + 10));
            moveTo(window, to);
            release(window, to);
        }

        private static Rect boundsIn(Visual root, Control control) => new Rect(control.TranslatePoint(new Point(0, 0), root) ?? default, control.Bounds.Size);

        private static Rect corners(Visual root, Control control) =>
            new Rect(control.TranslatePoint(new Point(0, 0), root) ?? default, control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), root) ?? default);

        private static MainViewModel shellOf(MainWindow window) => (MainViewModel)window.DataContext!;

        private static List<ListBoxItem> cards(MainWindow window) => [.. window.GetVisualDescendants().OfType<ListBoxItem>()];

        private static Rect topCard(MainWindow window) => cards(window).Select(card => boundsIn(window, card)).OrderBy(bounds => bounds.Top).First();

        private static Rect bottomCard(MainWindow window) => cards(window).Select(card => boundsIn(window, card)).OrderBy(bounds => bounds.Top).Last();

        private static List<Border> modCards(MainWindow window) =>
            [.. window.GetVisualDescendants().OfType<InstanceView>().First().GetVisualDescendants().OfType<Border>().Where(b => b.ContextFlyout is MenuFlyout)];

        private static void layout(Window window)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }

        private void threeInstances()
        {
            fixture.Store.Create("alpha");
            fixture.Store.Create("beta");
            fixture.Store.Create("gamma");
        }

        private LazerInstance threeMods()
        {
            LazerInstance alpha = fixture.Store.Create("alpha");
            UiFixture.AddMod(alpha, "a1");
            UiFixture.AddMod(alpha, "b2");
            UiFixture.AddMod(alpha, "c3");

            return alpha;
        }

        private MainWindow show()
        {
            var window = new MainWindow { DataContext = fixture.CreateShell() };
            window.Show();
            windows.Add(window);
            layout(window);

            return window;
        }
    }
}

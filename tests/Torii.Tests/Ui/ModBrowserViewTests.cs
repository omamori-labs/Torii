using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NUnit.Framework;
using Torii.Core.Lazer;
using Torii.ViewModels;
using Torii.Views;

namespace Torii.Tests
{
    [TestFixture]
    public class ModBrowserViewTests
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
        public void BrowseButtonIsOnlyOnAnInstanceThatIsSetUp()
        {
            fixture.Store.Create("alpha");
            Assert.That(browseButton(show()), Is.Null);

            UiFixture.PlaceGame(fixture.Store.Create("beta"));
            MainWindow window = show();
            selectNamed(window, "beta");

            Assert.That(browseButton(window), Is.Not.Null);
        }

        [AvaloniaTest]
        public async Task BrowsingSwapsTheInstalledListForTheModList()
        {
            LazerInstance alpha = fixture.Store.Create("alpha");
            UiFixture.PlaceGame(alpha);
            UiFixture.AddMod(alpha, "installed.one");
            fixture.ServeMod("ui.one", "First", "does one thing");
            fixture.ServeMod("ui.two", "Second");
            MainWindow window = show();
            ScrollViewer installed = scroller(window, "ModScroller");

            Assert.That(installed.IsEffectivelyVisible, Is.True);

            await selected(window).BrowseCommand.ExecuteAsync(null);
            layout(window);

            ScrollViewer browser = scroller(window, "BrowserScroller");
            List<Button> installs = [.. browser.GetVisualDescendants().OfType<Button>().Where(b => (string?)b.Content == "Install")];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(installed.IsEffectivelyVisible, Is.False);
                Assert.That(browser.IsEffectivelyVisible, Is.True);
                Assert.That(browseButton(window, "Installed mods"), Is.Not.Null);
                Assert.That(browser.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text), Does.Contain("First").And.Contain("Second").And.Contain("does one thing"));
                Assert.That(installs, Has.Count.EqualTo(2));

                foreach (Button install in installs)
                    Assert.That(boundsIn(window, browser).Contains(boundsIn(window, install)), Is.True, "an Install button sticks out of the list");
            }
        }

        [AvaloniaTest]
        public async Task BrowsingAgainShowsTheInstalledList()
        {
            LazerInstance alpha = fixture.Store.Create("alpha");
            UiFixture.PlaceGame(alpha);
            UiFixture.AddMod(alpha, "installed.one");
            fixture.ServeMod("ui.one", "First");
            MainWindow window = show();

            await selected(window).BrowseCommand.ExecuteAsync(null);
            await selected(window).BrowseCommand.ExecuteAsync(null);
            layout(window);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(scroller(window, "ModScroller").IsEffectivelyVisible, Is.True);
                Assert.That(window.GetVisualDescendants().OfType<DockPanel>().First(p => p.Name == "BrowserArea").IsEffectivelyVisible, Is.False);
                Assert.That(browseButton(window, "Browse mods"), Is.Not.Null);
            }
        }

        [AvaloniaTest]
        public async Task InstallingAModFromTheListAddsItToTheInstalledMods()
        {
            LazerInstance alpha = fixture.Store.Create("alpha");
            UiFixture.PlaceGame(alpha);
            fixture.ServeMod("ui.one", "First");
            MainWindow window = show();
            InstanceViewModel instance = selected(window);

            await instance.BrowseCommand.ExecuteAsync(null);
            layout(window);

            ScrollViewer browser = scroller(window, "BrowserScroller");
            Button install = browser.GetVisualDescendants().OfType<Button>().Single(b => (string?)b.Content == "Install");

            Assert.That(install.Command, Is.SameAs(instance.Browser!.Mods.Single().InstallCommand));

            await instance.Browser.Mods.Single().InstallCommand.ExecuteAsync(null);
            layout(window);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(instance.ModList.Mods.Single().Label, Is.EqualTo("First"));
                Assert.That(browser.GetVisualDescendants().OfType<Button>().Any(b => b.IsEffectivelyVisible && (string?)b.Content == "Install"), Is.False);
                Assert.That(browser.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text), Does.Contain("Installed"));
                Assert.That(instance.IsBusy, Is.False);
            }
        }

        [AvaloniaTest]
        public async Task InstalledModWithANewerVersionOffersAnUpdate()
        {
            LazerInstance alpha = fixture.Store.Create("alpha");
            UiFixture.PlaceGame(alpha);
            UiFixture.AddMod(alpha, "ui.one");
            fixture.ServeMod("ui.one", "First", version: "2.0.0");
            MainWindow window = show();
            InstanceViewModel instance = selected(window);

            await instance.EnsureModIndexAsync();
            layout(window);

            ScrollViewer installed = scroller(window, "ModScroller");
            Button update = installed.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && (string?)b.Content == "Update to 2.0.0");

            Assert.That(boundsIn(window, installed).Contains(boundsIn(window, update)), Is.True, "the Update button sticks out of the list");
            Assert.That(update.Command, Is.SameAs(instance.ModList.Mods.Single().UpdateCommand));

            await instance.ModList.Mods.Single().UpdateCommand.ExecuteAsync(null);
            layout(window);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(instance.ModList.Mods.Single().Version, Is.EqualTo("2.0.0"));
                Assert.That(installed.GetVisualDescendants().OfType<Button>().Any(b => b.IsEffectivelyVisible && ((string?)b.Content)?.StartsWith("Update", StringComparison.Ordinal) == true), Is.False);
            }
        }

        [AvaloniaTest]
        public async Task ModThatIsUpToDateOffersNoUpdate()
        {
            LazerInstance alpha = fixture.Store.Create("alpha");
            UiFixture.PlaceGame(alpha);
            UiFixture.AddMod(alpha, "ui.one");
            fixture.ServeMod("ui.one", "First");
            MainWindow window = show();

            await selected(window).EnsureModIndexAsync();
            layout(window);

            Assert.That(scroller(window, "ModScroller").GetVisualDescendants().OfType<Button>().Any(b => b.IsEffectivelyVisible && ((string?)b.Content)?.StartsWith("Update", StringComparison.Ordinal) == true), Is.False);
        }

        [AvaloniaTest]
        public async Task OnlyTheModsOnScreenAreBuilt()
        {
            UiFixture.PlaceGame(fixture.Store.Create("alpha"));

            for (int i = 0; i < 300; i++)
                fixture.ServeMod($"ui.mod{i:000}", $"Mod {i:000}");

            MainWindow window = show();
            await selected(window).BrowseCommand.ExecuteAsync(null);
            layout(window);

            ScrollViewer browser = scroller(window, "BrowserScroller");
            int built = browser.GetVisualDescendants().OfType<Button>().Count(b => b.Content as string == "Install");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(selected(window).Browser!.Visible, Has.Count.EqualTo(300));
                Assert.That(built, Is.GreaterThan(0).And.LessThan(60));
            }

            browser.ScrollToEnd();
            layout(window);
            layout(window);

            string?[] names = [.. browser.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(names, Does.Contain("Mod 299"));
                Assert.That(names, Does.Not.Contain("Mod 000"));
            }
        }

        [AvaloniaTest]
        public async Task ListThatCantBeLoadedShowsWhyAndCanBeTriedAgain()
        {
            UiFixture.PlaceGame(fixture.Store.Create("alpha"));
            MainWindow window = show();

            await selected(window).BrowseCommand.ExecuteAsync(null);
            layout(window);

            ScrollViewer browser = scroller(window, "BrowserScroller");
            Control area = window.GetVisualDescendants().OfType<DockPanel>().First(p => p.Name == "BrowserArea");
            Button retry = area.GetVisualDescendants().OfType<Button>().Single(b => (string?)b.Content == "Try again");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(area.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Any(t => t?.StartsWith("Couldn't load the mod list", StringComparison.Ordinal) == true), Is.True);
                Assert.That(retry.IsEffectivelyVisible, Is.True);
                Assert.That(browser.GetVisualDescendants().OfType<Button>(), Is.Empty);
            }

            fixture.ServeMod("ui.one", "First");
            await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)retry.Command!).ExecuteAsync(null);
            layout(window);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(retry.IsEffectivelyVisible, Is.False);
                Assert.That(browser.GetVisualDescendants().OfType<Button>().Count(b => (string?)b.Content == "Install"), Is.EqualTo(1));
            }
        }

        [AvaloniaTest]
        public async Task EveryButtonStaysInsideTheWindowAtTheMinimumWidthWhileBrowsing()
        {
            UiFixture.PlaceGame(fixture.Store.Create("alpha"));
            fixture.ServeMod("ui.one", "First", "a description long enough to wrap onto a second line when the window is as narrow as it can get");
            fixture.ServeMod("ui.two", "Second");
            MainWindow window = show();
            window.Width = window.MinWidth;
            layout(window);

            await selected(window).BrowseCommand.ExecuteAsync(null);
            layout(window);

            List<Button> buttons = [.. window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Bounds.Width > 0)];

            Assert.That(buttons, Has.Count.GreaterThan(3));

            foreach (Button button in buttons)
                Assert.That(boundsIn(window, button).Right, Is.LessThanOrEqualTo(window.Bounds.Width + 0.5), $"'{button.Content}' runs off the right edge");
        }

        private static Button? browseButton(Window window, string label = "Browse mods") =>
            window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && b.Bounds.Width > 0 && (string?)b.Content == label);

        private static ScrollViewer scroller(Window window, string name) =>
            window.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == name);

        private static InstanceViewModel selected(MainWindow window) => ((MainViewModel)window.DataContext!).Selected!;

        private static void selectNamed(MainWindow window, string name)
        {
            var shell = (MainViewModel)window.DataContext!;
            shell.Selected = shell.Instances.First(i => i.Name == name);
            layout(window);
        }

        private static Rect boundsIn(Visual root, Control control) => new Rect(control.TranslatePoint(new Point(0, 0), root) ?? default, control.Bounds.Size);

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
    }
}

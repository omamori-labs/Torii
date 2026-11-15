using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Torii.Core.Mods;
using Torii.Core.Omamori;
using Torii.ViewModels;

namespace Torii.Tests
{
    [TestFixture]
    public class ModBrowserViewModelTests
    {
        private readonly List<string> served = [];

        private string directory = null!;
        private OmamoriInstall install = null!;
        private ModLibrary library = null!;
        private RouteHandler handler = null!;
        private HttpClient http = null!;
        private ModBrowserViewModel browser = null!;

        private List<string> problems = null!;
        private List<string?> statuses = null!;
        private List<bool> busy = null!;
        private List<double> progress = null!;
        private int refreshed;
        private bool canInstall;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"torii-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(directory, "downloads"));

            install = new OmamoriInstall(Path.Combine(directory, "data"));
            Directory.CreateDirectory(install.DataDirectory);
            library = new ModLibrary(install, () => false);

            handler = new RouteHandler();
            http = new HttpClient(handler);

            served.Clear();
            problems = [];
            statuses = [];
            busy = [];
            progress = [];
            refreshed = 0;
            canInstall = true;

            browser = new ModBrowserViewModel(
                new ModIndexFeed(http),
                new IndexedModInstaller(http, Path.Combine(directory, "downloads")),
                library,
                () => refreshed++,
                statuses.Add,
                message =>
                {
                    problems.Add(message);
                    return Task.CompletedTask;
                },
                () => canInstall,
                busy.Add,
                progress.Add);
        }

        [TearDown]
        public void TearDown()
        {
            http.Dispose();
            handler.Dispose();
            Directory.Delete(directory, recursive: true);
        }

        [Test]
        public async Task ModsAreListedByName()
        {
            serve(entry("a.zeta", "Zeta", "1.0.0"), entry("a.alpha", "Alpha", "1.0.0"));

            await browser.EnsureLoadedAsync();

            Assert.That(string.Join(", ", browser.Visible.Select(r => r.Name)), Is.EqualTo("Alpha, Zeta"));
        }

        [Test]
        public async Task FailedLoadIsShownAndTriedAgainNextTime()
        {
            await browser.EnsureLoadedAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(browser.Failure, Is.Not.Null);
                Assert.That(browser.Visible, Is.Empty);
                Assert.That(browser.IsLoading, Is.False);
            }

            serve(entry("a.mod", "Mod", "1.0.0"));
            await browser.EnsureLoadedAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(browser.Failure, Is.Null);
                Assert.That(browser.Visible, Has.Count.EqualTo(1));
            }
        }

        [Test]
        public async Task LoadedIndexIsNotFetchedAgain()
        {
            serve(entry("a.mod", "Mod", "1.0.0"));

            await browser.EnsureLoadedAsync();
            await browser.EnsureLoadedAsync();

            Assert.That(handler.Requested.Count(u => u == ModIndexFeed.Default), Is.EqualTo(1));
        }

        [Test]
        public async Task ModStatesFollowWhatIsInstalled()
        {
            serve(entry("a.new", "New", "1.0.0"), entry("a.current", "Current", "1.0.0"), entry("a.old", "Old", "2.0.0"));
            library.InstallFromZip(writeZip("a.current", "1.0.0"));
            library.InstallFromZip(writeZip("a.old", "1.0.0"));

            await browser.EnsureLoadedAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(row("a.new").State, Is.EqualTo(BrowsedModState.Available));
                Assert.That(row("a.current").State, Is.EqualTo(BrowsedModState.Installed));
                Assert.That(row("a.old").State, Is.EqualTo(BrowsedModState.Update));
                Assert.That(row("a.old").ActionLabel, Is.EqualTo("Update"));
                Assert.That(row("a.new").ActionLabel, Is.EqualTo("Install"));
            }
        }

        [Test]
        public async Task ModBuiltForANewerApiIsIncompatible()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(install.ApiAssembly)!);
            File.WriteAllBytes(install.ApiAssembly, Fixtures.ApiAssembly("1.0"));
            serve(entry("a.mod", "Mod", "1.0.0", api: "1.5"));

            await browser.EnsureLoadedAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(row("a.mod").State, Is.EqualTo(BrowsedModState.Incompatible));
                Assert.That(row("a.mod").Problem, Is.Not.Empty);
                Assert.That(row("a.mod").ShowsAction, Is.False);
            }
        }

        [Test]
        public async Task InstallingAddsTheModAndMarksItInstalled()
        {
            serveZip("a.mod", "Mod", "1.0.0");
            await browser.EnsureLoadedAsync();

            await row("a.mod").InstallCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(library.List().Single().Manifest!.Id, Is.EqualTo("a.mod"));
                Assert.That(row("a.mod").State, Is.EqualTo(BrowsedModState.Installed));
                Assert.That(problems, Is.Empty);
                Assert.That(refreshed, Is.EqualTo(1));
                Assert.That(statuses[^1], Is.EqualTo("Mod 1.0.0 is installed."));
            }
        }

        [Test]
        public async Task BusyIsSetWhileInstallingAndProgressEndsAtOne()
        {
            serveZip("a.mod", "Mod", "1.0.0");
            await browser.EnsureLoadedAsync();

            await row("a.mod").InstallCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(busy[0], Is.True);
                Assert.That(busy[^1], Is.False);
                Assert.That(progress, Is.Not.Empty);
                Assert.That(progress.Max(), Is.EqualTo(1).Within(0.0001));
            }
        }

        [Test]
        public async Task DependenciesAreInstalledToo()
        {
            serveZip("a.library", "Library", "1.0.0");
            serveZip("a.mod", "Mod", "1.0.0", dependencies: """[{ "id": "a.library" }]""");
            await browser.EnsureLoadedAsync();

            await row("a.mod").InstallCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(string.Join(", ", library.List().Select(m => m.Manifest!.Id).Order()), Is.EqualTo("a.library, a.mod"));
                Assert.That(row("a.library").State, Is.EqualTo(BrowsedModState.Installed));
                Assert.That(problems, Is.Empty);
            }
        }

        [Test]
        public async Task ModWithAMissingDependencyIsNotInstalled()
        {
            serveZip("a.mod", "Mod", "1.0.0", dependencies: """[{ "id": "a.missing" }]""");
            await browser.EnsureLoadedAsync();

            await row("a.mod").InstallCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(problems.Single(), Does.Contain("a.missing"));
                Assert.That(library.List(), Is.Empty);
                Assert.That(busy, Is.Empty);
            }
        }

        [Test]
        public async Task FailedDownloadIsReportedAndTheListRefreshed()
        {
            serve(entry("a.mod", "Mod", "1.0.0"));
            await browser.EnsureLoadedAsync();

            await row("a.mod").InstallCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(problems.Single(), Does.StartWith("Couldn't install Mod 1.0.0."));
                Assert.That(busy[^1], Is.False);
                Assert.That(refreshed, Is.EqualTo(1));
                Assert.That(row("a.mod").State, Is.EqualTo(BrowsedModState.Available));
            }
        }

        [Test]
        public async Task SearchMatchesNameAuthorAndDescription()
        {
            serve(entry("a.one", "Hit counter", "1.0.0", author: "someone", description: "counts hits"), entry("a.two", "Skin switch", "1.0.0", author: "other", description: "swaps skins"));
            await browser.EnsureLoadedAsync();

            using (Assert.EnterMultipleScope())
            {
                browser.Search = "counter";
                Assert.That(browser.Visible.Single().Mod.Id, Is.EqualTo("a.one"));

                browser.Search = "OTHER";
                Assert.That(browser.Visible.Single().Mod.Id, Is.EqualTo("a.two"));

                browser.Search = "swaps";
                Assert.That(browser.Visible.Single().Mod.Id, Is.EqualTo("a.two"));

                browser.Search = "nothing like this";
                Assert.That(browser.Visible, Is.Empty);
                Assert.That(browser.NoMatches, Is.True);

                browser.Search = string.Empty;
                Assert.That(browser.Visible, Has.Count.EqualTo(2));
            }
        }

        [Test]
        public async Task AvailableUpdatesNameTheNewVersion()
        {
            serve(entry("a.old", "Old", "2.0.0"), entry("a.current", "Current", "1.0.0"));
            library.InstallFromZip(writeZip("a.old", "1.0.0"));
            library.InstallFromZip(writeZip("a.current", "1.0.0"));

            await browser.EnsureLoadedAsync();

            Assert.That(browser.AvailableUpdates, Is.EqualTo(new Dictionary<string, string> { ["a.old"] = "2.0.0" }));
        }

        [Test]
        public async Task RefreshedIsRaisedWhenTheIndexLoadsAndAfterAnInstall()
        {
            serveZip("a.mod", "Mod", "1.0.0");
            int raised = 0;
            browser.Refreshed += (_, _) => raised++;

            await browser.EnsureLoadedAsync();
            Assert.That(raised, Is.EqualTo(1));

            await row("a.mod").InstallCommand.ExecuteAsync(null);
            Assert.That(raised, Is.EqualTo(2));
        }

        [Test]
        public async Task UpdatingInstallsTheNewerVersionAndClearsTheUpdate()
        {
            serveZip("a.mod", "Mod", "2.0.0");
            library.InstallFromZip(writeZip("a.mod", "1.0.0"));
            await browser.EnsureLoadedAsync();

            await browser.UpdateAsync("a.mod");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(library.List().Single().Manifest!.Version, Is.EqualTo("2.0.0"));
                Assert.That(browser.AvailableUpdates, Is.Empty);
            }
        }

        [Test]
        public async Task UpdatingAModWithNoUpdateDoesNothing()
        {
            serveZip("a.mod", "Mod", "1.0.0");
            library.InstallFromZip(writeZip("a.mod", "1.0.0"));
            await browser.EnsureLoadedAsync();

            await browser.UpdateAsync("a.mod");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(busy, Is.Empty);
                Assert.That(handler.Requested, Has.None.EqualTo(new Uri("https://example.com/a.mod-1.0.0.zip")));
            }
        }

        [Test]
        public async Task InstallingIsOffWhileTheInstanceIsBusy()
        {
            serve(entry("a.mod", "Mod", "1.0.0"));
            await browser.EnsureLoadedAsync();
            canInstall = false;

            browser.CanInstallChanged();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(row("a.mod").InstallCommand.CanExecute(null), Is.False);
                Assert.That(row("a.mod").CanInstall, Is.False);
            }

            canInstall = true;
            browser.CanInstallChanged();

            Assert.That(row("a.mod").InstallCommand.CanExecute(null), Is.True);
        }

        private static string entry(string id, string name, string version, string api = "0.2", string author = "", string description = "", string? hash = null, string dependencies = "[]") => $$"""
            { "id": "{{id}}", "name": "{{name}}", "author": "{{author}}", "description": "{{description}}", "versions": [{ "version": "{{version}}", "apiVersion": "{{api}}", "download": "https://example.com/{{id}}-{{version}}.zip", "sha256": "{{hash ?? new string('0', 64)}}", "dependencies": {{dependencies}} }] }
            """;

        private BrowsedModViewModel row(string id) => browser.Mods.Single(r => r.Mod.Id == id);

        private void serve(params string[] entries) =>
            handler.Add(ModIndexFeed.Default, Encoding.UTF8.GetBytes($$"""{ "mods": [{{string.Join(",", entries)}}] }"""));

        private void serveZip(string id, string name, string version, string dependencies = "[]")
        {
            byte[] zip = Fixtures.Zip(("mod.json", Encoding.UTF8.GetBytes($$"""{ "id": "{{id}}", "name": "{{name}}", "version": "{{version}}", "entry": "Mod.dll", "apiVersion": "0.2" }""")), ("Mod.dll", Encoding.UTF8.GetBytes("dll")));

            handler.Add(new Uri($"https://example.com/{id}-{version}.zip"), zip);
            served.Add(entry(id, name, version, hash: Fixtures.Sha256(zip), dependencies: dependencies));
            serve([.. served]);
        }

        private string writeZip(string id, string version) =>
            Fixtures.ZipFile(directory, "zip", ("mod.json", Encoding.UTF8.GetBytes($$"""{ "id": "{{id}}", "name": "{{id}}", "version": "{{version}}", "entry": "Mod.dll", "apiVersion": "0.2" }""")), ("Mod.dll", Encoding.UTF8.GetBytes("dll")));
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NUnit.Framework;
using Torii.ViewModels;
using Torii.Views;

namespace Torii.Tests
{
    [TestFixture]
    public class DialogTests
    {
        private List<Window> windows = null!;

        [SetUp]
        public void SetUp() => windows = [];

        [TearDown]
        public void TearDown()
        {
            foreach (Window window in windows)
                window.Close();
        }

        [AvaloniaTest]
        public void BadNameKeepsConfirmDisabledAndSaysWhy()
        {
            NameDialog dialog = open(new NameDialog(prompt(string.Empty, name => name == "con" ? "Windows doesn't allow that name." : null)));

            nameBox(dialog).Text = "con";
            flush();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(confirm(dialog).IsEnabled, Is.False);
                Assert.That(problem(dialog).IsVisible, Is.True);
                Assert.That(problem(dialog).Text, Is.EqualTo("Windows doesn't allow that name."));
            }
        }

        [AvaloniaTest]
        public void GoodNameEnablesConfirmAndShowsNoProblem()
        {
            NameDialog dialog = open(new NameDialog(prompt(string.Empty, _ => null)));

            nameBox(dialog).Text = "Cosmetic mods";
            flush();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(confirm(dialog).IsEnabled, Is.True);
                Assert.That(problem(dialog).IsVisible, Is.False);
            }
        }

        [AvaloniaTest]
        public void EmptyNameDisablesConfirmWithoutComplaining()
        {
            NameDialog dialog = open(new NameDialog(prompt(string.Empty, _ => "never shown for an empty name")));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(confirm(dialog).IsEnabled, Is.False);
                Assert.That(problem(dialog).IsVisible, Is.False);
            }
        }

        [AvaloniaTest]
        public void RenameStartsWithTheCurrentNameAndUsesItsOwnWording()
        {
            NameDialog dialog = open(new NameDialog(new NamePrompt("Rename instance", "Rename", "alpha", _ => null)));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(dialog.Title, Is.EqualTo("Rename instance"));
                Assert.That(confirm(dialog).Content, Is.EqualTo("Rename"));
                Assert.That(nameBox(dialog).Text, Is.EqualTo("alpha"));
                Assert.That(confirm(dialog).IsEnabled, Is.True);
            }
        }

        [AvaloniaTest]
        public async Task ConfirmingReturnsTheTrimmedName()
        {
            NameDialog dialog = new NameDialog(prompt(string.Empty, _ => null));
            Task<string?> result = showModal(dialog);

            nameBox(dialog).Text = "  fine  ";
            flush();
            confirm(dialog).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.That(await result, Is.EqualTo("fine"));
        }

        [AvaloniaTest]
        public async Task CancellingReturnsNothing()
        {
            NameDialog dialog = new NameDialog(prompt(string.Empty, _ => null));
            Task<string?> result = showModal(dialog);

            nameBox(dialog).Text = "something";
            flush();
            dialog.FindControl<Button>("CancelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.That(await result, Is.Null);
        }

        [AvaloniaTest]
        public async Task MessageDialogShowsItsMessageAndOkClosesIt()
        {
            var dialog = new MessageDialog("Couldn't start osu!. Close it first.");
            var owner = new Window();
            owner.Show();
            windows.Add(owner);

            Task task = dialog.ShowDialog(owner);
            flush();
            string? shown = dialog.FindControl<TextBlock>("MessageText")!.Text;
            dialog.FindControl<Button>("OkButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await task;

            Assert.That(shown, Is.EqualTo("Couldn't start osu!. Close it first."));
        }

        private static NamePrompt prompt(string initial, Func<string, string?> validate) => new NamePrompt("New instance", "Create", initial, validate);

        private static TextBox nameBox(NameDialog dialog) => dialog.FindControl<TextBox>("NameBox")!;

        private static Button confirm(NameDialog dialog) => dialog.FindControl<Button>("ConfirmButton")!;

        private static TextBlock problem(NameDialog dialog) => dialog.FindControl<TextBlock>("ProblemText")!;

        private static void flush() => Dispatcher.UIThread.RunJobs();

        private NameDialog open(NameDialog dialog)
        {
            dialog.Show();
            windows.Add(dialog);
            flush();

            return dialog;
        }

        private Task<string?> showModal(NameDialog dialog)
        {
            var owner = new Window();
            owner.Show();
            windows.Add(owner);

            Task<string?> result = dialog.ShowDialog<string?>(owner);
            flush();

            return result;
        }
    }
}

using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NUnit.Framework;
using Optris.Icons.Avalonia;

namespace Torii.Tests
{
    [TestFixture]
    public class IconTests
    {
        private static readonly string[] used_icons =
        [
            "mdi-tray-arrow-down",
            "mdi-folder-open",
            "mdi-pencil",
            "mdi-content-copy",
            "mdi-delete",
            "mdi-refresh",
            "mdi-alert",
            "mdi-alert-circle",
            "mdi-magnify",
        ];

        [AvaloniaTest]
        public void EveryIconTheAppUsesExistsAndDraws()
        {
            foreach (string name in used_icons)
            {
                var icon = new Icon { Value = name, FontSize = 20 };
                var window = new Window { Content = icon };

                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                Assert.That(icon.GetVisualDescendants().OfType<Image>().Any(image => image.Source != null), Is.True, $"{name} has no drawing");
                Assert.That(icon.Bounds.Width, Is.GreaterThan(0), $"{name} has no size");

                window.Close();
            }
        }
    }
}

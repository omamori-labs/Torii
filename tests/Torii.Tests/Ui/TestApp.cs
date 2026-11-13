using Avalonia;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Torii.Tests
{
    public class TestApp : Application
    {
        public override void Initialize()
        {
            RequestedThemeVariant = ThemeVariant.Dark;
            Styles.Add(new FluentTheme());
        }
    }
}

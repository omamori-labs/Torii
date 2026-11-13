using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;

namespace Torii.Views
{
    public partial class NameDialog : Window
    {
        private readonly Func<string, string?> validate;

        public NameDialog()
            : this(_ => null)
        {
        }

        private NameDialog(Func<string, string?> validate)
        {
            this.validate = validate;

            InitializeComponent();

            NameBox.TextChanged += (_, _) => check();
            CreateButton.Click += (_, _) => Close(NameBox.Text?.Trim());
            CancelButton.Click += (_, _) => Close(null);
            Opened += (_, _) => NameBox.Focus();

            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                    Close(null);
            };
        }

        public static Task<string?> ShowAsync(Window owner, Func<string, string?> validate) =>
            new NameDialog(validate).ShowDialog<string?>(owner);

        private void check()
        {
            string name = NameBox.Text?.Trim() ?? string.Empty;
            string? problem = name.Length == 0 ? null : validate(name);

            ProblemText.Text = problem;
            ProblemText.IsVisible = problem != null;
            CreateButton.IsEnabled = name.Length > 0 && problem == null;
        }
    }
}

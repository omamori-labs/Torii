using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Torii.ViewModels;

namespace Torii.Views
{
    public partial class NameDialog : Window
    {
        private readonly Func<string, string?> validate;

        public NameDialog()
            : this(new NamePrompt("Name", "OK", string.Empty, _ => null))
        {
        }

        private NameDialog(NamePrompt prompt)
        {
            validate = prompt.Validate;

            InitializeComponent();

            Title = prompt.Title;
            ConfirmButton.Content = prompt.Confirm;
            NameBox.Text = prompt.Initial;

            NameBox.TextChanged += (_, _) => check();
            ConfirmButton.Click += (_, _) => Close(NameBox.Text?.Trim());
            CancelButton.Click += (_, _) => Close(null);

            Opened += (_, _) =>
            {
                NameBox.Focus();
                NameBox.SelectAll();
            };

            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                    Close(null);
            };

            check();
        }

        public static Task<string?> ShowAsync(Window owner, NamePrompt prompt) =>
            new NameDialog(prompt).ShowDialog<string?>(owner);

        private void check()
        {
            string name = NameBox.Text?.Trim() ?? string.Empty;
            string? problem = name.Length == 0 ? null : validate(name);

            ProblemText.Text = problem;
            ProblemText.IsVisible = problem != null;
            ConfirmButton.IsEnabled = name.Length > 0 && problem == null;
        }
    }
}

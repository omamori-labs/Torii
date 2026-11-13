using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;

namespace Torii.Views
{
    public partial class MessageDialog : Window
    {
        public MessageDialog()
        {
            InitializeComponent();

            OkButton.Click += (_, _) => Close();

            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                    Close();
            };
        }

        public static Task ShowAsync(Window owner, string message)
        {
            var dialog = new MessageDialog();
            dialog.MessageText.Text = message;

            return dialog.ShowDialog(owner);
        }
    }
}

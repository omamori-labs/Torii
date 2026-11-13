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

        public MessageDialog(string message)
            : this()
        {
            MessageText.Text = message;
        }

        public static Task ShowAsync(Window owner, string message) => new MessageDialog(message).ShowDialog(owner);
    }
}

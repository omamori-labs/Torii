using System;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Torii
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer osuCheck = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };

        public MainWindow()
        {
            InitializeComponent();

            osuCheck.Tick += (_, _) => (DataContext as MainViewModel)?.RefreshOsuRunning();
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            osuCheck.Start();
            (DataContext as MainViewModel)?.LoadReleasesCommand.Execute(null);
        }

        protected override void OnClosed(EventArgs e)
        {
            osuCheck.Stop();

            base.OnClosed(e);
        }
    }
}

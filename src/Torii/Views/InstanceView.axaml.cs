using Avalonia.Controls;
using Torii.ViewModels;

namespace Torii.Views
{
    public partial class InstanceView : UserControl
    {
        public InstanceView()
        {
            InitializeComponent();

            _ = new ReorderDrag(
                ModItems,
                (item, index) => (DataContext as InstanceViewModel)?.ModList.Move((ModItemViewModel)item, index),
                () => (DataContext as InstanceViewModel)?.ModList.CanReorder ?? false,
                ModScroller);
        }
    }
}

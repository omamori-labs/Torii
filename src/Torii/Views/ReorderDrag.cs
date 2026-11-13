using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Torii.Views
{
    public sealed class ReorderDrag
    {
        public static readonly DataFormat<object> FORMAT = DataFormat.CreateInProcessFormat<object>("torii-reorder");

        private const double threshold = 8;

        private readonly ItemsControl host;
        private readonly Border line;
        private readonly Action<object, int> move;
        private readonly Func<bool> enabled;

        private PointerPressedEventArgs? pressed;
        private object? pressedItem;
        private Point start;

        public ReorderDrag(ItemsControl host, Border line, Action<object, int> move, Func<bool>? enabled = null)
        {
            this.host = host;
            this.line = line;
            this.move = move;
            this.enabled = enabled ?? (() => true);

            host.AddHandler(InputElement.PointerPressedEvent, onPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            host.AddHandler(InputElement.PointerMovedEvent, onMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
            host.AddHandler(InputElement.PointerReleasedEvent, onReleased, RoutingStrategies.Tunnel, handledEventsToo: true);

            DragDrop.SetAllowDrop(host, true);
            DragDrop.AddDragOverHandler(host, onDragOver);
            DragDrop.AddDragLeaveHandler(host, (_, _) => line.IsVisible = false);
            DragDrop.AddDropHandler(host, onDrop);
        }

        private void onPressed(object? sender, PointerPressedEventArgs e)
        {
            pressed = null;

            if (!enabled() || !e.GetCurrentPoint(host).Properties.IsLeftButtonPressed || e.Source is not Visual source)
                return;

            if (source.GetSelfAndVisualAncestors().TakeWhile(visual => visual != host).Any(visual => visual is Button or ToggleSwitch or TextBox))
                return;

            if (containerOf(source) is not { } container)
                return;

            pressed = e;
            pressedItem = container.DataContext;
            start = e.GetPosition(host);
        }

        private void onMoved(object? sender, PointerEventArgs e)
        {
            if (pressed == null || pressedItem == null)
                return;

            if (!e.GetCurrentPoint(host).Properties.IsLeftButtonPressed)
            {
                pressed = null;
                return;
            }

            Point position = e.GetPosition(host);

            if (Math.Abs(position.X - start.X) < threshold && Math.Abs(position.Y - start.Y) < threshold)
                return;

            PointerPressedEventArgs trigger = pressed;
            object item = pressedItem;
            pressed = null;

            _ = dragAsync(trigger, item);
        }

        private void onReleased(object? sender, PointerReleasedEventArgs e) => pressed = null;

        private async Task dragAsync(PointerPressedEventArgs trigger, object item)
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(FORMAT, item));

            try
            {
                await DragDrop.DoDragDropAsync(trigger, transfer, DragDropEffects.Move);
            }
            finally
            {
                line.IsVisible = false;
            }
        }

        private void onDragOver(object? sender, DragEventArgs e)
        {
            if (!enabled() || e.DataTransfer.TryGetValue(FORMAT) == null)
                return;

            e.DragEffects = DragDropEffects.Move;
            e.Handled = true;

            showLine(gapAt(e));
        }

        private void onDrop(object? sender, DragEventArgs e)
        {
            line.IsVisible = false;

            if (!enabled() || e.DataTransfer.TryGetValue(FORMAT) is not { } item)
                return;

            e.Handled = true;

            int from = containers().ToList().FindIndex(container => ReferenceEquals(container.DataContext, item));
            int gap = gapAt(e);
            int to = gap > from ? gap - 1 : gap;

            if (from >= 0 && to != from)
                move(item, to);
        }

        private int gapAt(DragEventArgs e)
        {
            if (host.ItemsPanelRoot is not { } panel)
                return 0;

            double y = e.GetPosition(panel).Y;
            List<Control> all = [.. containers()];
            int gap = all.FindIndex(container => y < container.Bounds.Top + container.Bounds.Height / 2);

            return gap < 0 ? all.Count : gap;
        }

        private void showLine(int gap)
        {
            List<Control> all = [.. containers()];

            if (all.Count == 0 || host.ItemsPanelRoot is not { } panel || line.GetVisualParent() is not { } parent)
            {
                line.IsVisible = false;
                return;
            }

            double y = gap < all.Count ? all[gap].Bounds.Top : all[^1].Bounds.Bottom;
            Point inParent = panel.TranslatePoint(new Point(0, y), parent) ?? default;

            line.Margin = new Thickness(0, inParent.Y - line.Height / 2, 0, 0);
            line.IsVisible = true;
        }

        private IEnumerable<Control> containers() =>
            host.ItemsPanelRoot?.Children.Where(child => child.IsVisible) ?? [];

        private Control? containerOf(Visual source) =>
            source.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(control => control.GetVisualParent() == host.ItemsPanelRoot);
    }
}

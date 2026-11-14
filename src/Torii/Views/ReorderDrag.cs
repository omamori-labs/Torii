using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Torii.Views
{
    public sealed class ReorderDrag
    {
        private const double threshold = 6;
        private const double lift_scale = 1.02;

        private static readonly TimeSpan slide_time = TimeSpan.FromMilliseconds(150);

        private readonly ItemsControl host;
        private readonly Action<object, int> move;
        private readonly Func<bool> enabled;
        private readonly ScrollViewer? scroller;
        private readonly DispatcherTimer scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };

        private Control? pressedContainer;
        private object? pressedItem;
        private Point start;

        private bool dragging;
        private bool arranged;
        private Control? dragged;
        private object? draggedItem;
        private List<Control> all = [];
        private List<double> tops = [];
        private List<double> heights = [];
        private int from;
        private int to;
        private double grab;
        private Point last;
        private TopLevel? level;

        public ReorderDrag(ItemsControl host, Action<object, int> move, Func<bool>? enabled = null, ScrollViewer? scroller = null)
        {
            this.host = host;
            this.move = move;
            this.enabled = enabled ?? (() => true);
            this.scroller = scroller;

            host.AddHandler(InputElement.PointerPressedEvent, onPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            host.AddHandler(InputElement.PointerMovedEvent, onMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
            host.AddHandler(InputElement.PointerReleasedEvent, onReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
            host.AddHandler(InputElement.PointerCaptureLostEvent, onCaptureLost, RoutingStrategies.Direct, handledEventsToo: true);

            scrollTimer.Tick += (_, _) => autoScroll();
        }

        public static bool Animate { get; set; } = true;

        private Visual viewport => scroller == null ? host : scroller;

        private static TransformOperations shift(double y, double scale = 1)
        {
            TransformOperations.Builder builder = TransformOperations.CreateBuilder(2);
            builder.AppendTranslate(0, y);
            builder.AppendScale(scale, scale);

            return builder.Build();
        }

        private static Transitions slideTransitions() =>
        [
            new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = slide_time, Easing = new CubicEaseOut() },
        ];

        private void onPressed(object? sender, PointerPressedEventArgs e)
        {
            pressedContainer = null;

            if (dragging || !enabled() || !e.GetCurrentPoint(host).Properties.IsLeftButtonPressed || e.Source is not Visual source)
                return;

            if (source.GetSelfAndVisualAncestors().TakeWhile(visual => visual != host).Any(visual => visual is Button or ToggleSwitch or TextBox))
                return;

            if (containerOf(source) is not { } container)
                return;

            pressedContainer = container;
            pressedItem = container.DataContext;
            start = e.GetPosition(viewport);
        }

        private void onMoved(object? sender, PointerEventArgs e)
        {
            if (dragging)
            {
                last = e.GetPosition(viewport);
                update();
                e.Handled = true;

                return;
            }

            if (pressedContainer == null)
                return;

            if (!e.GetCurrentPoint(host).Properties.IsLeftButtonPressed)
            {
                pressedContainer = null;
                return;
            }

            Point position = e.GetPosition(viewport);

            if (Math.Abs(position.X - start.X) < threshold && Math.Abs(position.Y - start.Y) < threshold)
                return;

            begin(e);
        }

        private void onReleased(object? sender, PointerReleasedEventArgs e)
        {
            pressedContainer = null;

            if (!dragging)
                return;

            end(commit: true);
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        private void onCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (dragging)
                end(commit: false, animated: false);
        }

        private void onKey(object? sender, KeyEventArgs e)
        {
            if (!dragging || e.Key != Key.Escape)
                return;

            end(commit: false);
            e.Handled = true;
        }

        private void begin(PointerEventArgs e)
        {
            Control? container = pressedContainer;
            object? item = pressedItem;
            pressedContainer = null;

            if (container == null || item == null || host.ItemsPanelRoot is not { } panel)
                return;

            // A recycling panel doesn't keep its children in the order they are shown
            all = [.. panel.Children.Where(child => child.IsVisible).OrderBy(child => child.Bounds.Top)];
            from = all.IndexOf(container);

            if (from < 0)
                return;

            tops = [.. all.Select(child => child.Bounds.Top)];
            heights = [.. all.Select(child => child.Bounds.Height)];
            dragged = container;
            draggedItem = item;
            to = from;
            arranged = false;
            last = e.GetPosition(viewport);
            grab = (viewport.TranslatePoint(start, panel) ?? default).Y - tops[from];

            foreach (Control child in all)
                child.RenderTransform = shift(0);

            dragged.ZIndex = 100;
            dragged.Opacity = 0.94;
            dragged.Effect = new DropShadowEffect { OffsetY = 6, BlurRadius = 18, Color = Colors.Black, Opacity = 0.45 };

            if (Animate)
            {
                foreach (Control child in all.Where(child => child != dragged))
                    child.Transitions = slideTransitions();
            }

            e.Pointer.Capture(host);

            level = TopLevel.GetTopLevel(host);
            level?.AddHandler(InputElement.KeyDownEvent, onKey, RoutingStrategies.Tunnel);

            if (scroller != null)
                scrollTimer.Start();

            dragging = true;
            update();
        }

        private void update()
        {
            if (dragged == null || host.ItemsPanelRoot is not { } panel)
                return;

            double height = heights[from];
            double y = (viewport.TranslatePoint(last, panel) ?? default).Y;
            double top = Math.Clamp(y - grab, tops[0], Math.Max(tops[0], tops[^1] + heights[^1] - height));

            dragged.RenderTransform = shift(top - tops[from], lift_scale);

            int target = Enumerable.Range(0, all.Count).MinBy(index => Math.Abs(top - slotTop(index)));

            if (arranged && target == to)
                return;

            arranged = true;
            to = target;
            double slot = height + gapBelow();

            for (int i = 0; i < all.Count; i++)
            {
                if (i == from)
                    continue;

                double offset = 0;

                if (from < to && i > from && i <= to)
                    offset = -slot;
                else if (from > to && i >= to && i < from)
                    offset = slot;

                all[i].RenderTransform = shift(offset);
            }
        }

        private double slotTop(int index) => index > from ? tops[index] + heights[index] - heights[from] : tops[index];

        private double gapBelow()
        {
            if (from + 1 < all.Count)
                return tops[from + 1] - tops[from] - heights[from];

            return from > 0 ? tops[from] - tops[from - 1] - heights[from - 1] : 0;
        }

        private void end(bool commit, bool animated = true)
        {
            if (!dragging || dragged == null)
                return;

            dragging = false;
            scrollTimer.Stop();
            level?.RemoveHandler(InputElement.KeyDownEvent, onKey);

            int destination = commit ? to : from;
            int origin = from;
            object? item = draggedItem;
            int index = host.IndexFromContainer(all[destination]);

            if (!commit)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    if (i != from)
                        all[i].RenderTransform = shift(0);
                }
            }

            if (!animated || !Animate)
            {
                settle(item, moved: commit && destination != origin, index);
                return;
            }

            dragged.Transitions = slideTransitions();
            dragged.Opacity = 1;
            dragged.RenderTransform = shift(slotTop(destination) - tops[origin]);

            DispatcherTimer.RunOnce(() => settle(item, moved: commit && destination != origin, index), slide_time + TimeSpan.FromMilliseconds(30));
        }

        private void settle(object? item, bool moved, int index)
        {
            foreach (Control child in all)
            {
                child.Transitions = null;
                child.ClearValue(Visual.RenderTransformProperty);
                child.ClearValue(Visual.ZIndexProperty);
                child.ClearValue(Visual.OpacityProperty);
                child.ClearValue(Visual.EffectProperty);
            }

            all = [];
            dragged = null;
            draggedItem = null;

            if (moved && item != null && index >= 0)
                move(item, index);
        }

        private void autoScroll()
        {
            if (!dragging || scroller == null)
                return;

            const double edge = 44;
            const double fastest = 20;

            double height = scroller.Bounds.Height;
            double most = Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);
            double delta = 0;

            if (last.Y < edge)
                delta = -fastest * Math.Min(1, (edge - last.Y) / edge);
            else if (last.Y > height - edge)
                delta = fastest * Math.Min(1, (last.Y - (height - edge)) / edge);

            if (delta == 0)
                return;

            scroller.Offset = new Vector(scroller.Offset.X, Math.Clamp(scroller.Offset.Y + delta, 0, most));
            update();
        }

        private Control? containerOf(Visual source) =>
            source.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(control => control.GetVisualParent() == host.ItemsPanelRoot);
    }
}

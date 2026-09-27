using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NoteSpace.Editor;
using Windows.Foundation;
using Windows.System;

namespace NoteSpace.Controls;

public sealed partial class PageListControl
{
    private Pointer? dragPointer;
    private FrameworkElement? dragGrip;
    private Point dragStart;
    private string? draggedId;
    private bool dragging;
    private ListViewItem? dropRow;
    private PageDropRequest? drop;
    private readonly DispatcherTimer dragScroll = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private double scrollDelta;
    private Point lastDragPoint;
    private bool scrollHooked;
    public event EventHandler<PageDropRequest>? PageMoveRequested;
    public Func<PageDropRequest, bool>? CanDrop { get; set; }

    private FrameworkElement CreateDragGrip(string id)
    {
        var grip = new Border { Width = 20, Height = 28, Background = OfficeTheme.Brush(0x00000000),
            Child = theme.Label("⠿", 15, color: theme.Muted), ManipulationMode = ManipulationModes.None };
        AutomationProperties.SetName(grip, "Drag page group"); AutomationProperties.SetAutomationId(grip, "page-grip-" + id);
        ToolTipService.SetToolTip(grip, "Drag above/below a page to reorder; drop in its middle to nest. Escape cancels. Context menus provide keyboard alternatives.");
        grip.PointerPressed += (_, e) => {
            if (dragPointer is not null || recent || PageMoveRequested is null) return;
            var point = e.GetCurrentPoint(list);
            if (!point.Properties.IsLeftButtonPressed) return;
            draggedId = id; dragStart = point.Position; dragGrip = grip; dragPointer = e.Pointer;
            if (!grip.CapturePointer(e.Pointer)) { CancelDrag(); return; }
            list.Focus(FocusState.Programmatic); e.Handled = true;
        };
        grip.PointerMoved += (_, e) => {
            if (dragPointer?.PointerId != e.Pointer.PointerId) return;
            var point = e.GetCurrentPoint(list).Position;
            if (!dragging && Math.Abs(point.X - dragStart.X) + Math.Abs(point.Y - dragStart.Y) < 6) return;
            dragging = true; lastDragPoint = point; UpdateDrop(point);
            scrollDelta = point.X >= 0 && point.X <= list.ActualWidth ? point.Y < 28 ? -12 : point.Y > list.ActualHeight - 28 ? 12 : 0 : 0;
            if (!scrollHooked) { dragScroll.Tick += (_, _) => ScrollDrag(); scrollHooked = true; }
            if (scrollDelta != 0) dragScroll.Start(); else dragScroll.Stop();
            e.Handled = true;
        };
        grip.PointerReleased += (_, e) => {
            if (dragPointer?.PointerId != e.Pointer.PointerId) return;
            if (dragging) UpdateDrop(e.GetCurrentPoint(list).Position);
            var request = dragging ? drop : null;
            CancelDrag(); e.Handled = true;
            if (request is not null) { restoreFocus = true; PageMoveRequested?.Invoke(this, request); }
        };
        grip.PointerCanceled += (_, _) => CancelDrag();
        grip.PointerCaptureLost += (_, _) => CancelDrag();
        return grip;
    }

    private void UpdateDrop(Point point)
    {
        ClearDrop();
        if (draggedId is null || point.X < 0 || point.X > list.ActualWidth || point.Y < 0 || point.Y > list.ActualHeight) return;
        foreach (var row in list.Items.OfType<ListViewItem>())
        {
            if (!row.IsLoaded || row.ActualHeight <= 0) continue;
            var top = row.TransformToVisual(list).TransformPoint(new Point());
            if (point.Y < top.Y || point.Y >= top.Y + row.ActualHeight || row.Tag is not string target) continue;
            var fraction = (point.Y - top.Y) / row.ActualHeight;
            var placement = fraction < 0.25 ? PageDropPlacement.Before : fraction > 0.75 ? PageDropPlacement.After : PageDropPlacement.Inside;
            var request = new PageDropRequest(draggedId, target, placement);
            if (CanDrop?.Invoke(request) != true) return;
            dropRow = row; drop = request;
            row.BorderBrush = OfficeTheme.Brush(OfficeTheme.Accent);
            row.BorderThickness = placement == PageDropPlacement.Before ? new Thickness(0, 3, 0, 0)
                : placement == PageDropPlacement.After ? new Thickness(0, 0, 0, 3) : new Thickness(2);
            row.Background = OfficeTheme.Brush(theme.Selection); return;
        }
    }
    private void ClearDrop()
    {
        if (dropRow is { } row)
        {
            var selected = row.Tag as string == selectedId;
            row.BorderBrush = OfficeTheme.Brush(selected ? OfficeTheme.Accent : 0x00000000);
            row.BorderThickness = new Thickness(3, 0, 0, 0);
            row.Background = OfficeTheme.Brush(selected ? theme.Selection : 0x00000000);
        }
        dropRow = null; drop = null;
    }
    private static ScrollViewer? FindScrollViewer(DependencyObject element)
    {
        if (element is ScrollViewer scroll) return scroll;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(element, i)) is { } found) return found;
        return null;
    }
    private void ScrollDrag()
    {
        if (!dragging || FindScrollViewer(list) is not { } scroll) { dragScroll.Stop(); return; }
        scroll.ChangeView(null, Math.Clamp(scroll.VerticalOffset + scrollDelta, 0, scroll.ScrollableHeight), null, true);
        UpdateDrop(lastDragPoint);
    }
    private void CancelDrag()
    {
        var pointer = dragPointer; var grip = dragGrip;
        dragPointer = null; dragGrip = null; draggedId = null; dragging = false; scrollDelta = 0;
        dragScroll.Stop(); ClearDrop();
        if (pointer is not null) grip?.ReleasePointerCapture(pointer);
    }
}

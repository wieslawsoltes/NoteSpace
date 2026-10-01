using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;

namespace NoteSpace.Controls;

public sealed record PaneResizeChange(double Width, bool Completed, bool Canceled = false);

/// <summary>Reusable pane edge with pointer/touch capture, Escape rollback, arrow-key resizing,
/// double-click reset and a range-value automation pattern. The host owns layout and persistence.</summary>
public sealed class PaneResizeHandle : UserControl
{
    private readonly Border line = new() { Width = 1, HorizontalAlignment = HorizontalAlignment.Center };
    private Pointer? pointer;
    private UIElement? coordinateRoot;
    private Point origin;
    private double startWidth;
    private double value = 204;
    private OfficeTheme palette = OfficeTheme.Light;
    private uint edgeColor;
    public double Minimum { get; set; } = 140;
    public double Maximum { get; set; } = 480;
    public double DefaultWidth { get; set; } = 204;
    public bool Reverse { get; set; }
    public double Value { get => value; set => this.value = Math.Clamp(value, Minimum, Maximum); }
    public event EventHandler<PaneResizeChange>? ResizeChanged;
    public PaneResizeHandle()
    {
        Width = 7; HorizontalAlignment = HorizontalAlignment.Right; IsTabStop = true;
        ManipulationMode = ManipulationModes.None;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        Content = new Border { Background = OfficeTheme.Brush(0), Child = line };
        AutomationProperties.SetHelpText(this, "Drag to resize. Left/Right: 10 pixels. Home: default width. Escape cancels a drag. Double-click resets.");
        ToolTipService.SetToolTip(this, "Drag to resize · Double-click to reset");
        SetTheme(OfficeTheme.Light);
        GotFocus += (_, _) => PaintEdge(); LostFocus += (_, _) => PaintEdge();
        PointerPressed += (_, e) => {
            if (pointer is not null || !IsEnabled) return;
            coordinateRoot = XamlRoot?.Content;
            if (coordinateRoot is null) return;
            var point = e.GetCurrentPoint(coordinateRoot);
            if (!point.Properties.IsLeftButtonPressed) return;
            origin = point.Position; startWidth = Value; pointer = e.Pointer;
            if (!CapturePointer(pointer)) { pointer = null; return; }
            Focus(FocusState.Pointer); e.Handled = true;
        };
        PointerMoved += (_, e) => {
            if (pointer?.PointerId != e.Pointer.PointerId || coordinateRoot is null) return;
            var change = (e.GetCurrentPoint(coordinateRoot).Position.X - origin.X) * (Reverse ? -1 : 1);
            SetValue(startWidth + change, false); e.Handled = true;
        };
        PointerReleased += (_, e) => { if (pointer?.PointerId == e.Pointer.PointerId) { Finish(false); e.Handled = true; } };
        PointerCanceled += (_, _) => Finish(true);
        PointerCaptureLost += (_, _) => Finish(true);
        Unloaded += (_, _) => Finish(true);
        DoubleTapped += (_, e) => { Finish(true); SetValue(DefaultWidth, true); e.Handled = true; };
        KeyDown += (_, e) => {
            if (e.Key == VirtualKey.Escape && pointer is not null) { Finish(true); e.Handled = true; return; }
            if (e.Key is not (VirtualKey.Left or VirtualKey.Right or VirtualKey.Home)) return;
            Finish(true);
            SetValue(e.Key == VirtualKey.Home ? DefaultWidth : Value + (e.Key == VirtualKey.Left ? -10 : 10) * (Reverse ? -1 : 1), true);
            e.Handled = true;
        };
    }
    public void SetTheme(OfficeTheme theme) { palette = theme; PaintEdge(); }
    private void PaintEdge()
    {
        var color = FocusState == FocusState.Unfocused ? palette.Border : OfficeTheme.Accent;
        if (color == edgeColor) return;
        edgeColor = color; line.Background = OfficeTheme.Brush(color); line.Width = FocusState == FocusState.Unfocused ? 1 : 2;
    }
    private void SetValue(double next, bool complete)
    {
        if (!double.IsFinite(next)) throw new ArgumentOutOfRangeException(nameof(next));
        var old = Value; Value = next;
        if (old == Value && !complete) return;
        ResizeChanged?.Invoke(this, new(Value, complete));
        if (FrameworkElementAutomationPeer.FromElement(this) is { } peer && old != Value)
            peer.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, old, Value);
    }
    private void Finish(bool cancel)
    {
        var old = pointer; if (old is null) return;
        pointer = null; coordinateRoot = null;
        if (cancel) Value = startWidth;
        ReleasePointerCapture(old); ResizeChanged?.Invoke(this, new(Value, true, cancel));
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);
    private sealed class Peer(PaneResizeHandle owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        protected override string GetClassNameCore() => nameof(PaneResizeHandle);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Thumb;
        protected override object GetPatternCore(PatternInterface pattern) => pattern == PatternInterface.RangeValue ? this : base.GetPatternCore(pattern);
        public bool IsReadOnly => !owner.IsEnabled;
        public double LargeChange => 40;
        public double SmallChange => 10;
        public double Maximum => owner.Maximum;
        public double Minimum => owner.Minimum;
        public double Value => owner.Value;
        public void SetValue(double value)
        {
            if (IsReadOnly) throw new InvalidOperationException("The pane edge is disabled.");
            if (!double.IsFinite(value) || value < Minimum || value > Maximum) throw new ArgumentOutOfRangeException(nameof(value));
            owner.SetValue(value, true);
        }
    }
}

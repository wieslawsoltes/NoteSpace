using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace NoteSpace.Controls;

// Uno's browser automation global-bounds path can report local panel rectangles.
// Resolve actual transforms to the XamlRoot, including scroll offsets. Keep the
// native control peers/patterns and replace only their browser geometry.
internal static class OfficeAutomation
{
    public static bool Offscreen(FrameworkElement owner) { var b = Bounds(owner); return b.IsEmpty || b.Width <= 0 || b.Height <= 0; }
    public static Rect Bounds(FrameworkElement owner)
    {
        if (!owner.IsLoaded || owner.XamlRoot?.Content is not UIElement root) return default;
        var bounds = owner.TransformToVisual(root).TransformBounds(new Rect(0, 0, owner.ActualWidth, owner.ActualHeight));
        for (DependencyObject? at = owner; at is UIElement element; at = VisualTreeHelper.GetParent(at))
        {
            if (element.Visibility != Visibility.Visible || element.Opacity == 0) return default;
            if (element is ScrollViewer scroll)
                bounds.Intersect(scroll.TransformToVisual(root).TransformBounds(new Rect(0, 0, scroll.ActualWidth, scroll.ActualHeight)));
        }
        bounds.Intersect(new Rect(0, 0, owner.XamlRoot.Size.Width, owner.XamlRoot.Size.Height));
        return bounds;
    }
}
public sealed partial class OfficeButton
{
    protected override AutomationPeer OnCreateAutomationPeer() => new OfficeButtonPeer(this);
    private sealed class OfficeButtonPeer(OfficeButton owner) : ButtonAutomationPeer(owner)
    {
        protected override Rect GetBoundingRectangleCore() => OperatingSystem.IsBrowser() ? OfficeAutomation.Bounds(owner) : base.GetBoundingRectangleCore();
        protected override bool IsOffscreenCore() => OperatingSystem.IsBrowser() ? OfficeAutomation.Offscreen(owner) : base.IsOffscreenCore();
    }
}
internal sealed class OfficeMenuItem : MenuFlyoutItem
{
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);
    private sealed class Peer(OfficeMenuItem owner) : MenuFlyoutItemAutomationPeer(owner)
    {
        protected override Rect GetBoundingRectangleCore() => OperatingSystem.IsBrowser() ? OfficeAutomation.Bounds(owner) : base.GetBoundingRectangleCore();
    }
}
internal sealed class OfficeComboBox : ComboBox
{
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);
    private sealed class Peer(OfficeComboBox owner) : ComboBoxAutomationPeer(owner)
    {
        protected override Rect GetBoundingRectangleCore() => OperatingSystem.IsBrowser() ? OfficeAutomation.Bounds(owner) : base.GetBoundingRectangleCore();
    }
}
internal sealed class OfficeCheckBox : CheckBox
{
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);
    private sealed class Peer(OfficeCheckBox owner) : CheckBoxAutomationPeer(owner)
    {
        protected override Rect GetBoundingRectangleCore() => OperatingSystem.IsBrowser() ? OfficeAutomation.Bounds(owner) : base.GetBoundingRectangleCore();
    }
}

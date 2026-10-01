using NoteSpace.Core;

namespace NoteSpace.Editor;

/// <summary>UI-independent geometry for bounded command strips and selection toolbars.</summary>
public static class EditorChromeLayout
{
    /// <summary>Return the largest leading command group that fits. Reserve the overflow
    /// button only when not every command fits. Command order is never changed.</summary>
    public static int VisibleCommands(IReadOnlyList<double> widths, double available, double overflowWidth = 40)
    {
        ArgumentNullException.ThrowIfNull(widths);
        if (!double.IsFinite(available) || available < 0 || !double.IsFinite(overflowWidth) || overflowWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(available));
        double total = 0;
        foreach (var width in widths)
        {
            if (!double.IsFinite(width) || width < 0) throw new ArgumentOutOfRangeException(nameof(widths));
            total += width;
        }
        if (total <= available) return widths.Count;
        var used = 0d; var count = 0; var budget = Math.Max(0, available - overflowWidth);
        while (count < widths.Count && used + widths[count] <= budget) used += widths[count++];
        return count;
    }

    /// <summary>Keep a floating toolbar inside the viewport, above or below its visible
    /// selection. Return null rather than covering the selection when neither side fits.</summary>
    public static NoteRect? PlaceToolbar(NoteRect selection, float viewportWidth, float viewportHeight,
        float width = 260, float height = 40, float margin = 8, float gap = 6)
    {
        static bool Finite(float value) => float.IsFinite(value);
        if (!Finite(selection.X) || !Finite(selection.Y) || !Finite(selection.Width) || !Finite(selection.Height)
            || selection.Width < 0 || selection.Height < 0 || !Finite(viewportWidth) || !Finite(viewportHeight)
            || !Finite(width) || !Finite(height) || !Finite(margin) || !Finite(gap)
            || viewportWidth < 0 || viewportHeight < 0 || width <= 0 || height <= 0 || margin < 0 || gap < 0)
            throw new ArgumentOutOfRangeException(nameof(selection));
        if (viewportWidth < width + margin * 2 || viewportHeight < height + margin * 2) return null;
        if (selection.X >= viewportWidth || selection.X + selection.Width <= 0
            || selection.Y >= viewportHeight || selection.Y + selection.Height <= 0) return null;
        var left = Math.Clamp(selection.X, margin, viewportWidth - width - margin);
        var top = selection.Y - height - gap;
        if (top < margin)
        {
            top = selection.Y + selection.Height + gap;
            if (top + height > viewportHeight - margin) return null;
        }
        return new(left, top, width, height);
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NoteSpace.Core;
using NoteSpace.Editor;

namespace NoteSpace.Controls;

public sealed partial class NoteSurface
{
    private readonly SelectionToolbar selectionToolbar = new() { Visibility = Visibility.Collapsed };
    private bool selectionToolbarEnabled = true;
    private string? toolbarBlockId;
    public bool SelectionToolbarEnabled
    {
        get => selectionToolbarEnabled;
        set { selectionToolbarEnabled = value; if (!value) HideSelectionToolbar(); }
    }
    public bool IsSelectionToolbarVisible => selectionToolbar.Visibility == Visibility.Visible;
    public NoteRect SelectionToolbarBounds => !IsSelectionToolbarVisible ? default : new((float)Canvas.GetLeft(selectionToolbar), (float)Canvas.GetTop(selectionToolbar), SelectionToolbar.ToolbarWidth, SelectionToolbar.ToolbarHeight);
    private void ConfigureSelectionToolbar()
    {
        overlay.Children.Add(selectionToolbar);
        Canvas.SetZIndex(selectionToolbar, 10);
        selectionToolbar.CommandInvoked += (_, id) => {
            if (richDraft is null || richDraft.SelectionLength == 0 || toolbarBlockId != richDraft.Block.Id) { HideSelectionToolbar(); return; }
            FlushPendingText(); if (HasPendingText) return;
            CommandRequested?.Invoke(this, new(id));
            UpdateSelectionToolbar();
        };
        selectionToolbar.DismissRequested += (_, _) => { HideSelectionToolbar(); FocusTextEditor(); };
        // Show only after pointer selection, not while dragging or during ordinary typing.
        canvas.PointerPressed += (_, _) => HideSelectionToolbar();
        canvas.PointerReleased += (_, _) => { if (richDraft?.SelectionLength > 0) ShowSelectionToolbar(); };
        canvas.DoubleTapped += (_, _) => { if (richDraft?.SelectionLength > 0) ShowSelectionToolbar(); };
        canvas.PointerWheelChanged += (_, _) => HideSelectionToolbar();
        DraftChanged += (_, _) => HideSelectionToolbar();
        SelectionChanged += (_, _) => UpdateSelectionToolbar();
        LostFocus += (_, _) => DispatcherQueue.TryEnqueue(() => {
            if (selectionToolbar.IsMenuOpen) return;
            var element = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            for (; element is not null; element = VisualTreeHelper.GetParent(element)) if (element == this) return;
            HideSelectionToolbar();
        });
        Unloaded += (_, _) => HideSelectionToolbar();
    }
    public void HideSelectionToolbar()
    {
        var wasVisible = IsSelectionToolbarVisible;
        selectionToolbar.Visibility = Visibility.Collapsed; toolbarBlockId = null;
        if (wasVisible) SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>Show without moving the text selection. Keyboard hosts may request focus
    /// explicitly; pointer selection never steals focus from the native input adapter.</summary>
    public bool ShowSelectionToolbar(bool focus = false)
    {
        if (!selectionToolbarEnabled || richDraft is null || richDraft.SelectionLength == 0) return false;
        toolbarBlockId = richDraft.Block.Id; selectionToolbar.Visibility = Visibility.Visible;
        UpdateSelectionToolbar();
        if (focus && IsSelectionToolbarVisible) selectionToolbar.FocusFirst();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        return IsSelectionToolbarVisible;
    }
    private void UpdateSelectionToolbar()
    {
        if (!IsSelectionToolbarVisible) return;
        if (!selectionToolbarEnabled || Visibility != Visibility.Visible || richDraft is null || richVisual is null
            || richDraft.SelectionLength == 0 || toolbarBlockId != richDraft.Block.Id) { HideSelectionToolbar(); return; }
        NoteRect? position = null;
        foreach (var rect in richVisual.Selection)
        {
            var block = richDraft.Block;
            var screen = new NoteRect((block.X + rect.X - canvas.Options.OffsetX) * Zoom,
                (block.Y + rect.Y - canvas.Options.OffsetY) * Zoom, rect.Width * Zoom, rect.Height * Zoom);
            position = EditorChromeLayout.PlaceToolbar(screen, (float)ActualWidth, (float)ActualHeight, SelectionToolbar.ToolbarWidth, SelectionToolbar.ToolbarHeight);
            if (position.HasValue) break;
        }
        if (position is not { } place) { HideSelectionToolbar(); return; }
        Canvas.SetLeft(selectionToolbar, place.X); Canvas.SetTop(selectionToolbar, place.Y);
        selectionToolbar.SetTheme(Dark ? OfficeTheme.Dark : OfficeTheme.Light);
        selectionToolbar.SetFont(richDraft.CurrentFormat);
        selectionToolbar.SetCommandState("bold", richDraft.AllHave(f => f.Bold));
        selectionToolbar.SetCommandState("italic", richDraft.AllHave(f => f.Italic));
        selectionToolbar.SetCommandState("underline", richDraft.AllHave(f => f.Underline));
        selectionToolbar.SetCommandState("strike", richDraft.AllHave(f => f.Strike));
        selectionToolbar.SetCommandState("text-highlight", richDraft.AllHave(f => f.Highlight != 0));
        selectionToolbar.SetCommandState("superscript", richDraft.AllHave(f => f.Baseline == 1));
        selectionToolbar.SetCommandState("subscript", richDraft.AllHave(f => f.Baseline == -1));
        selectionToolbar.SetCommandState("paste-format", false, HasCopiedFormat);
    }
}

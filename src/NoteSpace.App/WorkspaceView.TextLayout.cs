using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NoteSpace.Core;

namespace NoteSpace.App;

public sealed partial class WorkspaceView
{
    private bool HandleNativeFormatShortcut(bool paste)
    {
        if (!ready || disposed || dialogGate.CurrentCount == 0 || !surface.IsRichTextFocused) return false;
        Invoke(paste ? "paste-format" : "copy-format");
        return true;
    }
    private void UpdateTextRibbon()
    {
        ribbon.SetCommandState("bold", surface.TextHasAttribute(f => f.Bold));
        ribbon.SetCommandState("italic", surface.TextHasAttribute(f => f.Italic));
        ribbon.SetCommandState("underline", surface.TextHasAttribute(f => f.Underline));
        ribbon.SetCommandState("strike", surface.TextHasAttribute(f => f.Strike));
        ribbon.SetCommandState("superscript", surface.TextHasAttribute(f => f.Baseline == 1));
        ribbon.SetCommandState("subscript", surface.TextHasAttribute(f => f.Baseline == -1));
        ribbon.SetCommandState("paste-format", false, surface.HasCopiedFormat);
        var current = surface.CurrentTextFormat;
        ribbon.SetCommandState("bullets", current.Bullets); ribbon.SetCommandState("numbered", current.Numbered);
    }
    private static TextBox NumberInput(string label, float value, string id)
    {
        var input = new TextBox { Header = label, Text = value.ToString(CultureInfo.InvariantCulture), MaxLength = 12, MinWidth = 150 };
        AutomationProperties.SetAutomationId(input, id); return input;
    }
    private static float ReadNumber(TextBox input)
    {
        if (!float.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !float.IsFinite(number))
            throw new InvalidDataException($"Enter a finite number for {input.Header}.");
        return number;
    }
    private async Task TextLayoutDialogAsync()
    {
        surface.FlushPendingText(); if (surface.HasPendingText) return;
        if (surface.SelectedBlock is not { Kind: BlockKind.Text or BlockKind.Heading or BlockKind.Checklist })
            throw new InvalidOperationException("Select a text container first.");
        var f = surface.CurrentTextFlow;
        var inputs = new[] {
            NumberInput("Left indent (0–500 px)", f.LeftIndent, "flow-left"), NumberInput("First line (0–500 px)", f.FirstLineIndent, "flow-first"),
            NumberInput("Right indent (0–500 px)", f.RightIndent, "flow-right"), NumberInput("Line spacing (0.8–4)", f.LineSpacing, "flow-spacing"),
            NumberInput("Before paragraph (0–200 px)", f.SpaceBefore, "flow-before"), NumberInput("After paragraph (0–200 px)", f.SpaceAfter, "flow-after"),
            NumberInput("Tab interval (8–500 px)", f.TabWidth, "flow-tabs")
        };
        var panel = new StackPanel { Spacing = 10, MinWidth = 380 };
        panel.Children.Add(new TextBlock { Text = "Applies to the selected text container. Paragraph breaks retain their own list item; wrapped lines do not start a new item.", TextWrapping = TextWrapping.Wrap });
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 4; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < inputs.Length; i++) { Grid.SetRow(inputs[i], i / 2); Grid.SetColumn(inputs[i], i % 2); grid.Children.Add(inputs[i]); }
        panel.Children.Add(grid);
        if (await ShowDialogAsync(new ContentDialog { Title = "Text layout", Content = panel, PrimaryButtonText = "Apply", CloseButtonText = "Cancel" }) != ContentDialogResult.Primary) return;
        surface.SetTextFlow(new TextFlowSettings { LeftIndent = ReadNumber(inputs[0]), FirstLineIndent = ReadNumber(inputs[1]), RightIndent = ReadNumber(inputs[2]), LineSpacing = ReadNumber(inputs[3]), SpaceBefore = ReadNumber(inputs[4]), SpaceAfter = ReadNumber(inputs[5]), TabWidth = ReadNumber(inputs[6]) });
    }
    private async Task ContainerLayoutDialogAsync()
    {
        surface.EndEditing(); if (surface.HasPendingText) return;
        if (surface.SelectedBlock is not { } block || CurrentPage is null) throw new InvalidOperationException("Select a note container first.");
        var pageId = CurrentPage.Id; var id = block.Id;
        var x = NumberInput("X (logical pixels)", block.X, "container-x"); var y = NumberInput("Y (logical pixels)", block.Y, "container-y");
        var width = NumberInput("Width", block.Width, "container-width"); var height = NumberInput("Minimum height", block.Height, "container-height");
        var panel = new StackPanel { Spacing = 8, MinWidth = 300 };
        foreach (var input in new[] { x, y, width, height }) panel.Children.Add(input);
        panel.Children.Add(new TextBlock { Text = "Text reflows to the new width. Its height grows as needed; the change can be undone.", TextWrapping = TextWrapping.Wrap });
        if (await ShowDialogAsync(new ContentDialog { Title = "Size and position", Content = panel, PrimaryButtonText = "Apply", CloseButtonText = "Cancel" }) != ContentDialogResult.Primary) return;
        var px = ReadNumber(x); var py = ReadNumber(y); var w = ReadNumber(width); var h = ReadNumber(height);
        session.EditPage(pageId, "Container layout", page => {
            var b = page.Blocks.First(b => b.Id == id); b.X = px; b.Y = py; b.Width = w; b.Height = h;
            if (b.Kind is BlockKind.Text or BlockKind.Checklist or BlockKind.Heading) b.Height = Math.Max(h, surface.Renderer.MeasureHeight(b));
        });
    }
}

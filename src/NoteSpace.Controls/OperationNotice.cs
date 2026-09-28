using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace NoteSpace.Controls;

/// <summary>A non-modal, explicitly dismissible operation result. Never takes focus
/// when shown, never auto-expires, and delegates undo validation to its host.</summary>
public sealed class OperationNotice : UserControl
{
    private readonly Grid panel = new() { Padding = new Thickness(12, 6, 12, 6) };
    private readonly TextBlock text = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly OfficeButton undo;
    private readonly OfficeButton dismiss;
    public event EventHandler? UndoRequested;
    public event EventHandler? Dismissed;
    public bool IsOpen => Visibility == Visibility.Visible;
    public OperationNotice()
    {
        Visibility = Visibility.Collapsed;
        panel.ColumnDefinitions.Add(new ColumnDefinition());
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        text.Margin = new Thickness(0, 0, 12, 0); panel.Children.Add(text);
        undo = new OfficeButton("Undo", "undo", () => UndoRequested?.Invoke(this, EventArgs.Empty), "Undo last operation");
        dismiss = new OfficeButton("Dismiss", "", Dismiss, "Dismiss operation result");
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        actions.Children.Add(undo); actions.Children.Add(dismiss); Grid.SetColumn(actions, 1); panel.Children.Add(actions);
        Content = panel;
        AutomationProperties.SetAutomationId(this, "operation-notice");
        AutomationProperties.SetAutomationId(undo, "operation-undo");
        AutomationProperties.SetAutomationId(dismiss, "operation-dismiss");
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
    }
    public void Show(string message, bool canUndo, OfficeTheme theme)
    {
        ArgumentNullException.ThrowIfNull(message); ArgumentNullException.ThrowIfNull(theme);
        text.Text = message; AutomationProperties.SetName(this, message);
        text.Foreground = OfficeTheme.Brush(theme.Text); panel.Background = OfficeTheme.Brush(theme.Selection);
        undo.Theme = dismiss.Theme = theme; undo.Visibility = canUndo ? Visibility.Visible : Visibility.Collapsed;
        Visibility = Visibility.Visible;
    }
    public void Dismiss()
    {
        if (!IsOpen) return;
        Visibility = Visibility.Collapsed; Dismissed?.Invoke(this, EventArgs.Empty);
    }
}

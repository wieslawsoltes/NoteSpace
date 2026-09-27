using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace NoteSpace.Controls;

/// <summary>Host navigation keys are intercepted before TextBox inserts a return or
/// performs focus traversal. Unhandled keys retain the platform's native editing behavior.</summary>
internal sealed class NoteInputBox : TextBox
{
    public Func<KeyRoutedEventArgs, bool>? HandleKey { get; set; }
    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (HandleKey?.Invoke(e) == true) { e.Handled = true; return; }
        base.OnKeyDown(e);
    }
}

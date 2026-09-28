using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace NoteSpace.Controls;

/// <summary>Navigation keys belong to the host; other keys retain native editing.
/// The pinned Uno Skia TextBox also processes characters after the routed event, so
/// suppress its text write during a handled navigation dispatch, not during typing.</summary>
internal sealed class NoteInputBox : TextBox
{
    private bool suppressNavigationText;
    public Func<KeyRoutedEventArgs, bool>? HandleKey { get; set; }
    public NoteInputBox() => BeforeTextChanging += (_, e) => { if (suppressNavigationText) e.Cancel = true; };
    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Tab or VirtualKey.Enter)
        {
            suppressNavigationText = true;
            // Queue before the host's navigation callback so changing to the next
            // cell is allowed, while the current event's post-processing is not.
            DispatcherQueue.TryEnqueue(() => suppressNavigationText = false);
        }
        if (HandleKey?.Invoke(e) == true) { e.Handled = true; return; }
        suppressNavigationText = false;
        base.OnKeyDown(e);
    }
}

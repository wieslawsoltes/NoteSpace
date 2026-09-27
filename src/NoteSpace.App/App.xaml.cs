using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NoteSpace.App;

public partial class App : Application
{
    private Window? window;
    public App() { InitializeComponent(); }
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new Window
        {
            Title = "NoteSpace",
            Content = new TextBlock { Text = "Opening NoteSpace…", FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        window.Activate();
        var workspace = new WorkspaceView(new PlatformServices());
        await workspace.PrepareTypographyAsync();
        window.Content = workspace;
        window.Closed += (_, _) => workspace.Dispose();
    }
}

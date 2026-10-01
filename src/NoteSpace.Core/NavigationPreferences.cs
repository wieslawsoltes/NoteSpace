namespace NoteSpace.Core;

public enum PageSortMode { Manual, TitleAscending, TitleDescending, ModifiedNewest, CreatedNewest }

/// <summary>View preferences, not page organization. Old backups receive these defaults.</summary>
public sealed class NavigationPreferences
{
    public double NotebookWidth { get; set; } = 204;
    public double PageWidth { get; set; } = 206;
    public double SearchWidth { get; set; } = 310;
    public PageSortMode PageSort { get; set; }
    public bool ShowPagePreviews { get; set; }
    public bool ShowPageDates { get; set; }
    public bool SimplifiedRibbon { get; set; }
    public bool ShowSelectionToolbar { get; set; } = true;
    public NavigationPreferences Copy() => (NavigationPreferences)MemberwiseClone();
    public void Validate()
    {
        static bool Within(double n, double min, double max) => double.IsFinite(n) && n >= min && n <= max;
        if (!Within(NotebookWidth, 140, 480) || !Within(PageWidth, 140, 480)
            || !Within(SearchWidth, 200, 520) || !Enum.IsDefined(PageSort))
            throw new InvalidDataException("Invalid navigation preferences.");
    }
}

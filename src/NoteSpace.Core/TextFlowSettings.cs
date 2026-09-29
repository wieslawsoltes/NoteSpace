namespace NoteSpace.Core;

/// <summary>Document-space layout for a text container. Values are logical pixels,
/// except LineSpacing, which multiplies the tallest font on each visual line.</summary>
public sealed class TextFlowSettings
{
    public float LeftIndent { get; set; }
    public float FirstLineIndent { get; set; }
    public float RightIndent { get; set; }
    public float SpaceBefore { get; set; }
    public float SpaceAfter { get; set; }
    public float LineSpacing { get; set; } = 1.45f;
    public float TabWidth { get; set; } = 48;
    public TextFlowSettings Copy() => (TextFlowSettings)MemberwiseClone();
    public void Validate()
    {
        static bool In(float n, float min, float max) => float.IsFinite(n) && n >= min && n <= max;
        if (!In(LeftIndent, 0, 500) || !In(FirstLineIndent, 0, 500) || !In(RightIndent, 0, 500)
            || !In(SpaceBefore, 0, 200) || !In(SpaceAfter, 0, 200)
            || !In(LineSpacing, 0.8f, 4) || !In(TabWidth, 8, 500))
            throw new InvalidDataException("Invalid text layout settings.");
    }
}

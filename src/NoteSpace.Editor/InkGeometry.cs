using NoteSpace.Core;
namespace NoteSpace.Editor;

public static class InkGeometry
{
    public static NoteRect Bounds(InkStroke stroke)
    {
        if (stroke.Points.Count == 0) return new();
        var minX = float.MaxValue; var minY = float.MaxValue; var maxX = float.MinValue; var maxY = float.MinValue;
        foreach (var p in stroke.Points) { minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); }
        return new(minX - stroke.Width, minY - stroke.Width, maxX - minX + 2 * stroke.Width, maxY - minY + 2 * stroke.Width);
    }
    public static float SegmentDistance(InkPoint p, InkPoint a, InkPoint b)
    {
        var dx = b.X - a.X; var dy = b.Y - a.Y; var length = dx * dx + dy * dy;
        var t = length == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length, 0, 1);
        return MathF.Sqrt(MathF.Pow(p.X - a.X - t * dx, 2) + MathF.Pow(p.Y - a.Y - t * dy, 2));
    }
    public static bool HitTest(InkStroke s, InkPoint point, float radius)
    {
        if (s.Points.Count == 0 || !Bounds(s).Contains(point.X, point.Y, radius)) return false;
        var tolerance = radius + s.Width / 2;
        var a = s.Points[0]; var b = s.Points[^1];
        if (s.Tool == DrawingTool.Rectangle)
        {
            var c = new InkPoint(b.X, a.Y); var d = new InkPoint(a.X, b.Y);
            return SegmentDistance(point, a, c) <= tolerance || SegmentDistance(point, c, b) <= tolerance || SegmentDistance(point, b, d) <= tolerance || SegmentDistance(point, d, a) <= tolerance;
        }
        if (s.Tool == DrawingTool.Ellipse)
        {
            var rx = Math.Abs(b.X - a.X) / 2; var ry = Math.Abs(b.Y - a.Y) / 2;
            if (rx < 0.01f || ry < 0.01f) return SegmentDistance(point, a, b) <= tolerance;
            var dx = (point.X - (a.X + b.X) / 2) / rx; var dy = (point.Y - (a.Y + b.Y) / 2) / ry;
            return Math.Abs(MathF.Sqrt(dx * dx + dy * dy) - 1) * Math.Min(rx, ry) <= tolerance;
        }
        if (s.Tool == DrawingTool.Line || s.Points.Count == 1) return SegmentDistance(point, a, b) <= tolerance;
        for (var i = 1; i < s.Points.Count; i++) if (SegmentDistance(point, s.Points[i - 1], s.Points[i]) <= tolerance) return true;
        return false;
    }
    public static IReadOnlyList<InkPoint> Simplify(IReadOnlyList<InkPoint> input, float tolerance = 0.35f)
    {
        if (input.Count < 3) return input.ToArray();
        var keep = new bool[input.Count]; keep[0] = keep[^1] = true;
        var ranges = new Stack<(int A, int B)>(); ranges.Push((0, input.Count - 1));
        while (ranges.TryPop(out var range))
        {
            var distance = tolerance; var best = -1;
            for (var i = range.A + 1; i < range.B; i++)
            {
                var d = SegmentDistance(input[i], input[range.A], input[range.B]);
                if (Math.Abs(input[i].Pressure - input[range.A].Pressure) > 0.15f) d = Math.Max(d, tolerance + 1);
                if (d > distance) { distance = d; best = i; }
            }
            if (best < 0) continue;
            keep[best] = true; ranges.Push((range.A, best)); ranges.Push((best, range.B));
        }
        return input.Where((_, index) => keep[index]).ToArray();
    }
}

using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using NoteSpace.Core;

namespace NoteSpace.Storage;

public static class NoteExport
{
    public static string Markdown(NotePage page)
    {
        var output = new StringBuilder("# " + page.Title.Replace('\n', ' ') + "\n\n");
        foreach (var b in page.Blocks.OrderBy(b => b.Y).ThenBy(b => b.X))
        {
            if (b.Kind == BlockKind.Divider) output.AppendLine("---");
            else if (b.Kind == BlockKind.Table)
            {
                for (var row = 0; row < b.Cells.Count; row++)
                {
                    output.AppendLine("| " + string.Join(" | ", b.Cells[row].Select(c => c.Replace("|", "\\|").Replace("\n", " "))) + " |");
                    if (row == 0) output.AppendLine("| " + string.Join(" | ", b.Cells[row].Select(_ => "---")) + " |");
                }
            }
            else if (b.Kind is BlockKind.Image or BlockKind.Attachment) output.AppendLine($"[{b.FileName}] (attachment retained in the .notespace backup)");
            else
            {
                var prefix = b.Kind == BlockKind.Checklist ? b.Checked ? "- [x] " : "- [ ] " : b.Kind == BlockKind.Heading ? "## " : b.Format.Bullets ? "- " : b.Format.Numbered ? "1. " : "";
                var text = b.Text; if (b.Format.Bold) text = "**" + text + "**"; if (b.Format.Italic) text = "_" + text + "_";
                output.AppendLine(prefix + text);
            }
            if (b.Tags.Count > 0) output.AppendLine(string.Join(" ", b.Tags.Select(t => "#" + t.Replace(' ', '_'))));
            output.AppendLine();
        }
        if (page.Ink.Count > 0) output.AppendLine($"[{page.Ink.Count} ink strokes are retained in the .notespace backup. Export PNG for visual fidelity.]");
        return output.ToString();
    }
    public static string Html(NotePage page)
    {
        var html = new StringBuilder("<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>");
        html.Append(E(page.Title)).Append("</title><style>body{font:16px system-ui,sans-serif;color:#242424;max-width:1000px;margin:48px auto;padding:0 24px}h1{font-weight:400;border-bottom:1px solid #ddd;padding-bottom:16px}section{margin:24px 0;white-space:pre-wrap;overflow-wrap:anywhere}table{border-collapse:collapse;width:100%}td,th{border:1px solid #d7ccde;padding:10px;text-align:left;white-space:pre-wrap}th{background:#f0e7f6}img{max-width:100%;height:auto}.tags{color:#803ab3;font-size:12px}svg{max-width:100%;height:auto}@media print{body{margin:12mm;max-width:none}section,table{break-inside:avoid}}</style><body><h1>").Append(E(page.Title)).Append("</h1>");
        html.Append("<p>").Append(E(page.Created.ToString("f"))).Append("</p>");
        foreach (var b in page.Blocks.OrderBy(b => b.Y).ThenBy(b => b.X))
        {
            if (b.Kind == BlockKind.Divider) html.Append("<hr>");
            else if (b.Kind == BlockKind.Table)
            {
                html.Append("<table>"); for (var row = 0; row < b.Cells.Count; row++) { html.Append("<tr>"); var tag = row == 0 ? "th" : "td"; foreach (var c in b.Cells[row]) html.Append('<').Append(tag).Append('>').Append(E(c)).Append("</").Append(tag).Append('>'); html.Append("</tr>"); } html.Append("</table>");
            }
            else if (b.Kind == BlockKind.Image && b.Data is not null && b.MediaType is "image/png" or "image/jpeg" or "image/webp") html.Append("<section><img alt=\"").Append(E(b.FileName)).Append("\" src=\"data:").Append(b.MediaType).Append(";base64,").Append(Convert.ToBase64String(b.Data)).Append("\"></section>");
            else if (b.Kind is BlockKind.Image or BlockKind.Attachment) html.Append("<section>Attachment: ").Append(E(b.FileName)).Append(" (available in the NoteSpace backup)</section>");
            else
            {
                html.Append("<section style=\"text-align:").Append(b.Format.Alignment == 1 ? "center" : b.Format.Alignment == 2 ? "right" : "left")
                    .Append(";line-height:").Append(b.TextFlow.LineSpacing.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Append(";padding-left:").Append(b.TextFlow.LeftIndent.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("px;padding-right:")
                    .Append(b.TextFlow.RightIndent.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("px;text-indent:")
                    .Append(b.TextFlow.FirstLineIndent.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("px;\">"); if (b.Kind == BlockKind.Checklist) html.Append(b.Checked ? "☑ " : "☐ ");
                var bounds = new SortedSet<int> { 0, b.Text.Length }; foreach (var m in b.Marks) { bounds.Add(m.Start); bounds.Add(m.Start + m.Length); }
                var indices = bounds.ToArray();
                for (var i = 0; i < indices.Length - 1; i++)
                {
                    var pos = indices[i]; var mark = b.Marks.LastOrDefault(m => pos >= m.Start && pos < m.Start + m.Length); var f = mark?.Format ?? b.Format;
                    var text = E(b.Text[pos..indices[i + 1]]);
                    html.Append("<span style=\"font-size:").Append(f.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("px;color:#").Append((f.Color & 0xFFFFFF).ToString("X6")).Append(';');
                    if (f.Baseline != 0) html.Append(f.Baseline > 0 ? "vertical-align:super;font-size:70%;" : "vertical-align:sub;font-size:70%;");
                    if (f.Highlight != 0) html.Append("background:#").Append((f.Highlight & 0xFFFFFF).ToString("X6")).Append(';');
                    if (f.Bold) html.Append("font-weight:700;"); if (f.Italic) html.Append("font-style:italic;");
                    if (f.Underline || f.Strike) html.Append("text-decoration:").Append(f.Underline ? "underline " : "").Append(f.Strike ? "line-through" : "").Append(';');
                    html.Append("\">");
                    if (SafeLink(mark?.Link)) html.Append("<a rel=\"noreferrer noopener\" href=\"").Append(E(mark!.Link!)).Append("\">").Append(text).Append("</a>"); else html.Append(text);
                    html.Append("</span>");
                }
                html.Append("</section>");
            }
            if (b.Tags.Count > 0) html.Append("<p class=\"tags\">").Append(E(string.Join(" · ", b.Tags))).Append("</p>");
        }
        if (page.Ink.Count > 0) html.Append("<p>Ink is retained in the NoteSpace backup. Use PNG export to include the drawing.</p>");
        return html.Append("</body></html>").ToString();
    }
    public static bool SafeLink(string? link) => Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto";
    public static string SafeFileName(string title) => string.Concat(title.Select(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c) ? '_' : c)).Trim().TrimEnd('.');
    private static string E(string value) => WebUtility.HtmlEncode(value);
    public static NotePage ImportText(string text, string title)
    {
        if (text.Length > 2 * 1024 * 1024) throw new InvalidDataException("Text import exceeds 2 MiB characters.");
        var page = new NotePage { Title = title }; var y = 145f;
        foreach (var paragraph in Regex.Split(text.Replace("\r\n", "\n"), @"\n\s*\n"))
        {
            if (string.IsNullOrWhiteSpace(paragraph)) continue;
            var content = paragraph; var b = new NoteBlock { X = 48, Y = y, Width = 680 };
            if (content.StartsWith("# ") && page.Blocks.Count == 0) { page.Title = content[2..].Trim()[..Math.Min(500, content[2..].Trim().Length)]; continue; }
            if (content.StartsWith("## ")) { b.Kind = BlockKind.Heading; b.Format.FontSize = 24; b.Format.Bold = true; content = content[3..]; }
            else if (content.StartsWith("- [ ] ") || content.StartsWith("- [x] ")) { b.Kind = BlockKind.Checklist; b.Checked = content[3] == 'x'; content = content[6..]; }
            b.Text = content; b.Height = Math.Min(19000, Math.Max(52, (content.Split('\n').Sum(l => Math.Max(1, (l.Length + 69) / 70))) * 26 + 24));
            page.Blocks.Add(b); y += b.Height + 18;
            if (y > 95000) throw new InvalidDataException("Imported text exceeds the supported page extent.");
        }
        return page;
    }
}

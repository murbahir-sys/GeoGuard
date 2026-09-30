using System.Text.RegularExpressions;

namespace GeoGuard;

/// <summary>
/// Показывает текст справки в RichTextBox. Разметка: «# Заголовок», «## Подзаголовок», «- пункт», «1. шаг»,
/// **жирный**, `код`; пустая строка — небольшой отступ.
/// </summary>
internal static class RichMarkup
{
    private static readonly Font Body = new("Segoe UI", 10f);
    private static readonly Font BodyBold = new("Segoe UI Semibold", 10f);
    private static readonly Font H1 = new("Segoe UI Semibold", 15f);
    private static readonly Font H2 = new("Segoe UI Semibold", 11.5f);
    private static readonly Font Code = new("Consolas", 9.5f);
    private static readonly Font Spacer = new("Segoe UI", 4f);
    private static readonly Regex Numbered = new(@"^\d+\. ", RegexOptions.Compiled);

    public static void Render(RichTextBox box, string markup)
    {
        box.Clear();
        var firstBlock = true;
        foreach (var raw in markup.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
            {
                AppendLine(box, "", Spacer, Theme.Text);
                continue;
            }

            if (line.StartsWith("## "))
            {
                AppendLine(box, "", Spacer, Theme.Text);
                AppendLine(box, line[3..], H2, Theme.Text);
            }
            else if (line.StartsWith("# "))
            {
                if (!firstBlock)
                    AppendLine(box, "", Spacer, Theme.Text);

                AppendLine(box, line[2..], H1, Theme.Accent);
            }
            else if (line.StartsWith("- "))
            {
                AppendLine(box, line[2..], Body, Theme.Text, indent: 12, hanging: 0, bullet: true);
            }
            else if (Numbered.IsMatch(line))
            {
                AppendLine(box, line, Body, Theme.Text, indent: 4, hanging: 18);
            }
            else
            {
                AppendLine(box, line, Body, Theme.Text);
            }

            firstBlock = false;
        }

        box.Select(0, 0);
        box.ScrollToCaret();
    }

    private static void AppendLine(RichTextBox box, string text, Font font, Color color, int indent = 0, int hanging = 0, bool bullet = false)
    {
        box.SelectionStart = box.TextLength;
        box.SelectionLength = 0;
        box.SelectionIndent = indent;
        box.SelectionHangingIndent = hanging;
        box.SelectionBullet = bullet;

        foreach (var (segment, isBold, isCode) in Split(text))
        {
            box.SelectionFont = isCode ? Code : isBold ? (font == Body ? BodyBold : font) : font;
            box.SelectionColor = isCode ? Theme.AccentHover : color;
            box.SelectionBackColor = isCode ? Color.FromArgb(238, 242, 255) : Theme.Surface;
            box.AppendText(segment);
        }

        box.SelectionBackColor = Theme.Surface;
        box.SelectionBullet = false;
        box.AppendText("\n");
    }

    /// <summary>Делит строку на куски по маркерам ** (жирный) и ` (код).</summary>
    private static IEnumerable<(string Text, bool Bold, bool Code)> Split(string text)
    {
        var bold = false;
        var code = false;
        var current = new System.Text.StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            if (!code && text[i] == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                if (current.Length > 0)
                    yield return (current.ToString(), bold, code);
                current.Clear();
                bold = !bold;
                i++;
            }
            else if (text[i] == '`')
            {
                if (current.Length > 0)
                    yield return (current.ToString(), bold, code);
                current.Clear();
                code = !code;
            }
            else
            {
                current.Append(text[i]);
            }
        }

        if (current.Length > 0)
            yield return (current.ToString(), bold, code);
    }
}

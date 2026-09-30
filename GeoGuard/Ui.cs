using System.Drawing.Drawing2D;
using System.ComponentModel;
using System.Drawing.Text;

namespace GeoGuard;

/// <summary>Цвета и шрифты программы. Светлая тема, один акцентный цвет.</summary>
internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(243, 245, 249);
    public static readonly Color Surface = Color.White;
    public static readonly Color Border = Color.FromArgb(225, 229, 236);
    public static readonly Color Text = Color.FromArgb(31, 41, 55);
    public static readonly Color Muted = Color.FromArgb(107, 114, 128);
    public static readonly Color Accent = Color.FromArgb(37, 99, 235);
    public static readonly Color AccentHover = Color.FromArgb(29, 78, 216);
    public static readonly Color AccentSoft = Color.FromArgb(232, 240, 254);
    public static readonly Color Success = Color.FromArgb(22, 163, 74);
    public static readonly Color Danger = Color.FromArgb(220, 38, 38);
    public static readonly Color Warning = Color.FromArgb(217, 119, 6);
    public static readonly Color Neutral = Color.FromArgb(107, 114, 128);
    public static readonly Color HeaderFrom = Color.FromArgb(30, 58, 138);
    public static readonly Color HeaderTo = Color.FromArgb(37, 99, 235);
    public static readonly Color HeaderMuted = Color.FromArgb(191, 219, 254);

    public static readonly Font Base = new("Segoe UI", 9.5f);
    public static readonly Font Bold = new("Segoe UI Semibold", 9.5f);
    public static readonly Font Small = new("Segoe UI", 8.5f);
    public static readonly Font PillFont = new("Segoe UI Semibold", 9f);
    public static readonly Font Title = new("Segoe UI Semibold", 17f);
    public static readonly Font Heading = new("Segoe UI Semibold", 11f);
    public static readonly Font Mono = new("Consolas", 9.5f);

    private static readonly float DpiFactor = ReadDpiFactor();

    private static float ReadDpiFactor()
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        return g.DpiX / 96f;
    }

    /// <summary>Пиксели «для 96 dpi» → реальные пиксели. Нужно для ручной отрисовки; элементы форм масштабируются сами.</summary>
    public static int Px(int value) => (int)Math.Round(value * DpiFactor);

    public static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Label Heading2(string text, int width) => new()
    {
        Text = text, Font = Heading, ForeColor = Text, AutoSize = false,
        Size = new Size(width, 24), Margin = new Padding(0, 0, 0, 2),
    };

    /// <summary>Поясняющий текст серым цветом; переносится по словам в пределах ширины.</summary>
    public static Label Hint(string text, int width, int bottom = 10) => new()
    {
        Text = text, Font = Small, ForeColor = Muted, AutoSize = true,
        MaximumSize = new Size(width, 0), Margin = new Padding(0, 0, 0, bottom),
    };

    public static Label Caption(string text, int width) => new()
    {
        Text = text, Font = Bold, ForeColor = Text, AutoSize = false,
        Size = new Size(width, 22), Margin = new Padding(0, 4, 0, 2),
    };
}

/// <summary>Белая карточка со скруглёнными углами; содержимое складывается в <see cref="Flow"/> сверху вниз.</summary>
internal sealed class Card : Panel
{
    /// <summary>Внутренний отступ карточки со всех сторон.</summary>
    public const int Pad = 14;

    public FlowLayoutPanel Flow { get; }

    public int InnerWidth => Width - 2 * Pad;

    public Card(int width)
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Theme.Background;
        Margin = new Padding(0, 0, 0, 10);
        Width = width;
        Flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Surface,
            Location = new Point(Pad, Pad),
            Margin = Padding.Empty,
        };
        Controls.Add(Flow);
        Flow.SizeChanged += (_, _) => Height = Flow.Height + 2 * Pad;
        Height = 64;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 2f, Height - 2f), 9);
        using var fill = new SolidBrush(Theme.Surface);
        using var pen = new Pen(Theme.Border);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(pen, path);
    }
}

internal enum ButtonKind { Primary, Secondary }

internal sealed class FlatButton : Button
{
    public FlatButton(string text, ButtonKind kind = ButtonKind.Secondary, int width = 112)
    {
        Text = text;
        Size = new Size(width, 32);
        Font = Theme.Base;
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        Margin = new Padding(0, 0, 8, 0);
        FlatAppearance.BorderSize = 1;
        if (kind == ButtonKind.Primary)
        {
            BackColor = Theme.Accent;
            ForeColor = Color.White;
            FlatAppearance.BorderColor = Theme.Accent;
            FlatAppearance.MouseOverBackColor = Theme.AccentHover;
            FlatAppearance.MouseDownBackColor = Theme.AccentHover;
        }
        else
        {
            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
            FlatAppearance.BorderColor = Theme.Border;
            FlatAppearance.MouseOverBackColor = Color.FromArgb(243, 244, 246);
            FlatAppearance.MouseDownBackColor = Color.FromArgb(229, 231, 235);
        }
    }
}

/// <summary>Шапка окна: градиент, логотип, заголовок и (необязательно) строка состояния с цветной плашкой.</summary>
internal sealed class HeaderPanel : Panel
{
    private string? _pillText;
    private Color _pillColor = Theme.Neutral;
    private string? _statusPrefix;
    private string? _statusValue;
    private string? _flagCode;
    private string? _detailLine;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title { get; set; } = AppInfo.Name;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Subtitle { get; set; } = AppInfo.Tagline;

    public HeaderPanel(int height)
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        Height = height;
        Dock = DockStyle.Top;
    }

    /// <param name="statusPrefix">Подпись, например «Текущая страна по IP:».</param>
    /// <param name="statusValue">Значение после флага, например «Германия (DE)».</param>
    /// <param name="flagCode">Двухбуквенный код страны для флага между подписью и значением; null — без флага.</param>
    public void SetStatus(string pillText, Color pillColor, string statusPrefix, string? statusValue, string? flagCode, string? detailLine)
    {
        _pillText = pillText;
        _pillColor = pillColor;
        _statusPrefix = statusPrefix;
        _statusValue = statusValue;
        _flagCode = flagCode;
        _detailLine = detailLine;
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var brush = new LinearGradientBrush(ClientRectangle.Width > 0 ? ClientRectangle : new Rectangle(0, 0, 1, 1),
            Theme.HeaderFrom, Theme.HeaderTo, LinearGradientMode.ForwardDiagonal);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var px = Theme.Px;
        g.DrawImage(AppAssets.Logo, new Rectangle(px(22), px(16), px(52), px(52)));
        using var white = new SolidBrush(Color.White);
        using var muted = new SolidBrush(Theme.HeaderMuted);
        g.DrawString(Title, Theme.Title, white, px(86), px(14));
        g.DrawString(Subtitle, Theme.Base, muted, px(88), px(46));

        if (_pillText is null)
            return;

        // Плашка состояния: «Выбранным приложениям разрешён запуск» / «Выбранные приложения заблокированы» / …
        // Справа от неё крупный флаг, поэтому ширина ограничена; слишком длинный текст обрезается многоточием.
        var pillLeft = px(22);
        var pillMaxWidth = Width - px(16) - px(132) - px(14) - pillLeft;
        var pillTextWidth = TextRenderer.MeasureText(g, _pillText, Theme.PillFont, Size.Empty, TextFormatFlags.NoPadding).Width;
        var pill = new RectangleF(pillLeft, px(76), Math.Min(pillTextWidth + px(28), pillMaxWidth), px(26));
        using (var path = Theme.Rounded(pill, px(13)))
        using (var pillBrush = new SolidBrush(_pillColor))
            g.FillPath(pillBrush, path);
        TextRenderer.DrawText(g, _pillText, Theme.PillFont, Rectangle.Round(new RectangleF(pill.X + px(10), pill.Y, pill.Width - px(20), pill.Height)), Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        // Крупный флаг текущей страны справа вверху. Он заканчивается выше строки со страной, поэтому названию хватает всей ширины.
        if (_flagCode is not null)
            CountryFlags.Draw(g, _flagCode, new Rectangle(Width - px(16) - px(132), px(14), px(132), px(88)), shadow: true);

        // «Текущая страна по IP: [флаг] Германия (DE)»
        var x = px(22);
        var line = new Rectangle(x, px(110), Width - px(44), px(24));
        var prefix = _statusPrefix ?? "";
        var prefixWidth = TextRenderer.MeasureText(g, prefix, Theme.Bold, Size.Empty, TextFormatFlags.NoPadding).Width;
        TextRenderer.DrawText(g, prefix, Theme.Bold, new Rectangle(x, line.Y, prefixWidth + px(2), line.Height), Color.White,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        x += prefixWidth + px(8);

        if (_flagCode is not null)
        {
            var flagBox = new Rectangle(x, line.Y + (line.Height - px(18)) / 2, px(24), px(18));
            CountryFlags.Draw(g, _flagCode, flagBox);
            x += flagBox.Width + px(8);
        }

        TextRenderer.DrawText(g, _statusValue ?? "", Theme.Bold, new Rectangle(x, line.Y, Math.Max(0, line.Right - x), line.Height), Color.White,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        if (!string.IsNullOrEmpty(_detailLine))
        {
            TextRenderer.DrawText(g, _detailLine, Theme.Small, new Rectangle(px(22), px(138), Width - px(44), Height - px(138)), Theme.HeaderMuted,
                TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }
}

/// <summary>Горизонтальные вкладки с подчёркиванием выбранной.</summary>
internal sealed class TabStrip : Control
{
    private readonly string[] _tabs;
    private int _selected;
    private int _hover = -1;

    public event Action? SelectedChanged;

    public TabStrip(params string[] tabs)
    {
        _tabs = tabs;
        DoubleBuffered = true;
        ResizeRedraw = true;
        Height = 40;
        Dock = DockStyle.Top;
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (value == _selected)
                return;
            _selected = value;
            Invalidate();
            SelectedChanged?.Invoke();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Surface);
        using var border = new Pen(Theme.Border);
        g.DrawLine(border, 0, Height - 1, Width, Height - 1);

        var tabWidth = Theme.Px(120);
        var left = Theme.Px(16);
        for (var i = 0; i < _tabs.Length; i++)
        {
            var bounds = new Rectangle(left + i * tabWidth, 0, tabWidth, Height - 1);
            var selected = i == _selected;
            var color = selected ? Theme.Accent : i == _hover ? Theme.Text : Theme.Muted;
            TextRenderer.DrawText(g, _tabs[i], selected ? Theme.Bold : Theme.Base, bounds, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (selected)
            {
                using var accent = new SolidBrush(Theme.Accent);
                g.FillRectangle(accent, bounds.X + Theme.Px(14), Height - Theme.Px(4), bounds.Width - Theme.Px(28), Theme.Px(3));
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var index = IndexAt(e.Location);
        if (index != _hover)
        {
            _hover = index;
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        var index = IndexAt(e.Location);
        if (index >= 0)
            SelectedIndex = index;
        base.OnMouseClick(e);
    }

    private int IndexAt(Point point)
    {
        var left = Theme.Px(16);
        var index = (point.X - left) / Theme.Px(120);
        return point.X >= left && index >= 0 && index < _tabs.Length ? index : -1;
    }
}

/// <summary>Список приложений: имя крупно, путь (или пояснение) мелко серым.</summary>
internal sealed class AppListBox : ListBox
{
    public AppListBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = Theme.Px(40);
        BorderStyle = BorderStyle.None;
        BackColor = Theme.Surface;
        IntegralHeight = false;
        SelectionMode = SelectionMode.MultiExtended;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count)
            return;

        var entry = (string)Items[e.Index]!;
        var selected = (e.State & DrawItemState.Selected) != 0;
        var g = e.Graphics;
        var bounds = e.Bounds;

        using (var back = new SolidBrush(selected ? Theme.AccentSoft : Theme.Surface))
            g.FillRectangle(back, bounds);
        if (selected)
        {
            using var bar = new SolidBrush(Theme.Accent);
            g.FillRectangle(bar, bounds.X, bounds.Y, Theme.Px(3), bounds.Height);
        }

        using (var line = new Pen(Theme.Border))
            g.DrawLine(line, bounds.X + Theme.Px(12), bounds.Bottom - 1, bounds.Right - Theme.Px(12), bounds.Bottom - 1);

        var second = AppEntry.IsPath(entry) ? entry : "имя процесса — для работы файрвола лучше добавить через «Обзор…»";
        TextRenderer.DrawText(g, AppEntry.ProcessName(entry), Theme.Bold, new Rectangle(bounds.X + Theme.Px(12), bounds.Y + Theme.Px(3), bounds.Width - Theme.Px(24), Theme.Px(20)),
            Theme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, second, Theme.Small, new Rectangle(bounds.X + Theme.Px(12), bounds.Y + Theme.Px(21), bounds.Width - Theme.Px(24), Theme.Px(16)),
            Theme.Muted, TextFormatFlags.PathEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Тексты и цвета для показа состояния охраны в шапке и меню.</summary>
internal static class StatusView
{
    public static (string Pill, Color Color) Pill(GuardStatus status) =>
        status.State == GuardState.Allowed && !status.AppsBlocked ? ("Выбранным приложениям разрешён запуск", Theme.Success)
        : status.State == GuardState.Blocking ? ("Выбранные приложения заблокированы", Theme.Danger)
        : status.AppsBlocked ? ("Выбранные приложения заблокированы: идёт проверка страны", Theme.Warning)
        : ("Определяем страну…", Theme.Accent);

    public const string Prefix = "Текущая страна по IP:";

    public static string Value(GuardStatus status) => status.CountryLabel ?? "определяется…";

    public static string MainLine(GuardStatus status) => $"{Prefix} {Value(status)}";

    /// <summary>Пояснение под строкой со страной: только то, чего нет в плашке (причина, ошибка проверки, состояние файрвола).</summary>
    public static string? Details(GuardStatus status)
    {
        var parts = new List<string>();
        if (status.State == GuardState.Blocking)
            parts.Add("Страна не совпадает с выбранной.");
        else if (status.State == GuardState.Unknown && status.AppsBlocked)
            parts.Add("Страна не подтверждена.");
        if (status.Error is not null)
            parts.Add(status.Error + (status.Error.EndsWith('.') ? "" : "."));
        if (status.FirewallNote is not null)
            parts.Add(status.FirewallNote);
        return parts.Count == 0 ? null : string.Join("  ", parts);
    }
}

internal static class Ui
{
    /// <summary>Нижняя панель с кнопками у правого края; кнопки передаются справа налево.</summary>
    public static Panel Footer(params Button[] rightToLeft)
    {
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Theme.Surface };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(16, 13, 16, 0),
            BackColor = Theme.Surface,
        };
        foreach (var button in rightToLeft)
            flow.Controls.Add(button);
        footer.Controls.Add(flow);
        return footer;
    }

    public static void Flat(ComboBox box)
    {
        box.FlatStyle = FlatStyle.Flat;
        box.BackColor = Theme.Surface;
        box.ForeColor = Theme.Text;
        box.Font = Theme.Base;
    }
}

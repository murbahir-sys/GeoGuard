using System.Runtime.InteropServices;
using System.Text;

namespace GeoGuard;

internal sealed class AboutForm : Form
{
    private const int CardWidth = 468;
    private const int Inner = CardWidth - 2 * Card.Pad;

    public event Action? OpenHelpRequested;

    public AboutForm()
    {
        Text = $"О программе — {AppInfo.Name}";
        Icon = AppAssets.Icon(32);
        Font = Theme.Base;
        BackColor = Theme.Background;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(500, 710);

        var info = CollectInfo();

        var body = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(16, 16, 0, 0),
            BackColor = Theme.Background,
        };

        var about = new Card(CardWidth);
        about.Flow.Controls.Add(new Label
        {
            Text = AppInfo.Description,
            Font = Theme.Base, ForeColor = Theme.Text, AutoSize = true,
            MaximumSize = new Size(Inner, 0), Margin = new Padding(0, 0, 0, 10),
        });
        about.Flow.Controls.Add(Theme.Hint($"{AppInfo.Copyright}. Распространяется бесплатно, исходный код открыт для проверки; условия — в LICENSE.txt в папке программы.", Inner, 4));
        var source = new LinkLabel
        {
            Text = "Исходный код: " + AppInfo.RepositoryUrl.Replace("https://", ""),
            Font = Theme.Small, AutoSize = true, Margin = new Padding(0, 0, 0, 2),
            LinkColor = Theme.Accent, ActiveLinkColor = Theme.AccentHover, VisitedLinkColor = Theme.Accent,
            LinkBehavior = LinkBehavior.HoverUnderline,
        };
        source.LinkArea = new LinkArea("Исходный код: ".Length, source.Text.Length - "Исходный код: ".Length);
        source.LinkClicked += (_, _) => Shell.Open(AppInfo.RepositoryUrl);
        about.Flow.Controls.Add(source);
        about.Flow.Controls.Add(Theme.Hint("Флаги стран: flagpedia.net", Inner, 0));
        body.Controls.Add(about);

        var details = new Card(CardWidth);
        details.Flow.Controls.Add(Theme.Heading2("Сведения", Inner));
        var grid = new TableLayoutPanel
        {
            AutoSize = true, ColumnCount = 2, BackColor = Theme.Surface, Margin = Padding.Empty,
            Width = Inner,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Inner - 120));
        foreach (var (label, value) in info.Where(i => i.Label is not ("Версия" or "Автор")))
        {
            grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(new Label { Text = label, Font = Theme.Base, ForeColor = Theme.Muted, AutoSize = true, Margin = new Padding(0, 0, 0, 6) });
            grid.Controls.Add(new Label
            {
                Text = value, Font = Theme.Base, ForeColor = Theme.Text, AutoSize = true,
                MaximumSize = new Size(Inner - 124, 0), Margin = new Padding(0, 0, 0, 6),
            });
        }

        details.Flow.Controls.Add(grid);
        body.Controls.Add(details);

        var actions = new Card(CardWidth);
        var row1 = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(Inner, 0), Width = Inner, BackColor = Theme.Surface, Margin = Padding.Empty };
        var help = new FlatButton("Справка", ButtonKind.Primary, 100) { Margin = new Padding(0, 0, 8, 8) };
        help.Click += (_, _) => OpenHelpRequested?.Invoke();
        var folder = new FlatButton("Папка настроек", ButtonKind.Secondary, 140) { Margin = new Padding(0, 0, 8, 8) };
        folder.Click += (_, _) =>
        {
            Directory.CreateDirectory(SettingsStore.DirectoryPath);
            Shell.Open(SettingsStore.DirectoryPath);
        };
        var copy = new FlatButton("Копировать сведения", ButtonKind.Secondary, 160) { Margin = new Padding(0, 0, 0, 8) };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(string.Join(Environment.NewLine, info.Select(i => $"{i.Label}: {i.Value}"))); }
            catch (ExternalException) { /* буфер обмена занят другой программой */ }
        };
        var selfTest = new FlatButton("Проверка файрвола…", ButtonKind.Secondary, 170) { Margin = new Padding(0, 0, 8, 0) };
        selfTest.Click += (_, _) =>
        {
            if (!Elevation.IsElevated)
            {
                MessageBox.Show("Для проверки файрвола нужны права администратора. Перезапустите GeoGuard и подтвердите запрос UAC.",
                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Maintenance.FirewallSelfTest();
        };
        row1.Controls.Add(help);
        row1.Controls.Add(folder);
        row1.Controls.Add(copy);
        row1.Controls.Add(selfTest);
        actions.Flow.Controls.Add(row1);
        body.Controls.Add(actions);

        var close = new FlatButton("Закрыть", ButtonKind.Secondary, 110) { Margin = Padding.Empty };
        close.Click += (_, _) => Close();
        CancelButton = close;
        var footer = Ui.Footer(close);

        Controls.Add(body);
        Controls.Add(footer);
        Controls.Add(new HeaderPanel(84) { Title = AppInfo.Name, Subtitle = $"Версия {AppInfo.VersionText}" });
    }

    /// <summary>Сведения для окна и для обращения за помощью.</summary>
    internal static List<(string Label, string Value)> CollectInfo()
    {
        var firewall = "недоступен";
        if (Elevation.IsElevated)
        {
            try
            {
                firewall = $"доступен, правил GeoGuard: {new WindowsFirewall().CountOwnedRules()}";
            }
            catch (FirewallException ex)
            {
                firewall = ex.Message;
            }
        }
        else
        {
            firewall = "недоступен: нет прав администратора";
        }

        return new List<(string, string)>
        {
            ("Версия", AppInfo.VersionText),
            ("Автор", AppInfo.Author),
            ("Права", Elevation.IsElevated ? "администратор" : "обычные"),
            ("Файрвол", firewall),
            ("Настройки", SettingsStore.DirectoryPath),
            (".NET", RuntimeInformation.FrameworkDescription),
            ("Система", RuntimeInformation.OSDescription),
        };
    }
}

namespace GeoGuard;

internal sealed class HelpForm : Form
{
    public HelpForm()
    {
        Text = $"Справка — {AppInfo.Name}";
        Icon = AppAssets.Icon(32);
        Font = Theme.Base;
        BackColor = Theme.Surface;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 660);
        MinimumSize = new Size(560, 420);

        var box = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Surface,
            DetectUrls = false,
            TabStop = false,
            Cursor = Cursors.Default,
        };
        var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(28, 20, 20, 12) };
        host.Controls.Add(box);

        var close = new FlatButton("Закрыть", ButtonKind.Primary, 110) { Margin = Padding.Empty };
        close.Click += (_, _) => Close();
        CancelButton = close;
        var footer = Ui.Footer(close);

        Controls.Add(host);
        Controls.Add(footer);
        Controls.Add(new HeaderPanel(84) { Title = "Справка", Subtitle = "Как пользоваться GeoGuard" });

        RichMarkup.Render(box, HelpContent.Text);
    }
}

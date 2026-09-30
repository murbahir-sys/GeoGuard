using System.Drawing.Imaging;
using GeoGuard;

// Отрисовка окон в PNG для визуальной проверки: GeoGuard.Tests.exe <жертва> --render-ui <папка>
internal static partial class Program
{
    private static void RenderUi(string directory)
    {
        Directory.CreateDirectory(directory);
        var thread = new Thread(() =>
        {
            Application.EnableVisualStyles();
            var settings = new AppSettings
            {
                AllowedCountry = "DE",
                VpnAdapterMatch = "WireGuard",
                RestrictedApps =
                {
                    @"C:\Program Files\Mozilla Firefox\firefox.exe",
                    @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                    "Telegram",
                },
            }.Normalize();
            var geo = new GeoResult("DE", "Germany", "203.0.113.1");

            using (var form = new SettingsForm(settings, new GuardStatus(GuardState.Allowed, geo, DateTime.Now, null, false, "Файрвол: вне VPN закрыто приложениям (2)")))
            {
                Snap(form, Path.Combine(directory, "settings-1-protection.png"));
                form.SelectTab(1);
                Snap(form, Path.Combine(directory, "settings-2-network.png"));
                form.SelectTab(2);
                Snap(form, Path.Combine(directory, "settings-3-general.png"));
                form.SelectTab(0);
                form.UpdateStatus(new GuardStatus(GuardState.Blocking, new GeoResult("US", "United States", "198.51.100.7"), DateTime.Now, null, true, "Файрвол: сеть закрыта приложениям (2)"));
                Snap(form, Path.Combine(directory, "settings-4-blocked.png"));
                form.UpdateStatus(new GuardStatus(GuardState.Unknown, null, null, "Не удалось определить страну по IP", true, "Файрвол выключен: нет прав администратора. Работает только закрытие приложений."));
                Snap(form, Path.Combine(directory, "settings-5-unknown.png"));
                foreach (var code in new[] { "CH", "NP", "JP", "CD", "GB" })
                {
                    form.UpdateStatus(new GuardStatus(GuardState.Blocking, new GeoResult(code, code, "198.51.100.7"), DateTime.Now, null, true, null));
                    Snap(form, Path.Combine(directory, $"flag-{code}.png"));
                }
            }

            using (var form = new AboutForm())
                Snap(form, Path.Combine(directory, "about.png"));
            using (var form = new HelpForm())
                Snap(form, Path.Combine(directory, "help.png"));
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Console.WriteLine("Снимки сохранены в " + directory);
    }

    /// <summary>Раскрытый список VPN-адаптеров: окно на пару секунд показывается на экране, снимок — с экрана.</summary>
    private static void RenderAdapterDropDown(string path)
    {
        var thread = new Thread(() =>
        {
            Application.EnableVisualStyles();
            var settings = new AppSettings { AllowedCountry = "DE", RestrictedApps = { "chrome" } }.Normalize();
            using var form = new SettingsForm(settings, new GuardStatus(GuardState.Allowed, new GeoResult("DE", "Germany", "203.0.113.1"), DateTime.Now, null, false,
                "VPN-адаптер не выбран (вкладка «Сеть»): файрвол не работает, отключение VPN замечается медленнее."));
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(40, 40);
            form.TopMost = true;
            form.ShowInTaskbar = false;
            form.Show();
            form.SelectTab(1);
            Application.DoEvents();
            var combo = FindAll<ComboBox>(form).Single(c => c.DropDownStyle == ComboBoxStyle.DropDown);
            combo.DroppedDown = true;
            for (var i = 0; i < 20; i++) { Application.DoEvents(); Thread.Sleep(50); }

            var area = new Rectangle(form.Left, form.Top, form.Width, form.Height);
            using var bitmap = new Bitmap(area.Width, area.Height);
            using (var g = Graphics.FromImage(bitmap))
                g.CopyFromScreen(area.Location, Point.Empty, area.Size);
            bitmap.Save(path, ImageFormat.Png);
            combo.DroppedDown = false;
            form.CloseWithoutPrompt();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    private static void Snap(Form form, string path)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-5000, 0);
        form.ShowInTaskbar = false;
        form.Show();
        Application.DoEvents();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(path, ImageFormat.Png);
        form.Hide();
    }
}

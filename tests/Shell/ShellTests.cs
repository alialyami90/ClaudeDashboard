using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClaudeDashboard;

// Claude Dashboard's window (phase 4 of the transformation plan): the palettes, the window picking the card's design
// from its size, the header, the Settings window's preview, Cancel and Save, the window's place, and Start with
// Windows. Renders go to artifacts\renders, which CI keeps with the run. Settings files are throwaway ones, and the
// Startup folder is a throwaway one too.
internal static class ShellTests
{
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }

    private static string Fresh(string directory, string name)
    {
        string path = Path.Combine(directory, "shell", name);
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
        return path;
    }
    private static string Renders(string directory) { string path = Path.GetFullPath(Path.Combine(directory, "..", "renders")); Directory.CreateDirectory(path); return path; }
    private static string SettingsFile(string folder, AppSettings settings)
    {
        string path = Path.Combine(folder, "settings.xml");
        SettingsStore.Save(path, settings);
        return path;
    }
    private static Color ColorOf(object brush) { var solid = brush as SolidColorBrush; return solid == null ? Colors.Transparent : solid.Color; }
    private static Color Hex(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
    // Whether a TextBlock's words are wider than the room it got (cut off with an ellipsis, or clipped).
    private static bool Cut(TextBlock block)
    {
        if (String.IsNullOrEmpty(block.Text)) return false;
        var probe = new TextBlock { Text = block.Text, FontFamily = block.FontFamily, FontSize = block.FontSize, FontWeight = block.FontWeight, FontStyle = block.FontStyle, FontStretch = block.FontStretch };
        probe.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
        return probe.DesiredSize.Width > block.ActualWidth + 1;
    }
    private static void Click(Window window, string name) { ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Shell palettes: every palette sets every colour, Claude is light, and text stays readable", () => {
            foreach (string theme in Palettes.Themes)
            {
                var colors = Palettes.Colors(theme);
                Assert(colors.Length == Palettes.Keys.Length, theme + " sets " + colors.Length + " colours for " + Palettes.Keys.Length + " keys.");
                string page = colors[0], surface = colors[1], muted = colors[7], text = colors[8], amber = colors[12], turn = colors[13], onAccent = colors[14], onWarning = colors[16];
                bool light = Palettes.Luminance(page) > Palettes.Luminance(text);
                Assert(light == Palettes.Light(theme), theme + " is " + (light ? "light" : "dark") + " but says otherwise.");
                Assert(Palettes.Contrast(text, surface) >= 7, theme + ": text on the surface is too faint.");
                Assert(Palettes.Contrast(muted, surface) >= 4.5, theme + ": muted text on the surface is too faint.");
                Assert(Palettes.Contrast(amber, surface) >= 4.5, theme + ": Amber words on the surface are too faint.");
                Assert(Palettes.Contrast(onWarning, amber) >= 4.5, theme + ": words on an Amber fill are too faint.");
                Assert(Palettes.Contrast(turn, surface) >= 3, theme + ": the Your-turn edge is too faint.");
                foreach (string accent in Palettes.Accents)
                {
                    string color = Palettes.AccentColor(theme, accent);
                    Assert(Palettes.Contrast(color, surface) >= 3.5, theme + "/" + accent + ": the accent is too faint on the surface.");
                    Assert(Palettes.Contrast(onAccent, color) >= 4, theme + "/" + accent + ": button words on the accent are too faint.");
                }
            }
            Assert(Palettes.Light("Claude") && !Palettes.Light("Midnight") && Palettes.DefaultAccent("Claude") == "Terracotta" && Palettes.DefaultAccent("Warm") == "Lime", "Claude isn't the light palette with terracotta.");
            Assert(Palettes.CleanTheme("Claude") == "Claude" && Palettes.CleanTheme("Neon") == "Midnight" && Palettes.CleanAccent("Terracotta") == "Terracotta" && Palettes.CleanAccent(null) == "Lime", "Palette names cleaned wrongly.");
            string folder = Fresh(directory, "palette-file");
            string path = SettingsFile(folder, new AppSettings { Theme = "Claude", Accent = "Terracotta", WindowLeft = -1200, WindowTop = 40, WindowWidth = 1100, WindowHeight = 700, WindowMaximised = true, StartMinimised = true, WindowCentred = true });
            string warning; var back = SettingsStore.Load(path, out warning);
            Assert(warning == null && back.Theme == "Claude" && back.Accent == "Terracotta" && back.WindowLeft == -1200 && back.WindowWidth == 1100 && back.WindowMaximised && back.StartMinimised && back.WindowCentred, "The Claude palette or the window's place didn't survive settings.xml.");
        });

        check("Shell window: the card picks Small, Medium and Large from the window's size, without its own heading", () => {
            string renders = Renders(directory);
            var sizes = new[] {
                new { W = 360.0, H = 300.0, Scale = 1.0, Design = CardSizing.Design.Small, Name = "shell-360x300" },
                new { W = 640.0, H = 420.0, Scale = 1.0, Design = CardSizing.Design.Medium, Name = "shell-640x420" },
                new { W = 1000.0, H = 600.0, Scale = 1.0, Design = CardSizing.Design.Large, Name = "shell-1000x600" },
                new { W = 1600.0, H = 900.0, Scale = 1.0, Design = CardSizing.Design.Large, Name = "shell-1600x900" },
                new { W = 1600.0, H = 900.0, Scale = 1.5, Design = CardSizing.Design.Large, Name = "shell-1600x900-150" } };
            foreach (bool needs in new[] { false, true })
                foreach (var size in sizes)
                {
                    string folder = Fresh(directory, size.Name + (needs ? "-needs" : ""));
                    var window = AppController.LoadWindow("ShellWindow");
                    using (var controller = new AppController(window, SettingsFile(folder, new AppSettings { ClaudePeriod = "Week" })))
                    {
                        controller.UseClaudeSample(needs);
                        AppController.Render(window, new Size(size.W, size.H), size.Scale, Path.Combine(renders, size.Name + (needs ? "-needs" : "") + ".png"));
                        var card = controller.ClaudeCardView;
                        Assert(card.Current == size.Design, size.Name + " shows " + card.Current + ", not " + size.Design + ".");
                        Assert(window.FindName("ClaudeHeading") == null && window.FindName("ClaudePeriod0") == null, size.Name + ": the card drew its own heading in the window.");
                        var summary = (TextBlock)window.FindName("ClaudeSummary");
                        Assert(summary.Text.Length > 0 && summary.Text != "Reading Claude Code…", size.Name + ": the header has no summary.");
                        Assert(((TextBlock)window.FindName("ClaudeFlag")).Text.Contains("SAMPLE"), size.Name + ": the fixture doesn't say it's a sample.");
                        // The summary is never cut mid-word, and from 1000 units wide it's the whole summary (on the
                        // header's line, or on its own line under it).
                        Assert(!Cut(summary), size.Name + ": the header's summary is cut: \"" + summary.Text + "\" in " + Math.Round(summary.ActualWidth));
                        if (Math.Round(size.W / size.Scale) >= 1000) Assert(summary.Text == ClaudeText.Summary(card.View, card.Period, true), size.Name + ": the header doesn't show the whole summary: " + summary.Text);
                        var content = (Grid)window.FindName("DashboardContent");
                        Assert(content.RowDefinitions[0].Height.Value == (controller.HeaderTwoLines ? 84 : 56), size.Name + ": the header's height doesn't match its lines.");
                        var edge = (Border)window.FindName("WindowEdge");
                        if (needs)
                        {
                            Assert(card.Edge == "Warning" && ColorOf(edge.BorderBrush) == ColorOf(window.Resources["Warning"]), size.Name + ": the window's edge isn't Amber while a session needs you.");
                            Assert(window.Title.StartsWith("Claude needs you", StringComparison.Ordinal), size.Name + ": the title doesn't say Claude needs you.");
                        }
                        else Assert(card.Edge != "Warning" && window.Title == "Claude Dashboard", size.Name + ": the window looks like something needs you.");
                    }
                }
            // The side panel (5-hour lanes and facts) from 1000 units of card body: a 1600 × 900 window has it, 1000 × 600 doesn't.
            Assert(CardSizing.ClaudeSide(1600 - 6 - 48) && !CardSizing.ClaudeSide(1000 - 6 - 48) && CardSizing.ClaudeSide(Math.Round(1600 / 1.5) - 6 - 48), "The side panel's threshold moved.");
        });

        check("Shell header at the smallest window: Small keeps its 5-hour meter, and the summary stays whole words", () => {
            // A 360 × 300 window (its outer size, the minimum) has a client area of about 360 × 261 on Windows 10 and 11.
            string renders = Renders(directory);
            foreach (double scale in new[] { 1.0, 1.4 })
                foreach (bool needs in new[] { false, true })
                {
                    string name = "shell-360x261-" + Math.Round(scale * 100) + (needs ? "-needs" : "");
                    string folder = Fresh(directory, name);
                    var window = AppController.LoadWindow("ShellWindow");
                    using (var controller = new AppController(window, SettingsFile(folder, new AppSettings { ClaudePeriod = "Week", TextScale = scale })))
                    {
                        controller.UseClaudeSample(needs);
                        AppController.Render(window, new Size(360, 261), 1, Path.Combine(renders, name + ".png"));
                        var card = controller.ClaudeCardView;
                        Assert(card.Current == CardSizing.Design.Small, name + " isn't Small.");
                        Assert(window.FindName("ClaudeSmallMeter") != null, name + ": Small lost its 5-hour meter (header " + (controller.HeaderTwoLines ? 84 : 56) + ").");
                        var summary = (TextBlock)window.FindName("ClaudeSummary");
                        Assert(summary.Text == ClaudeText.Summary(card.View, card.Period, true) || summary.Text == ClaudeText.ShortSummary(card.View, card.Period), name + ": the summary isn't the whole or the short one: " + summary.Text);
                        Assert(summary.TextTrimming == TextTrimming.WordEllipsis, name + ": the summary could be cut mid-word.");
                    }
                }
        });

        check("Shell header: the period switch chooses and keeps the period, Quiet toggles, and narrow windows fold the words away", () => {
            string folder = Fresh(directory, "header");
            string path = SettingsFile(folder, new AppSettings { ClaudePeriod = "Today" });
            var window = AppController.LoadWindow("ShellWindow");
            using (var controller = new AppController(window, path))
            {
                controller.UseClaudeSample(false);
                AppController.Lay(window, new Size(1280, 760));
                Click(window, "ShellPeriod0");
                Assert(controller.ClaudeCardView.Period == ClaudePeriod.FiveHour && controller.CurrentSettings.ClaudePeriod == "FiveHour", "The header's 5 h button didn't choose the 5-hour window.");
                Assert(((TextBlock)window.FindName("ClaudeSummary")).Text.Contains("in 5 h"), "The summary didn't follow the period.");
                Click(window, "QuietButton");
                string warning; Assert(SettingsStore.Load(path, out warning).QuietMode && SettingsStore.Load(path, out warning).ClaudePeriod == "FiveHour", "Quiet mode or the period wasn't saved.");
                Assert(((TextBlock)window.FindName("AppName")).Visibility == Visibility.Visible && ((StackPanel)window.FindName("PeriodSwitch")).Visibility == Visibility.Visible, "The wide header hides its name or periods.");
                AppController.Lay(window, new Size(420, 320));
                Assert(((TextBlock)window.FindName("AppName")).Visibility == Visibility.Collapsed && ((StackPanel)window.FindName("PeriodSwitch")).Visibility == Visibility.Collapsed && ((TextBlock)window.FindName("SettingsLabel")).Visibility == Visibility.Collapsed, "A narrow header kept its words.");
                var summary = (TextBlock)window.FindName("ClaudeSummary");
                Assert(summary.ActualWidth > 40, "A narrow header squeezed the summary out.");
            }
        });

        check("Shell Settings: palettes preview on the dashboard, Cancel puts them back, Save keeps them", () => {
            string folder = Fresh(directory, "settings");
            string path = SettingsFile(folder, new AppSettings());
            string renders = Renders(directory);
            var window = AppController.LoadWindow("ShellWindow");
            using (var controller = new AppController(window, path))
            {
                controller.UseClaudeSample(false);
                AppController.Lay(window, new Size(1280, 760));
                Assert(ColorOf(window.Resources["Page"]) == Hex(Palettes.Colors("Midnight")[0]), "The window doesn't start in Midnight.");
                controller.OpenSettings();
                var settings = controller.SettingsWindow;
                Assert(settings != null, "Settings didn't open.");
                var tabs = (TabControl)settings.FindName("SettingsTabs");
                Assert(tabs.Items.OfType<TabItem>().Select(t => (string)t.Header).SequenceEqual(new[] { "Theme", "Claude", "Window" }), "Settings' tabs aren't Theme, Claude and Window.");
                ((ComboBox)settings.FindName("ThemeChoice")).SelectedItem = "Claude";
                Assert((string)((ComboBox)settings.FindName("AccentChoice")).SelectedItem == "Terracotta", "Picking Claude didn't bring terracotta along.");
                Assert(ColorOf(window.Resources["Page"]) == Hex(Palettes.Colors("Claude")[0]) && ColorOf(settings.Resources["Surface"]) == Hex(Palettes.Colors("Claude")[1]), "The Claude palette didn't preview on both windows.");
                AppController.Render(settings, new Size(980, 720), 1, Path.Combine(renders, "settings-theme-claude.png"));
                AppController.Render(window, new Size(1280, 760), 1, Path.Combine(renders, "shell-1280x760-claude.png"));
                controller.SelectSettingsTab("Claude");
                AppController.Render(settings, new Size(980, 720), 1, Path.Combine(renders, "settings-claude.png"));
                controller.SelectSettingsTab("Window");
                AppController.Render(settings, new Size(980, 720), 1, Path.Combine(renders, "settings-window.png"));
                controller.CancelSettings();
                Assert(controller.SettingsWindow == null && ColorOf(window.Resources["Page"]) == Hex(Palettes.Colors("Midnight")[0]), "Cancel didn't put Midnight back.");
                string warning; Assert(SettingsStore.Load(path, out warning).Theme == "Midnight", "Cancel saved the preview.");

                controller.OpenSettings();
                settings = controller.SettingsWindow;
                ((ComboBox)settings.FindName("ThemeChoice")).SelectedItem = "Claude";
                ((Slider)settings.FindName("ScaleChoice")).Value = 120;
                ((CheckBox)settings.FindName("RememberWindowChoice")).IsChecked = false;
                ((CheckBox)settings.FindName("StartMinimisedChoice")).IsChecked = true;
                controller.SaveSettings();
                var saved = SettingsStore.Load(path, out warning);
                Assert(controller.SettingsWindow == null && saved.Theme == "Claude" && saved.Accent == "Terracotta" && Math.Abs(saved.TextScale - 1.2) < 0.001 && saved.WindowCentred && saved.StartMinimised, "Save didn't keep the palette, text size and window choices.");
                Assert(ColorOf(window.Resources["Page"]) == Hex(Palettes.Colors("Claude")[0]) && Math.Abs((double)window.Resources["BodySize"] - 15 * 1.2) < 0.001, "The saved palette or text size didn't stay on the window.");
            }
            // The next start opens in the saved palette.
            var again = AppController.LoadWindow("ShellWindow");
            using (var controller = new AppController(again, path))
                Assert(ColorOf(again.Resources["Accent"]) == Hex(Palettes.AccentColor("Claude", "Terracotta")), "The next start forgot the palette.");
        });

        check("Shell window place: a saved place opens only where a screen can still show its title bar", () => {
            var main = new Rect(0, 0, 1920, 1040); var left = new Rect(-1280, 0, 1280, 984);
            Assert(WindowPlacement.Visible(new Rect(100, 100, 1200, 700), new[] { main }), "A window on the main screen was called off-screen.");
            Assert(WindowPlacement.Visible(new Rect(-1200, 60, 1000, 600), new[] { main, left }), "A window on the left monitor was called off-screen.");
            Assert(!WindowPlacement.Visible(new Rect(-1200, 60, 1000, 600), new[] { main }), "A window on an unplugged monitor was called visible.");
            Assert(!WindowPlacement.Visible(new Rect(200, -300, 1000, 280), new[] { main }), "A window with its title bar above the screen was called visible.");
            Assert(WindowPlacement.Visible(new Rect(1700, 200, 1000, 600), new[] { main }), "A window half off the right edge, title bar still reachable, was called off-screen.");
            Assert(!WindowPlacement.Visible(Rect.Empty, new[] { main }), "An empty place was called visible.");
        });

        check("Shell Start with Windows: the Startup shortcut is made and taken away (in a throwaway Startup folder)", () => {
            string folder = Fresh(directory, "startup");
            string before = null;
            try
            {
                StartupShortcut.Folder = folder;
                Assert(!StartupShortcut.Exists, "A shortcut was there before.");
                before = StartupShortcut.Set(true);
                Assert(before == null && StartupShortcut.Exists && StartupShortcut.Path.StartsWith(folder, StringComparison.OrdinalIgnoreCase), "The Startup shortcut wasn't made: " + before);
                Assert(StartupShortcut.Set(false) == null && !StartupShortcut.Exists, "The Startup shortcut wasn't taken away.");
            }
            finally { StartupShortcut.Folder = null; }
        });
    }
}

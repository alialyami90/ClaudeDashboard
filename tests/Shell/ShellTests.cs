using System;
using System.Collections.Generic;
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
    // The source-health line fits: every word it shows is whole, and its parts stay inside the window.
    private static void SourcesWhole(Window window, AppController controller, string name)
    {
        var line = (StackPanel)window.FindName("SourceLine");
        Assert(line.Visibility == Visibility.Visible && line.ActualHeight > 0, name + ": the source-health line isn't showing.");
        double right = 0;
        for (int i = 0; i < 4; i++)
        {
            var part = (FrameworkElement)window.FindName("Source" + i);
            var text = (TextBlock)window.FindName("SourceText" + i);
            Assert(((TextBlock)window.FindName("SourceGlyph" + i)).Text.Length == 1, name + ": source " + i + " has no glyph.");
            Assert(text.Visibility != Visibility.Visible || !Cut(text), name + ": source " + i + "'s words are cut: " + text.Text);
            right = Math.Max(right, part.TranslatePoint(new Point(part.ActualWidth, 0), line).X);
            Assert(!controller.Sources[i].Warns || text.Visibility == Visibility.Visible, name + ": an Amber source lost its words.");
        }
        Assert(right <= line.ActualWidth + 1, name + ": the source-health line runs past the window (" + Math.Round(right) + " of " + Math.Round(line.ActualWidth) + ").");
    }
    private static Color ColorOf(object brush) { var solid = brush as SolidColorBrush; return solid == null ? Colors.Transparent : solid.Color; }
    private static Color Hex(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
    // Whether a TextBlock's words are wider than the room it got (cut off with an ellipsis, or clipped).
    private static bool Cut(TextBlock block)
    {
        if (String.IsNullOrEmpty(block.Text)) return false;
        var probe = new TextBlock { Text = block.Text, FontFamily = block.FontFamily, FontSize = block.FontSize, FontWeight = block.FontWeight, FontStyle = block.FontStyle, FontStretch = block.FontStretch };
        // Measured as the window draws it (Display mode), not a lone TextBlock's Ideal widths.
        TextOptions.SetTextFormattingMode(probe, TextOptions.GetTextFormattingMode(block));
        probe.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
        return probe.DesiredSize.Width > block.ActualWidth + 1;
    }
    // Whether an element and every parent are Visible (IsVisible needs a shown window, and the checks never show one).
    private static bool Shown(DependencyObject element, DependencyObject root)
    {
        for (var e = element; e != null && e != root; e = VisualTreeHelper.GetParent(e))
            if (e is UIElement && ((UIElement)e).Visibility != Visibility.Visible) return false;
        return true;
    }
    private static List<TextBlock> Texts(DependencyObject root)
    {
        var found = new List<TextBlock>();
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var text = child as TextBlock;
            if (text != null) found.Add(text);
            found.AddRange(Texts(child));
        }
        return found;
    }
    // Every view at 640 × 420, 1000 × 600 and 1600 × 900: the tab on screen, the area it shows, the period switch only in
    // Garage, the source-health line in the tab row and whole, and the summary whole.
    private static void CheckViews(AppController controller, Window window, string renders, string suffix, double textScale)
    {
        var tabs = (TabControl)window.FindName("ViewTabs");
        foreach (var size in new[] { new Size(640, 420), new Size(1000, 600), new Size(1600, 900) })
        {
            string name = "views-" + size.Width + "x" + size.Height + suffix;
            foreach (string view in ClaudeViews.Names)
            {
                controller.ShowView(view);
                AppController.Render(window, size, 1, Path.Combine(renders, "shell-" + name + "-" + view.ToLowerInvariant() + ".png"));
                Assert(controller.TabsShown && controller.CurrentView == view && tabs.SelectedIndex == Array.IndexOf(ClaudeViews.Names, view), name + ": " + view + " isn't the tab on screen.");
                bool garage = view == ClaudeViews.Garage;
                var card = (FrameworkElement)window.FindName("ClaudeCard"); var host = (Border)window.FindName("ViewHost");
                Assert(card.Visibility == (garage ? Visibility.Visible : Visibility.Hidden) && host.Visibility == (garage ? Visibility.Collapsed : Visibility.Visible), name + ": " + view + " shows the wrong area.");
                Assert(((StackPanel)window.FindName("PeriodSwitch")).Visibility == (garage ? Visibility.Visible : Visibility.Collapsed), name + ": the period switch shows outside Garage, or not in it.");
                if (!garage)
                {
                    var shown = controller.View(view);
                    Assert(shown.Visible && host.Child != null && host.Tag as string == view, name + ": " + view + " wasn't built or isn't Visible.");
                    foreach (string other in ClaudeViews.Names.Where(n => n != view && n != ClaudeViews.Garage)) Assert(!controller.View(other).Visible, name + ": " + other + " is Visible behind " + view + ".");
                    if (shown is PlaceholderView) Assert(Texts(host).Any(t => t.Text == "Coming in " + ClaudeViews.Coming(view) + "."), name + ": " + view + "'s placeholder doesn't say when it comes.");
                }
                var line = (FrameworkElement)window.FindName("SourceLine");
                Assert(controller.SourcesInTabRow && line.Parent == window.FindName("TabSources"), name + ": the source-health line isn't in the tab row.");
                Assert(((TextBlock)window.FindName("SourceSample")).Visibility == Visibility.Visible, name + ": the tab row doesn't say Sample.");
                SourcesWhole(window, controller, name + "-" + view);
                var row = (FrameworkElement)window.FindName("TabRow");
                var holder = (FrameworkElement)window.FindName("TabSources");
                Assert(holder.TranslatePoint(new Point(holder.ActualWidth, 0), row).X <= row.ActualWidth + 1, name + ": the source-health line runs past the tab row.");
                if (!controller.SourcesUnderTabs) Assert(tabs.TranslatePoint(new Point(tabs.ActualWidth, 0), row).X <= holder.TranslatePoint(new Point(0, 0), row).X + 1, name + ": the tabs run under the source-health line.");
                var summary = (TextBlock)window.FindName("ClaudeSummary");
                Assert(controller.SummaryForms.Contains(summary.Text) && !Cut(summary), name + ": the summary is cut or not one of its forms: " + summary.Text);
            }
        }
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
                        // Sample is pinned on the source-health line at every size (observability critique, issue 3), and
                        // the header's stamp says when the plan numbers are from, never "LIVE".
                        Assert(((TextBlock)window.FindName("SourceSample")).Visibility == Visibility.Visible && ((TextBlock)window.FindName("SourceSample")).ActualWidth > 0, size.Name + ": the fixture doesn't say it's a sample.");
                        string stamp = ((TextBlock)window.FindName("ClaudeFlag")).Text;
                        Assert(stamp.StartsWith("PLAN · ") && !stamp.Contains("LIVE"), size.Name + ": the plan stamp is \"" + stamp + "\".");
                        SourcesWhole(window, controller, size.Name);
                        // The summary is never cut mid-word, and from 1000 units wide it's the whole summary (on the
                        // header's line, or on its own line under it).
                        Assert(!Cut(summary), size.Name + ": the header's summary is cut: \"" + summary.Text + "\" in " + Math.Round(summary.ActualWidth));
                        if (Math.Round(size.W / size.Scale) >= 1000) Assert(summary.Text == controller.SummaryForms[0], size.Name + ": the header doesn't show the whole summary: " + summary.Text);
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
            // And 360 × 300 at 140% text, where the summary was cut ("1 needs you · 1 your turn · 7…"; Lead's review of
            // v0.50.0).
            foreach (double tall in new[] { 261.0, 300.0 })
            foreach (double scale in new[] { 1.0, 1.4 })
                foreach (bool needs in new[] { false, true })
                {
                    if (tall == 300 && scale == 1.0) continue;
                    string name = "shell-360x" + tall + "-" + Math.Round(scale * 100) + (needs ? "-needs" : "");
                    string folder = Fresh(directory, name);
                    var window = AppController.LoadWindow("ShellWindow");
                    using (var controller = new AppController(window, SettingsFile(folder, new AppSettings { ClaudePeriod = "Week", TextScale = scale })))
                    {
                        controller.UseClaudeSample(needs);
                        AppController.Render(window, new Size(360, tall), 1, Path.Combine(renders, name + ".png"));
                        var card = controller.ClaudeCardView;
                        Assert(card.Current == CardSizing.Design.Small, name + " isn't Small.");
                        Assert(window.FindName("ClaudeSmallMeter") != null, name + ": Small lost its 5-hour meter (header " + (controller.HeaderTwoLines ? 84 : 56) + ").");
                        var summary = (TextBlock)window.FindName("ClaudeSummary");
                        var forms = controller.SummaryForms;
                        Assert(forms.Contains(summary.Text), name + ": the summary isn't the whole one or a short form: " + summary.Text);
                        Assert(!Cut(summary), name + ": the header's summary is cut: \"" + summary.Text + "\" in " + Math.Round(summary.ActualWidth));
                        Assert(summary.TextTrimming == TextTrimming.WordEllipsis, name + ": the summary could be cut mid-word.");
                        Assert(((TextBlock)window.FindName("SourceSample")).Visibility == Visibility.Visible, name + ": Small doesn't say it's a sample.");
                        SourcesWhole(window, controller, name);
                    }
                }
        });

        check("Shell source-health line: each source says ok, waiting, old or off and why, at every size", () => {
            DateTime now = DateTime.UtcNow;
            // The fixture: every source fine.
            var view = ClaudeView.Fixture(now, false);
            var fine = ClaudeHealth.Of(view, now, false, new ClaudeLinks { TelemetrySetUp = true, TelemetryProblem = view.TelemetryWarning });
            Assert(fine.Count == 4 && fine.Select(x => x.Name).SequenceEqual(new[] { "Hooks", "Logs", "Telemetry", "Plan" }) && fine.All(x => x.Level == ClaudeHealthLevel.Ok && x.Long == x.Name), "The sample's sources aren't all fine: " + String.Join(", ", fine.Select(x => x.Long)));
            // Each source's trouble, in words.
            view.LogsFailed = true; view.TelemetryWarning = "Telemetry off: port 43832 in use"; view.Limits.UpdatedAt = now.AddMinutes(-40);
            var bad = ClaudeHealth.Of(view, now, false, new ClaudeLinks { TelemetrySetUp = true, TelemetryProblem = view.TelemetryWarning });
            Assert(bad[1].Level == ClaudeHealthLevel.Failed && bad[1].Long == "Logs unreadable" && bad[1].Tip.StartsWith(ClaudeText.NoLogs), "Unreadable logs: " + bad[1].Long);
            Assert(bad[2].Level == ClaudeHealthLevel.Failed && bad[2].Long == "Telemetry off: port 43832 in use" && bad[2].Short == "Telemetry off", "A busy telemetry port: " + bad[2].Long);
            Assert(bad[3].Level == ClaudeHealthLevel.Old && bad[3].Long == "Plan 40 min old" && bad[3].Warns, "Old plan numbers: " + bad[3].Long);
            view.LogsFailed = false; view.LogsRead = false;
            Assert(ClaudeHealth.Of(view, now, false, new ClaudeLinks { TelemetrySetUp = true, TelemetryProblem = view.TelemetryWarning })[1].Level == ClaudeHealthLevel.Waiting, "Logs still being read don't say so.");
            view.Connected = false; view.Sample = false; view.Limits = null; view.Plan = ClaudePlanState.SignedOut; view.TelemetryWarning = "";
            var off = ClaudeHealth.Of(view, now, false, new ClaudeLinks());
            Assert(off[0].Long == "Hooks off: not connected" && off[2].Long == "Telemetry off: not connected" && off[3].Long == "Plan off: not signed in" && off.All(x => !x.Warns || x.Name == "Logs"), "Off sources: " + String.Join(", ", off.Select(x => x.Long)));
            view.Connected = true;
            Assert(ClaudeHealth.Of(view, now, false, new ClaudeLinks())[2].Long == "Telemetry off: not set up", "Telemetry that isn't sent here doesn't say so.");
            Assert(ClaudeHealth.Of(null, now, false, null).All(x => x.Level == ClaudeHealthLevel.Waiting), "Before the first reading the sources aren't waiting.");
            // Widget Screen (decision 028): holding the telemetry port is neither off nor an error; holding the hooks
            // asks for Reconnect, in Amber.
            var shared = ClaudeHealth.Of(view, now, false, new ClaudeLinks { TelemetrySetUp = true, TelemetryProblem = "Telemetry goes to Widget Screen while it's open", WidgetScreenHasTelemetry = true });
            Assert(shared[2].Long == "Telemetry with Widget Screen" && shared[2].Level == ClaudeHealthLevel.Waiting && !shared[2].Warns, "Telemetry with Widget Screen: " + shared[2].Long + " / " + shared[2].Level);
            var taken = ClaudeHealth.Of(view, now, false, new ClaudeLinks { TelemetrySetUp = true, HooksElsewhere = @"C:\Users\ali\AppData\Local\WidgetScreen\claude" });
            Assert(taken[0].Long == "Hooks with Widget Screen · Reconnect" && taken[0].Warns, "Hooks with Widget Screen: " + taken[0].Long);
            Assert(new[] { ClaudeHealthLevel.Ok, ClaudeHealthLevel.Waiting, ClaudeHealthLevel.Old, ClaudeHealthLevel.Off, ClaudeHealthLevel.Failed }.Select(l => ClaudeHealth.Glyph(l)).Distinct().Count() == 5, "Two levels share a glyph, so colour alone tells them apart.");

            // Loading and unavailable look different: "≈" while counting, "—" and why when the logs can't be read.
            var counting = ClaudeView.Fixture(now, false); counting.LogsRead = false;
            Assert(ClaudeText.Fact(ClaudeFactKinds.Tokens, counting, ClaudePeriod.Today, false, false)[0].StartsWith("≈"), "Tokens being counted don't say ≈.");
            var failed = ClaudeView.Fixture(now, false); failed.LogsFailed = true;
            var tokens = ClaudeText.Fact(ClaudeFactKinds.Tokens, failed, ClaudePeriod.Today, false, false);
            Assert(tokens[0] == "—" && tokens[1].EndsWith(" · NO LOGS") && tokens[2] == ClaudeText.NoLogs, "Unreadable logs look like a count in progress: " + String.Join(" / ", tokens));
            // One meaning for "running": the header counts working sessions as working.
            Assert(!ClaudeText.Summary(ClaudeView.Fixture(now, false), ClaudePeriod.Today, true).Contains("running"), "The header still says running for working sessions.");

            // In the window: the line shows each trouble, Amber for the failed and old ones, at the smallest and a wide size.
            string renders = Renders(directory);
            foreach (var size in new[] { new Size(360, 261), new Size(640, 420), new Size(1000, 600), new Size(1600, 900) })
            {
                string name = "shell-sources-" + size.Width + "x" + size.Height;
                var window = AppController.LoadWindow("ShellWindow");
                using (var controller = new AppController(window, SettingsFile(Fresh(directory, name), new AppSettings { ClaudePeriod = "Today" })))
                {
                    controller.UseClaudeSample(false);
                    var trouble = controller.ClaudeCardView.View;
                    trouble.LogsFailed = true; trouble.TelemetryWarning = "Telemetry off: port 43832 in use"; trouble.Limits.UpdatedAt = DateTime.UtcNow.AddMinutes(-40);
                    controller.ClaudeCardView.Update(trouble);
                    AppController.Render(window, size, 1, Path.Combine(renders, name + ".png"));
                    SourcesWhole(window, controller, name);
                    var logs = (TextBlock)window.FindName("SourceText1");
                    Assert(logs.Visibility == Visibility.Visible && logs.Text.StartsWith("Logs") && ColorOf(logs.Foreground) == ColorOf(window.Resources["Warning"]), name + ": unreadable logs don't show in Amber words: " + logs.Text);
                    Assert(((TextBlock)window.FindName("SourceText2")).Text.StartsWith("Telemetry"), name + ": the telemetry port problem isn't on the line.");
                    if (size.Width >= 1000) Assert(((TextBlock)window.FindName("SourceText2")).Text == "Telemetry off: port 43832 in use" && ((TextBlock)window.FindName("SourceText3")).Text == "Plan 40 min old", name + ": a wide window shortened the reasons.");
                    Assert(window.FindName("ClaudeTelemetryWarning") == null, name + ": the side panel still has its own telemetry warning.");
                }
            }
            // Every source in trouble at once, at the narrowest window with 140% text (Scribe's review of v0.50.0): not
            // connected, logs still counting and not signed in. The line still fits and keeps whole words.
            {
                string name = "shell-sources-360x300-140-off";
                var window = AppController.LoadWindow("ShellWindow");
                using (var controller = new AppController(window, SettingsFile(Fresh(directory, name), new AppSettings { ClaudePeriod = "Today", TextScale = 1.4 })))
                {
                    controller.UseClaudeSample(false);
                    var troubled = controller.ClaudeCardView.View;
                    troubled.Connected = false; troubled.LogsRead = false; troubled.Limits = null; troubled.Plan = ClaudePlanState.SignedOut;
                    controller.ClaudeCardView.Update(troubled);
                    AppController.Render(window, new Size(360, 300), 1, Path.Combine(renders, name + ".png"));
                    Assert(controller.Sources.Count(x => x.Level != ClaudeHealthLevel.Ok) == 4, name + ": the view isn't all trouble: " + String.Join(", ", controller.Sources.Select(x => x.Long)));
                    SourcesWhole(window, controller, name);
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
                // The summary is always live (decision 030): the last 5 hours, whichever period Garage shows.
                Click(window, "ShellPeriod2");
                Assert(controller.ClaudeCardView.Period == ClaudePeriod.Week && ((TextBlock)window.FindName("ClaudeSummary")).Text.Contains("in 5 h"), "The summary followed Garage's period.");
                Click(window, "ShellPeriod0");
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

        check("Shell alerts: Glacier only after 10 minutes, the taskbar dot and one flash for Needs you, Ctrl+1 and Keep on top", () => {
            string renders = Renders(directory);
            string folder = Fresh(directory, "alerts");
            var window = AppController.LoadWindow("ShellWindow");
            using (var controller = new AppController(window, SettingsFile(folder, new AppSettings())))
            {
                // The fixture's Your-turn thread has waited 12 minutes: Glacier. Turns that ended moments ago don't light it.
                controller.UseClaudeSample(false);
                // The card fills in once it's laid out, so each state is rendered before it's checked.
                AppController.Render(window, new Size(1000, 600), 1, Path.Combine(renders, "shell-alerts-1000x600-old-turn.png"));
                var card = controller.ClaudeCardView;
                var view = card.View;
                DateTime now = DateTime.UtcNow;
                Assert(view.Attention(now).Any(a => a.Kind == ClaudeAttention.Turn) && card.Edge == "Turn", "A reply owed for 12 minutes doesn't turn the edge Glacier: " + card.Edge);
                foreach (var turn in view.Sessions.Where(x => x.YourTurn)) turn.Since = now.AddMinutes(-3);
                card.Update(view);
                foreach (var size in new[] { new Size(360, 300), new Size(640, 420), new Size(1000, 600), new Size(1600, 900) })
                {
                    AppController.Render(window, size, 1, Path.Combine(renders, "shell-alerts-" + size.Width + "x" + size.Height + "-recent-turn.png"));
                    Assert(card.Edge == null && ColorOf(((Border)window.FindName("WindowEdge")).BorderBrush) == Colors.Transparent, size + ": a turn that ended 3 minutes ago lights the edge: " + card.Edge);
                }
                foreach (var turn in view.Sessions.Where(x => x.YourTurn)) turn.Since = now.AddMinutes(-(ClaudeAttention.TurnWaits.TotalMinutes + 1));
                card.Update(view);
                AppController.Render(window, new Size(1000, 600), 1, Path.Combine(renders, "shell-alerts-1000x600-turn-11min.png"));
                Assert(card.Edge == "Turn", "The edge didn't turn Glacier at 10 minutes.");
                // No dot while nothing needs you.
                Assert(window.TaskbarItemInfo == null || window.TaskbarItemInfo.Overlay == null, "The taskbar shows a dot with nothing needing you.");
                Assert(!controller.FlashForNeeds(new List<ClaudeAlert> { new ClaudeAlert { Kind = "done" } }) && controller.TaskbarFlashes == 0, "A finished turn flashed the taskbar.");

                controller.UseClaudeSample(true, true);
                view = card.View; now = DateTime.UtcNow;
                int needing = view.NeedsYou;
                Assert(needing > 0 && window.TaskbarItemInfo != null && window.TaskbarItemInfo.Overlay is DrawingImage, "The taskbar has no dot while " + needing + " need you.");
                Assert(window.TaskbarItemInfo.Description == (needing == 1 ? "1 session needs you" : needing + " sessions need you"), "The taskbar's words are wrong: " + window.TaskbarItemInfo.Description);
                var badge = (DrawingImage)AppController.Badge(12, Colors.Orange, Colors.Black);
                Assert(badge.IsFrozen && Math.Abs(badge.Width - 16) < 0.5 && Math.Abs(badge.Height - 16) < 0.5, "The dot isn't a 16-unit circle: " + badge.Width + " × " + badge.Height);
                var dot = new Window { Content = new Border { Background = Brushes.White, Child = new System.Windows.Controls.Image { Source = window.TaskbarItemInfo.Overlay, Width = 64, Height = 64 } } };
                AppController.Render(dot, new Size(64, 64), 1, Path.Combine(renders, "shell-alerts-taskbar-dot.png"));

                // One flash for each view that brings a new Needs you (the window isn't in front in the checks).
                var need = new List<ClaudeAlert> { new ClaudeAlert { Kind = "need" }, new ClaudeAlert { Kind = "need" } };
                Assert(controller.FlashForNeeds(need) && controller.TaskbarFlashes == 1, "A new Needs you didn't flash the taskbar once.");

                // Ctrl+1: the attention queue's first, Needs you waiting longest.
                var queue = view.Attention(now);
                var first = controller.WaitingLongest(now);
                Assert(first != null && first.Kind == ClaudeAttention.Need && first.Thread.Id == queue[0].Thread.Id, "Ctrl+1 doesn't open the queue's first.");
                var longest = view.Sessions.Where(s => s.Running && s.State == ClaudeState.NeedsYou).OrderBy(s => s.Since).ThenBy(s => s.Id, StringComparer.Ordinal).First();
                Assert(first.Thread.Id == longest.Id, "Ctrl+1 doesn't open the Needs you that has waited longest: " + first.Thread.Id + ", not " + longest.Id);
                Assert(controller.OpenWaitingLongest() == first.Thread.Id, "Ctrl+1 opened another session.");
                foreach (var size in new[] { new Size(360, 300), new Size(640, 420), new Size(1000, 600), new Size(1600, 900) })
                    AppController.Render(window, size, 1, Path.Combine(renders, "shell-alerts-" + size.Width + "x" + size.Height + "-needs.png"));

                // Keep on top: off by default, kept by Save, and on the window.
                Assert(!window.Topmost, "The window starts on top.");
                controller.OpenSettings();
                var box = (CheckBox)controller.SettingsWindow.FindName("KeepOnTopChoice");
                Assert(box != null && box.IsChecked == false, "Settings → Window has no Keep on top, or it's ticked.");
                box.IsChecked = true;
                controller.SaveSettings();
                string warning;
                Assert(SettingsStore.Load(Path.Combine(folder, "settings.xml"), out warning).KeepOnTop && window.Topmost, "Keep on top wasn't saved or didn't put the window on top.");
            }
            // Quiet mode: no flash.
            string quietFolder = Fresh(directory, "alerts-quiet");
            var quietWindow = AppController.LoadWindow("ShellWindow");
            using (var controller = new AppController(quietWindow, SettingsFile(quietFolder, new AppSettings { QuietMode = true, KeepOnTop = true })))
            {
                controller.UseClaudeSample(true);
                Assert(!controller.FlashForNeeds(new List<ClaudeAlert> { new ClaudeAlert { Kind = "need" } }) && controller.TaskbarFlashes == 0, "The taskbar flashed in Quiet mode.");
                Assert(quietWindow.TaskbarItemInfo != null && quietWindow.TaskbarItemInfo.Overlay != null, "Quiet mode took the dot away; it only stops the flash.");
                Assert(quietWindow.Topmost, "Keep on top didn't apply at start.");
            }
        });

        check("Shell logs: no logs yet is calm, not a failure, and an unreadable log's words fit the side panel", () => {
            // A fresh install: no projects folder, then an empty one. Neither is a failure.
            string config = Fresh(directory, "logs-none");
            var usage = new ClaudeUsage(config);
            usage.Scan(DateTime.UtcNow);
            Assert(usage.Scanned && !usage.Failed && usage.Empty, "A missing projects folder reads as a failure.");
            Directory.CreateDirectory(Path.Combine(config, "projects", "C--Code-app"));
            usage.Scan(DateTime.UtcNow);
            Assert(!usage.Failed && usage.Empty, "An empty projects folder reads as a failure.");
            File.WriteAllText(Path.Combine(config, "projects", "C--Code-app", "s1.jsonl"), "");
            usage.Scan(DateTime.UtcNow);
            Assert(!usage.Failed && !usage.Empty, "A log doesn't end \"none yet\".");
            DateTime now = DateTime.UtcNow;
            var none = ClaudeView.Fixture(now, false); none.Sample = false; none.LogsEmpty = true;
            var logs = ClaudeHealth.Of(none, now, false, null)[1];
            Assert(logs.Level == ClaudeHealthLevel.Waiting && !logs.Warns && logs.Long == "Logs: none yet" && logs.Short == "Logs", "No logs yet: " + logs.Level + " " + logs.Long);

            // Unreadable logs at 1280 wide: the side panel's words are whole.
            string folder = Fresh(directory, "logs-unreadable");
            var window = AppController.LoadWindow("ShellWindow");
            using (var controller = new AppController(window, SettingsFile(folder, new AppSettings { ClaudeFacts = new[] { ClaudeFactKinds.Tokens, ClaudeFactKinds.Tools, ClaudeFactKinds.Lines, ClaudeFactKinds.Cache } })))
            {
                controller.UseClaudeSample(false);
                var view = controller.ClaudeCardView.View; view.LogsFailed = true;
                controller.ClaudeCardView.Update(view);
                AppController.Render(window, new Size(1280, 760), 1, Path.Combine(Renders(directory), "shell-logs-unreadable-1280x760.png"));
                Assert(window.FindName("ClaudeSide") is FrameworkElement, "No side panel at 1280 × 760 (" + controller.ClaudeCardView.Current + ").");
                var keys = Enumerable.Range(0, 4).Select(i => window.FindName("ClaudeFact" + i) as Panel).Where(f => f != null).Select(f => f.Children.OfType<TextBlock>().Last()).ToList();
                var words = keys.Where(t => t.Text.Contains("NO LOGS")).ToList();
                Assert(words.Count >= 2, "The side panel doesn't say NO LOGS for the tokens and tools: " + String.Join(" / ", keys.Select(t => t.Text)));
                foreach (var t in words) Assert(!Cut(t), "The side panel cuts \"" + t.Text + "\".");
            }
        });

        check("Shell views: tabs from 640 wide, Garage keeps the card and its periods, the source-health line moves, Ctrl+1 to 6 and Ctrl+G", () => {
            string renders = Renders(directory);
            string folder = Fresh(directory, "views");
            var window = AppController.LoadWindow("ShellWindow");
            using (var controller = new AppController(window, SettingsFile(folder, new AppSettings())))
            {
                controller.UseClaudeSample(true, true);
                var tabs = (TabControl)window.FindName("ViewTabs");
                Assert(tabs.Items.Count == 6 && tabs.Items.OfType<TabItem>().Select(t => (string)t.Header).SequenceEqual(ClaudeViews.Names), "The tabs aren't Live, Garage, Limits, History, Reliability, Sources.");
                // Live opens first once its view is in the build; until then Garage.
                Assert(controller.ChosenView == (ClaudeViews.Create(ClaudeViews.Live) is PlaceholderView ? ClaudeViews.Garage : ClaudeViews.Live), "The first view is " + controller.ChosenView + ".");

                // Below 640 wide: no tabs, the card, the period switch in the header and the source line under the card.
                controller.ShowView(ClaudeViews.Limits);
                AppController.Render(window, new Size(600, 420), 1, Path.Combine(renders, "shell-views-600x420.png"));
                Assert(!controller.TabsShown && ((Grid)window.FindName("TabRow")).Visibility == Visibility.Collapsed && controller.CurrentView == ClaudeViews.Garage, "A 600-wide window shows tabs or another view.");
                Assert(((FrameworkElement)window.FindName("ClaudeCard")).Visibility == Visibility.Visible && ((FrameworkElement)window.FindName("ViewHost")).Visibility == Visibility.Collapsed, "A 600-wide window hides the card.");
                Assert(((FrameworkElement)window.FindName("SourceLine")).Parent == window.FindName("DashboardContent") && !controller.SourcesInTabRow, "Below 640 the source-health line left the card's foot.");
                SourcesWhole(window, controller, "views-600x420");

                // From 640 wide: the tabs, the chosen view, and the source-health line at the tab row's end (one control).
                CheckViews(controller, window, renders, "", 1.0);
                // The header is always live and says when the 5-hour limit comes at this pace; an Assertive live region.
                var sample = controller.ClaudeCardView.View;
                Assert(!Double.IsNaN(sample.LimitEtaMinutes) && controller.SummaryForms[0].Contains(" · ≈ limit ") && controller.SummaryForms[0].Contains("in 5 h"), "The header doesn't say ≈ limit: " + controller.SummaryForms[0]);
                Assert(controller.HeaderAnnounces, "The header summary isn't an Assertive live region.");
                Assert(AppController.LimitText(new ClaudeView(), DateTime.UtcNow, false) == "", "≈ limit shows with no pace to go by.");

                // The views' contract: only the one on screen is Visible, and none while minimised; unknown names do nothing.
                controller.ShowView(ClaudeViews.History);
                Assert(controller.View(ClaudeViews.History).Visible && !controller.View(ClaudeViews.Limits).Visible, "History isn't the one Visible view.");
                controller.ShowView("Pit lane");
                Assert(controller.ChosenView == ClaudeViews.History, "An unknown view name changed the view.");
                Assert(ClaudeViews.Create("Garage") == null && ClaudeViews.Create("Nope") == null && ClaudeViews.Create("limits").Name == ClaudeViews.Limits, "ClaudeViews.Create doesn't follow its contract.");
                Assert(ClaudeViews.Coming(ClaudeViews.Live) == "v0.56.0" && ClaudeViews.Coming(ClaudeViews.Limits) == "v0.57.0" && ClaudeViews.Coming(ClaudeViews.Sources) == "v0.58.0", "The placeholders name the wrong versions.");

                // Ctrl+G is the session that waited longest (Ctrl+1 until v0.55.0); Ctrl+1 to Ctrl+6 the views.
                Assert(controller.OpenWaitingLongest() == controller.WaitingLongest(DateTime.UtcNow).Thread.Id, "Ctrl+G's target moved.");

                // Views' actions: each known name answers (here the fixture's "nothing to change"), an unknown one is an error.
                foreach (string action in AppController.Actions) Assert(controller.Act(action) != null, action + " ran against the sample.");
                Assert(controller.Act("FormatDisk").StartsWith("Unknown action") && controller.Act(null).StartsWith("Unknown action"), "An unknown action didn't say so.");
            }
            // The same at 140% text.
            string bigFolder = Fresh(directory, "views-140");
            var bigWindow = AppController.LoadWindow("ShellWindow");
            using (var controller = new AppController(bigWindow, SettingsFile(bigFolder, new AppSettings { TextScale = 1.4 })))
            {
                controller.UseClaudeSample(true, true);
                CheckViews(controller, bigWindow, renders, "-140", 1.4);
            }
            // A live (not sample) controller: an unknown action is still an error, and Disconnect refuses while not connected.
            string liveFolder = Fresh(directory, "views-live");
            var liveWindow = AppController.LoadWindow("ShellWindow");
            using (var controller = new AppController(liveWindow, SettingsFile(liveFolder, new AppSettings())))
            {
                Assert(controller.Act("Nope") == "Unknown action: Nope.", "An unknown action crashed or ran.");
                Assert(controller.Act("Disconnect") == "Claude Code isn't connected.", "Disconnect ran without a connection.");
                Assert(controller.Act("ShowArchived") == null && controller.CurrentSettings.ClaudeShowArchived && controller.Act("HideArchived") == null && !controller.CurrentSettings.ClaudeShowArchived, "Show and hide archived didn't change the setting.");
            }
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

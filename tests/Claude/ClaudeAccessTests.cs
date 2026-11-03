using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using ClaudeDashboard;

// Keyboard and low vision (critique issue 4, v0.51.0): every thread row, car and garage is a button the keyboard reaches,
// ended rows have names and keep their contrast, every word on the card follows the text size, the light palette's
// Amber and Glacier read on Raised, and the header announces a new Needs you. Renders at four sizes and two text sizes
// go to artifacts\renders for review. Everything runs on the labelled sample in throwaway folders.
internal static class ClaudeAccessTests
{
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }
    private static string Fresh(string directory, string name)
    {
        string path = Path.Combine(directory, "claude-access", name);
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
        return Path.Combine(path, "settings.xml");
    }
    private static void Lay(Window window)
    {
        var dashboard = (FrameworkElement)window.FindName("Dashboard");
        for (int i = 0; i < 3; i++) { dashboard.Measure(new Size(1920, 720)); dashboard.Arrange(new Rect(0, 0, 1920, 720)); dashboard.UpdateLayout(); }
    }
    // Opens the dashboard on the sample at a card size, with a text size and the plain list or not.
    private static void With(string directory, string name, Size card, double scale, bool plain, bool needs, Action<Window, AppController> body)
    {
        string path = Fresh(directory, name);
        SettingsStore.Save(path, new AppSettings { TextScale = scale, ClaudePeriod = "Week", ClaudePlain = plain });
        var window = AppController.LoadWindow("ShellWindow");
        try
        {
            using (var controller = new AppController(window, path))
            {
                var sized = (FrameworkElement)window.FindName("ClaudeCard"); sized.Width = card.Width; sized.Height = card.Height;
                controller.UseClaudeSample(needs); Lay(window);
                body(window, controller);
            }
        }
        finally { window.Close(); }
    }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Claude's thread rows, cars and garages are buttons the keyboard reaches, with names", () => {
            foreach (bool plain in new[] { false, true })
                foreach (var size in new[] { new Size(1216, 576), new Size(560, 400), new Size(320, 260) })
                {
                    if (plain && size.Width < 640) continue;
                    With(directory, "keys-" + plain + size.Width, size, 1, plain, true, (window, controller) => {
                        var card = (FrameworkElement)window.FindName("ClaudeCard");
                        var view = controller.ClaudeCardView;
                        var buttons = Descendants(card).OfType<Button>().ToList();
                        string design = plain ? "plain" : view.Current.ToString();
                        Assert(buttons.Count > 1, "Only " + buttons.Count + " button on the " + design + " card.");
                        var rows = Enumerable.Range(0, 8).SelectMany(g => Enumerable.Range(0, 8).Select(t => window.FindName((plain ? "ClaudePlainThread" : "ClaudeThreadRow") + g + "_" + t))).OfType<FrameworkElement>().ToList();
                        if (plain || view.Current == CardSizing.Design.Large) Assert(rows.Count > 0 && rows.All(r => r is Button), "A " + design + " thread row isn't a button.");
                        foreach (var button in buttons)
                        {
                            Assert(button.Focusable && button.IsTabStop && System.Windows.Input.KeyboardNavigation.GetIsTabStop(button), "A " + design + " button can't take keyboard focus: " + AutomationProperties.GetName(button));
                            Assert(!String.IsNullOrWhiteSpace(AutomationProperties.GetName(button)), "A " + design + " button has no name.");
                            Assert(!AutomationProperties.GetName(button).Contains("Tap"), "A " + design + " button still says Tap: " + AutomationProperties.GetName(button));
                        }
                        // Every garage is a button whose click folds it, as Enter and Space do.
                        foreach (var garage in view.Garages().Select(g => g.Key)) Assert(garage is Button, "A " + design + " garage isn't a button.");
                        if (!plain && view.Current != CardSizing.Design.Small)
                        {
                            var first = (Button)view.Garages()[0].Key; string folder = view.View.Groups(view.Period)[0].Folder;
                            first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            Assert(!view.Toggle(folder), "Pressing a " + design + " garage didn't fold it.");
                        }
                        // Ended and idle threads have names that say so.
                        var ended = view.View.Threads.FirstOrDefault(t => !t.Running);
                        if (ended != null && (plain || view.Current == CardSizing.Design.Large))
                            Assert(rows.Any(r => (AutomationProperties.GetName(r) ?? "").Contains("No window to show")), "An ended " + design + " row has no name that says it has no window.");
                    });
                }
            Assert(!ClaudeText.ThreadTip(new ClaudeSession { Running = true, CanFocus = true, Title = "x" }, ClaudePeriod.Today, DateTime.UtcNow, true, true).Contains("Tap"), "A thread's tip still says Tap.");
        });

        check("Claude dims ended and parked rows by colour, never below the palette's contrast", () => {
            With(directory, "dim", new Size(1216, 576), 1, false, false, (window, controller) => {
                var view = controller.ClaudeCardView;
                var groups = view.View.Groups(view.Period);
                int ended = 0;
                for (int g = 0; g < groups.Count; g++)
                    for (int t = 0; t < groups[g].Threads.Count; t++)
                    {
                        var row = window.FindName("ClaudeThreadRow" + g + "_" + t) as FrameworkElement;
                        var title = window.FindName("ClaudeThreadTitle" + g + "_" + t) as TextBlock;
                        if (row == null || title == null) continue;
                        Assert(row.Opacity == 1, "A thread row is faded with opacity " + row.Opacity + ".");
                        if (groups[g].Threads[t].Running) continue;
                        ended++;
                        var muted = title.TryFindResource("Muted") as SolidColorBrush; var shown = title.Foreground as SolidColorBrush;
                        Assert(muted != null && shown != null && shown.Color == muted.Color && title.Opacity == 1, "An ended row's title isn't Muted at full opacity.");
                    }
                Assert(ended > 0, "The sample showed no ended row to check.");
            });
        });

        check("Claude's words follow the text size everywhere on the card", () => {
            foreach (bool plain in new[] { false, true })
                foreach (var size in new[] { new Size(1552, 804), new Size(592, 324), new Size(312, 204) })
                {
                    if (plain && size.Width < 640) continue;
                    var smallest = new Dictionary<double, double>();
                    foreach (double scale in new[] { 1.0, 1.4 })
                        With(directory, "scale-" + plain + size.Width + "-" + scale, size, scale, plain, true, (window, controller) => {
                            var card = (FrameworkElement)window.FindName("ClaudeCard");
                            var sizes = Descendants(card).OfType<TextBlock>().Where(t => !String.IsNullOrEmpty(t.Text)).Select(t => t.FontSize).ToList();
                            Assert(sizes.Count > 0, "No words on the card at " + size + ".");
                            smallest[scale] = sizes.Min();
                        });
                    // The smallest words at 140% are 140% of the smallest at 100% (rounded), so no literal size is left.
                    Assert(smallest[1.4] >= Math.Round(smallest[1.0] * 1.4) - 0.5, "Some words ignore the text size at " + size + (plain ? " (plain)" : "") + ": smallest " + smallest[1.0] + " at 100%, " + smallest[1.4] + " at 140%.");
                }
        });

        check("Claude's palettes keep Amber, Glacier and Muted at 4.5:1 on Raised and the surface", () => {
            foreach (string theme in Palettes.Themes)
            {
                var c = Palettes.Colors(theme); Func<string, string> key = k => c[Array.IndexOf(Palettes.Keys, k)];
                foreach (string fore in new[] { "Warning", "Turn", "Muted", "Text" })
                    foreach (string back in new[] { "Raised", "Surface", "Page" })
                        Assert(Palettes.Contrast(key(fore), key(back)) >= 4.5, theme + ": " + fore + " on " + back + " is " + Math.Round(Palettes.Contrast(key(fore), key(back)), 2) + ":1.");
            }
        });

        check("Claude's header summary announces a new Needs you", () => {
            With(directory, "announce", new Size(1216, 576), 1, false, false, (window, controller) => {
                Assert(controller.HeaderAnnounces, "The header summary isn't an assertive live region.");
                Assert(controller.AnnouncedNeeds == 0, "The calm sample counted " + controller.AnnouncedNeeds + " needing you.");
                controller.UseClaudeSample(true); Lay(window);
                Assert(controller.AnnouncedNeeds > 0, "A session needing you wasn't noted for announcing.");
            });
        });

        check("Claude renders keyboard and low-vision sizes for review (artifacts\\renders)", () => {
            string renders = Path.GetFullPath(Path.Combine(directory, "..", "renders")); Directory.CreateDirectory(renders);
            foreach (var size in new[] { new Size(360, 300), new Size(640, 420), new Size(1000, 600), new Size(1600, 900) })
                foreach (double scale in new[] { 1.0, 1.4 })
                {
                    string name = "claude-access-" + size.Width + "x" + size.Height + "-" + Math.Round(scale * 100);
                    string path = Fresh(directory, name);
                    SettingsStore.Save(path, new AppSettings { TextScale = scale, ClaudePeriod = "Today" });
                    var window = AppController.LoadWindow("ShellWindow");
                    try
                    {
                        using (var controller = new AppController(window, path))
                        {
                            controller.UseClaudeSample(true);
                            string png = Path.Combine(renders, name + ".png");
                            AppController.Render(window, size, 1, png);
                            Assert(File.Exists(png) && new FileInfo(png).Length > 0, name + " wasn't rendered.");
                        }
                    }
                    finally { window.Close(); }
                }
        });
    }
}

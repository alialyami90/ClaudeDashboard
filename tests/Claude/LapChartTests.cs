using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ClaudeDashboard;

// The Live tab (package C, v0.56.0): the lap chart's lanes in urgency order with spoken names, the 5-hour line and
// its wall, the feed, and the selected-session panel beside the chart from 1000 wide or as a drawer below. On samples.
internal static class LapChartTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static string Fresh(string directory, string name)
    {
        string path = Path.Combine(directory, "claude-lapchart", name);
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
        return path;
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

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Claude Live: lanes in urgency order, spoken names, the 5-hour line and the feed's words (v0.56.0)", () => {
            var view = ClaudeView.Fixture(Now, true, true);
            var axis = LapAxis.For(view, Now);
            Assert(axis.From == Now - LapAxis.Past && axis.To == view.Limits.FiveHour.ResetsAt.Value, "The axis doesn't run from 100 minutes ago to the reset.");
            Assert(axis.Wall.HasValue && axis.Wall.Value == view.LimitAtPace.LimitAt.Value, "The crowd's wall isn't at ≈ limit.");
            var lanes = LiveView.Lanes(view, axis);
            Assert(lanes.Count > 0 && lanes[0].Lane == ClaudeLaneState.NeedsYou && lanes[0].Id == "sample-1", "Needs you isn't the first lane: " + (lanes.Count > 0 ? lanes[0].Id : "none"));
            for (int i = 1; i < lanes.Count; i++) Assert((int)lanes[i].Lane >= (int)lanes[i - 1].Lane, "The lanes aren't in urgency order at " + lanes[i].Id);
            Assert(lanes.All(t => t.Running || view.Trails.ContainsKey(t.Id)), "A lane has no trail and isn't running.");
            var retrying = lanes.First(t => t.Id == "crowd-10");
            Assert(LiveView.StateWords(retrying, Now, false) == "Retrying · HTTP 529 · attempt 2", "Retrying's words: " + LiveView.StateWords(retrying, Now, false));
            var needs = lanes[0];
            string spoken = LiveView.LaneName(needs, Now);
            Assert(spoken == "Fix the login timeout, needs you " + (int)(Now - needs.LaneSince).TotalMinutes + " minutes, api-server", "The lane's name: " + spoken);
            Assert(LiveView.LimitWords(view, Now, false).StartsWith("72% · ≈ limit "), "The 5-hour line: " + LiveView.LimitWords(view, Now, false));
            var calm = ClaudeView.Fixture(Now, false, false);
            Assert(LiveView.LimitWords(calm, Now, false).StartsWith("23% · ≈ ") && LiveView.LimitWords(calm, Now, false).EndsWith("% at reset") && !LapAxis.For(calm, Now).Wall.HasValue, "The calm sample's line or wall: " + LiveView.LimitWords(calm, Now, false));
            calm.LimitAtPace = new ClaudeLimitProjection { Why = "Only 2 plan readings so far: ≈ limit needs 3" };
            Assert(LiveView.LimitWords(calm, Now, false) == "23% · Only 2 plan readings so far: ≈ limit needs 3" && !LapAxis.For(calm, Now).Wall.HasValue, "No projection doesn't say why, or still draws a wall.");
            Assert(LiveView.AverageTip(view, false).Contains("today's average") && LiveView.AverageTip(view, false).Contains("Today's tokens an hour, converted"), "The tip doesn't say today's average is converted from tokens.");
            var line = LiveView.FeedLine(new ClaudeFeedItem { At = Now, Kind = ClaudeFeedItem.ToolFailed, Detail = ClaudeView.SampleLongTool, Title = "Push token refresh" }, true);
            Assert(line.EndsWith("Push token refresh · " + ClaudeText.ToolName(ClaudeView.SampleLongTool) + " failed") && line.Contains("…"), "A feed line: " + line);
        });

        check("Claude Live: the window's Live tab at 640, 1000 and 1600 wide, 100% and 140% (v0.56.0)", () => {
            int seen = 0;
            foreach (double scale in new[] { 1.0, 1.4 })
                foreach (var size in new[] { new Size(640, 420), new Size(1000, 600), new Size(1600, 900) })
                {
                    string name = "live-" + (int)(scale * 100) + "-" + size.Width + "x" + size.Height;
                    string path = Path.Combine(Fresh(directory, name), "settings.xml");
                    SettingsStore.Save(path, new AppSettings { TextScale = scale });
                    var window = AppController.LoadWindow("ShellWindow");
                    try
                    {
                        using (var controller = new AppController(window, path))
                        {
                            controller.UseClaudeSample(true, true);
                            controller.ShowView(ClaudeViews.Live);
                            string png = Path.Combine(Path.GetFullPath(Path.Combine(directory, "..", "renders")), "claude-" + name + ".png");
                            Directory.CreateDirectory(Path.GetDirectoryName(png));
                            AppController.Render(window, size, 1, png);
                            AppController.Render(window, size, 1, png);
                            var live = controller.View(ClaudeViews.Live) as LiveView;
                            Assert(live != null && live.Visible && controller.CurrentView == ClaudeViews.Live, name + ": Live isn't the view on screen.");
                            seen++;
                            var host = (Border)window.FindName("ViewHost");
                            var order = live.LaneOrder;
                            Assert(order.Count > 0 && order[0] == "sample-1", name + ": Needs you isn't the first lane.");
                            var first = live.LaneButton("sample-1");
                            Assert(first != null && AutomationProperties.GetName(first).StartsWith("Fix the login timeout, needs you ") && AutomationProperties.GetName(first).EndsWith(", api-server"), name + ": the first lane's name: " + (first == null ? "none" : AutomationProperties.GetName(first)));
                            // Nothing that needs you sits below the fold: the first lane is inside the view.
                            var top = first.TranslatePoint(new Point(0, first.ActualHeight), host);
                            Assert(top.Y <= host.ActualHeight + 1, name + ": the Needs you lane is below the fold.");
                            var texts = Texts(host).Select(t => t.Text).ToList();
                            Assert(texts.Contains("What just happened") && texts.Contains("5-hour limit") && texts.Contains("Sample"), name + ": the feed, the 5-hour line or Sample is missing.");
                            Assert(texts.Any(t => t.StartsWith("72% · ≈ limit ")), name + ": the 5-hour line doesn't say ≈ limit.");
                            Assert(live.SidePanel == (size.Width >= LiveView.SideFrom), name + ": the panel's place is wrong.");
                            if (live.SidePanel) Assert(live.PanelOpen && texts.Contains("Last sign of life") && texts.Contains("Go to it"), name + ": the side panel doesn't show the most urgent session.");
                            else
                            {
                                Assert(!live.PanelOpen, name + ": the drawer is open before a lane is chosen.");
                                first.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                                AppController.Render(window, size, 1, png.Replace(".png", "-drawer.png"));
                                Assert(live.PanelOpen && live.Selected == "sample-1" && Texts(host).Any(t => t.Text == "Go to it"), name + ": a lane's click doesn't open the drawer.");
                            }
                        }
                    }
                    finally { window.Close(); }
                }
            Assert(seen == 6, "Live didn't render at every size: " + seen);
        });
    }
}

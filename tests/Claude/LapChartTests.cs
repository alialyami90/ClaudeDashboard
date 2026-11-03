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
            var full = ClaudeView.Fixture(Now, true, true);
            full.LimitAtPace = new ClaudeLimitProjection { Known = true, Reached = true, AtReset = 100 };
            Assert(LiveView.LimitWords(full, Now, false) == "72% · Limit reached" && !LapAxis.For(full, Now).Wall.HasValue, "A reached limit gives a time or a wall: " + LiveView.LimitWords(full, Now, false));
            Assert(LiveView.AverageTip(view, false).Contains("today's average") && LiveView.AverageTip(view, false).Contains("Today's tokens an hour, converted"), "The tip doesn't say today's average is converted from tokens.");
            var line = LiveView.FeedLine(new ClaudeFeedItem { At = Now, Kind = ClaudeFeedItem.ToolFailed, Detail = ClaudeView.SampleLongTool, Title = "Push token refresh" }, true);
            Assert(line.EndsWith("Push token refresh · " + ClaudeText.ToolName(ClaudeView.SampleLongTool) + " failed") && line.Contains("…"), "A feed line: " + line);
        });

        check("Claude one ≈ limit: the tank, Medium and Small say the engine's ≈ limit, Limit reached at 100% (v0.57.2)", () => {
            var crowded = ClaudeView.Fixture(Now, true, true);
            var w = crowded.Limits.FiveHour;
            double pct = ClaudeText.Percent(w, Now), projection = ClaudePace.Projection(pct, ClaudePace.Elapsed(w, ClaudePace.FiveHours, Now));
            var wall = LapAxis.For(crowded, Now).Wall.Value;
            var forms = ClaudeText.FiveHourPace(crowded, pct, projection, Now, false);
            Assert(forms.Last() == "≈ limit " + ClaudeText.When(wall, Now, false) && forms.All(f => f.StartsWith("≈ limit ")) && ClaudeText.FiveHourPaceWarns(crowded, pct, projection, Now), "The tank isn't at Live's wall: " + String.Join(" | ", forms));
            // Reached, or the window at 100% (Ali's PC with the window forced full): "Limit reached", never "empty".
            var full = ClaudeView.Fixture(Now, true, true);
            full.LimitAtPace = new ClaudeLimitProjection { Known = true, Reached = true, AtReset = 100 };
            Assert(ClaudeText.FiveHourPace(full, 72, projection, Now, false).SequenceEqual(new[] { ClaudeText.LimitReached }) && ClaudeText.FiveHourPaceWarns(full, 72, projection, Now), "A Reached window's tank predicts something.");
            var forced = ClaudeView.Fixture(Now, false, false);
            Assert(ClaudeText.FiveHourPace(forced, 100, 140, Now, false).SequenceEqual(new[] { ClaudeText.LimitReached }), "A window at 100% says something other than Limit reached.");
            Assert(ClaudeText.FiveHourPaceTip(full, 72, projection, Now).Contains("full"), "The Reached tip: " + ClaudeText.FiveHourPaceTip(full, 72, projection, Now));
            // Under 3 readings: no time at all, the window's own pace or nothing, and the tip says why.
            var early = ClaudeView.Fixture(Now, true, true);
            early.LimitAtPace = new ClaudeLimitProjection { Why = "Only 2 plan readings so far: ≈ limit needs 3" };
            Assert(!LapAxis.For(early, Now).Wall.HasValue, "Under 3 readings Live draws a wall.");
            Assert(ClaudeText.FiveHourPace(early, 40, 84.4, Now, false).SequenceEqual(new[] { "≈ 84% by reset", "≈ 84%" }), "Under 3 readings the tank's fallback: " + String.Join(" | ", ClaudeText.FiveHourPace(early, 40, 84.4, Now, false)));
            Assert(ClaudeText.FiveHourPace(early, 40, 130, Now, false).SequenceEqual(new[] { "≈ over by reset" }), "Under 3 readings over 100% gives a time or a percent.");
            // "≈ limit" only ever means the engine's LimitAtPace: the window's own pace never says "limit" (the Lead on #29).
            foreach (double p in new[] { 20.0, 84.4, 100, 130, 400 })
                Assert(ClaudeText.FiveHourPace(early, 40, p, Now, false).All(f => !f.Contains("limit")) && ClaudeText.PaceBy(p).IndexOf("limit", StringComparison.Ordinal) < 0, "The window's own pace says limit at " + p);
            Assert(ClaudeText.FiveHourPaceTip(early, 40, 130, Now).Contains("the window's own pace so far"), "The fallback's tip doesn't say it's the window's own pace.");
            Assert(ClaudeText.FiveHourPace(early, 0, Double.NaN, Now, false).SequenceEqual(new[] { "" }) && ClaudeText.FiveHourPaceTip(early, 40, 84.4, Now).StartsWith("Only 2 plan readings"), "Under 3 readings the tank doesn't say why.");
            // A pace that lasts the window: the engine's percent at reset; one in the past: no time.
            var lasting = ClaudeView.Fixture(Now, false, false);
            Assert(!LapAxis.For(lasting, Now).Wall.HasValue && ClaudeText.FiveHourPace(lasting, 23, 50, Now, false)[0] == "≈ " + Math.Round(Math.Min(100, lasting.LimitAtPace.AtReset)) + "% by reset", "A lasting pace's words: " + String.Join(" | ", ClaudeText.FiveHourPace(lasting, 23, 50, Now, false)));
            var stale = ClaudeView.Fixture(Now, true, true);
            stale.LimitAtPace = new ClaudeLimitProjection { Known = true, LimitAt = Now.AddMinutes(-3), AtReset = 120 };
            Assert(ClaudeText.FiveHourPace(stale, 90, 120, Now, false).SequenceEqual(new[] { "≈ limit now" }), "A ≈ limit in the past shows a time.");
        });

        check("Claude Live: archived lanes and feed lines follow Settings → Claude, cars move only through Frames, never in Quiet (v0.57.2)", () => {
            var view = ClaudeView.Fixture(Now, true, true);
            var axis = LapAxis.For(view, Now);
            var lanes = LiveView.Lanes(view, axis);
            var gone = lanes.First(t => t.Id != "sample-1");
            gone.Pit = PitState.Archived;
            view.Feed.Insert(0, new ClaudeFeedItem { At = Now, Session = gone.Id, Kind = ClaudeFeedItem.Working, Title = "An archived one" });
            view.ShowArchived = false;
            Assert(!LiveView.Lanes(view, axis).Any(t => t.Id == gone.Id) && !LiveView.FeedShown(view).Any(i => i.Session == gone.Id), "An archived thread has a lane or a feed line while archived threads are hidden.");
            view.ShowArchived = true;
            Assert(LiveView.Lanes(view, axis).Any(t => t.Id == gone.Id) && LiveView.FeedShown(view).Any(i => i.Session == gone.Id), "Show archived doesn't bring the lane and feed line back.");
            view.ShowArchived = false; view.HideIdle = true;
            Assert(LiveView.Lanes(view, axis).Count == lanes.Count - 1, "Hide idle took lanes off Live.");
            // Frames: asked for while a car moves, the step moves cars; in Quiet never asked for and every car still.
            foreach (bool quiet in new[] { false, true })
            {
                var asks = new List<Action<double>>();
                var live = new LiveView { Visible = true };
                live.Build(new ClaudeViewContext { Quiet = quiet, Frames = (step, fps) => { asks.Add(step); Assert(step == null || fps > 0, "Frames asked at " + fps + " fps."); } }, new Size(1200, 700));
                var sample = ClaudeView.Fixture(Now, true, true);
                live.Refresh(sample, Now);
                var moving = live.LaneOrder.Select(id => live.LaneCar(id)).Where(c => c != null && c.Moving).ToList();
                if (quiet) Assert(asks.Count == 0 && moving.Count == 0 && !live.FramesAsked, "Quiet asked for frames or left a car moving.");
                else
                {
                    Assert(moving.Count > 0 && asks.Count > 0 && asks.Last() != null && live.FramesAsked, "A moving car didn't ask for frames.");
                    var car = moving[0]; int before = car.Frame; bool moved = false;
                    for (int i = 0; i < 8; i++) { asks.Last()(0.25); if (car.Frame != before) moved = true; }
                    Assert(moved, "The frame step didn't move a car.");
                }
            }
        });

        check("Claude gauge and Garage lanes: no tick through an end label, no times meeting (v0.57.2)", () => {
            // At 140% "100" is about 24 units wide in the gauge's 140 × 84 box: the last tick is left out, the rest stay.
            var boxes = PitGauge.LabelBoxes(11, 19, 15, 24, 19, 15);
            Assert(PitGauge.TickHidden(PitGauge.Tick(10)[0], PitGauge.Tick(10)[1], boxes), "The last tick still crosses \"100\".");
            for (int i = 1; i < 10; i++) Assert(!PitGauge.TickHidden(PitGauge.Tick(i)[0], PitGauge.Tick(i)[1], boxes), "A middle tick was left out: " + i);
            // The lap chart's times: none meet, now goes first.
            var view = ClaudeView.Fixture(Now, true, true);
            var ticks = new LapTicks { Axis = LapAxis.For(view, Now), Reset = view.Limits.FiveHour.ResetsAt };
            foreach (double width in new[] { 200.0, 420, 900 })
            {
                var placed = ticks.Placed(width, t => t.Length * 7.0);
                Assert(placed.Count > 0 && placed[0].Item1 == "now", "Now isn't the first time at " + width);
                for (int i = 0; i < placed.Count; i++) for (int j = i + 1; j < placed.Count; j++)
                    Assert(placed[i].Item3 + LapTicks.Gap <= placed[j].Item2 || placed[j].Item3 + LapTicks.Gap <= placed[i].Item2, "Two times meet at " + width + ": " + placed[i].Item1 + ", " + placed[j].Item1);
                if (width >= 900) Assert(placed.Any(p => p.Item1.StartsWith("reset ")) && placed.Count >= 3, "At 900 the reset or the hours are missing: " + String.Join(" | ", placed.Select(p => p.Item1)));
            }
        });

        check("Claude Live: the window's Live tab at 640, 1000 and 1600 wide, 100% and 140% (v0.56.0)", () => {
            int seen = 0;
            foreach (double scale in new[] { 1.0, 1.4 })
                foreach (var size in new[] { new Size(640, 420), new Size(800, 500), new Size(1000, 600), new Size(1600, 900) })
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
                            // v0.57.2: the times under the curve, the key, the moving cars' frames.
                            var placed = live.Ticks.Placed();
                            Assert(placed.Count > 0 && placed[0].Item1 == "now", name + ": the times under the curve are missing.");
                            for (int i = 0; i < placed.Count; i++) for (int j = i + 1; j < placed.Count; j++)
                                Assert(placed[i].Item3 <= placed[j].Item2 || placed[j].Item3 <= placed[i].Item2, name + ": two times meet.");
                            Assert(live.TrailKey.Shown >= 2 && AutomationProperties.GetName(live.TrailKey).StartsWith("Key: Working"), name + ": the trails' key shows " + live.TrailKey.Shown);
                            Assert(live.FramesAsked, name + ": the moving cars didn't ask for frames.");
                            if (live.SidePanel) Assert(live.PanelOpen && texts.Contains("Last sign of life") && texts.Contains("Go to it"), name + ": the side panel doesn't show the most urgent session.");
                            else
                            {
                                Assert(!live.PanelOpen, name + ": the drawer is open before a lane is chosen.");
                                first.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                                AppController.Render(window, size, 1, png.Replace(".png", "-drawer.png"));
                                Assert(live.PanelOpen && live.Selected == "sample-1" && Texts(host).Any(t => t.Text == "Go to it"), name + ": a lane's click doesn't open the drawer.");
                            }
                            // v0.57.2: no number in the panel is cut, and its scroll area ends on a whole line.
                            var panelTexts = Texts(live.PanelScroll);
                            Assert(panelTexts.Where(t => t.Text.Contains("typical") || t.Text.Contains(" tokens · ")).All(t => t.TextWrapping == TextWrapping.Wrap && t.TextTrimming == TextTrimming.None), name + ": a panel number can be cut.");
                            var scroll = live.PanelScroll;
                            if (scroll.ExtentHeight > scroll.ViewportHeight + 1)
                            {
                                var body = (FrameworkElement)scroll.Content;
                                bool whole = body is Panel && ((Panel)body).Children.OfType<FrameworkElement>().Any(c => Math.Abs(c.TranslatePoint(new Point(0, c.ActualHeight + c.Margin.Bottom), body).Y - scroll.ViewportHeight) <= 1.5);
                                Assert(whole, name + ": the panel's last line is cut at the scroll edge (" + scroll.ViewportHeight + " of " + scroll.ExtentHeight + ").");
                            }
                            // v0.57.2: the Garage's lanes axis, where its side panel shows: the times never meet.
                            if (size.Width >= 1600)
                            {
                                controller.ShowView(ClaudeViews.Garage);
                                AppController.Render(window, size, 1, png.Replace(".png", "-garage.png"));
                                var shownTimes = new[] { "ClaudeLanesStart", "ClaudeLanesMiddle", "ClaudeLanesNow" }.Select(n => window.FindName(n) as TextBlock).Where(t => t != null && t.Visibility == Visibility.Visible && t.ActualWidth > 0).ToList();
                                Assert(shownTimes.Count >= 1, name + ": the Garage lanes have no times.");
                                var spans = shownTimes.Select(t => { var at = t.TranslatePoint(new Point(0, 0), (UIElement)t.Parent); return Tuple.Create(at.X, at.X + t.ActualWidth); }).OrderBy(r => r.Item1).ToList();
                                for (int i = 1; i < spans.Count; i++) Assert(spans[i - 1].Item2 <= spans[i].Item1, name + ": the Garage lanes' times meet.");
                            }
                        }
                    }
                    finally { window.Close(); }
                }
            Assert(seen == 8, "Live didn't render at every size: " + seen);
        });
    }
}

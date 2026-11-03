using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClaudeDashboard;

// The lap chart's package D (decision 030): the Limits and History tabs. Everything uses the labelled fixture or made-up
// days with fixed times; no file is read and nothing on this PC is touched.
internal static class LimitsHistoryTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 3, 1, 30, 0, DateTimeKind.Utc);
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
    private static List<string> Texts(DependencyObject root) { return Descendants(root).OfType<TextBlock>().Select(t => t.Text ?? "").ToList(); }

    // Builds a view in a palette at a size and text scale, refreshes it and draws it, as the window would.
    private static Border Show(IClaudeView view, ClaudeView snapshot, double width, double height, double scale, bool sample, Func<DateTime, int, IList<ClaudeDay>> history = null)
    {
        var host = new Border();
        Palettes.Apply(host.Resources, "Midnight", "Lime", scale);
        var context = new ClaudeViewContext { TextScale = scale, Sample = sample };
        if (history != null) context.History = history;
        host.Child = view.Build(context, new Size(width / scale, height / scale));
        view.Visible = true;
        view.Refresh(snapshot, Now);
        host.Measure(new Size(width, height)); host.Arrange(new Rect(0, 0, width, height)); host.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        return host;
    }

    private static List<LapReading> Readings(params double[] minutesAndPercents)
    {
        var list = new List<LapReading>();
        for (int i = 0; i + 1 < minutesAndPercents.Length; i += 2) list.Add(new LapReading(Now.AddMinutes(minutesAndPercents[i]), minutesAndPercents[i + 1]));
        return list;
    }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Lap chart: the Limits and History tabs are their packages' views, not placeholders", () => {
            Assert(ClaudeViews.Create(ClaudeViews.Limits) is LimitsView, "Limits is a placeholder");
            Assert(ClaudeViews.Create(ClaudeViews.History) is HistoryView, "History is a placeholder");
            Assert(new LimitsView().Name == ClaudeViews.Limits && new HistoryView().Name == ClaudeViews.History, "names");
        });

        check("Lap chart: Limits and History draw the sample at every RC size and text scale, saying Sample", () => {
            foreach (var size in new[] { new Size(640, 420), new Size(1000, 600), new Size(1600, 900) })
                foreach (double scale in new[] { 1.0, 1.4 })
                {
                    LapChart.LastError = null;
                    var limits = new LimitsView();
                    var host = Show(limits, ClaudeView.Fixture(Now, true), size.Width, size.Height, scale, true);
                    Assert(LapChart.LastError == null, "Limits chart failed at " + size + " × " + scale + ": " + LapChart.LastError);
                    Assert(Texts(host).Any(t => t.StartsWith("SAMPLE")), "Limits doesn't say Sample at " + size + " × " + scale);
                    Assert(limits.Shown != null && limits.Shown.Known, "Limits has no 5-hour window in the sample");
                    var history = new HistoryView();
                    host = Show(history, ClaudeView.Fixture(Now, false), size.Width, size.Height, scale, true);
                    Assert(LapChart.LastError == null, "History chart failed at " + size + " × " + scale + ": " + LapChart.LastError);
                    Assert(Texts(host).Any(t => t.StartsWith("SAMPLE")), "History doesn't say Sample at " + size + " × " + scale);
                    Assert(history.Shown != null && history.Shown.Missing.Length == 0 && !history.Shown.Empty, "History sample is empty");
                }
        });

        check("Lap chart: with fewer than 3 plan readings Limits shows no projection and says why", () => {
            var view = ClaudeView.Fixture(Now, true);
            view.PlanReadings = new List<ClaudePlanReading> { new ClaudePlanReading { At = Now.AddMinutes(-10), Percent = 80 }, new ClaudePlanReading { At = Now.AddMinutes(-5), Percent = 82 } };
            view.LimitAtPace = new ClaudeLimitProjection { Known = false, Readings = 2, Why = "Only 2 plan readings so far: ≈ limit needs 3" };
            view.LimitAtAverage = new ClaudeLimitProjection { Basis = ClaudeLimitProjection.Average, Known = false, Readings = 2, Why = "Only 2 plan readings so far: ≈ limit needs 3" };
            var m = LimitsModel.From(view, Now, true);
            Assert(m.NoProjection.Contains("needs 3"), "no reason: " + m.NoProjection);
            Assert(Double.IsNaN(m.PaceSlope) && Double.IsNaN(m.AverageSlope) && !m.PaceHits.HasValue && !m.Onset.HasValue, "a projection was made from 2 readings");
            Assert(m.ProjectionLine(false) == m.NoProjection, "the headline doesn't say why: " + m.ProjectionLine(false));
            LapChart.LastError = null;
            var limits = new LimitsView();
            var host = Show(limits, view, 1000, 600, 1, true);
            Assert(LapChart.LastError == null, "chart failed: " + LapChart.LastError);
            Assert(Texts(host).Any(t => t.Contains("needs 3")), "the reason isn't on screen");
        });

        check("Lap chart: Limits takes the engine's projections and marks them ≈", () => {
            var view = ClaudeView.Fixture(Now, true);
            view.Limits.FiveHour.Percent = 80; view.Limits.FiveHour.ResetsAt = Now.AddMinutes(100);
            view.PlanReadings = Enumerable.Range(0, 7).Select(i => new ClaudePlanReading { At = Now.AddMinutes(-30 + i * 5), Percent = 62 + i * 3, ResetsAt = Now.AddMinutes(100) }).ToList();
            view.LimitAtPace = new ClaudeLimitProjection { Known = true, Readings = 7, PerHour = 36, LimitAt = Now.AddMinutes(33), ResetsAt = Now.AddMinutes(100) };
            view.LimitAtAverage = new ClaudeLimitProjection { Basis = ClaudeLimitProjection.Average, Known = true, Readings = 7, PerHour = 6, AtReset = 90, ResetsAt = Now.AddMinutes(100) };
            var m = LimitsModel.From(view, Now, true);
            Assert(m.NoProjection.Length == 0, "no projection: " + m.NoProjection);
            Assert(m.PaceHits == Now.AddMinutes(33) && Math.Abs(m.PaceSlope - 0.6) < 1e-9, "pace not taken from the engine");
            Assert(Math.Abs(m.AverageAtReset - 90) < 1e-9 && !m.AverageHits.HasValue, "average not taken from the engine");
            string line = m.ProjectionLine(false);
            Assert(line.Contains("≈") && line.Contains("before it resets"), "headline: " + line);
            string legend = m.LegendLine(false);
            Assert(legend.Contains("≈ 90%") && legend.Contains("from tokens"), "legend: " + legend);
            Assert(m.WeekPaceLine(false).Contains("≈"), "week pace without ≈: " + m.WeekPaceLine(false));
            Assert(m.Days.Any(d => !Double.IsNaN(d.Percent)) && m.LapWords().Contains("≈"), "laps without ≈: " + m.LapWords());
            Assert(m.Readings.Count == 7, "readings in the window: " + m.Readings.Count);
        });

        check("Lap chart: heavy use's onset is where the readings start climbing at twice the window's pace", () => {
            var slowThenFast = Readings(-120, 5, -100, 8, -80, 11, -60, 14, -40, 17, -30, 27, -20, 37, -10, 47, 0, 57);
            Assert(LimitsModel.OnsetOf(slowThenFast) == Now.AddMinutes(-40), "onset: " + LimitsModel.OnsetOf(slowThenFast));
            Assert(!LimitsModel.OnsetOf(Readings(-30, 10, -20, 20, -10, 30, 0, 40)).HasValue, "steady use has an onset");
            Assert(!LimitsModel.OnsetOf(Readings(-30, 10, -20, 10, -10, 10)).HasValue, "flat use has an onset");
            Assert(!LimitsModel.OnsetOf(Readings(-60, 5, -50, 25, -40, 45, -30, 46, -20, 47, 0, 48)).HasValue, "heavy use that ended has an onset");
            Assert(!LimitsModel.OnsetOf(Readings(-10, 5, 0, 20)).HasValue, "two readings have an onset");
        });

        check("Lap chart: weekly windows name extra usage and the spend limit honestly", () => {
            var view = ClaudeView.Fixture(Now, false);
            view.Limits.ExtraUsage = null; view.Limits.Spend = null;
            var m = LimitsModel.From(view, Now, true);
            Assert(m.Windows.Any(r => r.Name == "All models" && !Double.IsNaN(r.Percent)), "no all-models row");
            Assert(m.Windows.Any(r => r.Name == "Opus" && Math.Abs(r.Percent - 18) < 0.01), "no Opus row");
            Assert(m.Windows.Any(r => r.Name == "Extra usage" && r.Value == "Not reported"), "extra usage");
            Assert(m.Windows.Any(r => r.Name == "Spend limit" && r.Value == "None reported (gateways only)"), "spend");
            view.Limits.ExtraUsage = new ClaudeExtraUsage { Enabled = false };
            view.Limits.Spend = new ClaudeSpendLimit { Percent = 85, UsedUsd = 85, LimitUsd = 100 };
            m = LimitsModel.From(view, Now, true);
            Assert(m.Windows.Any(r => r.Name == "Extra usage" && r.Value == "Off on this plan"), "extra usage off");
            Assert(m.Windows.Any(r => r.Name == "Spend limit" && r.Warns && r.Value.StartsWith("85%")), "spend at 85% doesn't warn");
        });

        check("Lap chart: Limits says why when there is no plan reading", () => {
            var view = ClaudeView.Fixture(Now, false);
            view.Limits = null; view.Sample = false;
            var m = LimitsModel.From(view, Now, false);
            Assert(!m.Known && m.Missing.Contains("No plan reading yet"), "missing: " + m.Missing);
            var limits = new LimitsView();
            var host = Show(limits, view, 1000, 600, 1, false);
            Assert(Texts(host).Any(t => t.Contains("No plan reading yet")), "the reason isn't on screen");
            Assert(!Texts(host).Any(t => t.StartsWith("SAMPLE")), "live numbers say Sample");
        });

        check("Lap chart: History works out the calendar, hours, last 7 days and projects from day files", () => {
            var days = HistoryModel.SampleDays(Now);
            Assert(days.Count == HistoryModel.CalendarDays, "sample days: " + days.Count);
            var m = HistoryModel.From(days, Now, true, "");
            var today = Now.ToLocalTime().Date;
            Assert(m.Last7.Count == 7 && m.Last7[0].Cells[0] == "Today" && m.Last7[0].Date == today, "last 7 days");
            Assert(m.Busiest != null && m.Busiest.Tokens == 9800000 && m.BusiestLine().Contains("9.8M"), "busiest: " + m.BusiestLine());
            Assert(m.Level(today.AddDays(-CalendarBefore())) == -1, "a day before the files kept isn't marked");
            Assert(m.Level(m.Busiest.Date) == 4, "the busiest day isn't the brightest");
            Assert(m.HasLastWeek && m.ThisWeek.Sum() > 0, "hours");
            Assert(m.Projects.Count == 4 && Math.Abs(m.Projects[0].Share - 1) < 1e-9 && m.Projects[0].Name == "widget-screen", "projects");
            Assert(m.FirstMonday.DayOfWeek == DayOfWeek.Monday && (today - m.FirstMonday).TotalDays < 56 && (today - m.FirstMonday).TotalDays >= 49, "calendar start: " + m.FirstMonday);
        });

        check("Lap chart: History shows a long wait in amber and never shows a missing cost as $0", () => {
            var today = Now.ToLocalTime().Date;
            var days = new List<ClaudeDay> {
                new ClaudeDay { Date = today, Tokens = 1000000, Threads = 3, Needs = 2, LongestWait = 20 * 60, Cost = 7.5 },
                new ClaudeDay { Date = today.AddDays(-1), Tokens = 500000, Threads = 1, Needs = 1, LongestWait = 4 * 60, Unpriced = 3 } };
            var m = HistoryModel.From(days, Now, false, "");
            Assert(m.Last7[0].WaitWarns && m.Last7[0].Cells[5] == "20 min", "a 20-minute wait isn't amber");
            Assert(!m.Last7[1].WaitWarns && m.Last7[1].Cells[2] == "unpriced", "unpriced cost: " + m.Last7[1].Cells[2]);
            Assert(m.Last7[2].Cells[1] == "—", "a day with no file shows a number");
            Assert(!m.HasLastWeek, "a week with no files has ticks");
        });

        check("Lap chart: History says when it is off, says when it is empty, and reads the day files at most once a minute", () => {
            {
                var view = ClaudeView.Fixture(Now, false); view.Sample = false;
                if (view.Days == null) view.Days = new long[7];
                var history = new HistoryView();
                var host = Show(history, view, 1000, 600, 1, false);
                Assert(history.Shown.Missing == HistoryModel.Off && Texts(host).Any(t => t.Contains("Settings → Claude → History")), "History off doesn't say so");

                int reads = 0;
                history = new HistoryView();
                var context = new ClaudeViewContext();
                context.History = (now, count) => { reads++; return HistoryModel.SampleDays(now).Skip(HistoryModel.CalendarDays - count).ToList(); };
                history.Build(context, new Size(1000, 600)); history.Visible = true;
                history.Refresh(view, Now); history.Refresh(view, Now.AddSeconds(1)); history.Refresh(view, Now.AddSeconds(30));
                Assert(reads == 1, "read " + reads + " times in 30 s");
                view.Days[view.Days.Length - 1] += 1000;
                history.Refresh(view, Now.AddSeconds(40));
                Assert(reads == 1, "read again within the minute");
                history.Refresh(view, Now.AddSeconds(61));
                Assert(reads == 2, "today's tokens changed and a minute passed, but the files weren't read: " + reads);
                history.Refresh(view, Now.AddSeconds(200));
                Assert(reads == 2, "read again with nothing changed");

                history = new HistoryView();
                host = Show(history, view, 1000, 600, 1, false, (now, count) => Enumerable.Range(0, count).Select(i => new ClaudeDay { Date = Now.ToLocalTime().Date.AddDays(i - count + 1) }).ToList());
                Assert(history.Shown.Empty && Texts(host).Any(t => t.Contains("No day files yet")), "empty history doesn't say so");
            }
        });
    }

    // A calendar cell before the 8 weeks kept: the first Monday can be up to 6 days older than the oldest file.
    private static int CalendarBefore() { return HistoryModel.CalendarDays; }
}

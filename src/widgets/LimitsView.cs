using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClaudeDashboard
{
    // The lap chart's Limits tab (decision 030, package D): the 5-hour window from its start to its reset with the use
    // curve, both projections and where heavy use began; the week's laps by day with the pace; the per-model weekly
    // windows, extra usage and the spend limit; and this window's tokens by project. Numbers, names and times only.
    //
    // Built once per room and text size (Build), filled in from the snapshot the Garage reads (Refresh), and drawn again
    // only when the snapshot or the minute changes. It starts no timers and reads no files. The plan readings and both
    // "≈ limit" projections are the engine's (package A: PlanReadings, LimitAtPace, LimitAtAverage), so Garage and
    // Limits agree; this view only adds where heavy use began.
    public sealed class LimitsView : IClaudeView
    {
        public string Name { get { return ClaudeViews.Limits; } }
        public bool Visible { get; set; }

        private ClaudeViewContext context = new ClaudeViewContext();
        private Size room;
        private Border root;
        private string shownKey;
        // For the checks: the model last drawn.
        public LimitsModel Shown { get; private set; }

        public FrameworkElement Build(ClaudeViewContext context, Size room)
        {
            this.context = context ?? new ClaudeViewContext();
            this.room = room;
            shownKey = null;
            root = new Border();
            AutomationProperties.SetName(root, "Limits");
            return root;
        }

        public void Refresh(ClaudeView snapshot, DateTime now)
        {
            if (root == null || snapshot == null) return;
            bool sample = snapshot.Sample || context.Sample;
            string key = now.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture) + "|" + snapshot.MadeAt.Ticks + "|" + (snapshot.PlanReadings == null ? 0 : snapshot.PlanReadings.Count) + "|" + sample + "|" + context.Use24Hour;
            if (key == shownKey) return;
            shownKey = key;
            var model = LimitsModel.From(snapshot, now, sample);
            Shown = model;
            root.Child = Draw(model);
        }

        // ---- Drawing ----

        private double S(double size) { return context.Scaled(size); }

        private FrameworkElement Draw(LimitsModel m)
        {
            bool wide = room.Width >= 960;
            var grid = wide ? CardParts.Columns(new GridLength(1.6, GridUnitType.Star), new GridLength(1, GridUnitType.Star)) : CardParts.Rows(CardParts.Auto, CardParts.Auto);
            var left = WindowTile(m);
            var right = new StackPanel();
            right.Children.Add(WeekTile(m));
            var windows = WindowsTile(m); windows.Margin = new Thickness(0, 16, 0, 0);
            right.Children.Add(windows);
            if (wide) { right.Margin = new Thickness(16, 0, 0, 0); grid.Children.Add(CardParts.At(left, 0, 0)); grid.Children.Add(CardParts.At(right, 0, 1)); }
            else { right.Margin = new Thickness(0, 16, 0, 0); grid.Children.Add(CardParts.At(left, 0)); grid.Children.Add(CardParts.At(right, 1)); }

            var page = new StackPanel { Margin = new Thickness(20, 12, 20, 16) };
            if (m.Sample) page.Children.Add(LapParts.SampleFlag(S(12)));
            page.Children.Add(grid);
            return new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        }

        private Border WindowTile(LimitsModel m)
        {
            var stack = new StackPanel();
            stack.Children.Add(LapParts.Heading("5-hour window", S(18)));
            stack.Children.Add(LapParts.Words(m.WindowLine(context.Use24Hour), S(13), "Muted"));

            if (!m.Known)
            {
                var why = LapParts.Words(m.Missing, S(15), "Text"); why.Margin = new Thickness(0, 16, 0, 0);
                stack.Children.Add(why);
                return CardParts.Tile(stack, 12, new Thickness(16));
            }

            var top = CardParts.Columns(CardParts.Auto, CardParts.Star); top.Margin = new Thickness(0, 10, 0, 10);
            var big = CardParts.Reading(Math.Round(m.Percent).ToString(CultureInfo.InvariantCulture) + "%", S(56)); big.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            top.Children.Add(CardParts.At(big, 0, 0));
            var projection = LapParts.Words(m.ProjectionLine(context.Use24Hour), S(15), m.PaceHits.HasValue ? "Warning" : "Text");
            projection.VerticalAlignment = VerticalAlignment.Center; projection.Margin = new Thickness(16, 0, 0, 0);
            top.Children.Add(CardParts.At(projection, 0, 1));
            stack.Children.Add(top);

            double chartHeight = Math.Max(160, Math.Min(300, room.Height - S(300)));
            var chart = new LapChart { Height = chartHeight };
            bool use24 = context.Use24Hour; double label = S(11);
            chart.Paint = (dc, size, c) => PaintWindow(dc, size, c, m, use24, label);
            AutomationProperties.SetName(chart, m.ChartWords(use24));
            stack.Children.Add(chart);
            var legend = LapParts.Words(m.LegendLine(use24), S(12), "Muted"); legend.Margin = new Thickness(0, 6, 0, 0);
            stack.Children.Add(legend);

            var caption = LapParts.Caption("Tokens in this window, by project", S(11)); caption.Margin = new Thickness(0, 18, 0, 8);
            stack.Children.Add(caption);
            if (m.Projects.Count == 0) stack.Children.Add(LapParts.Words("No replies in this window yet.", S(13), "Muted"));
            else stack.Children.Add(LapParts.ShareRows(m.Projects, S(13), S(120)));
            return CardParts.Tile(stack, 12, new Thickness(16));
        }

        private Border WeekTile(LimitsModel m)
        {
            var stack = new StackPanel();
            var head = CardParts.Columns(CardParts.Star, CardParts.Auto);
            head.Children.Add(CardParts.At(LapParts.Heading("Week", S(18)), 0, 0));
            var resets = LapParts.Words(m.Week == null ? "" : ClaudeText.WeekReset(m.Week, context.Use24Hour), S(13), "Muted"); resets.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(CardParts.At(resets, 0, 1));
            stack.Children.Add(head);
            if (m.Week == null)
            {
                var why = LapParts.Words("No weekly reading in the plan's last answer.", S(14), "Text"); why.Margin = new Thickness(0, 12, 0, 0);
                stack.Children.Add(why);
                return CardParts.Tile(stack, 12, new Thickness(16));
            }
            var top = CardParts.Columns(CardParts.Auto, CardParts.Star); top.Margin = new Thickness(0, 8, 0, 8);
            var big = CardParts.Reading(Math.Round(m.WeekPercent).ToString(CultureInfo.InvariantCulture) + "%", S(40)); big.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            top.Children.Add(CardParts.At(big, 0, 0));
            var pace = LapParts.Words(m.WeekPaceLine(context.Use24Hour), S(14), m.WeekHits ? "Warning" : "Muted"); pace.VerticalAlignment = VerticalAlignment.Center; pace.Margin = new Thickness(12, 0, 0, 0);
            top.Children.Add(CardParts.At(pace, 0, 1));
            stack.Children.Add(top);

            var bars = new LapChart { Height = Math.Round(S(110) + S(36)) };
            double label = S(11);
            bars.Paint = (dc, size, c) => PaintLaps(dc, size, c, m, label);
            AutomationProperties.SetName(bars, m.LapWords());
            stack.Children.Add(bars);
            if (m.DaysWhy.Length > 0) { var why = LapParts.Words(m.DaysWhy, S(12), "Muted"); why.Margin = new Thickness(0, 6, 0, 0); stack.Children.Add(why); }
            return CardParts.Tile(stack, 12, new Thickness(16));
        }

        private Border WindowsTile(LimitsModel m)
        {
            var stack = new StackPanel();
            stack.Children.Add(LapParts.Heading("Weekly windows", S(18)));
            var rows = LapParts.LimitRows(m.Windows, S(13), S(120));
            rows.Margin = new Thickness(0, 10, 0, 0);
            stack.Children.Add(rows);
            return CardParts.Tile(stack, 12, new Thickness(16));
        }

        // The 5-hour chart: start to reset across, 0 to 100% up.
        private static void PaintWindow(DrawingContext dc, Size size, LapChart c, LimitsModel m, bool use24, double label)
        {
            dc.DrawRoundedRectangle(c.SurfaceInk, null, new Rect(size), 8, 8);
            double left = 10, right = size.Width - 10, top = 14, bottom = size.Height - 22;
            if (right - left < 40 || bottom - top < 40) return;
            double span = (m.Reset - m.Start).TotalMinutes;
            Func<DateTime, double> X = t => left + (right - left) * Math.Max(0, Math.Min(1, (t - m.Start).TotalMinutes / span));
            Func<double, double> Y = p => bottom - (bottom - top) * Math.Max(0, Math.Min(100, p)) / 100.0;
            var grid = new Pen(c.TrackInk, 1);
            foreach (double p in new[] { 25.0, 50, 75 }) dc.DrawLine(grid, new Point(left, Y(p)), new Point(right, Y(p)));
            dc.DrawLine(new Pen(c.TrackInk, 1), new Point(left, Y(100)), new Point(right, Y(100)));

            // The use curve, with a light fill under it.
            var points = m.Readings.Select(r => new Point(X(r.At), Y(r.Percent))).ToList();
            if (points.Count >= 2)
            {
                var fill = new StreamGeometry();
                using (var g = fill.Open())
                {
                    g.BeginFigure(new Point(points[0].X, bottom), true, true);
                    foreach (var p in points) g.LineTo(p, false, false);
                    g.LineTo(new Point(points[points.Count - 1].X, bottom), false, false);
                }
                fill.Freeze();
                dc.PushOpacity(0.14); dc.DrawGeometry(c.AccentInk, null, fill); dc.Pop();
                var line = new StreamGeometry();
                using (var g = line.Open())
                {
                    g.BeginFigure(points[0], false, false);
                    for (int i = 1; i < points.Count; i++) g.LineTo(points[i], true, true);
                }
                line.Freeze();
                dc.DrawGeometry(null, new Pen(c.AccentInk, 2.5) { LineJoin = PenLineJoin.Round }, line);
            }
            foreach (var p in points) dc.DrawEllipse(c.AccentInk, null, p, 2.5, 2.5);

            var nowPoint = new Point(X(m.Now), Y(m.Percent));
            // Today's average pace, dotted, to the reset or the limit.
            if (!Double.IsNaN(m.AverageSlope))
            {
                var end = Project(m, m.AverageSlope);
                var dotted = new Pen(c.MutedInk, 1.5) { DashStyle = new DashStyle(new double[] { 1, 3 }, 0), DashCap = PenLineCap.Round };
                dc.DrawLine(dotted, nowPoint, new Point(X(end.At), Y(end.Percent)));
            }
            // The last 30 minutes' pace, dashed: amber to the wall when it hits the limit before the reset.
            if (!Double.IsNaN(m.PaceSlope))
            {
                var end = Project(m, m.PaceSlope);
                var dashed = new Pen(m.PaceHits.HasValue ? c.WarningInk : c.TextInk, 2) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
                dc.DrawLine(dashed, nowPoint, new Point(X(end.At), Y(end.Percent)));
                if (m.PaceHits.HasValue)
                {
                    double x = X(m.PaceHits.Value);
                    dc.DrawLine(new Pen(c.WarningInk, 2), new Point(x, top), new Point(x, bottom));
                    var words = LapParts.Ink(c, "≈ " + ClaudeText.Clock(m.PaceHits.Value, use24), label, c.OnWarningInk, true);
                    double bx = Math.Max(left, Math.Min(right - words.Width - 8, x - words.Width / 2 - 4));
                    dc.DrawRoundedRectangle(c.WarningInk, null, new Rect(bx, 0, words.Width + 8, words.Height + 2), 3, 3);
                    dc.DrawText(words, new Point(bx + 4, 1));
                }
            }
            // Where heavy use began.
            if (m.Onset.HasValue)
            {
                double x = X(m.Onset.Value);
                dc.DrawLine(new Pen(c.TurnInk, 1.5) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) }, new Point(x, top), new Point(x, bottom));
                var words = LapParts.Ink(c, "≈ " + ClaudeText.Clock(m.Onset.Value, use24) + " heavy use starts", label, c.TurnInk, false);
                double wx = x - words.Width - 6 < left ? x + 6 : x - words.Width - 6;
                dc.DrawText(words, new Point(wx, top + 4));
            }
            // Now.
            double nx = X(m.Now);
            dc.DrawLine(new Pen(c.TextInk, 1), new Point(nx, top), new Point(nx, bottom));
            dc.DrawEllipse(c.TextInk, null, nowPoint, 3.5, 3.5);

            // The axis words.
            dc.DrawText(LapParts.Ink(c, "100%", label, c.MutedInk, false), new Point(left + 2, Y(100) + 2));
            dc.DrawText(LapParts.Ink(c, "50%", label, c.MutedInk, false), new Point(left + 2, Y(50) + 2));
            var from = LapParts.Ink(c, ClaudeText.Clock(m.Start, use24), label, c.MutedInk, false);
            dc.DrawText(from, new Point(left, bottom + 4));
            var to = LapParts.Ink(c, "resets " + ClaudeText.Clock(m.Reset, use24), label, c.MutedInk, false);
            dc.DrawText(to, new Point(right - to.Width, bottom + 4));
        }

        // Where a pace from now ends: the reset, or the limit if it comes first.
        private static LapReading Project(LimitsModel m, double slope)
        {
            double left = (m.Reset - m.Now).TotalMinutes;
            double at = m.Percent + slope * left;
            if (slope > 0 && at > 100) return new LapReading(m.Now.AddMinutes((100 - m.Percent) / slope), 100);
            return new LapReading(m.Reset, at);
        }

        // The week's seven laps: past days dim, today in the accent, days to come outlined.
        private static void PaintLaps(DrawingContext dc, Size size, LapChart c, LimitsModel m, double label)
        {
            int n = m.Days.Count; if (n == 0) return;
            double gap = 8, width = (size.Width - gap * (n - 1)) / n;
            if (width < 6) return;
            double labelRow = label * 1.6, top = labelRow, bottom = size.Height - labelRow;
            double most = Math.Max(4, m.Days.Where(d => !Double.IsNaN(d.Percent)).Select(d => d.Percent).DefaultIfEmpty(0).Max() * 1.15);
            for (int i = 0; i < n; i++)
            {
                var d = m.Days[i]; double x = i * (width + gap);
                var name = LapParts.Ink(c, d.Label, label, d.Today ? c.AccentInk : c.MutedInk, d.Today);
                dc.DrawText(name, new Point(x + (width - name.Width) / 2, bottom + 4));
                if (d.Future || Double.IsNaN(d.Percent))
                {
                    var outline = new Pen(c.TrackInk, 1) { DashStyle = new DashStyle(new double[] { 3, 2 }, 0) };
                    dc.DrawRoundedRectangle(null, outline, new Rect(x + 0.5, top + 0.5, width - 1, bottom - top - 1), 4, 4);
                    continue;
                }
                double h = Math.Max(2, (bottom - top) * d.Percent / most);
                if (d.Today) dc.DrawRoundedRectangle(c.AccentInk, null, new Rect(x, bottom - h, width, h), 4, 4);
                else { dc.PushOpacity(0.45); dc.DrawRoundedRectangle(c.AccentInk, null, new Rect(x, bottom - h, width, h), 4, 4); dc.Pop(); }
                var value = LapParts.Ink(c, LimitsModel.Pct(d.Percent), label, d.Today ? c.TextInk : c.MutedInk, false);
                dc.DrawText(value, new Point(x + (width - value.Width) / 2, Math.Max(0, bottom - h - value.Height - 2)));
            }
        }
    }

    // One plan reading: when, and how much of the 5-hour window was used.
    public sealed class LapReading
    {
        public readonly DateTime At;
        public readonly double Percent;
        public LapReading(DateTime at, double percent) { At = at; Percent = percent; }
    }
    // A lap of the week: its day, its ≈ share of the weekly limit (NaN when unknown) and whether it is today or to come.
    public sealed class LapDay { public string Label = ""; public DateTime Date; public double Percent = Double.NaN; public bool Today, Future; }
    // A weekly window's row: its name, the bar's share (NaN: words only), the words on the right and whether it warns.
    public sealed class LapRow { public string Name = "", Value = ""; public double Percent = Double.NaN; public bool Warns; }
    // A project's share: its name, tokens, the bar's length (0–1) and the words on the right.
    public sealed class LapShare { public string Name = "", Folder = "", Value = ""; public long Tokens; public double Share; }

    // What the Limits tab shows at one moment, worked out from the snapshot and the plan readings (for the view and the
    // checks).
    public sealed class LimitsModel
    {
        // A projection needs this many plan readings in the window; with fewer the view says why instead.
        public const int ReadingsNeeded = 3;
        public static readonly TimeSpan FiveHours = TimeSpan.FromHours(5), WeekSpan = TimeSpan.FromDays(7);

        public bool Sample, Known;
        public string Missing = "";
        public DateTime Now, Start, Reset, UpdatedAt;
        public double Percent;
        public List<LapReading> Readings = new List<LapReading>();
        // Why there's no projection ("" when there is one).
        public string NoProjection = "";
        // Percent a minute at the last 30 minutes' pace and at today's average (NaN: none; the average is today's tokens an
        // hour turned into percent, so its words say "from tokens"), when the first reaches 100%
        // before the reset, and both projections at the reset (NaN: none, or the limit comes first).
        public double PaceSlope = Double.NaN, AverageSlope = Double.NaN, PaceAtReset = Double.NaN, AverageAtReset = Double.NaN;
        public DateTime? PaceHits, AverageHits;
        public string AverageName = "today's average", AverageWhy = "";
        public DateTime? Onset;
        public ClaudeLimitWindow Week;
        public double WeekPercent, WeekAtReset = Double.NaN;
        public bool WeekHits;
        public DateTime? WeekHitsAt;
        public string WeekWhy = "";
        public List<LapDay> Days = new List<LapDay>();
        public string DaysWhy = "";
        public List<LapRow> Windows = new List<LapRow>();
        public List<LapShare> Projects = new List<LapShare>();

        public static string Pct(double percent) { return Double.IsNaN(percent) ? "—" : (percent < 10 ? percent.ToString("0.0", CultureInfo.InvariantCulture) : Math.Round(percent).ToString(CultureInfo.InvariantCulture)) + "%"; }

        public static LimitsModel From(ClaudeView view, DateTime now, bool sample)
        {
            var m = new LimitsModel { Now = now, Sample = sample };
            var limits = view.Limits;
            var five = limits == null ? null : limits.FiveHour;
            m.Known = five != null && five.ResetsAt.HasValue && five.Current(now);
            m.Reset = m.Known ? five.ResetsAt.Value : now;
            m.Start = m.Reset - FiveHours;
            if (limits != null) m.UpdatedAt = limits.UpdatedAt;
            if (!m.Known)
            {
                m.Missing = view.NoPlan ? "No plan limits here: this Claude Code uses an API key or isn't signed in, so there's no 5-hour window to show."
                    : limits == null ? "No plan reading yet. It comes from Claude Code's status line, or from Sign in with Claude in Settings → Claude."
                    : five == null ? "The plan's last answer had no 5-hour window."
                    : "The last 5-hour window has reset, and no new reading has come since. It starts with your next request.";
            }
            else
            {
                m.Percent = five.Percent;
                m.Readings = (view.PlanReadings ?? new List<ClaudePlanReading>()).Where(r => r != null && r.At >= m.Start && r.At <= now && (!r.ResetsAt.HasValue || Math.Abs((r.ResetsAt.Value - m.Reset).TotalMinutes) < 10))
                    .OrderBy(r => r.At).Select(r => new LapReading(r.At, r.Percent)).ToList();
                Project(m, view);
            }
            Weekly(m, view, now);
            Rows(m, limits, now);
            long total = view.UsageFor(ClaudePeriod.FiveHour).Sum(p => p.Tokens);
            m.Projects = view.UsageFor(ClaudePeriod.FiveHour).Where(p => p.Tokens > 0).OrderByDescending(p => p.Tokens).Take(6)
                .Select(p => new LapShare { Name = p.Name, Folder = p.Folder, Tokens = p.Tokens, Share = total > 0 ? (double)p.Tokens / total : 0, Value = ClaudeUsage.Format(p.Tokens) }).ToList();
            foreach (var p in m.Projects) p.Value = Math.Round(p.Share * 100) + "% · " + p.Value;
            return m;
        }

        // The engine's projections (none with fewer than 3 readings, and the reason why), and the onset of heavy use.
        private static void Project(LimitsModel m, ClaudeView view)
        {
            var pace = view.LimitAtPace; var average = view.LimitAtAverage;
            if (pace == null || !pace.Known)
            {
                string why = pace == null || String.IsNullOrEmpty(pace.Why) ? "not enough plan readings yet: ≈ limit needs " + ReadingsNeeded : pace.Why;
                m.NoProjection = "No projection: " + Lower(why) + ". The plan is read every 5 minutes.";
                return;
            }
            Take(pace, out m.PaceSlope, out m.PaceHits, out m.PaceAtReset);
            if (average != null && average.Known) Take(average, out m.AverageSlope, out m.AverageHits, out m.AverageAtReset);
            else if (average != null && !String.IsNullOrEmpty(average.Why)) m.AverageWhy = average.Why;
            m.Onset = Onset(m.Readings);
        }
        private static void Take(ClaudeLimitProjection p, out double slope, out DateTime? hits, out double atReset)
        {
            slope = Double.IsNaN(p.PerHour) ? Double.NaN : Math.Max(0, p.PerHour / 60);
            hits = p.BeforeReset ? p.LimitAt : null;
            atReset = hits.HasValue ? Double.NaN : p.AtReset;
        }
        private static string Lower(string words) { return words.Length > 1 && Char.IsUpper(words[0]) && !Char.IsUpper(words[1]) ? Char.ToLowerInvariant(words[0]) + words.Substring(1) : words; }

        // Where heavy use began: the start of the run of readings, up to the latest, that climb at least twice the
        // window's average pace. None when the whole window is that steep, the run has ended, or the use is flat.
        public static DateTime? Onset(IList<LapReading> readings)
        {
            if (readings == null || readings.Count < ReadingsNeeded) return null;
            var first = readings[0]; var last = readings[readings.Count - 1];
            double minutes = (last.At - first.At).TotalMinutes;
            if (minutes <= 0) return null;
            double average = (last.Percent - first.Percent) / minutes;
            if (average <= 0) return null;
            int start = -1;
            for (int i = readings.Count - 2; i >= 0; i--)
            {
                double span = (readings[i + 1].At - readings[i].At).TotalMinutes;
                if (span <= 0) continue;
                double slope = (readings[i + 1].Percent - readings[i].Percent) / span;
                if (slope >= 2 * average) start = i; else break;
            }
            return start > 0 ? readings[start].At : (DateTime?)null;
        }

        // The week: the pace to the reset and the laps by day, each day's share worked out from its tokens.
        private static void Weekly(LimitsModel m, ClaudeView view, DateTime now)
        {
            var limits = view.Limits;
            m.Week = limits == null ? null : limits.Week;
            if (m.Week == null) return;
            m.WeekPercent = m.Week.Current(now) ? m.Week.Percent : 0;
            DateTime reset = m.Week.ResetsAt.HasValue && m.Week.Current(now) ? m.Week.ResetsAt.Value : now.AddDays(7);
            DateTime start = reset - WeekSpan;
            double elapsed = (now - start).TotalHours, total = WeekSpan.TotalHours;
            if (!m.Week.ResetsAt.HasValue) m.WeekWhy = "The plan didn't say when the week resets.";
            else if (elapsed < 12) m.WeekWhy = "Too early in the week for a pace.";
            else
            {
                double at = m.WeekPercent * total / elapsed;
                if (at >= 100 && m.WeekPercent < 100) { m.WeekHits = true; m.WeekHitsAt = start.AddHours(elapsed * 100 / Math.Max(0.01, m.WeekPercent)); }
                else m.WeekAtReset = Math.Min(100, at);
            }

            int todayLap = (int)Math.Floor((now - start).TotalDays);
            DateTime today = now.ToLocalTime().Date;
            var tokens = new Dictionary<DateTime, long>();
            var days = view.Days;
            if (days == null && m.Sample) days = SampleDays();
            if (days != null) for (int i = 0; i < days.Length; i++) tokens[today.AddDays(i - (days.Length - 1))] = days[i];
            long used = 0;
            for (int i = 0; i < 7; i++)
            {
                var date = start.AddDays(i).ToLocalTime();
                var lap = new LapDay { Label = date.ToString("ddd", CultureInfo.InvariantCulture), Date = date.Date, Today = i == todayLap, Future = i > todayLap };
                long t; if (!lap.Future && tokens.TryGetValue(lap.Date, out t)) used += t;
                m.Days.Add(lap);
            }
            if (days == null) { m.DaysWhy = "Day shares come from the history files (Settings → Claude → History)."; return; }
            if (used <= 0) { m.DaysWhy = "No tokens in this week's history files yet."; return; }
            foreach (var lap in m.Days)
            {
                long t; if (lap.Future || !tokens.TryGetValue(lap.Date, out t)) continue;
                lap.Percent = m.WeekPercent * t / used;
            }
            m.DaysWhy = "≈ each day's share of the week, from its tokens.";
        }

        // The weekly windows tile: all models, each model's own, extra usage and the spend limit.
        private static void Rows(LimitsModel m, ClaudeLimits limits, DateTime now)
        {
            if (limits == null)
            {
                m.Windows.Add(new LapRow { Name = "All models", Value = "No reading yet" });
                return;
            }
            if (limits.Week != null) { double p = ClaudeText.Percent(limits.Week, now); m.Windows.Add(new LapRow { Name = "All models", Percent = p, Value = Math.Round(p) + "%", Warns = ClaudeText.Warns(p) }); }
            foreach (var pair in limits.Extra)
            {
                double p = ClaudeText.Percent(pair.Value, now);
                m.Windows.Add(new LapRow { Name = pair.Key.Replace(" week", ""), Percent = p, Value = Math.Round(p) + "%", Warns = ClaudeText.Warns(p) });
            }
            var extra = limits.ExtraUsage;
            if (extra == null) m.Windows.Add(new LapRow { Name = "Extra usage", Value = "Not reported" });
            else if (!extra.Enabled) m.Windows.Add(new LapRow { Name = "Extra usage", Value = "Off on this plan" });
            else
            {
                var c = CultureInfo.InvariantCulture;
                string amounts = !Double.IsNaN(extra.Used) && !Double.IsNaN(extra.Limit) ? extra.Used.ToString("0.##", c) + " of " + extra.Limit.ToString("0.##", c) : "On";
                m.Windows.Add(new LapRow { Name = "Extra usage", Percent = extra.Percent, Value = Double.IsNaN(extra.Percent) ? amounts : Math.Round(extra.Percent) + "% · " + amounts, Warns = !Double.IsNaN(extra.Percent) && ClaudeText.Warns(extra.Percent) });
            }
            var spend = limits.Spend;
            if (spend == null) m.Windows.Add(new LapRow { Name = "Spend limit", Value = "None reported (gateways only)" });
            else
            {
                string dollars = !Double.IsNaN(spend.UsedUsd) && !Double.IsNaN(spend.LimitUsd) ? " · $" + ClaudeText.Dollars(spend.UsedUsd) + " of $" + ClaudeText.Dollars(spend.LimitUsd) : "";
                m.Windows.Add(new LapRow { Name = "Spend limit", Percent = Math.Min(100, spend.Percent), Value = Math.Round(spend.Percent) + "%" + dollars, Warns = ClaudeText.Warns(spend.Percent) });
            }
        }

        // ---- Words ----

        public string WindowLine(bool use24)
        {
            if (!Known) return UpdatedAt == DateTime.MinValue ? "" : "plan as of " + ClaudeText.Clock(UpdatedAt, use24);
            return ClaudeText.Clock(Start, use24) + " to " + ClaudeText.Clock(Reset, use24) + (UpdatedAt == DateTime.MinValue ? "" : " · plan as of " + ClaudeText.Clock(UpdatedAt, use24));
        }

        public string ProjectionLine(bool use24)
        {
            if (Percent >= 100) return "Limit reached. It resets " + ClaudeText.Clock(Reset, use24) + ".";
            if (NoProjection.Length > 0) return NoProjection;
            if (PaceHits.HasValue)
                return "At the last 30 minutes' pace you hit the limit ≈ " + ClaudeText.Clock(PaceHits.Value, use24) + ", " + ClaudeText.Duration(Reset - PaceHits.Value) + " before it resets.";
            if (!Double.IsNaN(PaceAtReset)) return PaceSlope <= 0 ? "No use in the last 30 minutes: ≈ " + Pct(PaceAtReset) + " by reset at that pace." : "At the last 30 minutes' pace, ≈ " + Pct(PaceAtReset) + " by reset.";
            return "No pace for the last 30 minutes yet.";
        }

        public string LegendLine(bool use24)
        {
            if (!Known) return "";
            if (NoProjection.Length > 0) return "Line: plan readings in this window.";
            var parts = new List<string> { "line: plan readings" };
            if (!Double.IsNaN(PaceSlope)) parts.Add("dashed: last 30 minutes' pace");
            if (AverageHits.HasValue) parts.Add("dotted: ≈ limit at " + AverageName + ", from tokens, " + ClaudeText.Clock(AverageHits.Value, use24));
            else if (!Double.IsNaN(AverageAtReset)) parts.Add("dotted: at " + AverageName + ", from tokens, ≈ " + Pct(AverageAtReset) + " by reset");
            else if (AverageWhy.Length > 0) parts.Add("no average line: " + AverageWhy);
            return String.Join(" · ", parts);
        }

        public string ChartWords(bool use24)
        {
            string words = "5-hour window, " + Math.Round(Percent) + "% used, " + WindowLine(use24) + ". " + ProjectionLine(use24);
            if (Onset.HasValue) words += " Heavy use started ≈ " + ClaudeText.Clock(Onset.Value, use24) + ".";
            return words;
        }

        public string WeekPaceLine(bool use24)
        {
            if (WeekWhy.Length > 0) return WeekWhy;
            if (WeekHits && WeekHitsAt.HasValue) return "on pace to hit the limit ≈ " + WeekHitsAt.Value.ToLocalTime().ToString("ddd", CultureInfo.InvariantCulture) + " " + ClaudeText.Clock(WeekHitsAt.Value, use24);
            return Double.IsNaN(WeekAtReset) ? "" : "≈ on pace for " + Math.Round(WeekAtReset) + "% at reset";
        }

        public string LapWords()
        {
            return "Week laps: " + String.Join(", ", Days.Select(d => d.Label + (d.Today ? " (today)" : "") + " " + (d.Future ? "to come" : Double.IsNaN(d.Percent) ? "unknown" : "≈ " + Pct(d.Percent))));
        }

        // ---- The labelled sample ----

        // Tokens a day for the fixture's last 7 days, oldest first.
        public static long[] SampleDays() { return new long[] { 6200000, 4800000, 2000000, 8800000, 9800000, 2100000, 4600000 }; }
    }

    // A chart drawn in code with the palette's brushes, which follow the theme (no images, no effects).
    public sealed class LapChart : FrameworkElement
    {
        private static DependencyProperty Ink(string key) { return DependencyProperty.Register(key + "Ink", typeof(Brush), typeof(LapChart), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender)); }
        public static readonly DependencyProperty AccentInkProperty = Ink("Accent"), WarningInkProperty = Ink("Warning"), TextInkProperty = Ink("Text"), MutedInkProperty = Ink("Muted"),
            TrackInkProperty = Ink("Track"), SurfaceInkProperty = Ink("Surface"), TurnInkProperty = Ink("Turn"), OnWarningInkProperty = Ink("OnWarning");
        public Brush AccentInk { get { return (Brush)GetValue(AccentInkProperty); } }
        public Brush WarningInk { get { return (Brush)GetValue(WarningInkProperty); } }
        public Brush TextInk { get { return (Brush)GetValue(TextInkProperty); } }
        public Brush MutedInk { get { return (Brush)GetValue(MutedInkProperty); } }
        public Brush TrackInk { get { return (Brush)GetValue(TrackInkProperty); } }
        public Brush SurfaceInk { get { return (Brush)GetValue(SurfaceInkProperty); } }
        public Brush TurnInk { get { return (Brush)GetValue(TurnInkProperty); } }
        public Brush OnWarningInk { get { return (Brush)GetValue(OnWarningInkProperty); } }
        public Action<DrawingContext, Size, LapChart> Paint;
        // For the checks: the last drawing that failed (a chart that fails is left empty and logged).
        public static Exception LastError;

        public LapChart()
        {
            SetResourceReference(AccentInkProperty, "Accent"); SetResourceReference(WarningInkProperty, "Warning"); SetResourceReference(TextInkProperty, "Text");
            SetResourceReference(MutedInkProperty, "Muted"); SetResourceReference(TrackInkProperty, "Track"); SetResourceReference(SurfaceInkProperty, "Surface");
            SetResourceReference(TurnInkProperty, "Turn"); SetResourceReference(OnWarningInkProperty, "OnWarning");
            SnapsToDevicePixels = true;
        }
        protected override void OnRender(DrawingContext dc)
        {
            if (Paint == null || ActualWidth <= 0 || ActualHeight <= 0) return;
            try { Paint(dc, new Size(ActualWidth, ActualHeight), this); } catch (Exception e) { LastError = e; Program.Log(e); }
        }
    }

    // The parts the Limits and History tabs share.
    internal static class LapParts
    {
        public static TextBlock Heading(string text, double size) { var block = CardParts.Text(text, size, "Text"); block.FontWeight = FontWeights.SemiBold; return block; }
        public static TextBlock Words(string text, double size, string brush)
        {
            var block = CardParts.Text(text, size, brush); block.TextWrapping = TextWrapping.Wrap;
            Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
            return block;
        }
        public static TextBlock Caption(string text, double size) { var block = CardParts.Text(text.ToUpperInvariant(), size, "Muted"); block.FontWeight = FontWeights.SemiBold; return block; }
        // "SAMPLE": the fixture is showing, at every size.
        public static FrameworkElement SampleFlag(double size)
        {
            var words = CardParts.Text("SAMPLE · fixture numbers, not this PC's", size, "Muted"); words.FontWeight = FontWeights.SemiBold;
            words.HorizontalAlignment = HorizontalAlignment.Right; words.Margin = new Thickness(0, 0, 0, 8);
            AutomationProperties.SetName(words, "Sample: fixture numbers, not this PC's");
            return words;
        }
        public static FormattedText Ink(Visual visual, string text, double size, Brush brush, bool semibold)
        {
            var face = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, semibold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal);
            return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush, VisualTreeHelper.GetDpi(visual).PixelsPerDip);
        }
        // A bar: a track with the share filled in the accent (or amber when it warns).
        public static FrameworkElement Bar(double share, double height, bool warns)
        {
            var track = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
            var back = new Border { CornerRadius = new CornerRadius(height / 2) }; back.SetResourceReference(Border.BackgroundProperty, "Track");
            track.Children.Add(back);
            if (!Double.IsNaN(share) && share > 0)
            {
                double s = Math.Max(0.01, Math.Min(1, share));
                var fill = CardParts.Columns(new GridLength(s, GridUnitType.Star), new GridLength(1 - s, GridUnitType.Star));
                var bar = new Border { CornerRadius = new CornerRadius(height / 2) }; bar.SetResourceReference(Border.BackgroundProperty, warns ? "Warning" : "Accent");
                fill.Children.Add(CardParts.At(bar, 0, 0));
                track.Children.Add(fill);
            }
            return track;
        }
        // Rows of name | bar | words.
        public static Grid ShareRows(IList<LapShare> shares, double size, double nameWidth)
        {
            var grid = CardParts.Columns(new GridLength(nameWidth), CardParts.Star, CardParts.Auto);
            for (int i = 0; i < shares.Count; i++)
            {
                var s = shares[i];
                grid.RowDefinitions.Add(new RowDefinition { Height = CardParts.Auto });
                var name = CardParts.Text(s.Name, size, "Text"); name.TextTrimming = TextTrimming.CharacterEllipsis; name.ToolTip = s.Name; name.Margin = new Thickness(0, 3, 10, 3);
                var value = CardParts.Text(s.Value, size, "Muted"); value.Margin = new Thickness(10, 3, 0, 3); Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
                var bar = Bar(s.Share, 10, false);
                AutomationProperties.SetName(bar, s.Name + ", " + s.Value);
                grid.Children.Add(CardParts.At(name, i, 0)); grid.Children.Add(CardParts.At(bar, i, 1)); grid.Children.Add(CardParts.At(value, i, 2));
            }
            return grid;
        }
        // The weekly windows: name | bar (words only when there's no share) | words.
        public static Grid LimitRows(IList<LapRow> rows, double size, double nameWidth)
        {
            var grid = CardParts.Columns(new GridLength(nameWidth), CardParts.Star, CardParts.Auto);
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                grid.RowDefinitions.Add(new RowDefinition { Height = CardParts.Auto });
                var name = CardParts.Text(r.Name, size, "Text"); name.TextTrimming = TextTrimming.CharacterEllipsis; name.Margin = new Thickness(0, 5, 10, 5);
                grid.Children.Add(CardParts.At(name, i, 0));
                if (Double.IsNaN(r.Percent))
                {
                    var words = CardParts.Text(r.Value, size, "Muted"); words.TextWrapping = TextWrapping.Wrap; words.Margin = new Thickness(0, 5, 0, 5);
                    grid.Children.Add(CardParts.At(words, i, 1, 2));
                }
                else
                {
                    var bar = Bar(r.Percent / 100, 8, r.Warns);
                    var value = CardParts.Text(r.Value, size, r.Warns ? "Warning" : "Muted"); value.Margin = new Thickness(10, 5, 0, 5); Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
                    grid.Children.Add(CardParts.At(bar, i, 1)); grid.Children.Add(CardParts.At(value, i, 2));
                }
                AutomationProperties.SetName(name, r.Name + ", " + r.Value);
            }
            return grid;
        }
    }
}

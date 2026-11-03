using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace ClaudeDashboard
{
    // The lap chart's drawing (decision 030, package C): one time axis from the last 100 minutes to the 5-hour reset,
    // the 5-hour use curve over the lanes, each lane's trail of states, the future region after now and the Amber wall
    // at "≈ limit". Vectors in the theme's colours, no images, no shadows; nothing here moves.

    // The axis every part of the chart shares: a time maps to the same x in the curve and in every lane.
    public sealed class LapAxis
    {
        public static readonly TimeSpan Past = TimeSpan.FromMinutes(100), FutureLeast = TimeSpan.FromMinutes(20), FutureMost = TimeSpan.FromHours(5), FutureUnknown = TimeSpan.FromMinutes(60);
        public DateTime From, Now, To;
        // Where the 5-hour limit is reached at the last 30 minutes' pace (null: no projection, or not before the reset).
        public DateTime? Wall;
        public DateTime WatchStart;
        // The axis for a snapshot: back 100 minutes, ahead to the 5-hour reset (an hour when it's unknown).
        public static LapAxis For(ClaudeView view, DateTime now)
        {
            DateTime? reset = view == null || view.Limits == null || view.Limits.FiveHour == null ? null : view.Limits.FiveHour.ResetsAt;
            TimeSpan ahead = reset.HasValue && reset.Value > now ? reset.Value - now : FutureUnknown;
            if (ahead < FutureLeast) ahead = FutureLeast;
            if (ahead > FutureMost) ahead = FutureMost;
            var axis = new LapAxis { From = now - Past, Now = now, To = now + ahead, WatchStart = view == null ? DateTime.MinValue : view.WatchStart };
            var pace = view == null ? null : view.LimitAtPace;
            // v0.57.2: no wall once the limit is reached, by the engine or by the meter itself (the readings can lag).
            if (ClaudeText.FiveHourReached(view, now)) return axis;
            if (pace != null && pace.BeforeReset && pace.LimitAt.Value > now && pace.LimitAt.Value <= axis.To) axis.Wall = pace.LimitAt.Value;
            return axis;
        }
        public double X(DateTime at, double width)
        {
            double span = (To - From).TotalSeconds;
            if (span <= 0 || width <= 0) return 0;
            return Math.Max(0, Math.Min(width, (at - From).TotalSeconds / span * width));
        }
    }

    public abstract class LapPart : PitInstrument
    {
        public const uint Idle = 0xFF5B6474;
        public LapAxis Axis;
        protected double Width0 { get { return ActualWidth; } }
        // The future region: the Input colour after now, so past and future read differently without a label.
        protected void Future(DrawingContext dc, double h)
        {
            double x = Axis.X(Axis.Now, ActualWidth);
            dc.DrawRectangle(B("Input"), null, new Rect(x, 0, Math.Max(0, ActualWidth - x), h));
        }
        protected void NowLine(DrawingContext dc, double h)
        {
            double x = Math.Round(Axis.X(Axis.Now, ActualWidth)) + 0.5;
            dc.DrawLine(Line(B("Muted"), 1, false), new Point(x, 0), new Point(x, h));
        }
        // The Amber wall: a 2-unit line at "≈ limit", through the curve and every lane.
        protected void WallLine(DrawingContext dc, double h)
        {
            if (Axis.Wall == null) return;
            double x = Axis.X(Axis.Wall.Value, ActualWidth);
            dc.DrawLine(Line(B("Warning"), 2, false), new Point(x, 0), new Point(x, h));
        }
        // Before the app began watching there's no history: that part stays empty, with a dashed edge.
        protected void WatchEdge(DrawingContext dc, double h)
        {
            if (Axis.WatchStart <= Axis.From || Axis.WatchStart >= Axis.Now) return;
            double x = Math.Round(Axis.X(Axis.WatchStart, ActualWidth)) + 0.5;
            var pen = new Pen(B("Muted"), 1) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) }; pen.Freeze();
            dc.DrawLine(pen, new Point(x, 0), new Point(x, h));
        }
    }

    // One lane's trail: the session's states from the axis's start to now. Working is solid project colour, Needs you
    // an Amber block, Your turn a hollow Glacier outline, Idle and Silent a thin grey line, Compacting stripes in the
    // project colour, Stuck a hollow Amber outline, an API error a 2-unit rose mark.
    public sealed class LapTrail : LapPart
    {
        public List<ClaudeStateMark> Marks = new List<ClaudeStateMark>();
        public List<DateTime> Errors = new List<DateTime>();
        public uint Color = PitArt.Lime;
        // The x where the car sits (its nose at now), so the trail stops at its tail.
        public double CarWidth;
        protected override void OnRender(DrawingContext dc)
        {
            if (Axis == null) return;
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            Future(dc, h);
            WatchEdge(dc, h);
            double now = Axis.X(Axis.Now, w), mid = Math.Round(h / 2), band = Math.Max(8, Math.Round(h * 0.42));
            var project = Fixed(Color);
            var turn = B("Turn", Fixed(0xFF8ED8F8));
            for (int i = 0; i < Marks.Count; i++)
            {
                var m = Marks[i];
                DateTime end = i + 1 < Marks.Count ? Marks[i + 1].At : Axis.Now;
                if (end <= Axis.From || m.State == ClaudeLaneState.Ended) continue;
                double x0 = Axis.X(m.At < Axis.From ? Axis.From : m.At, w), x1 = Math.Min(Axis.X(end, w), now);
                if (x1 - x0 < 0.5) continue;
                var block = new Rect(x0, mid - band / 2, x1 - x0, band);
                switch (m.State)
                {
                    case ClaudeLaneState.Working: dc.DrawRectangle(project, null, block); break;
                    case ClaudeLaneState.NeedsYou: dc.DrawRectangle(B("Warning"), null, block); break;
                    case ClaudeLaneState.Stuck: dc.DrawRectangle(null, Line(B("Warning"), 1.5, false), Inset(block, 0.75)); break;
                    case ClaudeLaneState.YourTurn: dc.DrawRectangle(null, Line(turn, 1.5, false), Inset(block, 0.75)); break;
                    case ClaudeLaneState.Compacting: Hatch(dc, block, project, 0); break;
                    default: dc.DrawRectangle(Fixed(Idle), null, new Rect(x0, mid - 2, x1 - x0, 4)); break;
                }
            }
            var rose = Line(ClaudeReliability.Rose(this), 2, false);
            foreach (var at in Errors)
            {
                if (at < Axis.From || at > Axis.Now) continue;
                double x = Axis.X(at, w);
                dc.DrawLine(rose, new Point(x, mid - band / 2 - 3), new Point(x, mid + band / 2 + 3));
            }
            NowLine(dc, h);
            WallLine(dc, h);
        }
        private static Rect Inset(Rect r, double by) { return r.Width > 2 * by && r.Height > 2 * by ? new Rect(r.X + by, r.Y + by, r.Width - 2 * by, r.Height - 2 * by) : r; }
    }

    // The 5-hour use curve above the lanes: the readings so far, the dashed projection at the last 30 minutes' pace to
    // 100% (or to the reset), the 100% line, now, and the wall labelled once at the top ("≈ limit 3:21 pm").
    public sealed class LapCurve : LapPart
    {
        public List<ClaudePlanReading> Readings = new List<ClaudePlanReading>();
        public ClaudeLimitProjection Pace;
        public double Percent = Double.NaN;
        public string WallLabel = "", Note = "";
        // v0.56.1: the window is at 100%: the label says so in Amber, and no wall or time is drawn.
        public bool Reached;
        protected override void OnRender(DrawingContext dc)
        {
            if (Axis == null) return;
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            Future(dc, h);
            WatchEdge(dc, h);
            double size = Math.Round(11 * TextScale), top = Math.Ceiling(size * 1.4) + 2, bottom = h - 2;
            Func<double, double> y = p => bottom - Math.Max(0, Math.Min(110, p)) / 110 * (bottom - top);
            var muted = B("Muted");
            var dashed = new Pen(Faded(muted, 0.6), 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) }; dashed.Freeze();
            dc.DrawLine(dashed, new Point(0, Math.Round(y(100)) + 0.5), new Point(w, Math.Round(y(100)) + 0.5));
            var shown = Readings.Where(r => r.At >= Axis.From && r.At <= Axis.Now).OrderBy(r => r.At).ToList();
            var text = B("Text");
            if (shown.Count > 0)
            {
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    g.BeginFigure(new Point(Axis.X(shown[0].At, w), y(shown[0].Percent)), false, false);
                    foreach (var r in shown.Skip(1)) g.LineTo(new Point(Axis.X(r.At, w), y(r.Percent)), true, true);
                }
                geometry.Freeze();
                dc.DrawGeometry(null, Line(text, 2), geometry);
            }
            // The projection, dashed, from the newest reading at the pace.
            var last = Readings.OrderBy(r => r.At).LastOrDefault();
            if (!Reached && Pace != null && Pace.Known && last != null)
            {
                DateTime end = Pace.LimitAt.HasValue ? Pace.LimitAt.Value : Axis.To;
                double endPercent = Pace.LimitAt.HasValue ? 100 : last.Percent + Math.Max(0, Pace.PerHour) * (Axis.To - last.At).TotalHours;
                var pen = new Pen(Pace.BeforeReset ? B("Warning") : muted, 1.5) { DashStyle = new DashStyle(new double[] { 3, 2 }, 0) }; pen.Freeze();
                dc.DrawLine(pen, new Point(Axis.X(last.At, w), y(last.Percent)), new Point(Axis.X(end, w), y(endPercent)));
            }
            NowLine(dc, h);
            WallLine(dc, h);
            // One label, at the top: the wall's time, else why there's no projection.
            string label = Reached ? "Limit reached" : Axis.Wall != null ? WallLabel : Note;
            if (label.Length > 0)
            {
                var words = Words(label, size, Reached || Axis.Wall != null ? B("Warning") : muted);
                double x = Axis.Wall != null ? Axis.X(Axis.Wall.Value, w) + 4 : Axis.X(Axis.Now, w) + 4;
                if (x + words.Width > w) x = Math.Max(0, (Axis.Wall != null ? Axis.X(Axis.Wall.Value, w) : Axis.X(Axis.Now, w)) - 4 - words.Width);
                dc.DrawText(words, new Point(x, 0));
            }
            // v0.57.2: the percent sits at the curve's end, just left of the now line (Scribe's render note), below the
            // line when it would meet the label at the top.
            if (!Double.IsNaN(Percent))
            {
                var reading = Words(Math.Round(Percent) + "%", size, text);
                var at = PercentAt(w, h, reading.Width, reading.Height, last == null ? Percent : last.Percent);
                dc.DrawText(reading, at);
            }
        }
        // Where the percent goes: its right edge 6 units left of now, above the curve's end, or below it near the top.
        public Point PercentAt(double w, double h, double width, double height, double percent)
        {
            double size = Math.Round(11 * TextScale), top = Math.Ceiling(size * 1.4) + 2, bottom = h - 2;
            double y = bottom - Math.Max(0, Math.Min(110, percent)) / 110 * (bottom - top);
            double x = Math.Max(0, Axis.X(Axis.Now, w) - 6 - width);
            double above = y - height - 3;
            double ty = above >= top ? above : Math.Min(h - height, y + 4);
            return new Point(x, Math.Max(0, ty));
        }
    }

    // v0.57.2: the time under the curve (Scribe's render note): whole hours, "now" and "reset 2:16 pm", in the Muted
    // colour, each centred on its time; a label that would meet another is left out (now and the reset go first).
    public sealed class LapTicks : LapPart
    {
        public bool Use24;
        public DateTime? Reset;
        public const double Gap = 10;
        // The labels in the order they're placed: now, the reset, then the whole hours on the axis.
        public static List<KeyValuePair<DateTime, string>> Labels(LapAxis axis, DateTime? reset, bool use24)
        {
            var labels = new List<KeyValuePair<DateTime, string>> { new KeyValuePair<DateTime, string>(axis.Now, "now") };
            if (reset.HasValue && reset.Value > axis.Now && reset.Value <= axis.To) labels.Add(new KeyValuePair<DateTime, string>(reset.Value, "reset " + ClaudeText.Clock(reset.Value, use24)));
            var local = axis.From.ToLocalTime();
            var hour = new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0, DateTimeKind.Local).AddHours(1);
            for (int i = 0; i < 12 && hour.ToUniversalTime() < axis.To; i++, hour = hour.AddHours(1))
                labels.Add(new KeyValuePair<DateTime, string>(hour.ToUniversalTime(), ClaudeText.Clock(hour.ToUniversalTime(), use24)));
            return labels;
        }
        // Each shown label's text and its left and right edges, for the drawing and the checks.
        public List<Tuple<string, double, double>> Placed(double w, Func<string, double> measure)
        {
            var placed = new List<Tuple<string, double, double>>();
            if (Axis == null || w <= 0) return placed;
            foreach (var label in Labels(Axis, Reset, Use24))
            {
                double width = measure(label.Value), x = Axis.X(label.Key, w);
                double left = label.Value.StartsWith("reset ") ? x - width : x - width / 2;
                left = Math.Max(0, Math.Min(w - width, left));
                double right = left + width;
                if (placed.Any(p => left < p.Item3 + Gap && right > p.Item2 - Gap)) continue;
                placed.Add(Tuple.Create(label.Value, left, right));
            }
            return placed;
        }
        public List<Tuple<string, double, double>> Placed() { double size = Math.Round(11 * TextScale); var muted = B("Muted"); return Placed(ActualWidth, t => Words(t, size, muted).Width); }
        protected override void OnRender(DrawingContext dc)
        {
            if (Axis == null) return;
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            double size = Math.Round(11 * TextScale);
            var muted = B("Muted");
            var tick = Line(Faded(muted, 0.6), 1, false);
            foreach (var p in Placed())
            {
                var words = Words(p.Item1, size, p.Item1 == "now" ? B("Text") : muted);
                dc.DrawText(words, new Point(p.Item2, h - words.Height));
            }
            foreach (var label in Labels(Axis, Reset, Use24))
            {
                double x = Math.Round(Axis.X(label.Key, w)) + 0.5;
                dc.DrawLine(tick, new Point(x, 0), new Point(x, 3));
            }
        }
    }

    // v0.57.2: the trails' key on one line (Scribe's render note): a swatch drawn as the trail draws it, then its word.
    // What doesn't fit at the end is left out; the full key is the element's name for screen readers. The key tells
    // states, not projects: Working and Compacting are drawn in the Text colour and say the trail takes the project's.
    public sealed class LapKey : LapPart
    {
        public static readonly string[] Names = { "Working (project colour)", "Needs you", "Stuck", "Your turn", "Compacting", "Idle or silent", "API error" };
        public static string Spoken { get { return "Key: " + String.Join(", ", Names); } }
        public int Shown { get; private set; }
        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            double size = Math.Round(11 * TextScale), swatch = Math.Round(14 * TextScale), band = Math.Max(6, Math.Round(size * 0.7)), mid = Math.Round(h / 2), x = 0;
            var muted = B("Muted");
            Shown = 0;
            for (int i = 0; i < Names.Length; i++)
            {
                var words = Words(Names[i], size, muted);
                double need = swatch + 4 + words.Width;
                if (x + need > w) break;
                var r = new Rect(x, mid - band / 2, swatch, band);
                switch (i)
                {
                    case 0: dc.DrawRectangle(B("Text"), null, r); break;
                    case 1: dc.DrawRectangle(B("Warning"), null, r); break;
                    case 2: dc.DrawRectangle(null, Line(B("Warning"), 1.5, false), new Rect(r.X + 0.75, r.Y + 0.75, r.Width - 1.5, r.Height - 1.5)); break;
                    case 3: dc.DrawRectangle(null, Line(B("Turn", Fixed(0xFF8ED8F8)), 1.5, false), new Rect(r.X + 0.75, r.Y + 0.75, r.Width - 1.5, r.Height - 1.5)); break;
                    case 4: Hatch(dc, r, B("Text"), 0); break;
                    case 5: dc.DrawRectangle(Fixed(Idle), null, new Rect(x, mid - 2, swatch, 4)); break;
                    default: dc.DrawLine(Line(ClaudeReliability.Rose(this), 2, false), new Point(x + swatch / 2, mid - band / 2 - 2), new Point(x + swatch / 2, mid + band / 2 + 2)); break;
                }
                dc.DrawText(words, new Point(x + swatch + 4, mid - words.Height / 2));
                x += need + 14;
                Shown++;
            }
        }
    }
}

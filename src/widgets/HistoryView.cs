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
    // The lap chart's History tab (decision 030, package D), from ClaudeHistory's day files: the 8-week calendar, the
    // hour-of-day profile against the same hour last week, the last 7 days (tokens, ≈ $, threads, times it needed you,
    // longest wait) and the projects this week. Numbers and project names only, as the files keep.
    //
    // Built once per room and text size, filled in from the snapshot on Refresh. The day files are read through Source,
    // only when today's tokens or the date change (the service writes today's file once a minute) and never more than
    // once a minute, so the tab adds no polling of its own.
    public sealed class HistoryView : IClaudeView
    {
        public string Name { get { return ClaudeViews.History; } }
        public bool Visible { get; set; }

        // The day files, oldest first, count days up to today (ClaudeHistory.Days); null while History is off. It will
        // come from the view context (Shell, #21); until it does, the tab says the files aren't reaching it.
        public Func<DateTime, int, List<ClaudeDay>> Source { get; set; }
        public static readonly TimeSpan ReadEvery = TimeSpan.FromMinutes(1);

        private ClaudeViewContext context = new ClaudeViewContext();
        private Size room;
        private Border root;
        private string shownKey;
        private List<ClaudeDay> days;
        private string readKey;
        private DateTime readAt = DateTime.MinValue;
        // For the checks: the model last drawn, and how many times the day files were read.
        public HistoryModel Shown { get; private set; }
        public int Reads { get; private set; }

        public FrameworkElement Build(ClaudeViewContext context, Size room)
        {
            this.context = context ?? new ClaudeViewContext();
            this.room = room;
            shownKey = null;
            root = new Border();
            AutomationProperties.SetName(root, "History");
            return root;
        }

        public void Refresh(ClaudeView snapshot, DateTime now)
        {
            if (root == null || snapshot == null) return;
            bool sample = snapshot.Sample || context.Sample;
            string missing = "";
            if (sample) days = HistoryModel.SampleDays(now);
            else
            {
                string key = now.ToLocalTime().Date.Ticks + "|" + (snapshot.Days == null || snapshot.Days.Length == 0 ? "none" : snapshot.Days[snapshot.Days.Length - 1].ToString(CultureInfo.InvariantCulture));
                if (Source == null) { days = null; missing = "The day files aren't reaching this tab in this build yet."; }
                else if (days == null || (key != readKey && now - readAt >= ReadEvery))
                {
                    try { days = Source(now, HistoryModel.CalendarDays); } catch (Exception e) { Program.Log(e); days = null; missing = "The day files couldn't be read: " + e.GetBaseException().Message; }
                    readKey = key; readAt = now; Reads++;
                }
            }
            string drawKey = now.ToString("yyyyMMddHH", CultureInfo.InvariantCulture) + "|" + Reads + "|" + sample + "|" + missing + "|" + context.Use24Hour;
            if (drawKey == shownKey) return;
            shownKey = drawKey;
            var model = HistoryModel.From(days, now, sample, missing);
            Shown = model;
            root.Child = Draw(model);
        }

        // ---- Drawing ----

        private double S(double size) { return context.Scaled(size); }

        private FrameworkElement Draw(HistoryModel m)
        {
            var page = new StackPanel { Margin = new Thickness(20, 12, 20, 16) };
            if (m.Sample) page.Children.Add(LapParts.SampleFlag(S(12)));
            if (m.Missing.Length > 0 || m.Empty)
            {
                var stack = new StackPanel();
                stack.Children.Add(LapParts.Heading("History", S(18)));
                var why = LapParts.Words(m.Missing.Length > 0 ? m.Missing : "No day files yet. History keeps 8 weeks of daily numbers while Claude Code runs (Settings → Claude → History).", S(15), "Text");
                why.Margin = new Thickness(0, 12, 0, 0);
                stack.Children.Add(why);
                page.Children.Add(CardParts.Tile(stack, 12, new Thickness(16)));
            }
            else
            {
                bool wide = room.Width >= 960;
                var grid = wide ? CardParts.Columns(CardParts.Star, CardParts.Star) : CardParts.Rows(CardParts.Auto, CardParts.Auto);
                var left = WeeksTile(m); var right = DaysTile(m);
                if (wide) { right.Margin = new Thickness(16, 0, 0, 0); grid.Children.Add(CardParts.At(left, 0, 0)); grid.Children.Add(CardParts.At(right, 0, 1)); }
                else { right.Margin = new Thickness(0, 16, 0, 0); grid.Children.Add(CardParts.At(left, 0)); grid.Children.Add(CardParts.At(right, 1)); }
                page.Children.Add(grid);
            }
            return new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        }

        private Border WeeksTile(HistoryModel m)
        {
            var stack = new StackPanel();
            stack.Children.Add(LapParts.Heading("8 weeks", S(18)));
            stack.Children.Add(LapParts.Words("tokens per day · darker is quieter", S(13), "Muted"));
            var calendar = new LapChart { Height = Math.Round(S(18) + 7 * (S(26) + 4)), Margin = new Thickness(0, 12, 0, 0) };
            double label = S(11);
            calendar.Paint = (dc, size, c) => PaintCalendar(dc, size, c, m, label);
            AutomationProperties.SetName(calendar, m.CalendarWords());
            stack.Children.Add(calendar);
            var foot = CardParts.Columns(CardParts.Star, CardParts.Auto); foot.Margin = new Thickness(0, 8, 0, 0);
            foot.Children.Add(CardParts.At(LapParts.Words(m.BusiestLine(), S(12), "Muted"), 0, 0));
            var kept = LapParts.Words("Kept 8 weeks, numbers and project names only", S(12), "Muted"); kept.Margin = new Thickness(12, 0, 0, 0);
            foot.Children.Add(CardParts.At(kept, 0, 1));
            stack.Children.Add(foot);

            var caption = LapParts.Caption(m.HasLastWeek ? "Hour of day, this week · tick: same hour last week" : "Hour of day, this week", S(11)); caption.Margin = new Thickness(0, 20, 0, 8);
            stack.Children.Add(caption);
            var hours = new LapChart { Height = Math.Round(S(90) + S(20)) };
            bool use24 = context.Use24Hour;
            hours.Paint = (dc, size, c) => PaintHours(dc, size, c, m, label, use24);
            AutomationProperties.SetName(hours, m.HourWords());
            stack.Children.Add(hours);
            if (!m.HasLastWeek) { var why = LapParts.Words("No ticks: the week before has no day files.", S(12), "Muted"); why.Margin = new Thickness(0, 6, 0, 0); stack.Children.Add(why); }
            return CardParts.Tile(stack, 12, new Thickness(16));
        }

        private Border DaysTile(HistoryModel m)
        {
            var stack = new StackPanel();
            stack.Children.Add(LapParts.Heading("Last 7 days", S(18)));
            stack.Children.Add(LapParts.Words("waits counted while the window was open", S(13), "Muted"));
            var table = CardParts.Columns(CardParts.Auto, CardParts.Star, CardParts.Star, CardParts.Star, CardParts.Star, CardParts.Star);
            table.Margin = new Thickness(0, 12, 0, 0);
            string[] heads = { "Day", "Tokens", "≈ $ at API prices", "Threads", "Needed you", "Longest wait" };
            table.RowDefinitions.Add(new RowDefinition { Height = CardParts.Auto });
            for (int i = 0; i < heads.Length; i++)
            {
                var head = CardParts.Text(heads[i], S(11), "Muted"); head.FontWeight = FontWeights.SemiBold; head.TextWrapping = TextWrapping.Wrap;
                head.Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 6); head.TextAlignment = i == 0 ? TextAlignment.Left : TextAlignment.Right;
                table.Children.Add(CardParts.At(head, 0, i));
            }
            for (int r = 0; r < m.Last7.Count; r++)
            {
                var row = m.Last7[r];
                table.RowDefinitions.Add(new RowDefinition { Height = CardParts.Auto });
                for (int i = 0; i < 6; i++)
                {
                    bool warns = i == 5 && row.WaitWarns;
                    var cell = CardParts.Text(row.Cells[i], S(13), i == 0 ? "Text" : warns ? "Warning" : "Text");
                    if (r == 0 && i == 0) cell.FontWeight = FontWeights.SemiBold;
                    Typography.SetNumeralAlignment(cell, FontNumeralAlignment.Tabular);
                    cell.Margin = new Thickness(i == 0 ? 0 : 8, 4, 0, 4); cell.TextAlignment = i == 0 ? TextAlignment.Left : TextAlignment.Right;
                    table.Children.Add(CardParts.At(cell, r + 1, i));
                }
            }
            stack.Children.Add(table);
            var caption = LapParts.Caption("Projects this week", S(11)); caption.Margin = new Thickness(0, 20, 0, 8);
            stack.Children.Add(caption);
            if (m.Projects.Count == 0) stack.Children.Add(LapParts.Words("No project tokens in the last 7 days.", S(13), "Muted"));
            else stack.Children.Add(LapParts.ShareRows(m.Projects, S(13), S(130)));
            return CardParts.Tile(stack, 12, new Thickness(16));
        }

        // Eight week columns, Monday at the top; today outlined, days to come left empty.
        private static void PaintCalendar(DrawingContext dc, Size size, LapChart c, HistoryModel m, double label)
        {
            double labels = 36, head = label * 1.6, gap = 4;
            double width = (size.Width - labels - gap * 7) / 8, height = (size.Height - head - gap * 6) / 7;
            if (width < 4 || height < 4) return;
            string[] names = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            for (int r = 0; r < 7; r++)
            {
                var name = LapParts.Ink(c, names[r], label, c.MutedInk, false);
                dc.DrawText(name, new Point(0, head + r * (height + gap) + (height - name.Height) / 2));
            }
            for (int w = 0; w < 8; w++)
            {
                DateTime monday = m.FirstMonday.AddDays(7 * w);
                double x = labels + w * (width + gap);
                var week = LapParts.Ink(c, w == 0 || monday.Day <= 7 ? monday.ToString("d MMM", CultureInfo.InvariantCulture) : monday.Day.ToString(CultureInfo.InvariantCulture), label, c.MutedInk, false);
                dc.DrawText(week, new Point(x, 0));
                for (int r = 0; r < 7; r++)
                {
                    DateTime date = monday.AddDays(r);
                    var rect = new Rect(x, head + r * (height + gap), width, height);
                    if (date > m.Today) continue;
                    int level = m.Level(date);
                    if (level < 0) { dc.DrawRoundedRectangle(null, new Pen(c.TrackInk, 1), new Rect(rect.X + 0.5, rect.Y + 0.5, rect.Width - 1, rect.Height - 1), 4, 4); continue; }
                    if (level == 0) dc.DrawRoundedRectangle(c.TrackInk, null, rect, 4, 4);
                    else { dc.PushOpacity(HistoryModel.Shades[level]); dc.DrawRoundedRectangle(c.AccentInk, null, rect, 4, 4); dc.Pop(); }
                    if (date == m.Today) dc.DrawRoundedRectangle(null, new Pen(c.TextInk, 2), new Rect(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2), 4, 4);
                }
            }
        }

        // Tokens by hour of day over the last 7 days, the current hour in the accent, a tick at the week before's.
        private static void PaintHours(DrawingContext dc, Size size, LapChart c, HistoryModel m, double label, bool use24)
        {
            double axis = label * 1.7, bottom = size.Height - axis, gap = 2, width = (size.Width - gap * 23) / 24;
            if (width < 2 || bottom < 10) return;
            long most = Math.Max(1, Math.Max(m.ThisWeek.Max(), m.HasLastWeek ? m.LastWeek.Max() : 0));
            for (int h = 0; h < 24; h++)
            {
                double x = h * (width + gap);
                double bar = (bottom - 4) * m.ThisWeek[h] / most;
                if (m.ThisWeek[h] > 0)
                {
                    var rect = new Rect(x, bottom - Math.Max(2, bar), width, Math.Max(2, bar));
                    if (h == m.Hour) dc.DrawRectangle(c.AccentInk, null, rect);
                    else { dc.PushOpacity(0.45); dc.DrawRectangle(c.AccentInk, null, rect); dc.Pop(); }
                }
                if (m.HasLastWeek)
                {
                    double y = bottom - (bottom - 4) * m.LastWeek[h] / most;
                    dc.DrawLine(new Pen(c.TextInk, 2), new Point(x, y), new Point(x + width, y));
                }
            }
            foreach (int h in new[] { 0, 6, 12, 18, 23 })
            {
                var words = LapParts.Ink(c, HistoryModel.HourName(h, use24), label, c.MutedInk, false);
                double x = h * (width + gap) + width / 2 - words.Width / 2;
                dc.DrawText(words, new Point(Math.Max(0, Math.Min(size.Width - words.Width, x)), bottom + 4));
            }
        }
    }

    // One row of the last 7 days table.
    public sealed class HistoryRow { public DateTime Date; public string[] Cells = new string[6]; public bool WaitWarns; }

    // What the History tab shows, worked out from the day files (for the view and the checks).
    public sealed class HistoryModel
    {
        public const int CalendarDays = ClaudeHistory.KeptDays;
        // A wait this long or longer is shown in amber.
        public static readonly TimeSpan LongWait = TimeSpan.FromMinutes(15);
        // The calendar's shades: none, then four steps of the accent (darker is quieter).
        public static readonly double[] Shades = { 0, 0.22, 0.42, 0.7, 1 };

        public bool Sample, Empty;
        public string Missing = "";
        public DateTime Today, FirstMonday;
        public int Hour;
        public readonly Dictionary<DateTime, ClaudeDay> ByDate = new Dictionary<DateTime, ClaudeDay>();
        public DateTime Oldest;
        public long Most;
        public ClaudeDay Busiest;
        public long[] ThisWeek = new long[24], LastWeek = new long[24];
        public bool HasLastWeek;
        public List<HistoryRow> Last7 = new List<HistoryRow>();
        public List<LapShare> Projects = new List<LapShare>();

        public static HistoryModel From(IList<ClaudeDay> days, DateTime now, bool sample, string missing)
        {
            var local = now.ToLocalTime();
            var m = new HistoryModel { Sample = sample, Missing = missing ?? "", Today = local.Date, Hour = local.Hour };
            int fromMonday = ((int)m.Today.DayOfWeek + 6) % 7;
            m.FirstMonday = m.Today.AddDays(-fromMonday - 7 * 7);
            m.Oldest = m.Today.AddDays(-(CalendarDays - 1));
            if (days != null) foreach (var d in days) if (d != null) m.ByDate[d.Date.Date] = d;
            m.Empty = m.Missing.Length == 0 && !m.ByDate.Values.Any(d => d.Tokens > 0 || d.Replies > 0);
            if (m.Missing.Length > 0 || m.Empty) return m;

            m.Busiest = m.ByDate.Values.Where(d => d.Date >= m.Oldest && d.Date <= m.Today).OrderByDescending(d => d.Tokens).ThenByDescending(d => d.Date).FirstOrDefault();
            m.Most = m.Busiest == null ? 0 : m.Busiest.Tokens;
            for (int i = 0; i < 14; i++)
            {
                ClaudeDay d; if (!m.ByDate.TryGetValue(m.Today.AddDays(-i), out d)) continue;
                var into = i < 7 ? m.ThisWeek : m.LastWeek;
                for (int h = 0; h < 24; h++) into[h] += d.Hours[h];
            }
            m.HasLastWeek = m.LastWeek.Any(v => v > 0);

            var garages = new Dictionary<string, LapShare>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < 7; i++)
            {
                DateTime date = m.Today.AddDays(-i);
                ClaudeDay d; m.ByDate.TryGetValue(date, out d);
                if (d == null) d = new ClaudeDay { Date = date };
                var c = CultureInfo.InvariantCulture;
                var row = new HistoryRow { Date = date };
                row.Cells[0] = i == 0 ? "Today" : date.ToString("ddd d", c);
                row.Cells[1] = d.Tokens > 0 ? ClaudeUsage.Format(d.Tokens) : "—";
                row.Cells[2] = d.Cost > 0 ? (d.Unpriced > 0 ? "≥ " : "") + d.Cost.ToString("0.00", c) : d.Unpriced > 0 ? "unpriced" : "—";
                row.Cells[3] = d.Threads.ToString(c);
                row.Cells[4] = d.Needs.ToString(c);
                row.Cells[5] = d.LongestWait > 0 ? Wait(TimeSpan.FromSeconds(d.LongestWait)) : "—";
                row.WaitWarns = d.LongestWait >= LongWait.TotalSeconds;
                m.Last7.Add(row);
                foreach (var g in d.Garages)
                {
                    string key = g.Folder.Length > 0 ? g.Folder.Replace('\\', '/').TrimEnd('/') : g.Name;
                    LapShare share;
                    if (!garages.TryGetValue(key, out share)) { share = new LapShare { Folder = g.Folder, Name = g.Name }; garages[key] = share; }
                    share.Tokens += g.Tokens;
                    if (share.Name.Length == 0) share.Name = g.Name;
                }
            }
            var top = garages.Values.Where(g => g.Tokens > 0).OrderByDescending(g => g.Tokens).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase).Take(6).ToList();
            long first = top.Count == 0 ? 1 : top[0].Tokens;
            foreach (var g in top)
            {
                if (g.Name.Length == 0) g.Name = Leaf(g.Folder);
                g.Share = (double)g.Tokens / first; g.Value = ClaudeUsage.Format(g.Tokens);
            }
            m.Projects = top;
            return m;
        }

        // The calendar cell's shade for a day: -1 before the files kept, 0 for none, 1–4 by its share of the busiest.
        public int Level(DateTime date)
        {
            if (date < Oldest) return -1;
            ClaudeDay d;
            if (!ByDate.TryGetValue(date, out d) || d.Tokens <= 0 || Most <= 0) return 0;
            return Math.Max(1, Math.Min(4, (int)Math.Ceiling(4.0 * d.Tokens / Most)));
        }

        public static string Wait(TimeSpan wait)
        {
            if (wait < TimeSpan.FromMinutes(1)) return (int)wait.TotalSeconds + " s";
            if (wait < TimeSpan.FromHours(1)) return (int)wait.TotalMinutes + " min";
            return (int)wait.TotalHours + " h " + wait.Minutes + " min";
        }
        public static string HourName(int hour, bool use24)
        {
            if (use24) return hour.ToString("00", CultureInfo.InvariantCulture) + ":00";
            return (hour % 12 == 0 ? 12 : hour % 12) + (hour < 12 ? " am" : " pm");
        }
        private static string Leaf(string folder)
        {
            string trimmed = (folder ?? "").Replace('\\', '/').TrimEnd('/');
            int slash = trimmed.LastIndexOf('/');
            return slash >= 0 ? trimmed.Substring(slash + 1) : trimmed;
        }

        public string BusiestLine()
        {
            return Busiest == null || Busiest.Tokens <= 0 ? "" : "Busiest day: " + Busiest.Date.ToString("ddd d MMM", CultureInfo.InvariantCulture) + ", " + ClaudeUsage.Format(Busiest.Tokens);
        }
        public string CalendarWords()
        {
            long total = ByDate.Values.Where(d => d.Date >= Oldest && d.Date <= Today).Sum(d => d.Tokens);
            int active = ByDate.Values.Count(d => d.Date >= Oldest && d.Date <= Today && d.Tokens > 0);
            return "8 weeks of tokens per day: " + active + " days with use, " + ClaudeUsage.Format(total) + " in all. " + BusiestLine();
        }
        public string HourWords()
        {
            int peak = Array.IndexOf(ThisWeek, ThisWeek.Max());
            return "Hour of day this week: busiest hour starts " + HourName(peak, false) + (HasLastWeek ? "; ticks show the same hours last week." : "; no day files for the week before.");
        }

        // ---- The labelled sample ----

        // Eight weeks of made-up days up to today: weekdays busier than weekends, a busy Tuesday, waits and projects.
        public static List<ClaudeDay> SampleDays(DateTime now)
        {
            var today = now.ToLocalTime().Date;
            var list = new List<ClaudeDay>();
            string[] names = { "widget-screen", "api-server", "claude-dashboard", "photo-manager" };
            double[] weights = { 0.5, 0.23, 0.16, 0.11 };
            for (int i = CalendarDays - 1; i >= 0; i--)
            {
                var date = today.AddDays(-i);
                int seed = (date.DayOfYear * 37 + 11) % 100;
                bool weekend = date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
                long tokens = i == 6 ? 9800000 : weekend ? 300000 + seed * 20000L : 1500000 + seed * 70000L;
                if (seed % 9 == 0 && i != 6) tokens = 0;
                var day = new ClaudeDay { Date = date, Tokens = tokens, Replies = (int)(tokens / 12000), Threads = tokens == 0 ? 0 : 2 + seed % 12, Needs = tokens == 0 ? 0 : 1 + seed % 9, LongestWait = tokens == 0 ? 0 : 60 + (seed % 25) * 60, Cost = tokens / 1e6 * 7.8 };
                int last = i == 0 ? now.ToLocalTime().Hour : 23;
                double spread = 0;
                for (int h = 8; h <= Math.Min(last, 23); h++) spread += Profile(h);
                for (int h = 8; h <= Math.Min(last, 23) && spread > 0; h++) day.Hours[h] = (long)(tokens * Profile(h) / spread);
                for (int g = 0; g < names.Length && tokens > 0; g++) day.Garages.Add(new ClaudeDayGarage { Folder = "C:/Code/" + names[g], Name = names[g], Tokens = (long)(tokens * weights[g]), Threads = 1 + g });
                list.Add(day);
            }
            return list;
        }
        private static double Profile(int hour) { return hour < 8 ? 0 : hour < 12 ? 1 + (hour - 8) * 0.4 : hour < 14 ? 1.2 : hour < 18 ? 2 : 1.4 - (hour - 18) * 0.2; }
    }
}

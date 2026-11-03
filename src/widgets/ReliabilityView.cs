using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace ClaudeDashboard
{
    // What the Reliability view reads (decision 030, package E): today's facts and threads, and from the engine's
    // package A (v0.54.0) telemetry's tool runs today, the last day's API errors and today's compactions. Asks per tool
    // today come from the engine's AskedToday. Names, states and times only: never a command, a prompt or a
    // reply.
    public sealed class ReliabilityInput
    {
        public ClaudeFacts Facts = new ClaudeFacts();
        public List<ClaudeSession> Threads = new List<ClaudeSession>();
        // Today's finished tool runs and the last day's API errors, from telemetry. Null: unknown.
        public List<ClaudeToolRun> Runs;
        public List<ClaudeApiError> Errors;
        // Times each tool asked you today, by tool name. Null: not known per tool.
        public Dictionary<string, int> AskedByTool;
        // Today's compactions. Null: unknown.
        public List<ClaudeCompaction> Compactions;
        // Whether telemetry reaches the dashboard, and if not, why in words ("Telemetry off: port 43832 in use").
        public bool Telemetry;
        public string TelemetryWhy = "";
        public bool Sample;

        public static ReliabilityInput From(ClaudeView view, DateTime now)
        {
            if (view == null) return new ReliabilityInput();
            if (view.Sample && view.ToolRunsToday.Count == 0) return Fixture(view, now);
            var input = new ReliabilityInput { Facts = view.FactsFor(ClaudePeriod.Today) ?? new ClaudeFacts(), Threads = view.Threads.ToList(), Telemetry = view.Facts.Telemetry, Sample = view.Sample };
            // Telemetry's runs and errors: known once it has named a tool run or reached the dashboard at all.
            if (view.ToolTimesKnown || input.Telemetry) { input.Runs = view.ToolRunsToday.ToList(); input.Errors = view.ApiErrors.ToList(); }
            input.Compactions = view.Compactions.ToList();
            if (input.Telemetry && view.AskedToday != null) input.AskedByTool = new Dictionary<string, int>(view.AskedToday, StringComparer.Ordinal);
            input.TelemetryWhy = (view.TelemetryWarning ?? "").Length > 0 ? view.TelemetryWarning : !view.Connected ? "Telemetry off: not connected" : "No telemetry has arrived today";
            return input;
        }

        // The labelled sample (until package A's fixtures land): the prototype's numbers on the fixture's threads.
        public static ReliabilityInput Fixture(ClaudeView view, DateTime now)
        {
            var threads = view == null ? new List<ClaudeSession>() : view.Threads.ToList();
            Func<int, string> id = i => threads.Count == 0 ? "sample-" + i : threads[i % threads.Count].Id;
            var runs = new List<ClaudeToolRun>();
            var rng = new Random(30);
            Action<string, int, double, double, int> add = (tool, count, typical, longest, failed) => {
                for (int i = 0; i < count; i++)
                {
                    double ms = i == 0 ? longest : typical * (0.4 + rng.NextDouble() * 1.2);
                    runs.Add(new ClaudeToolRun { At = now.AddMinutes(-rng.Next(5, 400)), Session = id(i), Tool = tool, Ms = ms, Failed = i > 0 && i <= failed });
                }
            };
            add("Read", 243, 100, 900, 0); add("Edit", 135, 300, 1400, 2); add("Bash", 95, 6400, 130000, 9);
            add("Grep", 61, 200, 1100, 0); add("WebFetch", 7, 2800, 11000, 2); add("Task", 3, 180000, 240000, 0);
            var errors = new List<ClaudeApiError> {
                new ClaudeApiError { At = now.AddMinutes(-56), Session = id(4), Status = 529, Attempt = 1 },
                new ClaudeApiError { At = now.AddMinutes(-58), Session = id(4), Status = 529, Attempt = 2 },
                new ClaudeApiError { At = now.AddMinutes(-4), Session = id(1), Status = 429, Attempt = 1 },
                new ClaudeApiError { At = now.AddMinutes(-2), Session = id(1), Status = 429, Attempt = 2 },
                new ClaudeApiError { At = now.AddMinutes(-1), Session = id(1), Status = 429, Attempt = 3 } };
            var facts = new ClaudeFacts { Telemetry = true, Metrics = true, Runs = runs.Count, RunFailures = runs.Count(r => r.Failed), Asked = 41, Denied = 3, EditsAccepted = 188, EditsRejected = 8, Errors = 3, Tools = runs.Count };
            return new ReliabilityInput {
                Facts = facts, Threads = threads, Runs = runs, Errors = errors, Telemetry = true, Sample = true,
                AskedByTool = new Dictionary<string, int> { { "Bash", 28 }, { "Edit", 9 }, { "WebFetch", 4 } },
                Compactions = new List<ClaudeCompaction> { Compaction(id(3), now.AddMinutes(-200)), Compaction(id(2), now.AddMinutes(-120)), Compaction(id(3), now.AddMinutes(-40)), Compaction(id(3), now.AddSeconds(-30)) } };
        }
        private static ClaudeCompaction Compaction(string session, DateTime at) { return new ClaudeCompaction { Session = session, At = at };
        }
    }

    // One tool's row: runs today, the typical (median) and longest run, how many failed and asked you.
    public sealed class ReliabilityTool
    {
        public string Name = "";
        public int Runs, Failed;
        public double Typical = Double.NaN, Longest = Double.NaN;
        // -1: not known per tool.
        public int Asked = -1;
    }

    // A stuck turn today: one run of API errors on a session (a gap of 10 minutes starts another), or a session the
    // hooks say is stuck now. Its reason is in plain words from the error's status or StopFailure's error, never text
    // from the turn.
    public sealed class ReliabilityStuck
    {
        public const string Retrying = "retrying", StuckNow = "stuck", Recovered = "recovered";
        public string Session = "", Title = "", Kind = "", Reason = "";
        public DateTime Start, End;
        public int Tries;
    }

    public static class ClaudeReliability
    {
        public static readonly TimeSpan EpisodeGap = TimeSpan.FromMinutes(10), StripSpan = TimeSpan.FromHours(1);
        public const int MostTools = 8;

        // Local midnight today, in UTC.
        public static DateTime Today(DateTime now) { return now.ToLocalTime().Date.ToUniversalTime(); }

        public static double Median(IEnumerable<double> values)
        {
            var list = values.Where(v => !Double.IsNaN(v)).OrderBy(v => v).ToList();
            if (list.Count == 0) return Double.NaN;
            int middle = list.Count / 2;
            return list.Count % 2 == 1 ? list[middle] : (list[middle - 1] + list[middle]) / 2;
        }

        // Every tool run today, by tool, busiest first.
        public static List<ReliabilityTool> Tools(IEnumerable<ClaudeToolRun> runs, IDictionary<string, int> asked, DateTime now)
        {
            DateTime from = Today(now);
            return (runs ?? new ClaudeToolRun[0]).Where(r => r.At >= from && r.At <= now && !String.IsNullOrEmpty(r.Tool)).GroupBy(r => r.Tool)
                .Select(g => {
                    int ask; bool known = asked != null;
                    if (!known || !asked.TryGetValue(g.Key, out ask)) ask = known ? 0 : -1;
                    var times = g.Select(r => r.Ms).Where(m => !Double.IsNaN(m)).ToList();
                    return new ReliabilityTool { Name = g.Key, Runs = g.Count(), Failed = g.Count(r => r.Failed), Typical = Median(times), Longest = times.Count == 0 ? Double.NaN : times.Max(), Asked = ask };
                })
                .OrderByDescending(t => t.Runs).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // API errors in the hour up to now.
        public static List<ClaudeApiError> LastHour(IEnumerable<ClaudeApiError> errors, DateTime now)
        {
            return (errors ?? new ClaudeApiError[0]).Where(e => e.At > now - StripSpan && e.At <= now).OrderBy(e => e.At).ToList();
        }
        // "429 ×2, 529 ×1"
        public static string ByStatus(IEnumerable<ClaudeApiError> errors)
        {
            return String.Join(", ", errors.GroupBy(e => e.Status).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => (g.Key > 0 ? g.Key.ToString(CultureInfo.InvariantCulture) : "error") + " ×" + g.Count()));
        }

        // What an HTTP status means, in words.
        public static string Words(int status)
        {
            if (status == 429) return "rate limited";
            if (status == 529) return "overloaded (529)";
            if (status == 401 || status == 403) return "sign-in refused (" + status + ")";
            if (status == 400 || status == 413) return "request refused (" + status + ")";
            if (status >= 500) return "server error (" + status + ")";
            return status > 0 ? "API error (" + status + ")" : "API error";
        }

        // Today's stuck turns, newest first.
        public static List<ReliabilityStuck> Stuck(ReliabilityInput input, ClaudeView view, DateTime now, bool use24)
        {
            var result = new List<ReliabilityStuck>();
            DateTime from = Today(now);
            var threads = (input.Threads ?? new List<ClaudeSession>()).GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());
            var withEpisode = new HashSet<string>();
            foreach (var group in (input.Errors ?? new List<ClaudeApiError>()).Where(e => e.At >= from && e.At <= now).GroupBy(e => e.Session))
            {
                ClaudeSession thread; threads.TryGetValue(group.Key ?? "", out thread);
                var ordered = group.OrderBy(e => e.At).ToList();
                var episodes = new List<List<ClaudeApiError>> { new List<ClaudeApiError> { ordered[0] } };
                for (int i = 1; i < ordered.Count; i++)
                {
                    if (ordered[i].At - ordered[i - 1].At > EpisodeGap) episodes.Add(new List<ClaudeApiError>());
                    episodes[episodes.Count - 1].Add(ordered[i]);
                }
                for (int i = 0; i < episodes.Count; i++)
                {
                    var e = episodes[i]; bool last = i == episodes.Count - 1;
                    int status = e.GroupBy(x => x.Status).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Max(x => x.At)).First().Key;
                    var item = new ReliabilityStuck { Session = group.Key ?? "", Title = Title(thread, group.Key, now, use24), Start = e[0].At, End = e[e.Count - 1].At, Tries = Math.Max(e.Count, e.Max(x => x.Attempt)), Reason = Words(status), Kind = ReliabilityStuck.Recovered };
                    if (last && thread != null && thread.Running && thread.State == ClaudeState.Stuck) { item.Kind = ReliabilityStuck.StuckNow; if ((thread.Reason ?? "").Length > 0) item.Reason = Lower(ClaudeText.StuckReason(thread, view, now, use24)); }
                    else if (last && thread != null && thread.Running && (thread.Retrying || thread.Pit == PitState.Stuck) && now - item.End < EpisodeGap) item.Kind = ReliabilityStuck.Retrying;
                    result.Add(item);
                }
                withEpisode.Add(group.Key ?? "");
            }
            // Stuck now with no API error telemetry saw (telemetry off, or StopFailure alone): the hooks' reason.
            foreach (var t in threads.Values.Where(t => t.Running && t.State == ClaudeState.Stuck && !withEpisode.Contains(t.Id)))
                result.Add(new ReliabilityStuck { Session = t.Id, Title = Title(t, t.Id, now, use24), Start = t.Since, End = t.Since, Tries = Math.Max(1, t.ErrorAttempt), Kind = ReliabilityStuck.StuckNow, Reason = Lower(ClaudeText.StuckReason(t, view, now, use24)) });
            return result.OrderBy(s => s.Kind == ReliabilityStuck.Recovered ? 1 : 0).ThenByDescending(s => s.End).ToList();
        }
        private static string Title(ClaudeSession thread, string id, DateTime now, bool use24) { return thread != null ? ClaudeText.ThreadTitle(thread, now, use24) : ClaudeTitles.IdLabel(id ?? ""); }
        private static string Lower(string text) { return String.IsNullOrEmpty(text) ? "" : Char.ToLowerInvariant(text[0]) + text.Substring(1); }

        // "Retrying now · Retry flaky upload test · rate limited, try 3 since 2:42"
        public static string Line(ReliabilityStuck s, DateTime now, bool use24)
        {
            return Head(s, use24) + " · " + s.Title + " · " + Rest(s, use24);
        }
        public static string Head(ReliabilityStuck s, bool use24)
        {
            return s.Kind == ReliabilityStuck.Retrying ? "Retrying now" : s.Kind == ReliabilityStuck.StuckNow ? "Stuck now" : "Recovered " + ClaudeText.Clock(s.End, use24);
        }
        public static string Rest(ReliabilityStuck s, bool use24)
        {
            string tries = s.Kind == ReliabilityStuck.Retrying ? "try " + s.Tries + " since " + ClaudeText.Clock(s.Start, use24) : s.Tries == 1 ? "1 try" : s.Tries + " tries";
            return s.Reason + ", " + tries;
        }

        // A run's time: "0.3 s", "6.4 s", "11 s", "2 min 10 s", "3 min", "1 h 5 min".
        public static string Time(double ms)
        {
            if (Double.IsNaN(ms) || ms < 0) return "—";
            double s = ms / 1000;
            if (s < 10) return s.ToString("0.0", CultureInfo.InvariantCulture) + " s";
            if (s < 60) return Math.Round(s).ToString(CultureInfo.InvariantCulture) + " s";
            var time = TimeSpan.FromSeconds(Math.Round(s));
            if (time.TotalHours >= 1) return (int)time.TotalHours + " h" + (time.Minutes > 0 ? " " + time.Minutes + " min" : "");
            return (int)time.TotalMinutes + " min" + (time.Seconds > 0 ? " " + time.Seconds + " s" : "");
        }

        // The five figures across the top: label, value, line under it, and whether the value is a warning (rose for API
        // errors, Amber for failures) or unknown.
        public sealed class Figure { public string Key = "", Label = "", Value = "", Note = "", Brush = "Text"; }
        public const string Failures = "failures", Errors = "errors", Asked = "asked", Compactions = "compactions", Edits = "edits";

        // "4.2%", "0.4%", "0%": one decimal below 10%, else whole.
        public static string Percent(int part, int whole)
        {
            double p = whole <= 0 ? 0 : 100.0 * part / whole;
            return (p > 0 && p < 10 ? p.ToString("0.0", CultureInfo.InvariantCulture) : Math.Round(p).ToString(CultureInfo.InvariantCulture)) + "%";
        }

        public static List<Figure> Figures(ReliabilityInput input, DateTime now)
        {
            var f = input.Facts ?? new ClaudeFacts();
            var figures = new List<Figure>();
            const string needs = "needs telemetry";
            // Tool failures: telemetry's tool results, else the hooks' failure counts over the logs' calls (≈).
            var failure = new Figure { Key = Failures, Label = "Tool failures" };
            // Telemetry's runs today, the same ones the table lists, when the engine has them.
            var today = input.Runs == null ? null : Tools(input.Runs, null, now);
            int todayRuns = today == null ? 0 : today.Sum(t => t.Runs), todayFailed = today == null ? 0 : today.Sum(t => t.Failed);
            if (todayRuns > 0)
            {
                failure.Value = Percent(todayFailed, todayRuns);
                failure.Note = todayFailed + " failed of " + todayRuns + " runs";
                var worst = today.Where(t => t.Failed > 0).OrderByDescending(t => t.Failed).FirstOrDefault();
                if (worst != null && worst.Failed < todayFailed) failure.Note += ", " + worst.Failed + " of them " + worst.Name;
                else if (worst != null) failure.Note += ", all " + worst.Name;
                if (todayFailed > 0) failure.Brush = "Warning";
            }
            else if (f.Runs > 0)
            {
                failure.Value = (f.FailuresFromHooks ? "≈ " : "") + Percent(f.RunFailures, f.Runs);
                failure.Note = f.RunFailures + " failed of " + f.Runs + " runs";
                if (f.FailuresFromHooks) failure.Note += " · from the hooks";
                if (f.RunFailures > 0) failure.Brush = "Warning";
            }
            else { failure.Value = "—"; failure.Note = input.Telemetry ? "no tool runs today" : needs; failure.Brush = "Muted"; }
            figures.Add(failure);
            // API errors in the last hour.
            var errors = new Figure { Key = Errors, Label = "API errors" };
            if (input.Telemetry || input.Errors != null)
            {
                var hour = input.Errors == null ? null : LastHour(input.Errors, now);
                int count = hour == null ? f.Errors : hour.Count;
                errors.Value = count.ToString(CultureInfo.InvariantCulture);
                errors.Note = "last hour" + (hour != null && hour.Count > 0 ? " · " + ByStatus(hour) : "");
                errors.Brush = count > 0 ? "Rose" : "Text";
            }
            else { errors.Value = "—"; errors.Note = needs; errors.Brush = "Muted"; }
            figures.Add(errors);
            // Asks and denials.
            var asked = new Figure { Key = Asked, Label = "Asked you" };
            if (input.Telemetry)
            {
                asked.Value = (f.AskedPartial ? "≈ " : "") + f.Asked.ToString(CultureInfo.InvariantCulture);
                asked.Note = "all asks: " + Math.Max(0, f.Asked - f.Denied) + " allowed · " + f.Denied + " denied" + (f.AskedPartial ? " · partial" : "");
            }
            else { asked.Value = "—"; asked.Note = needs; asked.Brush = "Muted"; }
            figures.Add(asked);
            // Compactions today, and how many are running now.
            int running = (input.Threads ?? new List<ClaudeSession>()).Count(t => t.Running && t.Compacting);
            var compact = new Figure { Key = Compactions, Label = "Compactions" };
            if (input.Compactions != null)
            {
                DateTime from = Today(now);
                compact.Value = input.Compactions.Count(c => c.At >= from && c.At <= now).ToString(CultureInfo.InvariantCulture);
                compact.Note = "today" + (running > 0 ? " · " + running + " running now" : "");
            }
            else { compact.Value = running > 0 ? running.ToString(CultureInfo.InvariantCulture) : "—"; compact.Note = running > 0 ? "running now · today's count unknown" : "today's count unknown"; compact.Brush = running > 0 ? "Text" : "Muted"; }
            figures.Add(compact);
            // Edits accepted and rejected (telemetry's metrics).
            var edits = new Figure { Key = Edits, Label = "Edits" };
            if (f.Metrics)
            {
                edits.Value = (f.EditsAccepted + f.EditsRejected).ToString(CultureInfo.InvariantCulture);
                edits.Note = f.EditsAccepted + " accepted · " + f.EditsRejected + " rejected";
            }
            else { edits.Value = "—"; edits.Note = input.Telemetry ? "needs telemetry's metrics" : needs; edits.Brush = "Muted"; }
            figures.Add(edits);
            return figures;
        }

        // A view's page: a vertical ScrollViewer that is a named tab stop, so Tab from the tab strip lands in it and the
        // arrows, Page Up and Down, and Home and End scroll it (ScrollViewer's own keys). Controls inside come after it.
        public static ScrollViewer Page(UIElement content, string name)
        {
            var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = true, IsTabStop = true, FocusVisualStyle = CardParts.PageFocus };
            KeyboardNavigation.SetTabNavigation(scroll, KeyboardNavigationMode.Continue);
            AutomationProperties.SetName(scroll, name);
            AutomationProperties.SetHelpText(scroll, "Arrow keys, Page Up, Page Down, Home and End scroll the page.");
            // ScrollViewer's own Home and End go to the left and right ends; on a page that only scrolls down they go
            // to the top and the bottom.
            scroll.PreviewKeyDown += (sender, e) => {
                if (e.Handled || Keyboard.Modifiers != ModifierKeys.None || (e.Key != Key.Home && e.Key != Key.End)) return;
                if (e.Key == Key.Home) scroll.ScrollToTop(); else scroll.ScrollToBottom();
                e.Handled = true;
            };
            return scroll;
        }

        // Rose (API errors and Retrying, decision 030): #F09A9A on the dark palettes, a deeper shade on paper so it reads
        // at 4.5:1 there too.
        public static Brush Rose(FrameworkElement scope)
        {
            var page = scope == null ? null : scope.TryFindResource("Page") as SolidColorBrush;
            bool paper = page != null && (0.2126 * page.Color.R + 0.7152 * page.Color.G + 0.0722 * page.Color.B) / 255 > 0.5;
            var brush = new SolidColorBrush(paper ? Color.FromRgb(0xA9, 0x3B, 0x35) : Color.FromRgb(0xF0, 0x9A, 0x9A)); brush.Freeze();
            return brush;
        }
    }

    // The API error strip: the last hour as a line, one 2-unit rose mark per failed request, its status above it (and
    // the attempt when it was a retry).
    public sealed class ApiErrorStrip : FrameworkElement
    {
        public List<ClaudeApiError> Errors = new List<ClaudeApiError>();
        public DateTime Now = DateTime.UtcNow;
        public double TextScale = 1;
        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            var line = TryFindResource("ControlBorder") as Brush ?? Brushes.Gray;
            dc.DrawRectangle(line, null, new Rect(0, h - 1, w, 1));
            var rose = ClaudeReliability.Rose(this);
            double size = Math.Round(11 * TextScale, 1), dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            double lastLabel = Double.NegativeInfinity;
            foreach (var e in Errors.OrderBy(x => x.At))
            {
                double f = 1 - (Now - e.At).TotalSeconds / ClaudeReliability.StripSpan.TotalSeconds;
                if (f < 0 || f > 1) continue;
                double x = Math.Round(Math.Max(1, Math.Min(w - 1, f * w)));
                double mark = Math.Max(12, h * 0.5);
                dc.DrawRectangle(rose, null, new Rect(x - 1, h - 1 - mark, 2, mark));
                var words = new FormattedText((e.Status > 0 ? e.Status.ToString(CultureInfo.InvariantCulture) : "error") + (e.Attempt > 1 ? " ·" + e.Attempt : ""), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, PitInstrument.Ui, size, rose, dip);
                double left = Math.Max(0, Math.Min(w - words.Width, x - words.Width / 2));
                // Labels that would overlap the one before step aside; the marks still show every request.
                if (left < lastLabel + 2) continue;
                dc.DrawText(words, new Point(left, Math.Max(0, h - 1 - mark - words.Height - 2)));
                lastLabel = left + words.Width;
            }
        }
    }

    // Reliability (decision 030, package E): how often things go wrong today. Five figures (tool failures, API errors in
    // the last hour, asks and denials, compactions, edits), the API error strip, every tool across all sessions (runs,
    // typical and longest time, failures, asks) and today's stuck turns with their reasons. With telemetry off it says
    // so and shows the logs' tool count.
    public sealed class ReliabilityView : IClaudeView
    {
        public string Name { get { return ClaudeViews.Reliability; } }
        public bool Visible { get; set; }

        private ClaudeViewContext context = new ClaudeViewContext();
        private Size room;
        private Grid root;
        private readonly List<TextBlock[]> figures = new List<TextBlock[]>();
        private ApiErrorStrip strip;
        private TextBlock stripNote, stripStart, stripMiddle, stripEnd, sample;
        private Grid tools; private StackPanel stuck;
        private string toolsKey, stuckKey;
        // For the checks: the last report shown.
        public List<ReliabilityTool> ToolsShown { get; private set; }
        public List<ReliabilityStuck> StuckShown { get; private set; }
        public List<ClaudeReliability.Figure> FiguresShown { get; private set; }

        private double S(double size) { return context.Scaled(size); }
        private bool Wide { get { return room.Width >= 1000; } }

        public FrameworkElement Build(ClaudeViewContext context, Size room)
        {
            this.context = context ?? new ClaudeViewContext(); this.room = room;
            figures.Clear(); toolsKey = null; stuckKey = null;
            var stack = new StackPanel { Margin = new Thickness(0, 0, 4, 8) };
            // Heading, with Sample at its right when the fixture shows.
            var head = CardParts.Columns(CardParts.Star, CardParts.Auto);
            var title = CardParts.Text("Reliability today", S(18), "Text"); title.FontWeight = FontWeights.SemiBold;
            head.Children.Add(title);
            sample = CardParts.Text("Sample", S(12), "Muted"); sample.FontWeight = FontWeights.SemiBold; sample.VerticalAlignment = VerticalAlignment.Center; sample.Visibility = this.context.Sample ? Visibility.Visible : Visibility.Collapsed;
            head.Children.Add(CardParts.At(sample, 0, 1));
            stack.Children.Add(head);
            // The five figures: one row from 900 wide, else three and two.
            var grid = new UniformGrid { Columns = room.Width >= 900 ? 5 : 3, Margin = new Thickness(0, 10, 0, 0) };
            foreach (string label in new[] { "Tool failures", "API errors", "Asked you", "Compactions", "Edits" })
            {
                var cell = new StackPanel();
                var key = CardParts.Text(label.ToUpperInvariant(), S(12), "Muted"); key.TextTrimming = TextTrimming.CharacterEllipsis;
                var value = CardParts.Reading("—", S(30)); value.SetResourceReference(TextBlock.ForegroundProperty, "Text");
                var note = CardParts.Text("", S(12), "Muted"); note.TextWrapping = TextWrapping.Wrap;
                cell.Children.Add(key); cell.Children.Add(value); cell.Children.Add(note);
                var tile = CardParts.Tile(cell, 12, new Thickness(14, 10, 14, 12)); tile.Margin = new Thickness(0, 0, 10, 10);
                AutomationProperties.SetName(tile, label);
                figures.Add(new[] { key, value, note });
                grid.Children.Add(tile);
            }
            stack.Children.Add(grid);
            // The API error strip.
            var stripStack = new StackPanel();
            var stripHead = new WrapPanel();
            var stripTitle = CardParts.Text("API errors, last 60 minutes", S(16), "Text"); stripTitle.FontWeight = FontWeights.SemiBold; stripTitle.Margin = new Thickness(0, 0, 12, 0);
            stripNote = CardParts.Text("", S(13), "Muted"); stripNote.VerticalAlignment = VerticalAlignment.Bottom; stripNote.TextWrapping = TextWrapping.Wrap;
            stripHead.Children.Add(stripTitle); stripHead.Children.Add(stripNote);
            stripStack.Children.Add(stripHead);
            strip = new ApiErrorStrip { Height = Math.Round(40 + S(11) * 1.4), Margin = new Thickness(0, 8, 0, 0), TextScale = this.context.TextScale };
            AutomationProperties.SetName(strip, "API errors in the last hour");
            stripStack.Children.Add(strip);
            var axis = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            stripStart = CardParts.Text("", S(11), "Muted"); stripMiddle = CardParts.Text("", S(11), "Muted"); stripMiddle.HorizontalAlignment = HorizontalAlignment.Center;
            stripEnd = CardParts.Text("", S(11), "Muted"); stripEnd.HorizontalAlignment = HorizontalAlignment.Right;
            axis.Children.Add(stripStart); axis.Children.Add(stripMiddle); axis.Children.Add(stripEnd);
            stripStack.Children.Add(axis);
            var stripTile = CardParts.Tile(stripStack, 12, new Thickness(16, 12, 16, 10)); stripTile.Margin = new Thickness(0, 0, 10, 10);
            stack.Children.Add(stripTile);
            // Tools and stuck turns: side by side from 1000 wide, else one under the other.
            tools = new Grid(); stuck = new StackPanel();
            var toolsTile = Section("Tools today, all sessions", tools);
            var stuckTile = Section("Stuck turns today", stuck);
            if (Wide)
            {
                var pair = CardParts.Columns(new GridLength(1.5, GridUnitType.Star), new GridLength(1, GridUnitType.Star));
                pair.Children.Add(toolsTile); pair.Children.Add(CardParts.At(stuckTile, 0, 1));
                stack.Children.Add(pair);
            }
            else { stack.Children.Add(toolsTile); stack.Children.Add(stuckTile); }
            var scroll = ClaudeReliability.Page(stack, "Reliability");
            root = new Grid(); root.Children.Add(scroll);
            return root;
        }

        private Border Section(string heading, Panel body)
        {
            var stack = new StackPanel();
            var title = CardParts.Text(heading, S(16), "Text"); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0, 0, 0, 8);
            stack.Children.Add(title); stack.Children.Add(body);
            var tile = CardParts.Tile(stack, 12, new Thickness(16, 12, 16, 14)); tile.Margin = new Thickness(0, 0, 10, 10);
            return tile;
        }

        public void Refresh(ClaudeView snapshot, DateTime now)
        {
            if (root == null) return;
            var input = ReliabilityInput.From(snapshot, now);
            bool use24 = context.Use24Hour;
            sample.Visibility = context.Sample || input.Sample ? Visibility.Visible : Visibility.Collapsed;
            // Figures.
            FiguresShown = ClaudeReliability.Figures(input, now);
            for (int i = 0; i < figures.Count && i < FiguresShown.Count; i++)
            {
                var f = FiguresShown[i];
                figures[i][1].Text = f.Value; figures[i][2].Text = f.Note;
                if (f.Brush == "Rose") figures[i][1].Foreground = ClaudeReliability.Rose(root); else figures[i][1].SetResourceReference(TextBlock.ForegroundProperty, f.Brush);
                AutomationProperties.SetName((FrameworkElement)((FrameworkElement)figures[i][0].Parent).Parent, f.Label + ": " + f.Value + ", " + f.Note);
            }
            // The strip.
            var hour = ClaudeReliability.LastHour(input.Errors, now);
            strip.Errors = hour; strip.Now = now; strip.InvalidateVisual();
            stripStart.Text = ClaudeText.Clock(now - ClaudeReliability.StripSpan, use24); stripMiddle.Text = ClaudeText.Clock(now - TimeSpan.FromMinutes(30), use24); stripEnd.Text = "now " + ClaudeText.Clock(now, use24);
            stripNote.Text = input.Errors == null && !input.Telemetry ? input.TelemetryWhy + ". API errors come from telemetry." : input.Errors == null ? "Waiting for telemetry's first event; " + input.Facts.Errors + " in the last hour." : hour.Count == 0 ? "No API errors in the last hour." : "Each mark is one failed request; a retry's attempt is beside its status.";
            AutomationProperties.SetName(strip, hour.Count == 0 ? "No API errors in the last hour" : hour.Count + " API errors in the last hour: " + ClaudeReliability.ByStatus(hour));
            // The tools table and the stuck turns, rebuilt only when what they show changes.
            ToolsShown = input.Runs == null ? null : ClaudeReliability.Tools(input.Runs, input.AskedByTool, now);
            string key = ToolsShown == null ? "none|" + input.Telemetry + "|" + input.Facts.Tools : String.Join(";", ToolsShown.Select(t => t.Name + "," + t.Runs + "," + ClaudeReliability.Time(t.Typical) + "," + ClaudeReliability.Time(t.Longest) + "," + t.Failed + "," + t.Asked));
            if (key != toolsKey) { toolsKey = key; FillTools(input); }
            StuckShown = ClaudeReliability.Stuck(input, snapshot, now, use24);
            string skey = String.Join(";", StuckShown.Select(s => ClaudeReliability.Line(s, now, use24))) + "|" + input.Telemetry;
            if (skey != stuckKey) { stuckKey = skey; FillStuck(input, now, use24); }
        }

        private void FillTools(ReliabilityInput input)
        {
            tools.Children.Clear(); tools.RowDefinitions.Clear(); tools.ColumnDefinitions.Clear();
            if (ToolsShown == null || ToolsShown.Count == 0)
            {
                string words = !input.Telemetry
                    ? "Run times, failures and asks come from telemetry, which is off. Tool calls in the logs today: " + input.Facts.Tools.ToString(CultureInfo.InvariantCulture) + "."
                    : ToolsShown == null ? "Tool calls today: " + input.Facts.Tools.ToString(CultureInfo.InvariantCulture) + ". Each tool's runs and times show once telemetry names a tool run."
                    : "No tool runs today.";
                var message = CardParts.Text(words, S(13), "Muted"); message.TextWrapping = TextWrapping.Wrap;
                tools.Children.Add(message);
                AutomationProperties.SetName(tools, words);
                return;
            }
            string[] heads = { "Tool", "Runs", "Typical", "Longest", "Failed", "Asked you" };
            tools.ColumnDefinitions.Add(new ColumnDefinition { Width = CardParts.Star, MinWidth = S(70) });
            for (int c = 1; c < heads.Length; c++) tools.ColumnDefinitions.Add(new ColumnDefinition { Width = CardParts.Auto });
            int shown = Math.Min(ClaudeReliability.MostTools, ToolsShown.Count);
            for (int r = 0; r <= shown + (ToolsShown.Count > shown ? 1 : 0); r++) tools.RowDefinitions.Add(new RowDefinition { Height = CardParts.Auto });
            for (int c = 0; c < heads.Length; c++) tools.Children.Add(CardParts.At(Cell(heads[c], c, "Muted"), 0, c));
            for (int i = 0; i < shown; i++)
            {
                var t = ToolsShown[i];
                string[] values = { t.Name, t.Runs.ToString(CultureInfo.InvariantCulture), ClaudeReliability.Time(t.Typical), ClaudeReliability.Time(t.Longest), t.Failed.ToString(CultureInfo.InvariantCulture), t.Asked < 0 ? "—" : t.Asked.ToString(CultureInfo.InvariantCulture) };
                for (int c = 0; c < values.Length; c++) tools.Children.Add(CardParts.At(Cell(values[c], c, c == 4 && t.Failed > 0 ? "Warning" : c == 5 && t.Asked < 0 ? "Muted" : "Text"), i + 1, c));
            }
            if (ToolsShown.Count > shown)
            {
                var more = CardParts.Text("+" + (ToolsShown.Count - shown) + " more tools", S(12), "Muted"); more.Margin = new Thickness(0, 6, 0, 0);
                tools.Children.Add(CardParts.At(more, shown + 1, 0, heads.Length));
            }
            // The card's Asked you is every ask; this column is the asks that named their tool.
            if (ToolsShown.Any(t => t.Asked >= 0))
            {
                tools.RowDefinitions.Add(new RowDefinition { Height = CardParts.Auto });
                var key = CardParts.Text("Asked you here counts the asks that named their tool; the card above counts every ask.", S(12), "Muted");
                key.TextWrapping = TextWrapping.Wrap; key.Margin = new Thickness(0, 6, 0, 0);
                tools.Children.Add(CardParts.At(key, tools.RowDefinitions.Count - 1, 0, heads.Length));
            }
            AutomationProperties.SetName(tools, "Tools today: " + String.Join("; ", ToolsShown.Take(shown).Select(t => t.Name + ", " + t.Runs + " runs, typical " + ClaudeReliability.Time(t.Typical) + ", longest " + ClaudeReliability.Time(t.Longest) + ", " + t.Failed + " failed" + (t.Asked >= 0 ? ", asked you " + t.Asked : ""))));
        }
        private TextBlock Cell(string text, int column, string brush)
        {
            var block = CardParts.Text(text, S(13), brush);
            Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
            block.Margin = new Thickness(column == 0 ? 0 : S(14), 3, 0, 3);
            block.TextAlignment = column == 0 ? TextAlignment.Left : TextAlignment.Right;
            block.HorizontalAlignment = column == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            if (column == 0) block.TextTrimming = TextTrimming.CharacterEllipsis;
            return block;
        }

        private void FillStuck(ReliabilityInput input, DateTime now, bool use24)
        {
            stuck.Children.Clear();
            if (StuckShown.Count == 0)
                stuck.Children.Add(Wrapped(input.Telemetry ? "No stuck turns today." : "No session is stuck now. Earlier ones today show when telemetry is on.", "Muted"));
            foreach (var s in StuckShown.Take(6))
            {
                var line = Wrapped("", "Text"); line.Margin = new Thickness(0, 0, 0, 8);
                var head = new Run(ClaudeReliability.Head(s, use24));
                if (s.Kind == ReliabilityStuck.Retrying) head.Foreground = ClaudeReliability.Rose(root);
                else head.SetResourceReference(TextElement.ForegroundProperty, s.Kind == ReliabilityStuck.StuckNow ? "Warning" : "Muted");
                line.Inlines.Add(head);
                line.Inlines.Add(new Run(" · " + s.Title + " · " + ClaudeReliability.Rest(s, use24)));
                AutomationProperties.SetName(line, ClaudeReliability.Line(s, now, use24));
                stuck.Children.Add(line);
            }
            if (StuckShown.Count > 6) stuck.Children.Add(Wrapped("+" + (StuckShown.Count - 6) + " more earlier today", "Muted"));
            stuck.Children.Add(Wrapped("Reasons come from the error's status or the turn's own StopFailure, in plain words, never the reply.", "Muted"));
        }
        private TextBlock Wrapped(string text, string brush)
        {
            var block = CardParts.Text(text, S(13), brush); block.TextWrapping = TextWrapping.Wrap;
            return block;
        }
    }
}

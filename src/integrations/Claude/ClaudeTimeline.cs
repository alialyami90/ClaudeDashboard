using System;
using System.Collections.Generic;
using System.Linq;

namespace ClaudeDashboard
{
    // The lap chart's engine (package A, v0.54.0; the lap chart build brief, decision 030). Everything here lives in
    // memory only and holds names, states and times, never prompts, replies, commands or file contents. Nothing is
    // written to disk, and nothing here polls: ClaudeService feeds it from the watchers, telemetry and the plan check.

    // A lane's state on the lap chart, most urgent first (the brief's lane order: Needs you, Retrying, Silent, Your
    // turn, Compacting, Working, then Idle; Stuck sits beside Needs you as in the attention queue, Ended last).
    public enum ClaudeLaneState { NeedsYou, Stuck, Retrying, Silent, YourTurn, Compacting, Working, Idle, Ended }

    // One change on a session's trail: from At it was in State, until the next mark.
    public sealed class ClaudeStateMark
    {
        public DateTime At;
        public ClaudeLaneState State;
    }

    // One plan reading: the 5-hour window's percent at a time, and when that window resets.
    public sealed class ClaudePlanReading
    {
        public DateTime At;
        public double Percent;
        public DateTime? ResetsAt;
    }

    // "≈ limit" at one pace: when the 5-hour window reaches 100% (LimitAt), or, when it doesn't before the reset, where
    // it stands at the reset (AtReset). Known is false with fewer than 3 readings, and Why says so.
    public sealed class ClaudeLimitProjection
    {
        public const string Pace = "pace", Average = "average";
        public string Basis = Pace;
        public bool Known;
        public string Why = "";
        public int Readings;
        // Percent an hour at this pace, the time it reaches 100% (null: not before the reset), and the percent at reset.
        public double PerHour = Double.NaN, AtReset = Double.NaN;
        public DateTime? LimitAt, ResetsAt;
        public bool BeforeReset { get { return Known && LimitAt.HasValue; } }
    }

    // A tool's run times from telemetry: how many runs, how many failed, the median and the longest.
    public sealed class ClaudeToolTime
    {
        public string Tool = "";
        public int Runs, Failed;
        public double MedianMs = Double.NaN, LongestMs = Double.NaN;
    }

    // One line of "What just happened": a state change, a tool failure, an API error or a compaction. Session is the
    // id; Title and Project are filled from the threads when the snapshot is made. Detail is a tool name, "HTTP 529 ·
    // attempt 2" or empty: never a prompt, reply, command or file content.
    public sealed class ClaudeFeedItem
    {
        public const string Needs = "needs", Stuck = "stuck", Retrying = "retrying", Silent = "silent", Turn = "turn", Compacting = "compacting",
            Working = "working", Idle = "idle", Ended = "ended", ToolFailed = "tool_failed", ApiError = "api_error";
        public DateTime At;
        public string Session = "", Kind = "", Detail = "", Title = "", Project = "";
        public ClaudeFeedItem Copy() { return (ClaudeFeedItem)MemberwiseClone(); }
        public static string KindOf(ClaudeLaneState state)
        {
            switch (state)
            {
                case ClaudeLaneState.NeedsYou: return Needs;
                case ClaudeLaneState.Stuck: return Stuck;
                case ClaudeLaneState.Retrying: return Retrying;
                case ClaudeLaneState.Silent: return Silent;
                case ClaudeLaneState.YourTurn: return Turn;
                case ClaudeLaneState.Compacting: return Compacting;
                case ClaudeLaneState.Working: return Working;
                case ClaudeLaneState.Ended: return Ended;
                default: return Idle;
            }
        }
    }

    // A compaction: which session and when it started (Reliability counts today's).
    public sealed class ClaudeCompaction { public DateTime At; public string Session = ""; }

    public sealed class ClaudeTimeline
    {
        // The trails and the plan readings cover the last five hours; the feed keeps 50 lines; compactions a day.
        public static readonly TimeSpan Kept = TimeSpan.FromHours(5), CompactionsKept = TimeSpan.FromHours(24);
        public static readonly TimeSpan PaceWindow = TimeSpan.FromMinutes(30);
        public const int FeedKept = 50, FeedHeld = 500, ReadingsKept = 600, SessionsKept = 300, MinReadings = 3;
        // Sessions in projects Settings hides (decision 020): their lines never enter the feed. The service sets this with
        // each view; lines that came before it knew are filtered out when the snapshot is made, before the cap of 50.
        private HashSet<string> hidden = new HashSet<string>(StringComparer.Ordinal);
        public void Hide(IEnumerable<string> sessions) { lock (gate) hidden = new HashSet<string>(sessions ?? Enumerable.Empty<string>(), StringComparer.Ordinal); }
        // When the app began watching: no lane starts before it, and the chart draws that edge.
        public readonly DateTime WatchStart;
        private readonly object gate = new object();
        private readonly Dictionary<string, List<ClaudeStateMark>> marks = new Dictionary<string, List<ClaudeStateMark>>(StringComparer.Ordinal);
        private readonly List<ClaudePlanReading> readings = new List<ClaudePlanReading>();
        private readonly List<ClaudeFeedItem> feed = new List<ClaudeFeedItem>();
        private readonly List<ClaudeCompaction> compactions = new List<ClaudeCompaction>();

        public ClaudeTimeline(DateTime watchStart) { WatchStart = watchStart; }

        // ----- The state timeline -----

        // A session's lane state now, and since when, from what the snapshot knows of it.
        public static KeyValuePair<ClaudeLaneState, DateTime> LaneOf(ClaudeSession t, DateTime now)
        {
            if (!t.Running) return Pair(ClaudeLaneState.Ended, t.LastActive > DateTime.MinValue ? t.LastActive : t.Since);
            if (t.State == ClaudeState.NeedsYou) return Pair(ClaudeLaneState.NeedsYou, t.Since);
            if (t.State == ClaudeState.Stuck) return Pair(ClaudeLaneState.Stuck, t.Since);
            if (t.Retrying) return Pair(ClaudeLaneState.Retrying, t.ErrorAt > DateTime.MinValue ? t.ErrorAt : now);
            if (t.Compacting) return Pair(ClaudeLaneState.Compacting, t.Since);
            bool working = t.State == ClaudeState.Working || t.Pit == PitState.Work || t.Pit == PitState.Rev;
            if (working && t.LastSign > DateTime.MinValue && now - t.LastSign >= ClaudeAttention.SilentAfter) return Pair(ClaudeLaneState.Silent, t.LastSign + ClaudeAttention.SilentAfter);
            if (working) return Pair(ClaudeLaneState.Working, t.Since);
            if (t.YourTurn) return Pair(ClaudeLaneState.YourTurn, t.Since);
            return Pair(ClaudeLaneState.Idle, t.Since);
        }
        private static KeyValuePair<ClaudeLaneState, DateTime> Pair(ClaudeLaneState s, DateTime at) { return new KeyValuePair<ClaudeLaneState, DateTime>(s, at); }

        // Records a session's state from at (clamped to the watch start, now, and its last mark). Returns whether it
        // changed. A change after the watch began goes on the feed; a compaction is counted.
        public bool Note(string session, DateTime at, ClaudeLaneState state, DateTime now)
        {
            if (String.IsNullOrEmpty(session)) return false;
            lock (gate)
            {
                List<ClaudeStateMark> list;
                if (!marks.TryGetValue(session, out list))
                {
                    // An ended session the timeline never saw running has no lane.
                    if (state == ClaudeLaneState.Ended) return false;
                    if (marks.Count >= SessionsKept) Forget(now);
                    marks[session] = list = new List<ClaudeStateMark>();
                }
                var last = list.Count > 0 ? list[list.Count - 1] : null;
                if (last != null && last.State == state) return false;
                if (at < WatchStart) at = WatchStart;
                if (at > now) at = now;
                if (last != null && at < last.At) at = last.At;
                if (last != null && at == last.At) last.State = state;
                else list.Add(new ClaudeStateMark { At = at, State = state });
                // A session already running when the watch began joins at the edge, which is no news.
                bool news = last != null || at > WatchStart;
                if (news) AddFeed(new ClaudeFeedItem { At = at, Session = session, Kind = ClaudeFeedItem.KindOf(state) });
                if (state == ClaudeLaneState.Compacting && news) { compactions.Add(new ClaudeCompaction { At = at, Session = session }); compactions.RemoveAll(c => c.At < now - CompactionsKept); }
                Prune(list, now);
                return true;
            }
        }
        // Drops what fell out of the five hours, keeping the mark the trail starts in.
        private static void Prune(List<ClaudeStateMark> list, DateTime now)
        {
            DateTime cutoff = now - Kept;
            int drop = 0;
            while (drop + 1 < list.Count && list[drop + 1].At <= cutoff) drop++;
            if (drop > 0) list.RemoveRange(0, drop);
        }
        // Ended sessions whose last change is older than five hours have nothing left to draw.
        private void Forget(DateTime now)
        {
            foreach (var key in marks.Where(p => p.Value.Count == 0 || (p.Value[p.Value.Count - 1].State == ClaudeLaneState.Ended && p.Value[p.Value.Count - 1].At < now - Kept)).Select(p => p.Key).ToList()) marks.Remove(key);
            if (marks.Count >= SessionsKept) foreach (var key in marks.OrderBy(p => p.Value[p.Value.Count - 1].At).Take(marks.Count - SessionsKept + 1).Select(p => p.Key).ToList()) marks.Remove(key);
        }
        public IEnumerable<string> Tracked { get { lock (gate) return marks.Keys.ToList(); } }
        // Each session's trail for the last five hours, oldest first; the first mark starts no earlier than the five
        // hours or the watch start.
        public Dictionary<string, List<ClaudeStateMark>> Trails(DateTime now)
        {
            DateTime from = now - Kept > WatchStart ? now - Kept : WatchStart;
            var result = new Dictionary<string, List<ClaudeStateMark>>(StringComparer.Ordinal);
            lock (gate)
                foreach (var pair in marks)
                {
                    Prune(pair.Value, now);
                    if (pair.Value.Count == 0) continue;
                    var last = pair.Value[pair.Value.Count - 1];
                    if (last.State == ClaudeLaneState.Ended && last.At < from) continue;
                    result[pair.Key] = pair.Value.Select(m => new ClaudeStateMark { At = m.At < from ? from : m.At, State = m.State }).ToList();
                }
            return result;
        }

        // ----- The feed -----

        // A tool failure or an API error from telemetry, or anything else worth a line.
        public void Event(DateTime at, string session, string kind, string detail)
        {
            lock (gate) AddFeed(new ClaudeFeedItem { At = at, Session = session ?? "", Kind = kind, Detail = detail ?? "" });
        }
        private void AddFeed(ClaudeFeedItem item)
        {
            if (hidden.Contains(item.Session)) return;
            int i = feed.Count;
            while (i > 0 && feed[i - 1].At > item.At) i--;
            feed.Insert(i, item);
            if (feed.Count > FeedHeld) feed.RemoveRange(0, feed.Count - FeedHeld);
        }
        // The last 50 lines, newest first (of every session not hidden; the snapshot filters to the shown threads first).
        public List<ClaudeFeedItem> Feed { get { return Lines(null); } }
        public List<ClaudeFeedItem> Lines(Func<ClaudeFeedItem, bool> shown)
        {
            lock (gate) return Enumerable.Reverse(feed).Where(f => !hidden.Contains(f.Session) && (shown == null || shown(f))).Take(FeedKept).Select(f => f.Copy()).ToList();
        }
        public List<ClaudeCompaction> Compactions(DateTime from) { lock (gate) return compactions.Where(c => c.At >= from).Select(c => new ClaudeCompaction { At = c.At, Session = c.Session }).ToList(); }

        // ----- The plan reading ring -----

        // A 5-hour reading from the status line or the plan check. The same reading twice, or one older than the
        // newest, adds nothing; an unchanged percent within a minute of the last reading adds nothing either.
        public bool Reading(DateTime at, double percent, DateTime? resetsAt, DateTime now)
        {
            if (Double.IsNaN(percent) || at == DateTime.MinValue || at > now + TimeSpan.FromMinutes(1)) return false;
            lock (gate)
            {
                var last = readings.Count > 0 ? readings[readings.Count - 1] : null;
                if (last != null && at <= last.At) return false;
                if (last != null && last.Percent == percent && SameWindow(last.ResetsAt, resetsAt) && at - last.At < TimeSpan.FromMinutes(1)) return false;
                readings.Add(new ClaudePlanReading { At = at, Percent = percent, ResetsAt = resetsAt });
                readings.RemoveAll(r => r.At < now - Kept);
                if (readings.Count > ReadingsKept) readings.RemoveRange(0, readings.Count - ReadingsKept);
                return true;
            }
        }
        public List<ClaudePlanReading> Readings(DateTime now)
        {
            lock (gate) return readings.Where(r => r.At >= now - Kept).Select(r => new ClaudePlanReading { At = r.At, Percent = r.Percent, ResetsAt = r.ResetsAt }).ToList();
        }
        // Reset times move by a few seconds between readings of the same window.
        public static bool SameWindow(DateTime? a, DateTime? b)
        {
            if (!a.HasValue || !b.HasValue) return a.HasValue == b.HasValue;
            return Math.Abs((a.Value - b.Value).TotalMinutes) < 5;
        }
        // The readings of the window that resets at resetsAt (all of them when the reset is unknown), oldest first.
        public static List<ClaudePlanReading> InWindow(IEnumerable<ClaudePlanReading> all, DateTime? resetsAt)
        {
            var list = (all ?? Enumerable.Empty<ClaudePlanReading>()).OrderBy(r => r.At).ToList();
            if (!resetsAt.HasValue) return list;
            DateTime start = resetsAt.Value - ClaudePace.FiveHours;
            return list.Where(r => r.At >= start && (!r.ResetsAt.HasValue || SameWindow(r.ResetsAt, resetsAt))).ToList();
        }

        // "≈ limit" at the last 30 minutes' pace, from the readings: the percent climbed since the reading at or just
        // before 30 minutes ago, per hour.
        public static ClaudeLimitProjection AtPace(IEnumerable<ClaudePlanReading> all, ClaudeLimitWindow window, DateTime now)
        {
            var p = new ClaudeLimitProjection { Basis = ClaudeLimitProjection.Pace };
            var list = Start(p, all, window, now);
            if (list == null) return p;
            var last = list[list.Count - 1];
            DateTime from = now - PaceWindow;
            var start = list.LastOrDefault(r => r.At <= from) ?? list[0];
            if (start == last) start = list[0];
            double hours = (last.At - start.At).TotalHours;
            if (hours <= 0) { p.Known = false; p.Why = "The plan readings all came at once"; return p; }
            return Finish(p, last, (last.Percent - start.Percent) / hours, window);
        }
        // "≈ limit" at today's average pace, given in percent an hour (worked out from today's tokens), from the newest
        // reading. Still needs 3 readings, so both projections appear together.
        public static ClaudeLimitProjection AtAverage(IEnumerable<ClaudePlanReading> all, ClaudeLimitWindow window, DateTime now, double perHour)
        {
            var p = new ClaudeLimitProjection { Basis = ClaudeLimitProjection.Average };
            var list = Start(p, all, window, now);
            if (list == null) return p;
            if (Double.IsNaN(perHour) || perHour < 0) { p.Known = false; p.Why = "No use today to average"; return p; }
            return Finish(p, list[list.Count - 1], perHour, window);
        }
        private static List<ClaudePlanReading> Start(ClaudeLimitProjection p, IEnumerable<ClaudePlanReading> all, ClaudeLimitWindow window, DateTime now)
        {
            if (window == null) { p.Why = "No 5-hour reading"; return null; }
            p.ResetsAt = window.ResetsAt;
            if (!window.Current(now)) { p.Why = "The 5-hour window has reset"; return null; }
            var list = InWindow(all, window.ResetsAt);
            p.Readings = list.Count;
            if (list.Count < MinReadings) { p.Why = list.Count == 0 ? "No plan readings yet: ≈ limit needs 3" : "Only " + list.Count + " plan reading" + (list.Count == 1 ? "" : "s") + " so far: ≈ limit needs 3"; return null; }
            return list;
        }
        private static ClaudeLimitProjection Finish(ClaudeLimitProjection p, ClaudePlanReading last, double perHour, ClaudeLimitWindow window)
        {
            p.Known = true; p.PerHour = perHour;
            if (last.Percent >= 100) { p.LimitAt = last.At; p.AtReset = last.Percent; return p; }
            DateTime? reset = window.ResetsAt;
            if (perHour > 1e-6)
            {
                double hours = (100 - last.Percent) / perHour;
                DateTime at = last.At.AddHours(Math.Min(hours, 24 * 7));
                if ((!reset.HasValue && hours <= 24 * 7) || (reset.HasValue && at < reset.Value)) { p.LimitAt = at; p.AtReset = Double.NaN; if (reset.HasValue) p.AtReset = last.Percent + perHour * (reset.Value - last.At).TotalHours; return p; }
            }
            p.LimitAt = null;
            p.AtReset = reset.HasValue ? last.Percent + Math.Max(0, perHour) * Math.Max(0, (reset.Value - last.At).TotalHours) : last.Percent;
            return p;
        }

        // ----- The snapshot -----

        // Puts the trails, readings, projections, compactions and feed on the view, and each thread's lane now. Only the
        // view's threads count: a project Settings hides has no trail or feed line.
        public void Fill(ClaudeView view, DateTime now, double averagePerHour)
        {
            var byId = new Dictionary<string, ClaudeSession>(StringComparer.Ordinal);
            foreach (var t in view.Threads) if (!byId.ContainsKey(t.Id)) byId[t.Id] = t;
            view.WatchStart = WatchStart;
            view.Trails = Trails(now).Where(p => byId.ContainsKey(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            view.PlanReadings = Readings(now);
            var window = view.Limits == null ? null : view.Limits.FiveHour;
            view.LimitAtPace = AtPace(view.PlanReadings, window, now);
            view.LimitAtAverage = AtAverage(view.PlanReadings, window, now, averagePerHour);
            DateTime today; if (!view.Starts.TryGetValue(ClaudePeriod.Today, out today)) today = now.Date;
            view.Compactions = Compactions(today).Where(c => byId.ContainsKey(c.Session)).ToList();
            // Only the shown threads' lines, filtered before the 50 are counted.
            var lines = Lines(f => byId.ContainsKey(f.Session));
            foreach (var item in lines) { var t = byId[item.Session]; item.Title = t.Shown; item.Project = t.ProjectName; }
            view.Feed = lines;
            foreach (var t in view.Threads)
            {
                var lane = LaneOf(t, now);
                t.Lane = lane.Key; t.LaneSince = lane.Value;
                // The trail's own mark says since when better, once it has seen the change.
                List<ClaudeStateMark> trail;
                if (view.Trails.TryGetValue(t.Id, out trail) && trail.Count > 0 && trail[trail.Count - 1].State == lane.Key) t.LaneSince = trail[trail.Count - 1].At;
            }
        }
        // Today's average pace in percent of the 5-hour window an hour: today's tokens an hour since today's first reply,
        // at the window's percent per token so far. NaN when there's nothing to go on.
        public static double AveragePerHour(ClaudeView view, IList<ClaudeUsageEntry> usage, DateTime now)
        {
            var w = view.Limits == null ? null : view.Limits.FiveHour;
            if (usage == null || w == null || !w.ResetsAt.HasValue || !w.Current(now) || w.Percent <= 0) return Double.NaN;
            DateTime windowStart = w.ResetsAt.Value - ClaudePace.FiveHours;
            double windowTokens = usage.Where(e => e.At >= windowStart && e.At <= now).Sum(e => (double)e.Tokens);
            if (windowTokens <= 0) return Double.NaN;
            DateTime today; if (!view.Starts.TryGetValue(ClaudePeriod.Today, out today)) today = now.Date;
            var todays = usage.Where(e => e.At >= today && e.At <= now).ToList();
            if (todays.Count == 0) return Double.NaN;
            double hours = Math.Max(0.25, (now - todays.Min(e => e.At)).TotalHours);
            return todays.Sum(e => (double)e.Tokens) / hours * w.Percent / windowTokens;
        }
        // An API error's feed detail: "HTTP 529 · attempt 2".
        public static string ErrorDetail(int status, int attempt)
        {
            var parts = new List<string>();
            if (status > 0) parts.Add("HTTP " + status);
            if (attempt > 0) parts.Add("attempt " + attempt);
            return String.Join(" · ", parts);
        }

        // ----- The samples (--render-claude-sample and the checks) -----

        // Made-up history for the fixtures: watching for three hours, plan readings every five minutes climbing at the
        // sample's pace, each thread's trail of work, turns, waits and idles ending in its state now, a few failures and
        // API errors, today's tool runs and the sources' freshness. Every number is a sample.
        public static void Sample(ClaudeView view, DateTime now, bool needsYou, bool crowded)
        {
            var timeline = new ClaudeTimeline(now.AddHours(-3));
            var w = view.Limits == null ? null : view.Limits.FiveHour;
            if (w != null && w.ResetsAt.HasValue)
            {
                double pace = crowded ? 27 : needsYou ? 25 : 6;
                DateTime start = w.ResetsAt.Value - ClaudePace.FiveHours, half = now - PaceWindow;
                double atHalf = Math.Max(0, w.Percent - pace * 0.5);
                for (DateTime at = timeline.WatchStart; at < now.AddMinutes(-2); at = at.AddMinutes(5))
                {
                    if (at < start) continue;
                    double percent = at <= half ? atHalf * (at - start).TotalMinutes / Math.Max(1, (half - start).TotalMinutes) : atHalf + pace * (at - half).TotalHours;
                    timeline.Reading(at, Math.Round(percent, 1), w.ResetsAt, now);
                }
                timeline.Reading(now.AddMinutes(-1), w.Percent, w.ResetsAt, now);
            }
            // The crowd's retrying thread: two overloaded answers in the last minute and a half.
            var retrying = crowded ? view.Threads.FirstOrDefault(t => t.Id == "crowd-10") : null;
            if (retrying != null) { retrying.Retrying = true; retrying.ErrorStatus = 529; retrying.ErrorAttempt = 2; retrying.ErrorAt = now.AddSeconds(-40); }
            ClaudeLaneState[] cycle = { ClaudeLaneState.Working, ClaudeLaneState.YourTurn, ClaudeLaneState.Working, ClaudeLaneState.NeedsYou, ClaudeLaneState.Working, ClaudeLaneState.Idle };
            foreach (var t in view.Threads)
            {
                DateTime end = t.Running ? LaneOf(t, now).Value : t.LastActive;
                if (end < timeline.WatchStart.AddMinutes(10)) continue;
                int seed = t.Id.Sum(c => (int)c);
                DateTime at = timeline.WatchStart.AddMinutes(seed % 25);
                for (int i = seed % cycle.Length; at < end.AddMinutes(-2); i++)
                {
                    var state = cycle[i % cycle.Length];
                    timeline.Note(t.Id, at, state, now);
                    double minutes = state == ClaudeLaneState.Working ? 18 + (seed * (i + 3)) % 27 : state == ClaudeLaneState.Idle ? 6 + (seed + i) % 15 : 2 + (seed + i) % 7;
                    at = at.AddMinutes(minutes);
                }
                if (t.Running) { var lane = LaneOf(t, now); timeline.Note(t.Id, lane.Value, lane.Key, now); }
                else timeline.Note(t.Id, t.LastActive, ClaudeLaneState.Ended, now);
            }
            timeline.Event(now.AddMinutes(-26), "sample-2", ClaudeFeedItem.ToolFailed, "Bash");
            timeline.Event(now.AddMinutes(-70), "sample-2", ClaudeFeedItem.ApiError, ErrorDetail(500, 1));
            if (retrying != null)
            {
                timeline.Event(now.AddSeconds(-75), retrying.Id, ClaudeFeedItem.ApiError, ErrorDetail(529, 1));
                timeline.Event(now.AddSeconds(-40), retrying.Id, ClaudeFeedItem.ApiError, ErrorDetail(529, 2));
                timeline.Event(now.AddMinutes(-11), "crowd-4", ClaudeFeedItem.ToolFailed, "Edit");
            }
            // Today's tool runs: a handful of tools on the working and done threads, at made-up times.
            string[] tools = { "Bash", "Read", "Edit", "Grep", "Read", "Bash", "WebFetch", "Read", "Edit", "Glob" };
            double[] ms = { 4200, 90, 160, 310, 60, 8800, 2600, 120, 240, 45 };
            var runs = new List<ClaudeToolRun>();
            foreach (var t in view.Threads.Where(x => x.Running))
            {
                int seed = t.Id.Sum(c => (int)c);
                for (int i = 0; i < 6 + seed % 9; i++)
                {
                    int k = (seed + i * 7) % tools.Length;
                    runs.Add(new ClaudeToolRun { At = now.AddMinutes(-(3 + (seed * (i + 1)) % 170)), Session = t.Id, Tool = tools[k], Ms = ms[k] * (0.6 + ((seed + i) % 9) / 10.0), Failed = k == 5 && i % 3 == 0, ErrorType = k == 5 && i % 3 == 0 ? "exit_code" : "" });
                }
            }
            runs.Add(new ClaudeToolRun { At = now.AddMinutes(-26), Session = "sample-2", Tool = "Bash", Ms = 12400, Failed = true, ErrorType = "exit_code" });
            if (crowded) runs.Add(new ClaudeToolRun { At = now.AddMinutes(-4), Session = "crowd-7", Tool = ClaudeView.SampleLongTool, Ms = 1900 });
            view.ToolRunsToday = runs.OrderBy(r => r.At).ToList();
            view.ToolTimes = ToolTimes(runs);
            view.ToolTimesKnown = true;
            foreach (var t in view.Threads) t.ToolTimes = ToolTimes(runs.Where(r => r.Session == t.Id));
            foreach (var t in view.Threads.Where(x => x.Running)) t.AskedToday = t.Id.Sum(c => (int)c) % 5;
            view.AskedToday = new Dictionary<string, int>(StringComparer.Ordinal) { { "Bash", 9 }, { "Edit", 4 }, { "WebFetch", 2 } };
            view.ApiErrors = new List<ClaudeApiError> { new ClaudeApiError { At = now.AddMinutes(-70), Session = "sample-2", Status = 500, Attempt = 1 } };
            if (retrying != null) { view.ApiErrors.Add(new ClaudeApiError { At = now.AddSeconds(-75), Session = retrying.Id, Status = 529, Attempt = 1 }); view.ApiErrors.Add(new ClaudeApiError { At = now.AddSeconds(-40), Session = retrying.Id, Status = 529, Attempt = 2 }); }
            view.LogsReadAt = now.AddSeconds(-2); view.TelemetryAt = now.AddSeconds(-4); view.TelemetryPort = ClaudeTelemetry.DefaultPort;
            view.AppThreadsReadAt = now.AddSeconds(-10); view.AppThreadCount = 6; view.PlanNextCheck = now.AddMinutes(4);
            timeline.Fill(view, now, crowded ? 20 : needsYou ? 18 : 7);
        }

        // ----- Typical tool times -----

        public static double Median(IEnumerable<double> values)
        {
            var times = (values ?? Enumerable.Empty<double>()).Where(v => !Double.IsNaN(v) && v >= 0).OrderBy(v => v).ToList();
            if (times.Count == 0) return Double.NaN;
            return times.Count % 2 == 1 ? times[times.Count / 2] : (times[times.Count / 2 - 1] + times[times.Count / 2]) / 2;
        }
        // Each tool's runs, failures, median and longest time, most runs first.
        public static List<ClaudeToolTime> ToolTimes(IEnumerable<ClaudeToolRun> runs)
        {
            return (runs ?? Enumerable.Empty<ClaudeToolRun>()).Where(r => !String.IsNullOrEmpty(r.Tool)).GroupBy(r => r.Tool, StringComparer.Ordinal)
                .Select(g => {
                    var ms = g.Select(r => r.Ms).Where(v => !Double.IsNaN(v) && v >= 0).ToList();
                    return new ClaudeToolTime { Tool = g.Key, Runs = g.Count(), Failed = g.Count(r => r.Failed), MedianMs = Median(ms), LongestMs = ms.Count == 0 ? Double.NaN : ms.Max() };
                })
                .OrderByDescending(t => t.Runs).ThenBy(t => t.Tool, StringComparer.Ordinal).ToList();
        }
    }
}

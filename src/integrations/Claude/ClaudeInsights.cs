using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ClaudeDashboard
{
    // v0.60.0, Insights (decision 031): one range of days (7 days, 30 days or all kept) worked out from the day files
    // plus today, with the previous equal range for comparison, ready for the Insights view to draw. Computed off the
    // UI thread after each background scan and published on the ClaudeView snapshot (ClaudeInsightsSet); the view only
    // picks one. Numbers and names only (model ids, effort levels, tools, MCP servers, skills, plugins, project folders).

    // One row of a ranking: a model, a tool, a project, an MCP server, a skill or a plugin.
    public sealed class ClaudeInsightRank
    {
        public string Name = "", Folder = "";
        // What it's ranked by (tokens for models and projects, calls for tools and MCP servers, replies for skills and
        // plugins), the same in the previous range, and its share of the range's total (0–1).
        public long Value, Prior;
        public double Share;
        public int Replies, Errors, Sessions, Timed;
        public double Cost;
        public long Thinking, LinesAdded, LinesRemoved;
        // ≈ median time of a tool call, from the calls whose result was seen (null: none timed).
        public long? MedianMs;
        // Tools only, per model id: the errors and the ≈ median time (null: none timed for that model).
        public Dictionary<string, int> ErrorSplit = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, long?> MedianByModel = new Dictionary<string, long?>(StringComparer.Ordinal);
        // Per model, the calls (tools) or per effort level, the replies (models).
        public Dictionary<string, int> Split = new Dictionary<string, int>(StringComparer.Ordinal);
    }

    public sealed class ClaudeInsights
    {
        public const string Week = "7 days", Month = "30 days", All = "All";
        public string Label = Week;
        // The range in local days, both ends included; the previous equal range (null for All, which has none).
        public int Days;
        public DateTime Start, End;
        public DateTime? PriorStart, PriorEnd;
        public bool Sample;
        // Why there is nothing to show ("" when there is something).
        public string Missing = "";
        // Days in the range with a day file, with the v2 detail, and built from Claude Code's logs.
        public int DaysWithFiles, DaysWithDetail, DaysFromLogs;
        // Days of the previous range with a reply (a change is worth showing only with enough of them).
        public int PriorActiveDays;
        // Today is in the range and has replies but no detail yet (the logs are still being read). Ratios over the
        // detail (thinking, effort, tools per reply) use Detail.Replies, the replies on days that have it, so they hold.
        public bool TodayPending;
        // What the range's detail leaves out, for the footer ("" when nothing is left out).
        public string Note = "";
        // Sessions in the range: on days with the detail, those that started that day (each counted once); on older
        // days, the day file's threads.
        public int Sessions, PriorSessions;

        public long Tokens, CacheRead, PriorTokens;
        public int Replies, PriorReplies, Threads, Unpriced;
        public double Cost, PriorCost;
        public ClaudeDayDetail Detail = new ClaudeDayDetail(), PriorDetail = new ClaudeDayDetail();
        // One entry per day of the range, oldest first (a day with no file is an empty day).
        public List<ClaudeDay> Series = new List<ClaudeDay>();
        // Rankings, biggest first, ties by name. Most(n) and Least(n) take the ends.
        public List<ClaudeInsightRank> Models = new List<ClaudeInsightRank>(), Tools = new List<ClaudeInsightRank>(), Projects = new List<ClaudeInsightRank>(),
            Mcp = new List<ClaudeInsightRank>(), Skills = new List<ClaudeInsightRank>(), Plugins = new List<ClaudeInsightRank>();
        // Tokens by weekday (Monday first) and hour of day.
        public long[,] Heat = new long[7, 24];

        // The change from the previous range, as a fraction (0.25 = 25 % more); null when there's nothing to compare.
        public static double? Change(double now, double prior) { return prior <= 0 ? (double?)null : (now - prior) / prior; }
        public static List<ClaudeInsightRank> Most(List<ClaudeInsightRank> list, int n) { return list.Where(r => r.Value > 0).Take(n).ToList(); }
        // The least used, fewest first, of those used at all in the range.
        public static List<ClaudeInsightRank> Least(List<ClaudeInsightRank> list, int n) { return list.Where(r => r.Value > 0).OrderBy(r => r.Value).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).Take(n).ToList(); }

        // A range ending today: length days (0: every day kept, back to the oldest file).
        public static ClaudeInsights Compute(IList<ClaudeDay> days, DateTime today, int length, string label)
        {
            today = today.Date;
            var byDate = new Dictionary<DateTime, ClaudeDay>();
            foreach (var d in days ?? new List<ClaudeDay>()) if (d != null) byDate[d.Date.Date] = d;
            var r = new ClaudeInsights { Label = label, End = today };
            if (length > 0) { r.Start = today.AddDays(-(length - 1)); r.PriorEnd = r.Start.AddDays(-1); r.PriorStart = r.Start.AddDays(-length); }
            else r.Start = byDate.Keys.Where(k => k <= today).DefaultIfEmpty(today).Min();
            r.Days = (int)(r.End - r.Start).TotalDays + 1;

            var models = new Dictionary<string, ClaudeInsightRank>(StringComparer.Ordinal);
            var projects = new Dictionary<string, ClaudeInsightRank>(StringComparer.OrdinalIgnoreCase);
            for (var date = r.Start; date <= r.End; date = date.AddDays(1))
            {
                ClaudeDay d; byDate.TryGetValue(date, out d);
                r.Series.Add(d ?? new ClaudeDay { Date = date });
                if (d == null) continue;
                r.DaysWithFiles++; if (d.Detail != null) r.DaysWithDetail++; if (d.FromLogs) r.DaysFromLogs++;
                r.Tokens += d.Tokens; r.CacheRead += d.CacheRead; r.Replies += d.Replies; r.Cost += d.Cost; r.Threads += d.Threads; r.Unpriced += d.Unpriced;
                r.Sessions += d.Detail != null ? d.Detail.Sessions : d.Threads;
                if (date == today && d.Detail == null && d.Replies > 0) r.TodayPending = true;
                r.Detail.Add(d.Detail);
                int weekday = ((int)date.DayOfWeek + 6) % 7;
                for (int h = 0; h < 24; h++) r.Heat[weekday, h] += d.Hours[h];
                foreach (var g in d.Garages)
                {
                    string key = g.Folder.Length > 0 ? g.Folder : g.Name;
                    ClaudeInsightRank p;
                    if (!projects.TryGetValue(key, out p)) { p = new ClaudeInsightRank { Name = g.Name, Folder = g.Folder }; projects[key] = p; }
                    p.Value += g.Tokens; p.Replies += g.Replies; p.Cost += g.Cost;
                    if (d.Detail == null) p.Sessions += g.Threads;
                }
                if (d.Detail != null)
                    foreach (var started in d.Detail.ProjectSessions)
                    {
                        ClaudeInsightRank p;
                        if (!projects.TryGetValue(started.Key, out p)) { p = new ClaudeInsightRank { Folder = started.Key, Name = Leaf(started.Key) }; projects[started.Key] = p; }
                        p.Sessions += started.Value;
                    }
            }
            if (r.PriorStart.HasValue)
                for (var date = r.PriorStart.Value; date <= r.PriorEnd.Value; date = date.AddDays(1))
                {
                    ClaudeDay d; if (!byDate.TryGetValue(date, out d)) continue;
                    r.PriorTokens += d.Tokens; r.PriorReplies += d.Replies; r.PriorCost += d.Cost; r.PriorDetail.Add(d.Detail);
                    r.PriorSessions += d.Detail != null ? d.Detail.Sessions : d.Threads;
                    if (d.Replies > 0) r.PriorActiveDays++;
                    foreach (var g in d.Garages) { ClaudeInsightRank p; if (projects.TryGetValue(g.Folder.Length > 0 ? g.Folder : g.Name, out p)) p.Prior += g.Tokens; }
                }

            // Models by tokens, with their effort split.
            foreach (var p in r.Detail.Models)
            {
                ClaudeModelUse prior; r.PriorDetail.Models.TryGetValue(p.Key, out prior);
                var row = new ClaudeInsightRank { Name = p.Key, Value = p.Value.Tokens, Prior = prior == null ? 0 : prior.Tokens, Replies = p.Value.Replies, Cost = p.Value.Cost, Thinking = p.Value.Thinking };
                Dictionary<string, int> effort; if (r.Detail.Effort.TryGetValue(p.Key, out effort)) row.Split = new Dictionary<string, int>(effort, StringComparer.Ordinal);
                models[p.Key] = row;
            }
            r.Models = Ranked(models.Values);
            // Tools by calls, with errors, ≈ median time and calls per model.
            r.Tools = Ranked(r.Detail.Tools.Select(p => {
                ClaudeToolUse prior; r.PriorDetail.Tools.TryGetValue(p.Key, out prior);
                return new ClaudeInsightRank { Name = p.Key, Value = p.Value.Calls, Prior = prior == null ? 0 : prior.Calls, Errors = p.Value.Errors, Timed = p.Value.Timed,
                    MedianMs = ClaudeDayDetail.Median(p.Value.Buckets), Split = new Dictionary<string, int>(p.Value.ByModel, StringComparer.Ordinal),
                    ErrorSplit = new Dictionary<string, int>(p.Value.ErrorsByModel, StringComparer.Ordinal),
                    MedianByModel = p.Value.BucketsByModel.ToDictionary(q => q.Key, q => ClaudeDayDetail.Median(q.Value), StringComparer.Ordinal) };
            }));
            // Projects by tokens, with their lines.
            foreach (var p in r.Detail.ProjectLines)
            {
                ClaudeInsightRank row;
                if (!projects.TryGetValue(p.Key, out row)) { row = new ClaudeInsightRank { Folder = p.Key, Name = Leaf(p.Key) }; projects[p.Key] = row; }
                row.LinesAdded += p.Value[0]; row.LinesRemoved += p.Value[1];
            }
            r.Projects = Ranked(projects.Values);
            r.Mcp = Counted(r.Detail.Mcp, r.PriorDetail.Mcp);
            r.Skills = Counted(r.Detail.Skills, r.PriorDetail.Skills);
            r.Plugins = Counted(r.Detail.Plugins, r.PriorDetail.Plugins);
            if (r.DaysWithFiles == 0) r.Missing = "No day files in this range yet. History keeps them while Claude Code runs (Settings → Claude → History).";
            else if (r.DaysWithDetail == 0 && r.TodayPending && r.DaysWithFiles == 1) r.Missing = TodayText;
            else if (r.DaysWithDetail == 0) r.Missing = "These days were kept before v0.60.0, so models, tools and effort aren't in them yet: they're added from Claude Code's logs while those still hold the days.";
            if (r.Missing.Length == 0 && r.TodayPending) r.Note = TodayText;
            else if (r.Missing.Length == 0 && r.DaysWithDetail < r.DaysWithFiles)
                r.Note = "Models, tools and effort cover " + r.DaysWithDetail + " of " + r.DaysWithFiles + " days: the others were kept before v0.60.0 and Claude Code's logs no longer hold them.";
            return r;
        }
        private const string TodayText = "Today's models, tools and effort are added once Claude Code's logs have been read; until then they cover the days before.";

        private static List<ClaudeInsightRank> Ranked(IEnumerable<ClaudeInsightRank> rows)
        {
            var list = rows.OrderByDescending(x => x.Value).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            long total = list.Sum(x => x.Value);
            foreach (var x in list) x.Share = total <= 0 ? 0 : (double)x.Value / total;
            return list;
        }
        private static List<ClaudeInsightRank> Counted(Dictionary<string, int> now, Dictionary<string, int> prior)
        {
            return Ranked(now.Select(p => { int before; prior.TryGetValue(p.Key, out before); return new ClaudeInsightRank { Name = p.Key, Value = p.Value, Prior = before }; }));
        }
        private static string Leaf(string folder)
        {
            string trimmed = (folder ?? "").Replace('\\', '/').TrimEnd('/');
            int slash = trimmed.LastIndexOf('/');
            return slash >= 0 ? trimmed.Substring(slash + 1) : trimmed;
        }

        // ---- The labelled sample (for the view's fixture and renders) ----

        public static List<ClaudeDay> SampleDays(DateTime today)
        {
            today = today.Date;
            var list = new List<ClaudeDay>();
            string[] models = { "claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5" };
            string[] tools = { "Bash", "Read", "Edit", "Grep", "Write", "Glob", "TodoWrite", "WebFetch", "mcp__github__create_pull_request", "NotebookEdit" };
            string[] folders = { "C:/Code/claude-dashboard", "C:/Code/api-server", "C:/Code/photo-manager" };
            for (int i = 59; i >= 0; i--)
            {
                var date = today.AddDays(-i);
                int seed = (date.DayOfYear * 37 + 11) % 100;
                bool weekend = date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
                if (seed % 11 == 0) continue;
                int replies = weekend ? 20 + seed / 4 : 80 + seed * 2;
                var d = new ClaudeDay { Date = date, Replies = replies, Threads = 2 + seed % 6, FromLogs = i > 30, Detail = new ClaudeDayDetail() };
                for (int m = 0; m < models.Length; m++)
                {
                    int r = m == 0 ? replies * 6 / 10 : m == 1 ? replies * 3 / 10 : replies - replies * 9 / 10;
                    var use = new ClaudeModelUse { Replies = r, Input = r * 900L, Output = r * 700L, CacheRead = r * 40000L, CacheWrite = r * 3000L, Thinking = r * 200L };
                    use.Cost = (use.Input + use.Output * 5 + use.CacheWrite * 1.25 + use.CacheRead * 0.1) / 1e6 * (m == 0 ? 5 : m == 1 ? 3 : 1);
                    d.Detail.Models[models[m]] = use;
                    d.Tokens += use.Tokens; d.CacheRead += use.CacheRead; d.Cost += use.Cost;
                    d.Detail.Effort[models[m]] = new Dictionary<string, int> { { "high", r / 2 }, { "medium", r - r / 2 - r / 5 }, { "xhigh", r / 5 } };
                }
                d.Detail.ThinkingReplies = replies / 3; d.Detail.ThinkingMs = replies / 3 * 4200L; d.Detail.LongestThinkingMs = 30000 + seed * 500;
                d.Detail.ThinkingBuckets[ClaudeDayDetail.Bucket(4200)] = replies / 3;
                for (int t = 0; t < tools.Length; t++)
                {
                    int calls = t == tools.Length - 1 ? (seed % 3 == 0 ? 1 : 0) : Math.Max(1, replies / (t + 1));
                    if (calls == 0) continue;
                    var use = new ClaudeToolUse { Calls = calls, Errors = t == 0 ? calls / 12 : t == 2 ? calls / 30 : 0, Timed = calls, TimeMs = calls * (t == 0 ? 6000L : 400L) };
                    use.Buckets[ClaudeDayDetail.Bucket(t == 0 ? 6000 : 400)] = calls;
                    use.ByModel[models[0]] = calls * 6 / 10; use.ByModel[models[1]] = calls - calls * 6 / 10;
                    use.ErrorsByModel[models[0]] = use.Errors - use.Errors / 3; if (use.Errors / 3 > 0) use.ErrorsByModel[models[1]] = use.Errors / 3;
                    use.ModelBuckets(models[0])[ClaudeDayDetail.Bucket(t == 0 ? 6000 : 400)] = use.ByModel[models[0]];
                    use.ModelBuckets(models[1])[ClaudeDayDetail.Bucket(t == 0 ? 2500 : 250)] = use.ByModel[models[1]];
                    d.Detail.Tools[tools[t]] = use;
                }
                d.Detail.Mcp["github"] = d.Detail.Tools.ContainsKey(tools[8]) ? d.Detail.Tools[tools[8]].Calls : 0;
                d.Detail.Skills["code-review"] = 1 + seed % 3; d.Detail.Plugins["superpowers"] = seed % 4;
                d.Detail.PermRule = replies / 2; d.Detail.PermHook = replies / 8; d.Detail.PermAsked = 1 + seed % 5; d.Detail.PermDenied = seed % 2;
                d.Detail.ApiErrors = seed % 7 == 0 ? 1 : 0; d.Detail.Retries = seed % 5 == 0 ? 2 : 0; d.Detail.Compactions = seed % 3;
                d.Detail.Stops["tool_use"] = replies * 7 / 10; d.Detail.Stops["end_turn"] = replies - replies * 7 / 10;
                for (int s = 0; s < d.Threads; s++) d.Detail.SessionMinutes.Add(5 + (seed * (s + 3)) % 140);
                for (int g = 0; g < folders.Length; g++)
                {
                    long share = d.Tokens * (g == 0 ? 6 : g == 1 ? 3 : 1) / 10;
                    d.Garages.Add(new ClaudeDayGarage { Folder = folders[g], Name = Leaf(folders[g]), Tokens = share, Replies = replies * (g == 0 ? 6 : g == 1 ? 3 : 1) / 10, Threads = 1 + (g == 0 ? d.Threads / 2 : 0), Cost = d.Cost * (g == 0 ? 0.6 : g == 1 ? 0.3 : 0.1) });
                    d.Detail.ProjectLines[folders[g]] = new long[] { 40L * (3 - g) + seed, 10L * (3 - g) + seed / 3 };
                }
                d.Detail.ProjectSessions[folders[0]] = d.Threads - 1; d.Detail.ProjectSessions[folders[1]] = 1;
                d.Detail.LinesAdded = d.Detail.ProjectLines.Values.Sum(v => v[0]); d.Detail.LinesRemoved = d.Detail.ProjectLines.Values.Sum(v => v[1]);
                for (int h = 9; h <= 18; h++) d.Hours[h] = d.Tokens / 10;
                list.Add(d);
            }
            return list;
        }
    }

    // The three ranges the Insights view offers, computed together after each background scan.
    public sealed class ClaudeInsightsSet
    {
        public ClaudeInsights Week, Month, All;
        public DateTime MadeAt;
        public bool Sample;
        public ClaudeInsights Pick(string label) { return label == ClaudeInsights.Month ? Month : label == ClaudeInsights.All ? All : Week; }

        public static ClaudeInsightsSet Compute(IList<ClaudeDay> days, DateTime today, DateTime madeAt)
        {
            return new ClaudeInsightsSet { MadeAt = madeAt, Week = ClaudeInsights.Compute(days, today, 7, ClaudeInsights.Week), Month = ClaudeInsights.Compute(days, today, 30, ClaudeInsights.Month), All = ClaudeInsights.Compute(days, today, 0, ClaudeInsights.All) };
        }
        // The labelled sample: 60 made-up days up to today, the older half marked as built from the logs.
        public static ClaudeInsightsSet SampleSet(DateTime now)
        {
            var today = now.ToLocalTime().Date;
            var set = Compute(ClaudeInsights.SampleDays(today), today, now);
            set.Sample = true; set.Week.Sample = set.Month.Sample = set.All.Sample = true;
            return set;
        }
    }
}

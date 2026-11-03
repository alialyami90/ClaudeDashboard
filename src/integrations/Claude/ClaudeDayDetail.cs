using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ClaudeDashboard
{
    // v0.60.0, Insights (decision 031): the second part of a day file ("v": 2), from Claude Code's logs. Numbers only,
    // plus these names: model ids, effort levels, tool names, MCP server names, skill and plugin names and project
    // folders. Never prompts, replies or thinking, never tool inputs or outputs, file paths from tools or branch names.

    // One model's use in a day.
    public sealed class ClaudeModelUse
    {
        public int Replies, Unpriced;
        public long Input, Output, CacheRead, CacheWrite, Thinking;
        public double Cost;
        public long Tokens { get { return Input + Output + CacheWrite; } }
        public void Add(ClaudeModelUse o) { Replies += o.Replies; Unpriced += o.Unpriced; Input += o.Input; Output += o.Output; CacheRead += o.CacheRead; CacheWrite += o.CacheWrite; Thinking += o.Thinking; Cost += o.Cost; }
    }

    // One tool's calls in a day: errors, the calls timed (both the call and its result were seen) and their ≈ time, a
    // histogram of those times (ClaudeDayDetail.Edges) for an ≈ median, and the calls per model.
    public sealed class ClaudeToolUse
    {
        public int Calls, Errors, Timed;
        public long TimeMs;
        public int[] Buckets = new int[ClaudeDayDetail.Edges.Length + 1];
        // Per model id: the calls, the errors and the ≈ time buckets.
        public Dictionary<string, int> ByModel = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, int> ErrorsByModel = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, int[]> BucketsByModel = new Dictionary<string, int[]>(StringComparer.Ordinal);
        public int[] ModelBuckets(string model)
        {
            int[] b; if (!BucketsByModel.TryGetValue(model, out b)) { b = new int[ClaudeDayDetail.Edges.Length + 1]; BucketsByModel[model] = b; }
            return b;
        }
        public void Add(ClaudeToolUse o)
        {
            Calls += o.Calls; Errors += o.Errors; Timed += o.Timed; TimeMs += o.TimeMs;
            for (int i = 0; i < Buckets.Length && i < o.Buckets.Length; i++) Buckets[i] += o.Buckets[i];
            ClaudeDayDetail.AddAll(ByModel, o.ByModel);
            ClaudeDayDetail.AddAll(ErrorsByModel, o.ErrorsByModel);
            foreach (var p in o.BucketsByModel)
            {
                var b = ModelBuckets(p.Key);
                for (int i = 0; i < b.Length && i < p.Value.Length; i++) b[i] += p.Value[i];
            }
        }
    }

    public sealed class ClaudeDayDetail
    {
        public const int Version = 2;
        // The tally's revision inside v2 ("rev" in the detail). A kept day with an older revision gets its detail again
        // from the logs. 2 (v0.60.1): sessions on the day they started, hook permissions, API errors from api_error lines.
        public const int CurrentRevision = 2;
        public int Revision = CurrentRevision;
        // Bucket edges in milliseconds for tool and thinking times: an ≈ median comes from the bucket it falls in.
        public static readonly long[] Edges = { 100, 300, 1000, 3000, 10000, 30000, 60000, 180000, 600000, 1800000, 3600000 };

        public Dictionary<string, ClaudeModelUse> Models = new Dictionary<string, ClaudeModelUse>(StringComparer.Ordinal);
        // Per model id, per effort level, the replies.
        public Dictionary<string, Dictionary<string, int>> Effort = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        public int ThinkingReplies;
        public long ThinkingMs, LongestThinkingMs;
        public int[] ThinkingBuckets = new int[Edges.Length + 1];
        public Dictionary<string, ClaudeToolUse> Tools = new Dictionary<string, ClaudeToolUse>(StringComparer.Ordinal);
        public Dictionary<string, int> Mcp = new Dictionary<string, int>(StringComparer.Ordinal), Skills = new Dictionary<string, int>(StringComparer.Ordinal), Plugins = new Dictionary<string, int>(StringComparer.Ordinal);
        // permissionDecision: allowed by a rule (source "config"), allowed by a hook ("hook"), asked and allowed
        // (the person answered, "user_temporary" and the like), denied ("reject").
        public int PermRule, PermHook, PermAsked, PermDenied;
        // API errors (Claude Code's api_error lines and replies it marked as an API error), retries (the api_error
        // lines that say a retry follows), compactions.
        public int ApiErrors, Retries, Compactions;
        // Stop reasons: tool_use, end_turn, and the rest under "other".
        public Dictionary<string, int> Stops = new Dictionary<string, int>(StringComparer.Ordinal);
        // Each session that started that day: its length in minutes (its first to its last line, past midnight too),
        // longest first. A session counts only on the day it started, so a range's sum is exact.
        public List<int> SessionMinutes = new List<int>();
        // Per project folder: the sessions that started there that day.
        public Dictionary<string, int> ProjectSessions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public long LinesAdded, LinesRemoved;
        // Per project folder: lines added and removed.
        public Dictionary<string, long[]> ProjectLines = new Dictionary<string, long[]>(StringComparer.OrdinalIgnoreCase);

        public int Sessions { get { return SessionMinutes.Count; } }
        public int LongestSession { get { return SessionMinutes.Count == 0 ? 0 : SessionMinutes.Max(); } }
        public int MedianSession { get { if (SessionMinutes.Count == 0) return 0; var s = SessionMinutes.OrderBy(m => m).ToList(); return s[s.Count / 2]; } }
        public int Replies { get { return Models.Values.Sum(m => m.Replies); } }

        // The bucket for a time, and the ≈ median of a histogram. The median falls in one bucket; within it, it sits as
        // far along as its rank among that bucket's counts, on a log scale (the edges grow about threefold), so it
        // isn't pinned to the bucket's upper edge. Below the first edge the scale is straight; past the last edge, the
        // last edge. Null with nothing counted.
        public static int Bucket(long ms) { int i = 0; while (i < Edges.Length && ms >= Edges[i]) i++; return i; }
        public static long? Median(int[] buckets)
        {
            long total = buckets == null ? 0 : buckets.Sum(b => (long)Math.Max(0, b));
            if (total == 0) return null;
            double half = total / 2.0, before = 0;
            for (int i = 0; i < buckets.Length; i++)
            {
                int n = Math.Max(0, buckets[i]);
                if (n == 0 || before + n < half) { before += n; continue; }
                if (i >= Edges.Length) return Edges[Edges.Length - 1];
                double f = Math.Max(0, Math.Min(1, (half - before) / n));
                if (f == 0) f = 0.5 / n;
                double low = i == 0 ? 0 : Edges[i - 1], high = Edges[i];
                double v = i == 0 ? high * f : low * Math.Pow(high / low, f);
                return (long)Math.Round(v);
            }
            return Edges[Edges.Length - 1];
        }

        public static void AddAll(Dictionary<string, int> into, Dictionary<string, int> from) { foreach (var p in from) { int v; into.TryGetValue(p.Key, out v); into[p.Key] = v + p.Value; } }
        public static void Count(Dictionary<string, int> into, string key, int by = 1) { if (String.IsNullOrEmpty(key)) return; int v; into.TryGetValue(key, out v); into[key] = v + by; }

        // Adds another day's detail (a range's totals).
        public void Add(ClaudeDayDetail o)
        {
            if (o == null) return;
            foreach (var p in o.Models) { ClaudeModelUse m; if (!Models.TryGetValue(p.Key, out m)) { m = new ClaudeModelUse(); Models[p.Key] = m; } m.Add(p.Value); }
            foreach (var p in o.Effort) { Dictionary<string, int> e; if (!Effort.TryGetValue(p.Key, out e)) { e = new Dictionary<string, int>(StringComparer.Ordinal); Effort[p.Key] = e; } AddAll(e, p.Value); }
            ThinkingReplies += o.ThinkingReplies; ThinkingMs += o.ThinkingMs; LongestThinkingMs = Math.Max(LongestThinkingMs, o.LongestThinkingMs);
            for (int i = 0; i < ThinkingBuckets.Length && i < o.ThinkingBuckets.Length; i++) ThinkingBuckets[i] += o.ThinkingBuckets[i];
            foreach (var p in o.Tools) { ClaudeToolUse t; if (!Tools.TryGetValue(p.Key, out t)) { t = new ClaudeToolUse(); Tools[p.Key] = t; } t.Add(p.Value); }
            AddAll(Mcp, o.Mcp); AddAll(Skills, o.Skills); AddAll(Plugins, o.Plugins); AddAll(Stops, o.Stops);
            Revision = Math.Min(Revision, o.Revision);
            PermHook += o.PermHook; PermRule += o.PermRule; PermAsked += o.PermAsked; PermDenied += o.PermDenied;
            ApiErrors += o.ApiErrors; Retries += o.Retries; Compactions += o.Compactions;
            SessionMinutes.AddRange(o.SessionMinutes);
            AddAll(ProjectSessions, o.ProjectSessions);
            LinesAdded += o.LinesAdded; LinesRemoved += o.LinesRemoved;
            foreach (var p in o.ProjectLines) { long[] l; if (!ProjectLines.TryGetValue(p.Key, out l)) { l = new long[2]; ProjectLines[p.Key] = l; } l[0] += p.Value[0]; l[1] += p.Value[1]; }
        }

        // ---- The file ----

        private static JsonNumber N(double v) { return new JsonNumber(v); }
        private static JsonObject Counts(Dictionary<string, int> d) { var o = new JsonObject(); foreach (var p in d.OrderBy(p => p.Key, StringComparer.Ordinal)) o[p.Key] = N(p.Value); return o; }
        private static List<object> Ints(IEnumerable<int> values) { return values.Select(v => (object)N(v)).ToList(); }
        public JsonObject ToJson()
        {
            var o = new JsonObject();
            var models = new JsonObject();
            foreach (var p in Models.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var m = new JsonObject(); m["replies"] = N(p.Value.Replies); m["unpriced"] = N(p.Value.Unpriced); m["input"] = N(p.Value.Input); m["output"] = N(p.Value.Output);
                m["cacheRead"] = N(p.Value.CacheRead); m["cacheWrite"] = N(p.Value.CacheWrite); m["thinking"] = N(p.Value.Thinking); m["cost"] = N(Math.Round(p.Value.Cost, 4));
                models[p.Key] = m;
            }
            o["models"] = models;
            var effort = new JsonObject(); foreach (var p in Effort.OrderBy(p => p.Key, StringComparer.Ordinal)) effort[p.Key] = Counts(p.Value); o["effort"] = effort;
            var thinking = new JsonObject(); thinking["replies"] = N(ThinkingReplies); thinking["ms"] = N(ThinkingMs); thinking["longest"] = N(LongestThinkingMs); thinking["buckets"] = Ints(ThinkingBuckets); o["thinking"] = thinking;
            var tools = new JsonObject();
            foreach (var p in Tools.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var t = new JsonObject(); t["calls"] = N(p.Value.Calls); t["errors"] = N(p.Value.Errors); t["timed"] = N(p.Value.Timed); t["ms"] = N(p.Value.TimeMs);
                t["buckets"] = Ints(p.Value.Buckets); t["byModel"] = Counts(p.Value.ByModel); t["errorsByModel"] = Counts(p.Value.ErrorsByModel);
                var bm = new JsonObject(); foreach (var q in p.Value.BucketsByModel.OrderBy(q => q.Key, StringComparer.Ordinal)) bm[q.Key] = Ints(q.Value); t["bucketsByModel"] = bm;
                tools[p.Key] = t;
            }
            o["tools"] = tools;
            o["mcp"] = Counts(Mcp); o["skills"] = Counts(Skills); o["plugins"] = Counts(Plugins);
            var perm = new JsonObject(); perm["rule"] = N(PermRule); perm["hook"] = N(PermHook); perm["asked"] = N(PermAsked); perm["denied"] = N(PermDenied); o["permissions"] = perm;
            var health = new JsonObject(); health["apiErrors"] = N(ApiErrors); health["retries"] = N(Retries); health["compactions"] = N(Compactions); o["health"] = health;
            o["stops"] = Counts(Stops);
            o["sessions"] = Ints(SessionMinutes.OrderByDescending(m => m));
            o["projectSessions"] = Counts(ProjectSessions);
            o["rev"] = N(Revision);
            var lines = new JsonObject(); lines["added"] = N(LinesAdded); lines["removed"] = N(LinesRemoved); o["lines"] = lines;
            var pl = new JsonObject(); foreach (var p in ProjectLines.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)) pl[p.Key] = new List<object> { N(p.Value[0]), N(p.Value[1]) }; o["projectLines"] = pl;
            return o;
        }

        private static long L(JsonObject o, string k) { double v = ClaudeJson.Num(o, k); return Double.IsNaN(v) || v < 0 ? 0 : (long)v; }
        private static Dictionary<string, int> ReadCounts(JsonObject o)
        {
            var d = new Dictionary<string, int>(StringComparer.Ordinal);
            if (o != null) foreach (var p in o) { var n = p.Value as JsonNumber; if (n != null && !Double.IsNaN(n.Value) && n.Value > 0) d[p.Key] = (int)n.Value; }
            return d;
        }
        private static void ReadInts(List<object> list, int[] into) { if (list == null) return; for (int i = 0; i < into.Length && i < list.Count; i++) { var n = list[i] as JsonNumber; into[i] = n == null || Double.IsNaN(n.Value) || n.Value < 0 ? 0 : (int)n.Value; } }
        public static ClaudeDayDetail FromJson(JsonObject o)
        {
            if (o == null) return null;
            var d = new ClaudeDayDetail();
            var models = ClaudeJson.Obj(o, "models");
            if (models != null) foreach (var p in models)
            {
                var m = p.Value as JsonObject; if (m == null) continue;
                double cost = ClaudeJson.Num(m, "cost");
                d.Models[p.Key] = new ClaudeModelUse { Replies = (int)L(m, "replies"), Unpriced = (int)L(m, "unpriced"), Input = L(m, "input"), Output = L(m, "output"), CacheRead = L(m, "cacheRead"),
                    CacheWrite = L(m, "cacheWrite"), Thinking = L(m, "thinking"), Cost = Double.IsNaN(cost) || cost < 0 ? 0 : cost };
            }
            var effort = ClaudeJson.Obj(o, "effort");
            if (effort != null) foreach (var p in effort) d.Effort[p.Key] = ReadCounts(p.Value as JsonObject);
            var thinking = ClaudeJson.Obj(o, "thinking");
            d.ThinkingReplies = (int)L(thinking, "replies"); d.ThinkingMs = L(thinking, "ms"); d.LongestThinkingMs = L(thinking, "longest"); ReadInts(ClaudeJson.List(thinking, "buckets"), d.ThinkingBuckets);
            var tools = ClaudeJson.Obj(o, "tools");
            if (tools != null) foreach (var p in tools)
            {
                var t = p.Value as JsonObject; if (t == null) continue;
                var use = new ClaudeToolUse { Calls = (int)L(t, "calls"), Errors = (int)L(t, "errors"), Timed = (int)L(t, "timed"), TimeMs = L(t, "ms"), ByModel = ReadCounts(ClaudeJson.Obj(t, "byModel")), ErrorsByModel = ReadCounts(ClaudeJson.Obj(t, "errorsByModel")) };
                ReadInts(ClaudeJson.List(t, "buckets"), use.Buckets);
                var bm = ClaudeJson.Obj(t, "bucketsByModel");
                if (bm != null) foreach (var q in bm) if (q.Value is List<object>) ReadInts((List<object>)q.Value, use.ModelBuckets(q.Key));
                d.Tools[p.Key] = use;
            }
            d.Mcp = ReadCounts(ClaudeJson.Obj(o, "mcp")); d.Skills = ReadCounts(ClaudeJson.Obj(o, "skills")); d.Plugins = ReadCounts(ClaudeJson.Obj(o, "plugins")); d.Stops = ReadCounts(ClaudeJson.Obj(o, "stops"));
            var perm = ClaudeJson.Obj(o, "permissions");
            d.PermHook = (int)L(perm, "hook"); d.PermRule = (int)L(perm, "rule"); d.PermAsked = (int)L(perm, "asked"); d.PermDenied = (int)L(perm, "denied");
            var health = ClaudeJson.Obj(o, "health");
            d.ApiErrors = (int)L(health, "apiErrors"); d.Retries = (int)L(health, "retries"); d.Compactions = (int)L(health, "compactions");
            d.ProjectSessions = new Dictionary<string, int>(ReadCounts(ClaudeJson.Obj(o, "projectSessions")), StringComparer.OrdinalIgnoreCase);
            d.Revision = (int)L(o, "rev");
            foreach (var n in (ClaudeJson.List(o, "sessions") ?? new List<object>()).OfType<JsonNumber>()) if (!Double.IsNaN(n.Value) && n.Value >= 0) d.SessionMinutes.Add((int)n.Value);
            var lines = ClaudeJson.Obj(o, "lines"); d.LinesAdded = L(lines, "added"); d.LinesRemoved = L(lines, "removed");
            var pl = ClaudeJson.Obj(o, "projectLines");
            if (pl != null) foreach (var p in pl)
            {
                var pair = p.Value as List<object>; if (pair == null || pair.Count < 2) continue;
                var a = pair[0] as JsonNumber; var r = pair[1] as JsonNumber;
                d.ProjectLines[p.Key] = new long[] { a == null || Double.IsNaN(a.Value) ? 0 : (long)a.Value, r == null || Double.IsNaN(r.Value) ? 0 : (long)r.Value };
            }
            return d;
        }
    }
}

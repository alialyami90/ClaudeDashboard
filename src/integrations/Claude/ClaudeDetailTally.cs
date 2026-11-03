using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ClaudeDashboard
{
    // v0.60.0, Insights: each local day's ClaudeDayDetail, tallied from pieces of Claude Code's session logs (whole
    // lines). Only names and numbers are kept (model ids, effort levels, tool, MCP server, skill and plugin names, and
    // project folders); text is never read past what's needed to find them.
    //
    // From the logs (inventory of Ali's logs, spec part A):
    // - assistant lines: message.model (normalised: "[1m]" and the like taken off), usage (with
    //   output_tokens_details.thinking_tokens and the 1-hour cache write), stop_reason, effort or perTurnEffort,
    //   thinkingDurationMs, attributionSkill, attributionPlugin, isApiErrorMessage, and tool_use blocks by name. A
    //   streamed reply repeats its usage on several lines, so a reply counts once by its request id (message.id when
    //   there is none), as the card counts it; its tool calls count once each by their id.
    // - api-request-blob lines repeat content blocks, so they are skipped altogether.
    // - user lines: tool_result (tool_use_id, is_error, and the lines a toolUseResult added and removed) and
    //   permissionDecision (decision and source).
    // - system lines: compact_boundary (a compaction) and api_error (a retry).
    // - every line's timestamp and session: each session's first and last line that day.
    // A tool's ≈ time is the gap between its call's line and its result's line (there is no duration in the logs).
    public sealed class ClaudeDetailTally
    {
        private readonly Func<DateTime, DateTime> localDate;
        private readonly Dictionary<DateTime, ClaudeDayDetail> days = new Dictionary<DateTime, ClaudeDayDetail>();
        // Seen once: replies (by request id), tool calls and results (by tool id), other lines (by uuid).
        private readonly Seen replies = new Seen(), stops = new Seen(), thinking = new Seen(), thought = new Seen(), results = new Seen(), lines = new Seen();
        // Ids already counted, each with the day it was counted on, so Forget can drop old ones.
        private sealed class Seen
        {
            private readonly Dictionary<string, DateTime> ids = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            public bool Add(string id, DateTime date)
            {
                if (ids.ContainsKey(id)) return false;
                ids[id] = date; return true;
            }
            public void Forget(DateTime before)
            {
                foreach (var id in ids.Where(p => p.Value < before).Select(p => p.Key).ToList()) ids.Remove(id);
            }
        }
        // Tool calls waiting for their result: name, model, when, and the day they count on.
        private sealed class Call { public string Name, Model; public DateTime At; public DateTime Day; }
        private readonly Dictionary<string, Call> calls = new Dictionary<string, Call>(StringComparer.Ordinal);
        // Results seen before their call (a helper's log read first): when, and whether it failed.
        private readonly Dictionary<string, Tuple<DateTime, bool>> early = new Dictionary<string, Tuple<DateTime, bool>>(StringComparer.Ordinal);
        // Per day and session: the first and last line.
        private readonly Dictionary<string, DateTime[]> spans = new Dictionary<string, DateTime[]>(StringComparer.Ordinal);
        // The folder each session started in.
        private readonly Dictionary<string, string> folders = new Dictionary<string, string>(StringComparer.Ordinal);

        public ClaudeDetailTally(Func<DateTime, DateTime> localDate) { this.localDate = localDate; }

        // "claude-opus-5-5[1m]" → "claude-opus-5-5"; blank and Claude Code's own "<synthetic>" → "".
        public static string Model(string id)
        {
            string m = (id ?? "").Trim();
            int bracket = m.IndexOf('[');
            if (bracket >= 0) m = m.Substring(0, bracket).Trim();
            if (m.StartsWith("<", StringComparison.Ordinal)) return "";
            return m.Length > 80 ? m.Substring(0, 80) : m;
        }
        // A name kept in a day file: trimmed, and no longer than 80 characters.
        private static string Name(string value) { string v = (value ?? "").Trim(); return v.Length > 80 ? v.Substring(0, 80) : v; }
        // "mcp__github__create_pull_request" → "github".
        public static string McpServer(string tool)
        {
            if (tool == null || !tool.StartsWith("mcp__", StringComparison.Ordinal)) return null;
            int end = tool.IndexOf("__", 5, StringComparison.Ordinal);
            return end > 5 ? tool.Substring(5, end - 5) : null;
        }

        private ClaudeDayDetail Day(DateTime date) { ClaudeDayDetail d; if (!days.TryGetValue(date, out d)) { d = new ClaudeDayDetail(); days[date] = d; } return d; }

        // Adds a piece of a log (whole lines). session: the log's file name, for lines that don't carry their own.
        public void Fold(string text, string session)
        {
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                bool assistant = line.IndexOf("\"assistant\"", StringComparison.Ordinal) >= 0;
                bool result = line.IndexOf("\"tool_result\"", StringComparison.Ordinal) >= 0;
                bool permission = line.IndexOf("\"permissionDecision\"", StringComparison.Ordinal) >= 0;
                bool system = line.IndexOf("\"system\"", StringComparison.Ordinal) >= 0;
                bool cwd = line.IndexOf("\"cwd\"", StringComparison.Ordinal) >= 0;
                bool stamp = line.IndexOf("\"timestamp\"", StringComparison.Ordinal) >= 0;
                if (!stamp) continue;
                object parsed; if (!ClaudeJson.TryParse(line, out parsed)) continue;
                var o = ClaudeJson.Obj(parsed);
                string type = ClaudeJson.Str(o, "type") ?? "";
                if (type == "api-request-blob") continue;
                var at = ClaudeBridge.ParseTime(ClaudeJson.Str(o, "timestamp"));
                if (!at.HasValue) continue;
                DateTime date = localDate(at.Value);
                string id = ClaudeJson.Str(o, "sessionId") ?? session ?? "";
                if (cwd && !folders.ContainsKey(id) && !String.IsNullOrEmpty(ClaudeJson.Str(o, "cwd"))) folders[id] = ClaudeUsage.Key(ClaudeJson.Str(o, "cwd"));
                Span(date, id, at.Value);
                if (assistant && type == "assistant") Reply(o, date, at.Value);
                else if (type == "user")
                {
                    if (result) Results(o, date, at.Value, id);
                    if (permission) Permission(o, date);
                }
                else if (system && type == "system") SystemLine(o, date);
            }
        }

        private void Span(DateTime date, string session, DateTime at)
        {
            if (session.Length == 0) return;
            string key = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "|" + session;
            DateTime[] span;
            if (!spans.TryGetValue(key, out span)) { spans[key] = new[] { at, at }; return; }
            if (at < span[0]) span[0] = at;
            if (at > span[1]) span[1] = at;
        }

        private void Reply(JsonObject o, DateTime date, DateTime at)
        {
            var message = ClaudeJson.Obj(o, "message");
            string model = Model(ClaudeJson.Str(message, "model"));
            string request = ClaudeJson.Str(o, "requestId") ?? "", key = request.Length > 0 ? request : (ClaudeJson.Str(message, "id") ?? "");
            var day = Day(date);
            // Its tool calls, once each, by id.
            foreach (var block in (ClaudeJson.List(message, "content") ?? new List<object>()).OfType<JsonObject>())
            {
                if (ClaudeJson.Str(block, "type") != "tool_use") continue;
                string callId = ClaudeJson.Str(block, "id");
                if (String.IsNullOrEmpty(callId) || calls.ContainsKey(callId)) continue;
                string tool = Name(ClaudeJson.Str(block, "name"));
                if (tool.Length == 0) continue;
                var call = new Call { Name = tool, Model = model, At = at, Day = date };
                calls[callId] = call;
                ClaudeToolUse use; if (!day.Tools.TryGetValue(tool, out use)) { use = new ClaudeToolUse(); day.Tools[tool] = use; }
                use.Calls++;
                if (model.Length > 0) ClaudeDayDetail.Count(use.ByModel, model);
                ClaudeDayDetail.Count(day.Mcp, McpServer(tool));
                Tuple<DateTime, bool> seen; if (early.TryGetValue(callId, out seen)) { early.Remove(callId); Close(call, seen.Item1, seen.Item2); }
            }
            if (key.Length == 0) return;
            // The stop reason and thinking can come on a later line of the same reply: each once per reply.
            string stop = ClaudeJson.Str(message, "stop_reason");
            if (!String.IsNullOrEmpty(stop) && stops.Add(key, date)) ClaudeDayDetail.Count(day.Stops, stop == "tool_use" || stop == "end_turn" ? stop : "other");
            double ms = ClaudeJson.Num(o, "thinkingDurationMs");
            if (Double.IsNaN(ms)) ms = ClaudeJson.Num(message, "thinkingDurationMs");
            var usage = ClaudeJson.Obj(message, "usage");
            long thinkingTokens = Count(ClaudeJson.Obj(usage, "output_tokens_details"), "thinking_tokens");
            bool timed = !Double.IsNaN(ms) && ms > 0;
            // A reply that thought counts once; its thinking time once, from whichever line carries it.
            if ((timed || thinkingTokens > 0) && thinking.Add(key, date)) day.ThinkingReplies++;
            if (timed && thought.Add(key, date))
            {
                long t = (long)ms;
                day.ThinkingMs += t; day.LongestThinkingMs = Math.Max(day.LongestThinkingMs, t); day.ThinkingBuckets[ClaudeDayDetail.Bucket(t)]++;
            }
            if (usage == null || !replies.Add(key, date)) return;
            if (ClaudeJson.Bool(o, "isApiErrorMessage")) day.ApiErrors++;
            ClaudeDayDetail.Count(day.Skills, Name(ClaudeJson.Str(o, "attributionSkill")));
            ClaudeDayDetail.Count(day.Plugins, Name(ClaudeJson.Str(o, "attributionPlugin")));
            if (model.Length == 0) return;
            long input = Count(usage, "input_tokens"), output = Count(usage, "output_tokens"), creation = Count(usage, "cache_creation_input_tokens"), read = Count(usage, "cache_read_input_tokens");
            long hour = Math.Min(creation, Count(ClaudeJson.Obj(usage, "cache_creation"), "ephemeral_1h_input_tokens"));
            ClaudeModelUse use2; if (!day.Models.TryGetValue(model, out use2)) { use2 = new ClaudeModelUse(); day.Models[model] = use2; }
            use2.Replies++; use2.Input += input; use2.Output += output; use2.CacheWrite += creation; use2.CacheRead += read; use2.Thinking += thinkingTokens;
            if (ClaudePrices.Known(model)) use2.Cost += ClaudePrices.Cost(model, input, output, creation - hour, hour, read); else use2.Unpriced++;
            string effort = Effort(o, message);
            if (effort.Length > 0)
            {
                Dictionary<string, int> e; if (!day.Effort.TryGetValue(model, out e)) { e = new Dictionary<string, int>(StringComparer.Ordinal); day.Effort[model] = e; }
                ClaudeDayDetail.Count(e, effort);
            }
        }
        // The reply's effort level: "effort" or "perTurnEffort", as a word or an object with a level.
        private static string Effort(JsonObject o, JsonObject message)
        {
            foreach (var holder in new[] { o, message })
                foreach (var name in new[] { "effort", "perTurnEffort" })
                {
                    if (holder == null) continue;
                    object v = holder.Get(name);
                    string s = v as string;
                    if (s == null && v is JsonObject) s = ClaudeJson.Str((JsonObject)v, "level") ?? ClaudeJson.Str((JsonObject)v, "effort");
                    if (!String.IsNullOrWhiteSpace(s)) return Name(s.ToLowerInvariant());
                }
            return "";
        }

        private void Results(JsonObject o, DateTime date, DateTime at, string session)
        {
            var message = ClaudeJson.Obj(o, "message");
            foreach (var block in (ClaudeJson.List(message, "content") ?? new List<object>()).OfType<JsonObject>())
            {
                if (ClaudeJson.Str(block, "type") != "tool_result") continue;
                string callId = ClaudeJson.Str(block, "tool_use_id");
                if (String.IsNullOrEmpty(callId) || !results.Add(callId, date)) continue;
                bool failed = ClaudeJson.Bool(block, "is_error");
                Call call;
                if (calls.TryGetValue(callId, out call)) Close(call, at, failed);
                else early[callId] = Tuple.Create(at, failed);
            }
            // The lines it added and removed, on the result's day, for the session's project.
            var toolResult = ClaudeJson.Obj(o, "toolUseResult");
            string uuid = ClaudeJson.Str(o, "uuid") ?? "";
            if (toolResult != null && (uuid.Length == 0 || lines.Add("l" + uuid, date)))
            {
                long added, removed;
                ClaudeLogActivity.CountLines(toolResult, out added, out removed);
                if (added + removed > 0)
                {
                    var day = Day(date);
                    day.LinesAdded += added; day.LinesRemoved += removed;
                    string folder; folders.TryGetValue(session, out folder);
                    folder = folder ?? "";
                    long[] pair; if (!day.ProjectLines.TryGetValue(folder, out pair)) { pair = new long[2]; day.ProjectLines[folder] = pair; }
                    pair[0] += added; pair[1] += removed;
                }
            }
        }
        // A call's result: an error counts on the call's day; the ≈ time when the gap is sane (0 to 6 hours).
        private void Close(Call call, DateTime at, bool failed)
        {
            var day = Day(call.Day);
            ClaudeToolUse use; if (!day.Tools.TryGetValue(call.Name, out use)) { use = new ClaudeToolUse(); day.Tools[call.Name] = use; }
            if (failed) { use.Errors++; if (call.Model.Length > 0) ClaudeDayDetail.Count(use.ErrorsByModel, call.Model); }
            var gap = at - call.At;
            if (gap >= TimeSpan.Zero && gap <= TimeSpan.FromHours(6))
            {
                long ms = (long)gap.TotalMilliseconds;
                use.Timed++; use.TimeMs += ms; use.Buckets[ClaudeDayDetail.Bucket(ms)]++;
                if (call.Model.Length > 0) use.ModelBuckets(call.Model)[ClaudeDayDetail.Bucket(ms)]++;
            }
        }

        private void Permission(JsonObject o, DateTime date)
        {
            string uuid = ClaudeJson.Str(o, "uuid") ?? "";
            if (uuid.Length > 0 && !lines.Add("p" + uuid, date)) return;
            var p = ClaudeJson.Obj(o, "permissionDecision");
            string decision = (ClaudeJson.Str(p, "decision") ?? ClaudeJson.Str(p, "behavior") ?? "").ToLowerInvariant();
            string source = (ClaudeJson.Str(p, "source") ?? ClaudeJson.Str(p, "decisionReason") ?? "").ToLowerInvariant();
            var day = Day(date);
            if (decision == "deny" || decision == "denied" || decision == "reject") day.PermDenied++;
            else if (decision == "ask") day.PermAsked++;
            else if (decision == "allow" || decision == "allowed" || decision == "accept")
            {
                // Allowed by a rule in settings, or without a rule (the mode, a hook, or the person answering yes).
                if (source.Contains("rule") || source.Contains("config") || source.Contains("setting")) day.PermRule++;
                else if (source.Contains("user") || source.Contains("prompt")) day.PermAsked++;
                else day.PermAuto++;
            }
        }

        private void SystemLine(JsonObject o, DateTime date)
        {
            string uuid = ClaudeJson.Str(o, "uuid") ?? "";
            if (uuid.Length > 0 && !lines.Add("s" + uuid, date)) return;
            string subtype = ClaudeJson.Str(o, "subtype") ?? "";
            if (subtype == "compact_boundary") Day(date).Compactions++;
            else if (subtype == "api_error") Day(date).Retries++;
        }

        private static long Count(JsonObject o, string key) { double v = ClaudeJson.Num(o, key); return Double.IsNaN(v) || v < 0 ? 0 : (long)v; }

        // The days tallied so far.
        public IEnumerable<DateTime> Dates { get { return days.Keys.ToList(); } }
        // A day's detail as it stands, with its sessions' lengths worked out; null when nothing was seen that day.
        public ClaudeDayDetail For(DateTime date)
        {
            ClaudeDayDetail d; days.TryGetValue(date.Date, out d);
            string prefix = date.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "|";
            var minutes = spans.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(p => (int)Math.Round((p.Value[1] - p.Value[0]).TotalMinutes)).ToList();
            if (d == null && minutes.Count == 0) return null;
            var copy = new ClaudeDayDetail();
            copy.Add(d);
            copy.SessionMinutes = minutes.OrderByDescending(m => m).ToList();
            return copy;
        }
        // Drops the days before a date (and their sessions), keeping what's needed to count the rest once.
        public void Forget(DateTime before)
        {
            foreach (var date in days.Keys.Where(k => k < before).ToList()) days.Remove(date);
            string edge = before.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            foreach (var key in spans.Keys.Where(k => String.CompareOrdinal(k.Substring(0, 8), edge) < 0).ToList()) spans.Remove(key);
            foreach (var key in calls.Where(p => p.Value.Day < before).Select(p => p.Key).ToList()) calls.Remove(key);
            foreach (var key in early.Where(p => p.Value.Item1.Date < before.AddDays(-1)).Select(p => p.Key).ToList()) early.Remove(key);
            foreach (var set in new[] { replies, stops, thinking, thought, results, lines }) set.Forget(before);
        }
    }
}

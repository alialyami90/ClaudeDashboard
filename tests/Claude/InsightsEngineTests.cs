using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ClaudeDashboard;

// v0.60.0, Insights engine (decision 031, part A): day files v2 from Claude Code's logs (fixture logs in a throwaway
// Claude folder, never the PC's own) and the ClaudeInsights ranges.
internal static class InsightsEngineTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static string Fresh(string directory, string name)
    {
        string path = Path.Combine(directory, "claude-insights", name);
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
        return path;
    }
    private static string Stamp(DateTime at) { return at.ToString("yyyy-MM-ddTHH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture) + "Z"; }
    // JSON written with single quotes, for readability.
    private static string J(string text) { return text.Replace('\'', '"'); }

    // A day of one session at 10:00 UTC on day: two models, tools, an error, MCP, a skill, permissions, a compaction,
    // a retry, an API error, lines changed, and secret words in every place text can be.
    public static List<string> Day(DateTime day, string session)
    {
        DateTime t = day.Date.AddHours(10);
        string s = session;
        return new List<string> {
            J(@"{'type':'user','sessionId':'" + s + @"','cwd':'C:\\Code\\api','gitBranch':'SECRET-branch','timestamp':'" + Stamp(t) + "','uuid':'" + s + "u0','message':{'role':'user','content':'SECRET PROMPT'}}"),
            // Reply 1 (opus, [1m]) streamed over two lines: thinking, then a Bash call; effort high; a skill.
            J("{'type':'assistant','sessionId':'" + s + "','timestamp':'" + Stamp(t.AddSeconds(5)) + "','requestId':'" + s + "r1','effort':'high','attributionSkill':'code-review','message':{'id':'" + s + "m1','model':'claude-opus-5-5[1m]','content':[{'type':'thinking','thinking':'SECRET THOUGHT'}],"
                + "'usage':{'input_tokens':100,'output_tokens':50,'cache_creation_input_tokens':200,'cache_read_input_tokens':1000,'cache_creation':{'ephemeral_1h_input_tokens':50},'output_tokens_details':{'thinking_tokens':30}}}}"),
            J("{'type':'assistant','sessionId':'" + s + "','timestamp':'" + Stamp(t.AddSeconds(6)) + "','requestId':'" + s + "r1','effort':'high','thinkingDurationMs':1500,'message':{'id':'" + s + "m1','model':'claude-opus-5-5[1m]','stop_reason':'tool_use',"
                + @"'content':[{'type':'tool_use','id':'" + s + @"t1','name':'Bash','input':{'command':'SECRET rm C:\\secret\\file.txt'}}],"
                + "'usage':{'input_tokens':100,'output_tokens':50,'cache_creation_input_tokens':200,'cache_read_input_tokens':1000,'output_tokens_details':{'thinking_tokens':30}}}}"),
            // The request blob repeats content blocks: never counted.
            J("{'type':'api-request-blob','sessionId':'" + s + "','timestamp':'" + Stamp(t.AddSeconds(6)) + "','message':{'role':'assistant','model':'claude-opus-5-5','content':[{'type':'tool_use','id':'" + s + "t9','name':'Write','input':{}}],'usage':{'input_tokens':9999}}}"),
            // Bash fails after 4 s, and an edit adds two lines and removes one.
            J("{'type':'user','sessionId':'" + s + "','timestamp':'" + Stamp(t.AddSeconds(10)) + "','uuid':'" + s + "u1','message':{'role':'user','content':[{'type':'tool_result','tool_use_id':'" + s + "t1','is_error':true,'content':'SECRET OUTPUT'}]},"
                + @"'toolUseResult':{'filePath':'C:\\secret\\file.txt','structuredPatch':[{'lines':['+a','+b','-c',' d']}]}}"),
            // Reply 2 (sonnet, effort medium): an MCP call that works after 2 s; ends the turn on a later reply.
            J("{'type':'assistant','sessionId':'" + s + "','timestamp':'" + Stamp(t.AddMinutes(10)) + "','requestId':'" + s + "r2','perTurnEffort':'Medium','message':{'id':'" + s + "m2','model':'claude-sonnet-5-5','stop_reason':'tool_use',"
                + "'content':[{'type':'tool_use','id':'" + s + "t3','name':'mcp__github__create_pull_request','input':{'title':'SECRET'}}],'usage':{'input_tokens':10,'output_tokens':5,'cache_read_input_tokens':0}}}"),
            J("{'type':'user','sessionId':'" + s + "','timestamp':'" + Stamp(t.AddMinutes(10).AddSeconds(2)) + "','uuid':'" + s + "u2','permissionDecision':{'decision':'allow','source':'rule'},'message':{'role':'user','content':[{'type':'tool_result','tool_use_id':'" + s + "t3','content':'SECRET'}]}}"),
            J("{'type':'user','sessionId':'" + s + "','timestamp':'" + Stamp(t.AddMinutes(11)) + "','uuid':'" + s + "u3','permissionDecision':{'decision':'deny','source':'user'},'message':{'role':'user','content':'SECRET'}}"),
            J("{'type':'assistant','sessionId':'" + s + "','timestamp':'" + Stamp(t.AddMinutes(12)) + "','requestId':'" + s + "r3','message':{'id':'" + s + "m3','model':'claude-sonnet-5-5','stop_reason':'end_turn','content':[{'type':'text','text':'SECRET REPLY'}],'usage':{'input_tokens':10,'output_tokens':5}}}"),
            // An API error reply, a retry and a compaction.
            J("{'type':'assistant','sessionId':'" + s + "','timestamp':'" + Stamp(t.AddMinutes(20)) + "','requestId':'" + s + "r4','isApiErrorMessage':true,'message':{'id':'" + s + "m4','model':'<synthetic>','content':[{'type':'text','text':'SECRET'}],'usage':{'input_tokens':0,'output_tokens':0}}}"),
            J("{'type':'system','subtype':'api_error','sessionId':'" + s + "','timestamp':'" + Stamp(t.AddMinutes(21)) + "','uuid':'" + s + "s1','retryAttempt':1}"),
            J("{'type':'system','subtype':'compact_boundary','sessionId':'" + s + "','timestamp':'" + Stamp(t.AddMinutes(30)) + "','uuid':'" + s + "s2','compactMetadata':{'preTokens':1000}}"),
        };
    }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Insights: a day's detail from the logs counts models, effort, thinking, tools, MCP, skills, permissions, health, sessions and lines, once each (v0.60.0)", () => {
            DateTime day = Now.Date.AddDays(-3);
            var tally = new ClaudeDetailTally(at => at.Date);
            string text = String.Join("\n", Day(day, "s1")) + "\n";
            tally.Fold(text, "s1");
            // A helper's copy of the same lines counts nothing twice.
            tally.Fold(text, "agent-a1");
            var d = tally.For(day);
            Assert(d != null, "No detail.");
            Assert(ClaudeDetailTally.Model("claude-opus-5-5[1m]") == "claude-opus-5-5" && ClaudeDetailTally.Model("<synthetic>") == "" && ClaudeDetailTally.McpServer("mcp__github__x") == "github", "Model or MCP names.");
            ClaudeModelUse opus; Assert(d.Models.TryGetValue("claude-opus-5-5", out opus) && !d.Models.Keys.Any(k => k.Contains("[")), "Models: " + String.Join(",", d.Models.Keys));
            Assert(opus.Replies == 1 && opus.Input == 100 && opus.Output == 50 && opus.CacheWrite == 200 && opus.CacheRead == 1000 && opus.Thinking == 30 && opus.Cost > 0, "Opus: " + opus.Replies + " replies, " + opus.Input + " in, " + opus.Thinking + " thinking");
            Assert(d.Models["claude-sonnet-5-5"].Replies == 2 && d.Models.Count == 2, "Sonnet replies, or the synthetic model counted: " + d.Models.Count);
            Assert(d.Effort["claude-opus-5-5"]["high"] == 1 && d.Effort["claude-sonnet-5-5"]["medium"] == 1, "Effort.");
            Assert(d.ThinkingReplies == 1 && d.ThinkingMs == 1500 && d.LongestThinkingMs == 1500, "Thinking: " + d.ThinkingReplies + " / " + d.ThinkingMs);
            var bash = d.Tools["Bash"];
            Assert(bash.Calls == 1 && bash.Errors == 1 && bash.Timed == 1 && bash.TimeMs == 4000 && bash.ByModel["claude-opus-5-5"] == 1 && ClaudeDayDetail.Median(bash.Buckets) == 10000, "Bash: " + bash.Calls + " calls, " + bash.Errors + " errors, " + bash.TimeMs + " ms");
            Assert(!d.Tools.ContainsKey("Write"), "The request blob's tool was counted.");
            var pr = d.Tools["mcp__github__create_pull_request"];
            Assert(pr.Calls == 1 && pr.Errors == 0 && pr.TimeMs == 2000 && d.Mcp["github"] == 1, "MCP call.");
            Assert(d.Skills["code-review"] == 1, "Skill.");
            Assert(d.PermRule == 1 && d.PermDenied == 1 && d.PermAuto == 0, "Permissions: rule " + d.PermRule + ", denied " + d.PermDenied);
            Assert(d.ApiErrors == 1 && d.Retries == 1 && d.Compactions == 1, "Health: " + d.ApiErrors + " / " + d.Retries + " / " + d.Compactions);
            Assert(d.Stops["tool_use"] == 2 && d.Stops["end_turn"] == 1, "Stop reasons.");
            Assert(d.Sessions == 1 && d.SessionMinutes[0] == 30, "Sessions: " + d.Sessions + ", " + (d.Sessions > 0 ? d.SessionMinutes[0] : -1) + " min");
            Assert(d.LinesAdded == 2 && d.LinesRemoved == 1 && d.ProjectLines[@"C:\Code\api"][0] == 2, "Lines.");
            // Round trip through the file, with nothing but names and numbers.
            string json = ClaudeJson.Compact(d.ToJson());
            var back = ClaudeDayDetail.FromJson(ClaudeJson.Obj(ClaudeJson.Parse(json)));
            Assert(ClaudeJson.Compact(back.ToJson()) == json, "The detail doesn't survive its file.");
            foreach (string secret in new[] { "SECRET", "secret", "file.txt", "branch", "rm ", "s1r1", "s1t1" })
                Assert(json.IndexOf(secret, StringComparison.Ordinal) < 0, "\"" + secret + "\" landed in the detail: " + json);
        });

        check("Insights: the backfill writes v2 days, upgrades a v1 day keeping its needs and waits, follows the logs for today, and the live writer keeps v2 (v0.60.0)", () => {
            string config = Fresh(directory, "config"), bridge = Fresh(directory, "bridge");
            string project = Path.Combine(config, "projects", "C--Code-api");
            DateTime d5 = Now.Date.AddDays(-5), d6 = Now.Date.AddDays(-6);
            string log = Path.Combine(project, "s1.jsonl");
            Directory.CreateDirectory(project);
            File.WriteAllText(log, String.Join("\n", Day(d6, "s1").Concat(Day(d5, "s3"))) + "\n", new UTF8Encoding(false));
            File.SetLastWriteTimeUtc(log, Now);
            var history = new ClaudeHistory(bridge, TimeZoneInfo.Utc);
            // d5 was watched live before v0.60.0: all its replies (the card counts 3; the file says 4), with needs and a wait.
            var watched = new ClaudeDay { Date = d5, Tokens = 999, Replies = 4, Needs = 2, LongestWait = 60 };
            ClaudeBridge.WriteAtomic(Path.Combine(ClaudeHistory.Folder(bridge), watched.Name + ".json"), ClaudeJson.Compact(watched.ToJson()));
            var backfill = new ClaudeBackfill(config, history) { Enabled = true };
            for (int i = 0; i < 50 && !backfill.Done; i++) backfill.Step(Now);
            Assert(backfill.Done, "The backfill didn't finish.");
            var five = history.Read(d5); var six = history.Read(d6);
            Assert(five != null && five.Detail != null && five.Needs == 2 && five.LongestWait == 60 && five.Tokens == 999 && five.Replies == 4 && !five.FromLogs, "The v1 day wasn't upgraded as it was: " + (five == null ? "none" : five.Replies + " replies, " + five.Needs + " needs, detail " + (five.Detail != null)));
            Assert(five.Detail.Tools["Bash"].Calls == 1 && five.Detail.Models.ContainsKey("claude-opus-5-5"), "The upgraded day's detail.");
            Assert(six != null && six.Detail != null && six.FromLogs && six.Detail.Tools["Bash"].Errors == 1, "The new day isn't v2.");
            string file = File.ReadAllText(Path.Combine(ClaudeHistory.Folder(bridge), six.Name + ".json"));
            Assert(file.Contains("\"v\":2") && file.IndexOf("SECRET", StringComparison.Ordinal) < 0 && file.IndexOf("secret", StringComparison.Ordinal) < 0 && file.IndexOf("file.txt", StringComparison.Ordinal) < 0, "The v2 file: " + file);
            // Following: today's lines written after the pass are tallied for the live writer.
            Assert(backfill.DetailFor(Now.Date) == null, "Today has detail before any line today.");
            File.AppendAllText(log, String.Join("\n", Day(Now.Date, "s2")) + "\n", new UTF8Encoding(false));
            File.SetLastWriteTimeUtc(log, Now);
            backfill.Step(Now);
            var today = backfill.DetailFor(Now.Date);
            Assert(today != null && today.Tools["Bash"].Calls == 1 && today.Models["claude-sonnet-5-5"].Replies == 2, "Following didn't tally today's lines.");
            backfill.Step(Now.AddSeconds(30));
            Assert(backfill.DetailFor(Now.Date).Tools["Bash"].Calls == 1, "Following counted today's lines twice.");
            Assert(history.Read(Now.Date) == null, "The backfill wrote today.");
            // The live writer puts today's detail in today's file.
            history.Details = backfill.DetailFor;
            var replies = new List<ClaudeUsageEntry> { new ClaudeUsageEntry { At = Now.Date.AddHours(10), Session = "s2", Folder = @"C:\Code\api", Tokens = 150, Priced = true } };
            history.Save(replies, null, Now, true);
            var saved = history.Read(Now.Date);
            Assert(saved != null && saved.Detail != null && saved.Detail.Tools["Bash"].Calls == 1, "The live writer didn't write v2.");
            // A v1 file the live writer rewrites keeps a v2 detail it already had.
            history.Details = null;
            history.Save(replies.Concat(new[] { new ClaudeUsageEntry { At = Now.Date.AddHours(11), Session = "s2", Folder = @"C:\Code\api", Tokens = 10, Priced = true } }).ToList(), null, Now.AddMinutes(2), true);
            Assert(history.Read(Now.Date).Detail != null && history.Read(Now.Date).Replies == 2, "The live writer dropped the detail.");
        });

        check("Insights: 7 days, 30 days and All with the previous equal range, rankings with ties by name, and an honest empty range (v0.60.0)", () => {
            DateTime today = Now.Date;
            Func<DateTime, long, string[], ClaudeDay> make = (date, tokens, tools) => {
                var d = new ClaudeDay { Date = date, Tokens = tokens, Replies = (int)(tokens / 100), Cost = tokens / 1000.0, Detail = new ClaudeDayDetail() };
                d.Detail.Models["claude-opus-5-5"] = new ClaudeModelUse { Replies = d.Replies, Input = tokens };
                foreach (var t in tools) { ClaudeToolUse u; if (!d.Detail.Tools.TryGetValue(t, out u)) { u = new ClaudeToolUse(); d.Detail.Tools[t] = u; } u.Calls++; }
                d.Garages.Add(new ClaudeDayGarage { Folder = @"C:\Code\api", Name = "api", Tokens = tokens, Replies = d.Replies });
                d.Hours[10] = tokens;
                return d;
            };
            var days = new List<ClaudeDay> {
                make(today, 1000, new[] { "Bash", "Bash", "Read", "Edit" }),
                make(today.AddDays(-3), 1000, new[] { "Read", "Grep" }),
                make(today.AddDays(-8), 500, new[] { "Bash" }),
                make(today.AddDays(-40), 100, new[] { "Glob" }) };
            var set = ClaudeInsightsSet.Compute(days, today, Now);
            var week = set.Week;
            Assert(week.Days == 7 && week.Start == today.AddDays(-6) && week.PriorStart == today.AddDays(-13) && week.PriorEnd == today.AddDays(-7), "The week's range.");
            Assert(week.Tokens == 2000 && week.PriorTokens == 500 && ClaudeInsights.Change(week.Tokens, week.PriorTokens) == 3.0 && week.DaysWithFiles == 2 && week.DaysWithDetail == 2 && week.Series.Count == 7, "The week's totals: " + week.Tokens + " / " + week.PriorTokens);
            Assert(week.Tools[0].Name == "Bash" && week.Tools[0].Value == 2 && week.Tools[0].Prior == 1 && week.Tools[1].Name == "Read" && week.Tools[1].Value == 2, "Most used, ties by name: " + String.Join(",", week.Tools.Select(t => t.Name + t.Value)));
            var least = ClaudeInsights.Least(week.Tools, 2);
            Assert(least[0].Name == "Edit" && least[1].Name == "Grep", "Least used: " + String.Join(",", least.Select(t => t.Name)));
            Assert(Math.Abs(week.Tools.Sum(t => t.Share) - 1) < 1e-9 && week.Models[0].Name == "claude-opus-5-5" && week.Projects[0].Value == 2000 && week.Heat[((int)today.DayOfWeek + 6) % 7, 10] == 1000, "Shares, models, projects or the heat grid.");
            Assert(set.Month.Tokens == 2500 && set.Month.PriorTokens == 100 && set.All.Tokens == 2600 && set.All.Start == today.AddDays(-40) && !set.All.PriorStart.HasValue, "30 days and All.");
            Assert(ClaudeInsights.Change(5, 0) == null, "A change from nothing should be unknown, not infinite.");
            var empty = ClaudeInsightsSet.Compute(new List<ClaudeDay>(), today, Now);
            Assert(empty.Week.Missing.Length > 0 && empty.Week.Tokens == 0, "An empty range doesn't say why.");
            var v1 = ClaudeInsightsSet.Compute(new List<ClaudeDay> { new ClaudeDay { Date = today, Tokens = 5, Replies = 1 } }, today, Now);
            Assert(v1.Week.Missing.Contains("v0.60.0") && v1.Week.DaysWithDetail == 0, "Days without detail don't say why.");
            var sample = ClaudeInsightsSet.SampleSet(Now);
            Assert(sample.Sample && sample.Week.Sample && sample.Week.Models.Count == 3 && sample.Week.Tools.Count > 5 && sample.All.DaysFromLogs > 0 && ClaudeView.Fixture(Now, false).Insights != null, "The labelled sample.");
        });
    }
}

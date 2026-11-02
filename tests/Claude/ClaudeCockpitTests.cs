using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WidgetScreen;

// Package 4 of the watcher plan, "Cockpit" (decision 027). Answering a permission dialog from the dashboard: the ask
// hook, the listener, the answers and the Connect entry. Everything runs in throwaway folders with a stand-in clock, so
// no check waits for real time and no real Claude Code setting or session is touched.
internal static class ClaudeCockpitTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static string Fresh(string directory, string name)
    {
        string path = Path.Combine(directory, "claude-cockpit", name);
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
        return path;
    }
    private const string Request = "{\"session_id\":\"s1\",\"hook_event_name\":\"PermissionRequest\",\"cwd\":\"C:/Code/api\",\"tool_name\":\"Bash\",\"tool_input\":{\"command\":\"rm -rf node_modules\",\"description\":\"Remove node_modules\"}}";
    private const int Dashboard = 4242;
    private static bool Alive(int pid, long started) { return pid == Dashboard; }
    private static bool NotInFront(long window) { return false; }

    // Runs the ask hook with a stand-in clock; react sees each sleep (with the clock after it) and may answer.
    private static string Ask(string bridge, string input, Action<DateTime, int> react, out TimeSpan waited, Func<long, bool> inFront = null, Func<ClaudeProcesses.Owner> owner = null)
    {
        DateTime clock = Now; int sleeps = 0;
        string result = ClaudeBridge.Ask(input, bridge, owner ?? (() => new ClaudeProcesses.Owner { ClaudePid = 10, Window = 77 }), () => clock,
            ms => { clock = clock.AddMilliseconds(ms); sleeps++; if (react != null) react(clock, sleeps); }, Alive, inFront ?? NotInFront, 9001);
        waited = clock - Now;
        return result;
    }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Claude's ask hook does nothing unless a running dashboard listens and the session isn't in front", () => {
            string bridge = Fresh(directory, "ask-quiet");
            TimeSpan waited;
            Assert(Ask(bridge, Request, null, out waited) == "" && waited == TimeSpan.Zero, "With no listener the hook waited or answered.");
            ClaudeBridge.Listen(bridge, 999, 1, 30);
            Assert(Ask(bridge, Request, null, out waited) == "" && waited == TimeSpan.Zero, "A listener whose dashboard isn't running made the hook wait.");
            ClaudeBridge.Listen(bridge, Dashboard, 1, 30);
            Assert(Ask(bridge, Request, null, out waited, w => w == 77) == "" && waited == TimeSpan.Zero, "The hook waited although the user is looking at the session.");
            Assert(Ask(bridge, Request.Replace("PermissionRequest", "PreToolUse"), null, out waited) == "" && waited == TimeSpan.Zero, "Another event made the hook wait.");
            Assert(Ask(bridge, "not json", null, out waited) == "" && Ask(bridge, "{\"hook_event_name\":\"PermissionRequest\"}", null, out waited) == "", "A broken request answered.");
            Assert(!Directory.GetFiles(ClaudeBridge.AsksFolder(bridge), "*.ask.json").Any(), "An ask file was left behind.");
        });

        check("Claude's ask hook returns Allow once or Deny in Claude Code's form, and Open or no answer prints nothing", () => {
            string bridge = Fresh(directory, "ask-answers");
            ClaudeBridge.Listen(bridge, Dashboard, 1, 30);
            TimeSpan waited;
            foreach (var answer in new[] { ClaudeBridge.Allow, ClaudeBridge.Deny, ClaudeBridge.OpenIt })
            {
                ClaudeAsk seen = null;
                string printed = Ask(bridge, Request, (clock, n) => {
                    if (n != 3) return;
                    var open = ClaudeAsks.Pending(bridge, clock, pid => pid == 9001);
                    Assert(open.Count == 1, "The waiting ask isn't listed: " + open.Count);
                    seen = open[0];
                    Assert(ClaudeAsks.Answer(bridge, seen.Id, answer, clock), "The answer wasn't taken.");
                    Assert(ClaudeAsks.Pending(bridge, clock, pid => true).Count == 0, "An answered ask is still listed.");
                }, out waited);
                Assert(seen != null && seen.Session == "s1" && seen.Tool == "Bash" && seen.Detail == "rm -rf node_modules" && seen.ProjectName == "api" && seen.HookPid == 9001
                    && seen.Until == Now.AddSeconds(30) && seen.SecondsLeft(Now) == 30, "The ask doesn't say what it's about.");
                Assert(waited < TimeSpan.FromSeconds(1), "The hook didn't stop waiting once answered: " + waited);
                var o = ClaudeJson.Obj(ClaudeJson.Parse(printed.Length == 0 ? "{}" : printed));
                var decision = ClaudeJson.Obj(ClaudeJson.Obj(o, "hookSpecificOutput"), "decision");
                if (answer == ClaudeBridge.OpenIt) Assert(printed == "", "Open printed a decision: " + printed);
                else
                {
                    Assert(ClaudeJson.Str(ClaudeJson.Obj(o, "hookSpecificOutput"), "hookEventName") == "PermissionRequest" && ClaudeJson.Str(decision, "behavior") == answer, "The decision isn't Claude Code's form: " + printed);
                    // Allow once: no rules saved, the mode unchanged, the input as Claude sent it.
                    Assert(!decision.Contains("updatedPermissions") && !decision.Contains("updatedInput"), "Allow saved a rule or changed the input: " + printed);
                    Assert(answer == ClaudeBridge.Allow ? !decision.Contains("message") : ClaudeJson.Str(decision, "message") == ClaudeBridge.DeniedMessage && !decision.Contains("interrupt"), "Deny's message is wrong: " + printed);
                }
                Assert(Directory.GetFiles(ClaudeBridge.AsksFolder(bridge)).All(f => Path.GetFileName(f) == "listener.json"), "The ask or its answer wasn't cleared away.");
            }
            Assert(ClaudeBridge.Decision("maybe") == "" && ClaudeBridge.Decision("") == "", "An unknown answer printed a decision.");
        });

        check("Claude's ask hook gives up at its wait, or within a second of the dashboard closing", () => {
            string bridge = Fresh(directory, "ask-timeout");
            ClaudeBridge.Listen(bridge, Dashboard, 1, 12);
            TimeSpan waited;
            Assert(Ask(bridge, Request, null, out waited) == "" && waited >= TimeSpan.FromSeconds(12) && waited < TimeSpan.FromSeconds(12.5), "The hook didn't wait its 12 s: " + waited);
            Assert(!Directory.GetFiles(ClaudeBridge.AsksFolder(bridge), "*.ask.json").Any(), "A timed-out ask was left behind.");
            Assert(Ask(bridge, Request, (clock, n) => { if (n == 2) ClaudeAsks.StopListening(bridge); }, out waited) == "" && waited >= TimeSpan.FromSeconds(12), "StopListening took another dashboard's listener away.");
            Assert(Ask(bridge, Request, (clock, n) => { if (n == 2) File.Delete(ClaudeBridge.ListenerPath(bridge)); }, out waited) == "" && waited <= TimeSpan.FromSeconds(1.5), "The hook kept waiting after the dashboard stopped listening: " + waited);
            // The wait is kept to 10–120 s, and Connect's timeout stays above the longest.
            ClaudeBridge.Listen(bridge, Dashboard, 1, 500);
            Assert(ClaudeBridge.ReadListener(bridge).Item3 == ClaudeBridge.MostWait && ClaudeBridge.ClampWait(1) == ClaudeBridge.LeastWait && ClaudeBridge.AskTimeout > ClaudeBridge.MostWait, "The wait isn't clamped.");
            ClaudeBridge.StopListening(bridge, Dashboard);
            Assert(ClaudeBridge.ReadListener(bridge) == null, "StopListening left its own listener.");
        });

        check("Claude only answers an open ask, and clears asks whose hook has gone", () => {
            string bridge = Fresh(directory, "ask-guard");
            Assert(!ClaudeAsks.Answer(bridge, "nothing", ClaudeBridge.Allow, Now), "An ask that isn't there took an answer.");
            Assert(!ClaudeAsks.Answer(bridge, "..\\listener", ClaudeBridge.Allow, Now), "A path took an answer.");
            string folder = ClaudeBridge.AsksFolder(bridge);
            Directory.CreateDirectory(folder);
            Func<string, int, DateTime, string> write = (id, pid, until) => {
                File.WriteAllText(ClaudeBridge.AskPath(bridge, id), "{\"id\":\"" + id + "\",\"session\":\"s1\",\"tool\":\"Edit\",\"detail\":\"C:/Code/api/a.cs\",\"cwd\":\"C:/Code/api\",\"hookPid\":" + pid
                    + ",\"at\":" + ClaudeBridge.Epoch(Now.AddSeconds(-5)) + ",\"until\":" + ClaudeBridge.Epoch(until) + "}");
                return id;
            };
            write("live", 1, Now.AddSeconds(20)); write("dead", 2, Now.AddSeconds(20)); write("old", 1, Now.AddSeconds(-60)); write("over", 1, Now.AddSeconds(-1));
            var open = ClaudeAsks.Pending(bridge, Now, pid => pid == 1);
            Assert(open.Select(a => a.Id).SequenceEqual(new[] { "live" }), "The open asks are wrong: " + String.Join(",", open.Select(a => a.Id)));
            Assert(!File.Exists(ClaudeBridge.AskPath(bridge, "dead")) && !File.Exists(ClaudeBridge.AskPath(bridge, "old")) && File.Exists(ClaudeBridge.AskPath(bridge, "over")), "Left-behind asks weren't cleared, or one in its slack was.");
            Assert(!ClaudeAsks.Answer(bridge, "over", ClaudeBridge.Allow, Now) && !ClaudeAsks.Answer(bridge, "live", "always", Now), "A finished ask or an unknown answer was taken.");
            Assert(ClaudeAsks.Answer(bridge, "live", ClaudeBridge.Deny, Now) && !ClaudeAsks.Answer(bridge, "live", ClaudeBridge.Allow, Now), "A second answer replaced the first.");
            Assert(ClaudeBridge.ReadAnswer(bridge, "live") == ClaudeBridge.Deny, "The answer isn't Deny.");
            // An answer file naming another ask doesn't count.
            File.WriteAllText(ClaudeBridge.AnswerPath(bridge, "over"), "{\"id\":\"live\",\"answer\":\"allow\"}");
            Assert(ClaudeBridge.ReadAnswer(bridge, "over") == "", "An answer for another ask was read.");
        });

        check("Claude's ask says what the dialog is about in one short line", () => {
            Func<string, string> detail = json => ClaudeBridge.AskDetail("X", ClaudeJson.Obj(ClaudeJson.Parse(json)));
            Assert(detail("{\"command\":\"git push\\n--force\",\"description\":\"Push\"}") == "git push --force", "A command isn't one line: " + detail("{\"command\":\"git push\\n--force\"}"));
            Assert(detail("{\"file_path\":\"C:/a.cs\",\"old_string\":\"x\"}") == "C:/a.cs" && detail("{\"url\":\"https://example.com\",\"prompt\":\"read\"}") == "https://example.com", "A file or address isn't picked.");
            Assert(detail("{\"server\":\"db\",\"n\":3}") == "db" && detail("{}") == "" && ClaudeBridge.AskDetail("X", null) == "", "Another input isn't handled.");
            Assert(detail("{\"command\":\"" + new string('a', 400) + "\"}").Length == 300, "A long command isn't cut to 300 characters.");
        });

        check("Claude Connect adds the ask hook as an ordinary hook, and an older connection offers Update", () => {
            string config = Fresh(directory, "ask-config"), bridge = Fresh(directory, "ask-bridge");
            string hook = Path.Combine(directory, ClaudeBridge.HookExe);
            var connector = new ClaudeConnector(config, bridge);
            connector.Connect(hook);
            var hooks = ClaudeJson.Obj(ClaudeJson.Obj(ClaudeJson.Parse(File.ReadAllText(connector.SettingsPath))), "hooks");
            var asks = ((List<object>)hooks.Get("PermissionRequest")).OfType<JsonObject>().SelectMany(g => ClaudeJson.List(g, "hooks").OfType<JsonObject>()).Where(h => ClaudeJson.Str(h, "command").EndsWith("\" ask")).ToList();
            Assert(asks.Count == 1 && !ClaudeJson.Bool(asks[0], "async") && ClaudeJson.Num(asks[0], "timeout") == ClaudeBridge.AskTimeout && ClaudeConnector.Ours(asks[0]), "The ask hook isn't one ordinary hook with its timeout.");
            Assert(connector.HasAskHook() && !connector.NeedsUpdate(), "A fresh connection lacks the ask hook or asks for an update.");
            // A package 3 connection: no ask hook.
            string text = File.ReadAllText(connector.SettingsPath);
            var root = ClaudeJson.Obj(ClaudeJson.Parse(text));
            var permission = (List<object>)ClaudeJson.Obj(root, "hooks").Get("PermissionRequest");
            permission.RemoveAll(g => ClaudeJson.List((JsonObject)g, "hooks").OfType<JsonObject>().Any(h => ClaudeJson.Str(h, "command").EndsWith("\" ask")));
            File.WriteAllText(connector.SettingsPath, ClaudeJson.Write(root));
            Assert(!connector.HasAskHook() && connector.NeedsUpdate(), "A connection without the ask hook doesn't offer Update connection.");
            connector.Connect(hook);
            Assert(connector.HasAskHook() && !connector.NeedsUpdate(), "Update connection didn't add the ask hook.");
            connector.Disconnect();
            Assert(!connector.HasAskHook() && !connector.IsConnected(), "Disconnect left the ask hook.");
        });

        check("Claude history rolls replies into local days with hours, garages and needs", () => {
            var zone = TimeZoneInfo.CreateCustomTimeZone("Plus3", TimeSpan.FromHours(3), "Plus3", "Plus3");
            var history = new ClaudeHistory(Fresh(directory, "history-roll"), zone);
            Func<DateTime, string, string, long, bool, ClaudeUsageEntry> reply = (at, session, folder, tokens, priced) =>
                new ClaudeUsageEntry { At = at, Session = session, Folder = folder, Model = "claude-opus-5-5", Tokens = tokens, CacheRead = tokens * 10, Cost = tokens / 1000.0, Priced = priced };
            var replies = new List<ClaudeUsageEntry> {
                reply(Now, "a", "C:/Code/api", 1000, true),                  // 15:00 local, 2 Nov
                reply(Now.AddMinutes(30), "a", "C:\\Code\\api", 500, true),   // same garage, written the other way
                reply(Now.AddMinutes(40), "b", "C:/Code/web", 2000, false),
                reply(Now.AddHours(-14), "c", "C:/Code/api", 300, true) };   // 01:00 local, still 2 Nov
            history.Note(new[] { new ClaudeSession { Id = "a", Running = true, State = ClaudeState.NeedsYou } }, Now);
            history.Note(new[] { new ClaudeSession { Id = "a", Running = true, State = ClaudeState.NeedsYou } }, Now.AddSeconds(50));
            history.Note(new ClaudeSession[0], Now.AddSeconds(95));
            history.Note(new[] { new ClaudeSession { Id = "b", Running = true, State = ClaudeState.NeedsYou } }, Now.AddMinutes(5));
            var days = history.Roll(replies, f => f.EndsWith("api") ? "API server" : "");
            Assert(days.Count == 1, "Replies landed on " + days.Count + " days, not one local day.");
            var day = days[0];
            Assert(day.Date == new DateTime(2026, 11, 2) && day.Tokens == 3800 && day.CacheRead == 38000 && day.Replies == 4 && day.Threads == 3, "The day's totals are wrong.");
            Assert(Math.Abs(day.Cost - 1.8) < 1e-9 && day.Unpriced == 1, "The priced cost or the unpriced count is wrong: " + day.Cost);
            Assert(day.Hours[15] == 3500 && day.Hours[1] == 300 && day.TokensBy(TimeSpan.FromHours(15.5)) == 300 + 1750, "The hours are wrong.");
            Assert(day.Needs == 2 && day.LongestWait == 95, "Needs or the longest wait are wrong: " + day.Needs + ", " + day.LongestWait);
            Assert(day.Garages.Count == 2 && day.Garages[0].Name == "web" && day.Garages[0].Tokens == 2000 && day.Garages[1].Name == "API server" && day.Garages[1].Tokens == 1800 && day.Garages[1].Threads == 2,
                "The garages aren't counted by folder: " + String.Join(",", day.Garages.Select(g => g.Name + "=" + g.Tokens)));
            var back = ClaudeDay.FromJson(ClaudeJson.Obj(ClaudeJson.Parse(ClaudeJson.Compact(day.ToJson()))));
            Assert(ClaudeJson.Compact(back.ToJson()) == ClaudeJson.Compact(day.ToJson()), "A day doesn't read back as written.");
            Assert(!ClaudeJson.Compact(day.ToJson()).Contains("\"session\""), "A day file names a session.");
        });

        check("Claude history keeps 8 weeks, never shrinks a day, and clears when turned off", () => {
            string bridge = Fresh(directory, "history-files");
            var history = new ClaudeHistory(bridge, TimeZoneInfo.Utc);
            var big = new List<ClaudeUsageEntry> { new ClaudeUsageEntry { At = Now.AddDays(-1), Session = "a", Folder = "C:/Code/api", Tokens = 900, Priced = true },
                new ClaudeUsageEntry { At = Now.AddDays(-1).AddHours(1), Session = "a", Folder = "C:/Code/api", Tokens = 100, Priced = true },
                new ClaudeUsageEntry { At = Now, Session = "b", Folder = "C:/Code/api", Tokens = 50, Priced = true } };
            Assert(history.Save(big, null, Now, true) && history.Read(Now.Date.AddDays(-1)).Tokens == 1000 && history.Read(Now.Date).Tokens == 50, "The days weren't written.");
            Assert(!history.Due(Now.AddSeconds(30)) && history.Due(Now.AddMinutes(1)) && !history.Save(big, null, Now.AddSeconds(30)), "Saving isn't once a minute.");
            // A week on, the scan has lost yesterday's first reply: the stored day stays whole.
            Assert(!history.Save(big.Skip(1).ToList(), null, Now.AddMinutes(2), true) && history.Read(Now.Date.AddDays(-1)).Tokens == 1000, "A day shrank as its replies fell out of the scan.");
            // Old and stray files go; the sparkline is 7 days with today's live count.
            string folder = ClaudeHistory.Folder(bridge);
            File.WriteAllText(Path.Combine(folder, Now.Date.AddDays(-56).ToString("yyyy-MM-dd") + ".json"), "{\"date\":\"" + Now.Date.AddDays(-56).ToString("yyyy-MM-dd") + "\"}");
            File.WriteAllText(Path.Combine(folder, Now.Date.AddDays(-55).ToString("yyyy-MM-dd") + ".json"), "{\"date\":\"" + Now.Date.AddDays(-55).ToString("yyyy-MM-dd") + "\"}");
            File.WriteAllText(Path.Combine(folder, "notes.json"), "{}");
            history.Prune(Now);
            Assert(!File.Exists(Path.Combine(folder, Now.Date.AddDays(-56).ToString("yyyy-MM-dd") + ".json")) && File.Exists(Path.Combine(folder, Now.Date.AddDays(-55).ToString("yyyy-MM-dd") + ".json")) && !File.Exists(Path.Combine(folder, "notes.json")), "Pruning kept the wrong files.");
            var spark = history.Spark(Now, new ClaudeDay { Date = Now.Date, Tokens = 75 });
            Assert(spark.Length == 7 && spark[5] == 1000 && spark[6] == 75 && spark.Take(5).All(t => t == 0), "The sparkline is wrong: " + String.Join(",", spark));
            Assert(history.SameTimeLastWeek(Now) == null, "A missing day gave a last-week tick.");
            var lastWeek = new ClaudeDay { Date = Now.Date.AddDays(-7) }; lastWeek.Hours[11] = 600; lastWeek.Hours[12] = 400; lastWeek.Tokens = 1000;
            ClaudeBridge.WriteAtomic(Path.Combine(folder, lastWeek.Name + ".json"), ClaudeJson.Compact(lastWeek.ToJson()));
            Assert(history.SameTimeLastWeek(Now.AddMinutes(30)) == 800, "Same time last week isn't counted by the hour: " + history.SameTimeLastWeek(Now.AddMinutes(30)));
            history.Clear();
            Assert(!Directory.Exists(folder), "Turning History off left the files.");
        });

        check("Claude export writes a safe CSV of the last 7 days and a Markdown summary", () => {
            Assert(ClaudeExport.Field("plain") == "plain" && ClaudeExport.Field("a,b") == "\"a,b\"" && ClaudeExport.Field("say \"hi\"") == "\"say \"\"hi\"\"\"", "Quoting is wrong.");
            Assert(ClaudeExport.Field("=HYPERLINK(1)") == "'=HYPERLINK(1)" && ClaudeExport.Field("@cmd") == "'@cmd" && ClaudeExport.Field("-12.5") == "-12.5", "A formula isn't defused, or a number was.");
            var replies = new List<ClaudeUsageEntry> {
                new ClaudeUsageEntry { At = Now, Session = "s1", Folder = "C:/Code/api", Model = "claude-opus-5-5", Tokens = 1200, CacheRead = 5000, Cost = 0.5, Priced = true },
                new ClaudeUsageEntry { At = Now.AddMinutes(1), Session = "s1", Folder = "C:/Code/api", Model = "claude-sonnet-5-5", Tokens = 300, Cost = 0.25, Priced = true },
                new ClaudeUsageEntry { At = Now.AddDays(-1), Session = "s2", Folder = "C:/Code/web", Model = "mystery", Tokens = 10, Priced = false },
                new ClaudeUsageEntry { At = Now.AddDays(-8), Session = "s3", Folder = "C:/Code/old", Tokens = 99, Priced = true } };
            string csv = ClaudeExport.Csv(replies, id => id == "s1" ? "=Fix, the \"login\"" : "", f => "", Now, TimeZoneInfo.Utc);
            var lines = csv.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert(lines.Length == 3 && lines[0] == String.Join(",", ClaudeExport.Columns.Select(ClaudeExport.Field)), "The CSV has the wrong rows: " + csv);
            Assert(lines[1] == "2026-11-02,api,\"'=Fix, the \"\"login\"\"\",s1,Opus 5.5 Sonnet 5.5,2,1500,5000,0.7500", "The first row is wrong: " + lines[1]);
            Assert(lines[2].StartsWith("2026-11-01,web,") && lines[2].EndsWith(",1,10,0,"), "An unpriced row isn't blank in the API value: " + lines[2]);
            var days = new List<ClaudeDay> { new ClaudeDay { Date = Now.Date, Tokens = 1500, Replies = 2, Threads = 1, Needs = 1, LongestWait = 125,
                Garages = new List<ClaudeDayGarage> { new ClaudeDayGarage { Folder = "C:\\Code\\api", Name = "api_server*", Tokens = 1500, Replies = 2 } } } };
            string md = ClaudeExport.Summary(days, Now);
            Assert(md.StartsWith("## Claude Code, last 7 days") && md.Contains("| Mon 2 Nov |") && md.Contains("| 2 min |") && md.Contains("api\\_server\\*") && !md.Contains("s1"), "The summary is wrong: " + md);
        });

        check("Claude answering is off by default with a 30 s wait, kept to 10-120 s", () => {
            var fresh = new AppSettings();
            Assert(!fresh.ClaudeAnswer && fresh.ClaudeAnswerWait == 30 && fresh.ClaudeHistory, "The defaults are wrong.");
            string path = Path.Combine(Fresh(directory, "ask-settings"), "settings.xml");
            SettingsStore.Save(path, new AppSettings { ClaudeAnswer = true, ClaudeAnswerWait = 45 });
            string warning; var loaded = SettingsStore.Load(path, out warning);
            Assert(loaded.ClaudeAnswer && loaded.ClaudeAnswerWait == 45 && loaded.Copy().ClaudeAnswerWait == 45, "The choice wasn't kept.");
            File.WriteAllText(path, File.ReadAllText(path).Replace("<ClaudeAnswerWait>45</ClaudeAnswerWait>", "<ClaudeAnswerWait>999</ClaudeAnswerWait>"));
            Assert(SettingsStore.Load(path, out warning).ClaudeAnswerWait == 120, "A wait over 120 s survived loading.");
        });
    }
}

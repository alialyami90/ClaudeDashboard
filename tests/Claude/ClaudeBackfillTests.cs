using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ClaudeDashboard;

// v0.59.0, History from the logs: past days built from Claude Code's session logs (old-dated jsonl in a throwaway
// Claude folder), and Sources' Keep logs a year (cleanupPeriodDays in a throwaway settings.json). Never the PC's own.
internal static class ClaudeBackfillTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static string Fresh(string directory, string name)
    {
        string path = Path.Combine(directory, "claude-backfill", name);
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
        return path;
    }
    private static string Stamp(DateTime at) { return at.ToString("yyyy-MM-ddTHH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture) + "Z"; }
    // An assistant reply's log line: 100 fresh input, 50 output, 1000 cache reads (150 tokens as the card counts them).
    private static string Reply(string session, DateTime at, string request, string model = "claude-opus-5-5")
    {
        return "{\"type\":\"assistant\",\"sessionId\":\"" + session + "\",\"cwd\":\"C:\\\\Code\\\\api\",\"timestamp\":\"" + Stamp(at) + "\",\"requestId\":\"" + request + "\",\"message\":{\"id\":\"msg_" + request + "\",\"model\":\"" + model
            + "\",\"content\":[{\"type\":\"text\",\"text\":\"SECRET REPLY WORDS\"}],\"usage\":{\"input_tokens\":100,\"output_tokens\":50,\"cache_creation_input_tokens\":0,\"cache_read_input_tokens\":1000}}}";
    }
    private static string Prompt(string session, DateTime at)
    {
        return "{\"type\":\"user\",\"sessionId\":\"" + session + "\",\"cwd\":\"C:\\\\Code\\\\api\",\"timestamp\":\"" + Stamp(at) + "\",\"message\":{\"role\":\"user\",\"content\":\"SECRET PROMPT WORDS to name the thread\"}}";
    }
    private static void Log(string path, IEnumerable<string> lines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, String.Join("\n", lines) + "\n", new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(path, Now);
    }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Claude history from the logs: past days built once, existing days and today left alone, a reply counted once, numbers only (v0.59.0)", () => {
            string config = Fresh(directory, "config"), bridge = Fresh(directory, "bridge");
            string project = Path.Combine(config, "projects", "C--Code-api");
            DateTime d40 = Now.AddDays(-40), d10 = Now.AddDays(-10), d3 = Now.AddDays(-3), d1 = Now.AddDays(-1), old = Now.AddDays(-400);
            // Session one: a prompt, a reply 40 days ago streamed over two lines (one request id), one 3 days ago, two
            // yesterday, one today, and one 400 days ago (past the year kept).
            // Its first line is a prompt the day before that: the oldest day the logs hold comes from any line.
            Log(Path.Combine(project, "s1.jsonl"), new[] { Prompt("s1", old.AddDays(-1)), Prompt("s1", old), Reply("s1", old, "r0"), Reply("s1", d40, "r1"), Reply("s1", d40.AddSeconds(2), "r1"),
                Reply("s1", d3, "r2"), Reply("s1", d1, "r3"), Reply("s1", d1.AddHours(1), "r4", "gateway-model"), Reply("s1", Now, "r5") });
            // Session two, plus a helper's log that repeats r3 (the same reply, seen in both).
            Log(Path.Combine(project, "s2.jsonl"), new[] { Prompt("s2", d40), Reply("s2", d40.AddHours(2), "r6"), Reply("s2", d10, "r7"), Reply("s2", d10.AddHours(1), "r8") });
            Log(Path.Combine(project, "s1", "subagents", "agent-a1.jsonl"), new[] { Reply("s1", d1, "r3") });
            // A day that already has its file, and a big line that crosses a chunk.
            var history = new ClaudeHistory(bridge, TimeZoneInfo.Utc);
            var kept = new ClaudeDay { Date = d3.Date, Tokens = 7, Replies = 1, Needs = 2 };
            ClaudeBridge.WriteAtomic(Path.Combine(ClaudeHistory.Folder(bridge), kept.Name + ".json"), ClaudeJson.Compact(kept.ToJson()));
            string keptText = File.ReadAllText(Path.Combine(ClaudeHistory.Folder(bridge), kept.Name + ".json"));
            // A day the live History wrote from a partial scan: 1 reply where the logs hold 2, with needs it saw.
            var partial = new ClaudeDay { Date = d10.Date, Tokens = 7, Replies = 1, Needs = 3, LongestWait = 90 };
            ClaudeBridge.WriteAtomic(Path.Combine(ClaudeHistory.Folder(bridge), partial.Name + ".json"), ClaudeJson.Compact(partial.ToJson()));

            var backfill = new ClaudeBackfill(config, history) { StepBudget = 100 };
            Assert(!backfill.Step(Now) && !backfill.Started, "The backfill ran while History is off.");
            backfill.Enabled = true;
            int steps = 0;
            while (!backfill.Done && steps < 100) { backfill.Step(Now); steps++; }
            Assert(backfill.Done && steps > 1 && backfill.FilesTotal == 3 && backfill.FilesRead == 3, "The backfill didn't finish in budgeted steps: " + steps + " steps, " + backfill.FilesRead + " of " + backfill.FilesTotal);
            Assert(backfill.OldestLog.HasValue && backfill.OldestLog.Value == old.AddDays(-1).Date, "The oldest log day, from any line: " + backfill.OldestLog);

            var day40 = history.Read(d40.Date);
            Assert(day40 != null && day40.FromLogs && day40.Replies == 2 && day40.Tokens == 300 && day40.CacheRead == 2000 && day40.Threads == 2, "40 days ago: " + (day40 == null ? "none" : day40.Replies + " replies, " + day40.Tokens + " tokens, " + day40.Threads + " threads"));
            Assert(day40.Hours[12] == 150 && day40.Hours[14] == 150 && day40.Garages.Count == 1 && day40.Garages[0].Name == "api" && day40.Garages[0].Threads == 2 && day40.Cost > 0 && day40.Needs == 0 && day40.LongestWait == 0, "40 days ago's hours, garage or cost.");
            var day1 = history.Read(d1.Date);
            Assert(day1 != null && day1.Replies == 2 && day1.Tokens == 300 && day1.Unpriced == 1, "Yesterday counted the helper's copy of a reply, or the unpriced one: " + (day1 == null ? "none" : day1.Replies + " replies, " + day1.Unpriced + " unpriced"));
            // Since v0.60.0 a v1 day with as many replies gets the v2 detail from the logs; its counts, needs and waits stay.
            var keptNow = history.Read(kept.Date);
            Assert(keptNow != null && keptNow.Tokens == kept.Tokens && keptNow.Replies == kept.Replies && keptNow.Needs == kept.Needs && keptNow.LongestWait == kept.LongestWait && !keptNow.FromLogs && keptNow.Detail != null && keptText.IndexOf("\"v\"", StringComparison.Ordinal) < 0,
                "A day that had its file had its counts replaced, or wasn't upgraded to v2.");
            Assert(history.Read(Now.Date) == null && history.Read(old.Date) == null, "Today, or a day past the year kept, was written.");
            var day10 = history.Read(d10.Date);
            Assert(day10 != null && day10.Replies == 2 && day10.Tokens == 300 && day10.CacheRead == 2000 && day10.Threads == 1 && day10.Garages.Count == 1 && day10.Garages[0].Replies == 2
                && day10.Needs == 3 && day10.LongestWait == 90 && day10.FromLogs, "The partial day wasn't corrected from the logs, keeping its needs: " + (day10 == null ? "none" : day10.Replies + " replies, " + day10.Tokens + " tokens, " + day10.Needs + " needs, " + day10.LongestWait + " s"));
            Assert(backfill.DaysWritten == 4, "Days written (three built, one v1 day given its detail): " + backfill.DaysWritten);
            foreach (var file in Directory.GetFiles(ClaudeHistory.Folder(bridge)))
            {
                string text = File.ReadAllText(file);
                Assert(text.IndexOf("SECRET", StringComparison.Ordinal) < 0 && text.IndexOf("opus", StringComparison.OrdinalIgnoreCase) < 0 && text.IndexOf("s1", StringComparison.Ordinal) < 0 && text.IndexOf("r1", StringComparison.Ordinal) < 0, "Text, a model, a session or a request id landed in " + Path.GetFileName(file) + ": " + text);
            }
            // Once done it stays done; the live History keeps the day it built marked as from the logs.
            Assert(!backfill.Step(Now.AddMinutes(1)), "A finished backfill wrote again.");
            var later = new List<ClaudeUsageEntry> { new ClaudeUsageEntry { At = d1, Session = "s1", Folder = "C:/Code/api", Tokens = 400, Priced = true }, new ClaudeUsageEntry { At = d1.AddMinutes(5), Session = "s1", Folder = "C:/Code/api", Tokens = 400, Priced = true } };
            history.Save(later, null, Now, true);
            Assert(history.Read(d1.Date).Tokens == 800 && history.Read(d1.Date).FromLogs, "The live History dropped the from-logs mark.");
            // Off and on again: it starts over (Clear deleted the files).
            backfill.Enabled = false; history.Clear(); backfill.Enabled = true;
            Assert(!backfill.Done && !backfill.Started, "Turning History on again didn't start the backfill over.");
            for (int i = 0; i < 100 && !backfill.Done; i++) backfill.Step(Now);
            Assert(history.Read(d40.Date) != null && history.Read(d40.Date).Replies == 2, "The backfill didn't rebuild the days after History came back.");
        });

        check("Claude history from the logs: a Claude app scratch workspace's log past 260 characters is read, and a log that can't be read is counted (v0.59.0)", () => {
            string config = Fresh(directory, "config-long"), bridge = Fresh(directory, "bridge-long");
            // Shaped like the log Ali's PC dropped: a Claude app scratch workspace, its encoded folder name long enough
            // that the whole path passes 260 characters, a head with no cwd, three replies each streamed over three
            // lines (one request id each), late on 2 Oct UTC, which is 3 Oct at UTC+3.
            string name = "C--Users-PCD-AppData-Roaming-Claude-scratch-workspaces-8b5be831-0f6e-4c43-9d1a-6b2f3e4a5c7d-scratch-2026-10-02-1141a1";
            while (name.Length < 200) name += "-workspace";
            string folder = Path.Combine(config, "projects", name);
            string log = Path.Combine(folder, "f256e218-45c9-4519-9cfb-c4a43317cd1a.jsonl");
            if (log.Length <= 260) log = Path.Combine(folder, new string('f', 270 - folder.Length) + ".jsonl");
            Assert(log.Length > 260, "The test path isn't long: " + log.Length);
            Directory.CreateDirectory(ClaudeBackfill.LongPath(folder));
            DateTime at = new DateTime(2026, 10, 2, 22, 27, 0, DateTimeKind.Utc);
            var lines = new List<string> {
                "{\"type\":\"queue-operation\",\"operation\":\"enqueue\",\"timestamp\":\"" + Stamp(at.AddSeconds(-30)) + "\",\"sessionId\":\"f256\"}",
                "{\"type\":\"custom-title\",\"customTitle\":\"SECRET TITLE\",\"sessionId\":\"f256\"}",
                "{\"type\":\"agent-name\",\"agentName\":\"SECRET AGENT\",\"sessionId\":\"f256\"}",
                "{\"type\":\"user\",\"isSidechain\":false,\"sessionId\":\"f256\",\"timestamp\":\"" + Stamp(at.AddSeconds(-10)) + "\",\"message\":{\"role\":\"user\",\"content\":\"SECRET PROMPT WORDS\"}}" };
            for (int r = 0; r < 3; r++)
                for (int part = 0; part < 3; part++)
                    lines.Add("{\"type\":\"assistant\",\"isSidechain\":false,\"sessionId\":\"f256\",\"timestamp\":\"" + Stamp(at.AddMinutes(2 * r).AddSeconds(part)) + "\",\"requestId\":\"req_" + r + "\",\"message\":{\"id\":\"msg_" + r + "\",\"model\":\"claude-opus-5-5\",\"content\":[{\"type\":\"text\",\"text\":\"SECRET\"}],\"usage\":{\"input_tokens\":100,\"output_tokens\":50,\"cache_read_input_tokens\":1000}}}");
            lines.Add("{\"type\":\"last-prompt\",\"sessionId\":\"f256\"}");
            File.WriteAllText(ClaudeBackfill.LongPath(folder) + "\\" + Path.GetFileName(log), String.Join("\n", lines) + "\n", new UTF8Encoding(false));
            File.SetLastWriteTimeUtc(ClaudeBackfill.LongPath(folder) + "\\" + Path.GetFileName(log), new DateTime(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc));
            // And an ordinary log next to it, in a short folder.
            Log(Path.Combine(config, "projects", "C--Code-api", "s1.jsonl"), new[] { Reply("s1", Now.AddDays(-20), "r1") });

            var history = new ClaudeHistory(bridge, TimeZoneInfo.CreateCustomTimeZone("UTC+3", TimeSpan.FromHours(3), "UTC+3", "UTC+3"));
            var backfill = new ClaudeBackfill(config, history) { Enabled = true };
            for (int i = 0; i < 100 && !backfill.Done; i++) backfill.Step(Now);
            DateTime oct3 = new DateTime(2026, 10, 3);
            Assert(backfill.Done && backfill.FilesTotal == 2 && backfill.FilesSkipped == 0, "The long log wasn't listed or read: " + backfill.FilesRead + " of " + backfill.FilesTotal + ", " + backfill.FilesSkipped + " skipped");
            Assert(backfill.OldestLog == oct3, "The oldest day: " + backfill.OldestLog);
            var day = history.Read(oct3);
            Assert(day != null && day.Replies == 3 && day.Tokens == 450 && day.Threads == 1 && day.FromLogs && day.Hours[1] == 450, "3 Oct from the scratch workspace's log: " + (day == null ? "none" : day.Replies + " replies, " + day.Tokens + " tokens"));
            string text = File.ReadAllText(Path.Combine(ClaudeHistory.Folder(bridge), "2026-10-03.json"));
            Assert(text.IndexOf("SECRET", StringComparison.Ordinal) < 0 && text.IndexOf("f256", StringComparison.Ordinal) < 0, "Words or the session id landed in the day file: " + text);

            // A log that can't be read is skipped and counted; Sources says so.
            var view = new ClaudeView { BackfillOn = true, BackfillDone = true, BackfillDays = 2, BackfillSkipped = 1, OldestLog = oct3 };
            Assert(ClaudeSources.Backfill(view) == "Past days built from the logs: 2 days · logs from 3 Oct 2026 · 1 log couldn't be read", "Skipped: " + ClaudeSources.Backfill(view));
            // The long folder goes through the prefix too, so the next run starts clean.
            Directory.Delete(ClaudeBackfill.LongPath(Path.Combine(config, "projects")), true);
        });

        check("Claude history from the logs: a backfill that's behind is stepped on each heartbeat, not only by the log scan (v0.59.3)", () => {
            string config = Fresh(directory, "config-beat"), bridge = Fresh(directory, "bridge-beat");
            string project = Path.Combine(config, "projects", "C--Code-api");
            for (int f = 0; f < 6; f++) Log(Path.Combine(project, "s" + f + ".jsonl"), Enumerable.Range(0, 20).Select(r => Reply("s" + f, Now.AddDays(-5 - f).AddMinutes(r), "b" + f + "_" + r)));
            var usage = new ClaudeUsage(config);
            usage.Backfill = new ClaudeBackfill(config, new ClaudeHistory(bridge, TimeZoneInfo.Utc)) { StepBudget = 4000 };
            Assert(!usage.BackfillBehind, "Off, the backfill says it's behind.");
            usage.Backfill.Enabled = true;
            Assert(usage.BackfillBehind, "On and not started, the backfill doesn't say it's behind.");
            // The heartbeat's rule (AppController.TickClaude): behind, and the last step done, so step again. No log scan
            // runs here at all.
            int beats = 0, read = -1; bool moved = true;
            while (usage.BackfillBehind && beats < 200)
            {
                usage.StepBackfill(Now.AddSeconds(beats)); beats++;
                moved &= usage.Backfill.FilesRead >= read; read = usage.Backfill.FilesRead;
            }
            Assert(!usage.BackfillBehind && usage.Backfill.Done && beats > 2 && moved, "The backfill wasn't finished by consecutive heartbeats: " + beats + " beats, " + usage.Backfill.FilesRead + " of " + usage.Backfill.FilesTotal);
            Assert(usage.Backfill.DaysWritten == 6, "Days written by the heartbeats: " + usage.Backfill.DaysWritten);
            Assert(!usage.StepBackfill(Now.AddMinutes(5)), "A finished backfill stepped again.");
            // Nothing to list: the pass ends rather than asking for a step every second.
            var none = new ClaudeUsage(Path.Combine(config, "missing"));
            none.Backfill = new ClaudeBackfill(Path.Combine(config, "missing"), new ClaudeHistory(bridge, TimeZoneInfo.Utc)) { Enabled = true };
            none.StepBackfill(Now);
            Assert(!none.BackfillBehind, "With no logs folder the backfill stays behind.");
        });

        check("Claude history from the logs: History and Sources say so, and the calendar stays 8 weeks while the files keep a year (v0.59.0)", () => {
            Assert(ClaudeHistory.KeptDays == 365 && HistoryModel.CalendarDays == 56, "Kept " + ClaudeHistory.KeptDays + " days, calendar " + HistoryModel.CalendarDays);
            var local = Now.ToLocalTime().Date;
            var days = new List<ClaudeDay> { new ClaudeDay { Date = local.AddDays(-2), Tokens = 900, Replies = 3, Threads = 1, FromLogs = true }, new ClaudeDay { Date = local.AddDays(-1), Tokens = 500, Replies = 2, Needs = 1, LongestWait = 60 } };
            var m = HistoryModel.From(days, Now, false, "");
            var fromLogs = m.Last7.First(r => r.Date == local.AddDays(-2));
            Assert(fromLogs.FromLogs && fromLogs.Cells[4] == "—" && fromLogs.Cells[5] == HistoryModel.FromLogsCell, "A from-logs day shows 0 needs: " + String.Join(" | ", fromLogs.Cells));
            Assert(m.FromLogsDays == 1 && m.FromLogs(local.AddDays(-2)) && !m.FromLogs(local.AddDays(-1)) && m.FromLogsLine().StartsWith("Dot: built from Claude Code's logs, 1 day (") && m.CalendarWords().Contains(m.FromLogsLine()), "The calendar doesn't say which days came from the logs: " + m.FromLogsLine());
            // A day corrected from the logs keeps the wait the dashboard saw, and shows it.
            days[0].LongestWait = 90;
            var corrected = HistoryModel.From(days, Now, false, "").Last7.First(r => r.Date == local.AddDays(-2));
            Assert(corrected.FromLogs && corrected.Cells[5] == HistoryModel.Wait(TimeSpan.FromSeconds(90)), "A corrected day's wait: " + corrected.Cells[5]);
            days[0].LongestWait = 0;
            var watched = m.Last7.First(r => r.Date == local.AddDays(-1));
            Assert(!watched.FromLogs && watched.Cells[4] == "1", "A watched day's needs: " + watched.Cells[4]);
            var json = ClaudeDay.FromJson(ClaudeJson.Obj(ClaudeJson.Parse(ClaudeJson.Compact(days[0].ToJson()))));
            Assert(json.FromLogs && !ClaudeDay.FromJson(ClaudeJson.Obj(ClaudeJson.Parse(ClaudeJson.Compact(days[1].ToJson())))).FromLogs, "FromLogs doesn't survive the file.");

            var view = new ClaudeView();
            Assert(ClaudeSources.LogsKept(view) == "Claude Code keeps its logs 30 days (Claude Code's default)", "Unset: " + ClaudeSources.LogsKept(view));
            view.LogsKeptDays = 365; view.LogsKeptByUs = true;
            Assert(ClaudeSources.LogsKept(view).EndsWith("365 days (set by Claude Dashboard)") && ClaudeSources.LogsButton(view)[1] == ClaudeSources.UndoKeepLogs, "Set by the dashboard: " + ClaudeSources.LogsKept(view));
            view.LogsKeptByUs = false; view.LogsKeptDays = 400;
            Assert(ClaudeSources.LogsButton(view)[0] == "" && ClaudeSources.LogsKept(view) == "Claude Code keeps its logs 400 days", "A year already kept still offers the button.");
            view.LogsKeptDays = 60;
            Assert(ClaudeSources.LogsButton(view)[1] == ClaudeSources.KeepLogs, "60 days doesn't offer Keep logs a year.");
            Assert(ClaudeSources.Backfill(view).StartsWith("History is off"), "Off: " + ClaudeSources.Backfill(view));
            view.BackfillOn = true; view.BackfillTotal = 51; view.BackfillRead = 12; view.OldestLog = new DateTime(2026, 10, 3);
            Assert(ClaudeSources.Backfill(view) == "Past days from the logs: reading 12 of 51 logs · logs from 3 Oct 2026", "Reading: " + ClaudeSources.Backfill(view));
            view.BackfillDone = true; view.BackfillDays = 24;
            Assert(ClaudeSources.Backfill(view) == "Past days built from the logs: 24 days · logs from 3 Oct 2026", "Done: " + ClaudeSources.Backfill(view));
            // Widget Screen holding the hooks is the Hooks row's warning, not Telemetry's.
            var away = ClaudeView.Fixture(Now, false); away.Sample = false; away.Connected = true; away.HooksElsewhere = @"C:\Users\a\AppData\Local\WidgetScreen\claude";
            away.TelemetryWarning = "Live updates go to Widget Screen: reconnect in Settings → Claude"; away.TelemetryProblem = "";
            var rows = ClaudeSources.Of(away, Now, false, null);
            Assert(rows[0].Action == ClaudeSources.Reconnect && rows[0].Level == ClaudeHealthLevel.Failed && rows[2].Level != ClaudeHealthLevel.Failed && rows[2].State.IndexOf("Widget Screen", StringComparison.Ordinal) < 0,
                "The Widget Screen warning: hooks " + rows[0].State + " / " + rows[0].Action + ", telemetry " + rows[2].State);
            Assert(ClaudeSources.Exempt.Contains("Claude desktop app") && ClaudeSources.Exempt.Contains("kept past") && ClaudeSources.Exempt.StartsWith("Claude Code's docs say"), "The desktop app's exemption isn't said, with its source.");
        });

        check("Claude keep logs a year: adds cleanupPeriodDays with a backup, keeps a larger value, Undo and Disconnect put back the earlier one (v0.59.0)", () => {
            string config = Fresh(directory, "keep-config"), bridge = Fresh(directory, "keep-bridge");
            var connector = new ClaudeConnector(config, bridge);
            string settings = connector.SettingsPath;
            const string original = "{\n  \"model\": \"opus\",\n  \"env\": {\n    \"KEEP_ME\": \"1\"\n  },\n  \"permissions\": {\n    \"allow\": [\n      \"Bash(ls)\"\n    ]\n  }\n}\n";
            File.WriteAllText(settings, original, new UTF8Encoding(false));
            Func<JsonObject> load = () => ClaudeJson.Obj(ClaudeJson.Parse(File.ReadAllText(settings)));
            Func<JsonObject, string> others = node => { var copy = ClaudeJson.Obj(ClaudeJson.Parse(ClaudeJson.Compact(node))); copy.Remove(ClaudeConnector.CleanupKey); return ClaudeJson.Compact(copy); };
            string before = others(load());
            Assert(connector.CleanupDays() == null && !connector.LogsKeptByUs, "Unset reads as set.");

            Assert(connector.KeepLogsAYear() == null && connector.CleanupDays() == 365 && connector.LogsKeptByUs, "The key wasn't added.");
            Assert(others(load()) == before, "Another key changed: " + ClaudeJson.Compact(load()));
            Assert(File.Exists(settings + ClaudeConnector.BackupSuffix) && File.ReadAllText(settings + ClaudeConnector.BackupSuffix) == original && connector.DatedBackups().Count >= 1, "No backup of the earlier settings.json.");
            Assert(connector.KeepLogsAYear() == "Claude Code already keeps its logs 365 days." , "A second press wrote again.");
            Assert(connector.UndoKeepLogs() == null && connector.CleanupDays() == null && !load().Contains(ClaudeConnector.CleanupKey) && !connector.LogsKeptByUs && others(load()) == before, "Undo didn't take the key out again.");
            Assert(connector.UndoKeepLogs() != null, "Undo with nothing to undo said it worked.");

            // An earlier, smaller value comes back on Undo; a larger one is kept and nothing is written.
            var root = load(); root[ClaudeConnector.CleanupKey] = new JsonNumber(60); File.WriteAllText(settings, ClaudeJson.Write(root) + "\n");
            Assert(connector.KeepLogsAYear() == null && connector.CleanupDays() == 365, "60 days wasn't raised to a year.");
            Assert(connector.UndoKeepLogs() == null && connector.CleanupDays() == 60, "Undo didn't put 60 back: " + connector.CleanupDays());
            root = load(); root[ClaudeConnector.CleanupKey] = new JsonNumber(400); File.WriteAllText(settings, ClaudeJson.Write(root) + "\n");
            string larger = File.ReadAllText(settings); int backups = connector.DatedBackups().Count;
            Assert(ClaudeConnector.AlreadyKept(connector.KeepLogsAYear()) && connector.KeepLogsAYear() == "Claude Code already keeps its logs 400 days." && !ClaudeConnector.AlreadyKept(null) && File.ReadAllText(settings) == larger && connector.DatedBackups().Count == backups && !connector.LogsKeptByUs, "A larger value wasn't kept as it was.");

            // Disconnect takes the key back out only when the dashboard put it there.
            root = load(); root.Remove(ClaudeConnector.CleanupKey); File.WriteAllText(settings, ClaudeJson.Write(root) + "\n");
            Assert(connector.KeepLogsAYear() == null, "Keep failed before Disconnect.");
            connector.Disconnect();
            Assert(connector.CleanupDays() == null && !connector.LogsKeptByUs && others(load()) == before, "Disconnect left the dashboard's 365 days or changed another key.");
            root = load(); root[ClaudeConnector.CleanupKey] = new JsonNumber(90); File.WriteAllText(settings, ClaudeJson.Write(root) + "\n");
            connector.Disconnect();
            Assert(connector.CleanupDays() == 90, "Disconnect took out the user's own value.");
            // Invalid JSON is left alone.
            File.WriteAllText(settings, "{ not json");
            bool refused = false; try { connector.KeepLogsAYear(); } catch (InvalidOperationException) { refused = true; }
            Assert(refused && File.ReadAllText(settings) == "{ not json", "Invalid settings.json was written.");
        });
    }
}

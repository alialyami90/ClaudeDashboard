using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using WidgetScreen;

// Package 4 of the watcher plan, "Cockpit": the daily history files and the CSV and Markdown export. (Answering a
// permission dialog from the dashboard was declined, decision 027.) Everything runs in throwaway folders with fixed
// times, so no real Claude Code setting or session is touched.
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
    public static void Run(string directory, Action<string, Action> check)
    {
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

        check("Claude summary takes each of the last 7 days from the replies or its file, whichever holds more", () => {
            string bridge = Fresh(directory, "history-recent");
            var history = new ClaudeHistory(bridge, TimeZoneInfo.Utc);
            var older = new ClaudeDay { Date = Now.Date.AddDays(-2), Tokens = 5000, Replies = 9, Needs = 3 };
            var gone = new ClaudeDay { Date = Now.Date.AddDays(-10), Tokens = 7 };
            Directory.CreateDirectory(ClaudeHistory.Folder(bridge));
            foreach (var d in new[] { older, gone }) ClaudeBridge.WriteAtomic(Path.Combine(ClaudeHistory.Folder(bridge), d.Name + ".json"), ClaudeJson.Compact(d.ToJson()));
            history.Note(new[] { new ClaudeSession { Id = "a", Running = true, State = ClaudeState.NeedsYou } }, Now);
            var replies = new List<ClaudeUsageEntry> {
                new ClaudeUsageEntry { At = Now.AddDays(-2), Session = "a", Folder = "C:/Code/api", Tokens = 100, Priced = true },
                new ClaudeUsageEntry { At = Now, Session = "a", Folder = "C:/Code/api", Tokens = 40, Priced = true } };
            var days = history.Recent(replies, null, Now, 7);
            Assert(days.Count == 7 && days[0].Date == Now.Date.AddDays(-6) && days[6].Date == Now.Date, "The days aren't the last 7, oldest first.");
            Assert(days[4].Tokens == 5000 && days[4].Needs == 3, "A stored day with more in it was replaced by a partial scan: " + days[4].Tokens);
            Assert(days[6].Tokens == 40 && days[6].Needs == 1 && days.Sum(d => d.Tokens) == 5040, "Today isn't counted from its replies: " + days[6].Tokens);
        });

        check("Claude's 7-day bars say what they count, and a sample says so", () => {
            var days = new long[] { 0, 0, 0, 0, 0, 1500000, 40000 };
            string tip = ClaudeText.DaysTip(days, 30000, false);
            Assert(tip.Contains("today 40k") && tip.Contains("busiest 1.5M") && tip.Contains("last week: 30k"), "The tip is wrong: " + tip);
            Assert(ClaudeText.DaysTip(days, null, false).Contains("No file for this day last week") && ClaudeText.DaysTip(days, 1, true).StartsWith("Sample.") && ClaudeText.DaysTip(null, 1, false) == null, "A missing tick or a sample isn't said.");
            var fixture = ClaudeView.Fixture(Now, false);
            Assert(fixture.Days != null && fixture.Days.Length == ClaudeHistory.SparkDays && fixture.LastWeekByNow.HasValue, "The sample has no 7-day bars.");
        });

        check("Claude's plain list shows every thread as words, a sample says so, and switching back brings the pit wall", () => {
            string folder = Fresh(directory, "plain");
            string path = Path.Combine(folder, "plain.xml");
            SettingsStore.Save(path, new AppSettings { ClaudePeriod = "Week", ClaudePlain = true });
            var window = AppController.LoadWindow("ShellWindow");
            try
            {
                using (var controller = new AppController(window, path))
                {
                    var card = (FrameworkElement)window.FindName("ClaudeCard"); card.Width = 1216; card.Height = 576;
                    controller.UseClaudeSample(true);
                    var dashboard = (FrameworkElement)window.FindName("Dashboard");
                    for (int i = 0; i < 3; i++) { dashboard.Measure(new Size(1920, 720)); dashboard.Arrange(new Rect(0, 0, 1920, 720)); dashboard.UpdateLayout(); }
                    var view = controller.ClaudeCardView;
                    Assert(view.Plain, "Settings' plain choice didn't reach the card.");
                    var plan = window.FindName("ClaudePlainPlan") as TextBlock;
                    Assert(plan != null && plan.Text.StartsWith("Sample.") && plan.Text.Contains("needs you"), "The plain plan line is wrong: " + (plan == null ? "missing" : plan.Text));
                    Assert(!plan.Text.Contains("resets resets") && !plan.Text.Contains(", ,") && System.Text.RegularExpressions.Regex.IsMatch(plan.Text, @"Week \d+%, resets [A-Z][a-z]{2} "), "The plain plan line repeats or drops a word: " + plan.Text);
                    var groups = view.View.Groups(view.Period);
                    for (int g = 0; g < groups.Count; g++)
                        for (int t = 0; t < groups[g].Threads.Count; t++)
                        {
                            var row = window.FindName("ClaudePlainThread" + g + "_" + t) as FrameworkElement;
                            Assert(row != null, "Thread " + groups[g].Threads[t].Id + " has no plain row.");
                            string said = AutomationProperties.GetName(row);
                            Assert(said.Contains(ClaudeText.Chip(groups[g].Threads[t])) && said.Contains("tokens"), "A plain row doesn't say its state and numbers: " + said);
                        }
                    Assert(window.FindName("ClaudeTank") == null && view.Sprites.Count() == 0, "The plain list still built the pit wall's parts.");
                    view.Plain = false;
                    Assert(window.FindName("ClaudePlainPlan") == null && view.Sprites.Any(), "Switching back didn't bring the cars.");
                }
            }
            finally { window.Close(); }
            Assert(ClaudeText.PlainNumbers(new ClaudeSession(), ClaudePeriod.Today, false).StartsWith("≈"), "Numbers before the logs are read aren't marked ≈.");
            var limits = new ClaudeLimits { FiveHour = new ClaudeLimitWindow { Percent = 23, ResetsAt = DateTime.UtcNow.AddMinutes(90) }, Week = new ClaudeLimitWindow { Percent = 41, ResetsAt = DateTime.UtcNow.AddDays(3) }, UpdatedAt = DateTime.UtcNow, Source = "online" };
            string line = ClaudeText.PlainPlan(new ClaudeView { Limits = limits }, DateTime.UtcNow, true);
            Assert(line.StartsWith("5-hour 23%, resets ") && line.Contains(" · Week 41%, resets ") && !line.Contains("resets resets"), "The plain plan line is wrong: " + line);
        });

        check("Claude never writes a name taken from a request to the CSV", () => {
            bool asked;
            Assert(ClaudeTitles.Name("", "Fix login", "", "", "rm the secrets folder", out asked) == "Fix login" && !asked, "A typed name lost to the request.");
            Assert(ClaudeTitles.Name(null, null, "Remote Control session (from a project)", null, "rm the secrets folder", out asked) == "rm the secrets folder" && asked, "A request's name isn't marked.");
            Assert(ClaudeTitles.Name(null, null, null, null, "", out asked) == "" && !asked, "No name is marked as a request.");
            string csv = ClaudeExport.Csv(new[] { new ClaudeUsageEntry { At = Now, Session = "abcdef12", Folder = "C:/Code/api", Tokens = 5, Priced = true } }, id => "", f => "", Now, TimeZoneInfo.Utc);
            Assert(csv.Contains(ClaudeTitles.IdLabel("abcdef12")), "A thread without a kept name doesn't use its id label: " + csv);
        });

        check("Claude renders the 7-day bars and the plain list for review (artifacts\\renders)", () => {
            string renders = Path.GetFullPath(Path.Combine(directory, "..", "renders")); Directory.CreateDirectory(renders);
            foreach (bool plain in new[] { false, true })
                foreach (var size in new[] { new Size(1000, 600), new Size(640, 420) })
                {
                    string name = (plain ? "claude-plain-" : "claude-days-") + size.Width + "x" + size.Height;
                    string path = Path.Combine(Fresh(directory, name), "settings.xml");
                    SettingsStore.Save(path, new AppSettings { ClaudePeriod = "Week", ClaudePlain = plain });
                    var window = AppController.LoadWindow("ShellWindow");
                    try
                    {
                        using (var controller = new AppController(window, path))
                        {
                            controller.UseClaudeSample(plain);
                            string png = Path.Combine(renders, name + ".png");
                            AppController.Render(window, size, 1, png);
                            Assert(File.Exists(png) && new FileInfo(png).Length > 0, name + " wasn't rendered.");
                            if (!plain && window.FindName("ClaudeRevs") != null) Assert(window.FindName("ClaudeDays") is DaySpark, "The 7-day bars aren't on the rev tile at " + size + ".");
                            if (plain) Assert(window.FindName("ClaudePlainPlan") != null, "The plain list isn't drawn at " + size + ".");
                        }
                    }
                    finally { window.Close(); }
                }
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

        check("Claude history is on by default and its choice is kept", () => {
            Assert(new AppSettings().ClaudeHistory, "History is off by default.");
            string path = Path.Combine(Fresh(directory, "history-settings"), "settings.xml");
            SettingsStore.Save(path, new AppSettings { ClaudeHistory = false });
            string warning;
            Assert(!SettingsStore.Load(path, out warning).ClaudeHistory, "Turning History off wasn't kept.");
        });
    }
}

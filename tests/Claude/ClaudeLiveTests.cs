using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ClaudeDashboard;

// Observability critique, issue 2 (v0.53.0): in 5 h and Today the ledger is a live status board (For, Last sign, Now and
// a context bar on every thread), Week keeps the ledger's columns, and the plan row speaks in time. All on samples.
internal static class ClaudeLiveTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static string Fresh(string directory, string name)
    {
        string path = Path.Combine(directory, "claude-live", name);
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
        return path;
    }
    private static IEnumerable<FrameworkElement> Named(Window window, string prefix)
    {
        for (int i = 0; i < 12; i++) for (int j = 0; j < 12; j++) { var e = window.FindName(prefix + i + "_" + j) as FrameworkElement; if (e != null) yield return e; }
    }
    private static string Head(Window window, int i) { var t = window.FindName("ClaudeLedgerHead" + i) as TextBlock; return t == null ? null : t.Text; }
    // The text fits its block: no ellipsis cuts it.
    private static void Whole(TextBlock block, string what)
    {
        Assert(block != null, what + " is missing.");
        double needs = ClaudeCard.Wide(block, block.Text);
        Assert(block.Text.Length == 0 || needs <= block.ActualWidth + 1, what + " is cut: \"" + block.Text + "\" needs " + Math.Round(needs) + " of " + Math.Round(block.ActualWidth));
    }
    public static void Run(string directory, Action<string, Action> check)
    {
        check("Claude's live rows say For, Last sign (silent after 5 min) and Now by tool name only", () => {
            var view = ClaudeView.Fixture(Now, false, true);
            var bash = view.Threads.First(t => t.Id == "sample-2");
            Assert(ClaudeText.NowWords(bash, Now) == "Bash · 2 min" && ClaudeText.NowTip(bash, true).StartsWith("In Bash now"), "Now doesn't name the tool: " + ClaudeText.NowWords(bash, Now));
            var silent = view.Threads.First(t => t.Id == "crowd-3");
            Assert(ClaudeText.Sign(silent, Now) == "silent 8 min" && ClaudeText.Silent(silent, Now), "A quiet worker isn't silent: " + ClaudeText.Sign(silent, Now));
            var fresh = view.Threads.First(t => t.Id == "sample-1");
            Assert(ClaudeText.Sign(fresh, Now) == "just now" && !ClaudeText.Silent(fresh, Now), "A worker heard from 12 s ago reads " + ClaudeText.Sign(fresh, Now));
            Assert(ClaudeText.NowWords(fresh, Now) == "—", "A working thread with no tool known says " + ClaudeText.NowWords(fresh, Now));
            var ended = view.Threads.First(t => !t.Running);
            Assert(ClaudeText.StateFor(ended, Now) == "" && ClaudeText.Sign(ended, Now).EndsWith(" ago") && ClaudeText.NowWords(ended, Now) == "", "An ended thread's live words are wrong.");
            Assert(ClaudeText.StateFor(view.Threads.First(t => t.Id == "sample-7"), Now) == "12 min", "For isn't the time in the state.");
            Assert(ClaudeText.AttentionReason(ClaudeAttention.Silent, new ClaudeSession { Running = true, State = ClaudeState.Working, Tool = "Bash", ToolSince = Now.AddMinutes(-9), LastSign = Now.AddMinutes(-9) }, view, Now, false) == "In Bash for 9 min", "The strip doesn't name the tool a silent thread is in.");
            // Only a tool's name: an MCP tool's long name is cut with "…" and the tip has it whole; nothing else about the call is kept.
            string longName = "mcp__github__create_pull_request_review_comment_reply";
            var mcp = new ClaudeSession { Running = true, State = ClaudeState.Working, Tool = longName, ToolSince = Now };
            Assert(ClaudeText.NowWords(mcp, Now) == longName.Substring(0, 39) + "…" && ClaudeText.NowTip(mcp, true).Contains(longName), "A long tool name isn't cut with … or its tip lost it: " + ClaudeText.NowWords(mcp, Now));
            var fields = typeof(ClaudeSession).GetFields().Select(f => f.Name).ToList();
            Assert(!fields.Any(f => f.Contains("Input") || f.Contains("Command") || f.Contains("Args")), "A thread keeps a tool's details.");
        });

        check("Claude's plan row speaks in time: ≈ empty at, minutes before reset, ≈ % by reset", () => {
            var window5 = new ClaudeLimitWindow { Percent = 70, ResetsAt = Now.AddMinutes(100) };
            var forms = ClaudeText.PaceTimes(120, 63, window5, Now, false);
            string at = ClaudeText.When(Now.AddMinutes(63), Now, false);
            Assert(forms.Count == 2 && forms[0] == "≈ empty " + at + ", 37 min before reset" && forms[1] == "≈ empty " + at, "The pace in time is wrong: " + String.Join(" | ", forms));
            Assert(ClaudeText.PaceTimes(84.4, Double.NaN, window5, Now, false)[0] == "≈ 84% by reset" && ClaudeText.PaceTimes(Double.NaN, Double.NaN, window5, Now, false)[0] == "", "The projection's words are wrong.");
            Assert(ClaudeText.PaceBy(76.2) == "≈ 76% by reset" && ClaudeText.PaceBy(126) == "≈ empty before reset" && ClaudeText.PaceBy(Double.NaN) == "", "The week's pace is wrong.");
            // Over 100% never prints as "% by reset": with no ETA the window's own pace says when, else "before reset".
            var over = ClaudeText.PaceTimes(126, Double.NaN, window5, ClaudePace.FiveHours, Now, false);
            string dry = ClaudeText.When(window5.ResetsAt.Value - TimeSpan.FromTicks((long)(ClaudePace.FiveHours.Ticks * (1 - 100 / 126.0))), Now, false);
            Assert(over.All(f => !f.Contains("% by reset")) && over.Last() == "≈ empty " + dry, "Over 100% reads as a percentage: " + String.Join(" | ", over));
            Assert(ClaudeText.PaceTimes(126, Double.NaN, new ClaudeLimitWindow { Percent = 84 }, ClaudePace.FiveHours, Now, false).SequenceEqual(new[] { "≈ empty before reset" }), "With no reset time, over 100% doesn't say empty before reset.");
            var weekly = ClaudeText.PaceTimes(130, Double.NaN, new ClaudeLimitWindow { Percent = 70, ResetsAt = Now.AddDays(2) }, ClaudePace.Week, Now, false);
            Assert(weekly.Last().StartsWith("≈ empty ") && !weekly.Any(f => f.Contains("%")), "The week over 100% doesn't say when it runs dry: " + String.Join(" | ", weekly));
            // The samples' tanks never say "% by reset" over 100%.
            foreach (var sample in new[] { ClaudeView.Fixture(Now, true), ClaudeView.Fixture(Now, false), ClaudeView.Fixture(Now, true, true) })
            {
                var w = sample.Limits.FiveHour;
                double pct = ClaudeText.Percent(w, Now), projection = ClaudePace.Projection(pct, ClaudePace.Elapsed(w, ClaudePace.FiveHours, Now));
                var said = ClaudeText.PaceTimes(projection, sample.LimitEtaMinutes, w, Now, false);
                Assert(!(projection > 100) || (said.All(f => !f.Contains("% by reset")) && !Double.IsNaN(sample.LimitEtaMinutes)), "A sample over 100% has no ETA or says % by reset: " + String.Join(" | ", said));
            }
            Assert(ClaudeText.Place(new ClaudeSession { Where = "Desktop" }) == "Claude app" && ClaudeText.Where(new ClaudeSession { Running = true, Where = "Desktop", Since = Now.AddMinutes(-2) }, Now) == "Claude app · 2 min", "Week says Desktop where Today says Claude app.");
            var resets = ClaudeText.FiveHourResets(window5, Now, false);
            Assert(resets.Count == 3 && resets[0].StartsWith("resets ") && resets[0].Contains(" · in 1 h 40 m") && resets[1] == "resets " + ClaudeText.When(window5.ResetsAt.Value, Now, false), "The reset's forms are wrong: " + String.Join(" | ", resets));
        });

        check("Claude's 5 h and Today ledger is live and Week keeps its columns; the time words fit at 140% (renders)", () => {
            string renders = Path.GetFullPath(Path.Combine(directory, "..", "renders")); Directory.CreateDirectory(renders);
            bool liveSeen = false, nowSeen = false, longSeen = false;
            foreach (double scale in new[] { 1.0, 1.4 })
                foreach (string period in new[] { "Today", "Week" })
                    foreach (var size in new[] { new Size(1000, 600), new Size(1600, 900) })
                    {
                        string name = "claude-live-" + period.ToLowerInvariant() + "-" + (int)(scale * 100) + "-" + size.Width + "x" + size.Height;
                        string path = Path.Combine(Fresh(directory, name), "settings.xml");
                        SettingsStore.Save(path, new AppSettings { ClaudePeriod = period, TextScale = scale });
                        var window = AppController.LoadWindow("ShellWindow");
                        try
                        {
                            using (var controller = new AppController(window, path))
                            {
                                controller.UseClaudeSample(false, true);
                                string png = Path.Combine(renders, name + ".png");
                                AppController.Render(window, size, 1, png);
                                AppController.Render(window, size, 1, png);
                                Assert(window.FindName("ClaudeRevGauge") == null, "The rev dial is still drawn at " + name + ".");
                                if (window.FindName("ClaudeRevs") != null) Assert(window.FindName("ClaudeBurn") is BurnLine && window.FindName("ClaudeDays") is DaySpark, "The sparkline or the 7-day bars went with the dial at " + name + ".");
                                if (window.FindName("ClaudeLedger") == null) continue;
                                bool live = period != "Week";
                                Assert(live ? Head(window, 2) == "FOR" && Head(window, 3) == "LAST SIGN" && Head(window, 5) == "TOKENS" : Head(window, 2) == "TOKENS",
                                    "The time column isn't labelled, or Week lost its columns, at " + name + ": " + String.Join(",", Enumerable.Range(1, 6).Select(i => Head(window, i))));
                                var heads = Enumerable.Range(1, 6).Select(i => Head(window, i)).Where(h => h != null).ToList();
                                if (live) Assert(!heads.Contains("CACHE READS") && !heads.Contains("MODELS") && !heads.Contains("REPLIES"), "Cache reads, Models or Replies are still live at " + name + ".");
                                var bars = Named(window, "ClaudeThreadContext").ToList();
                                Assert(live ? bars.Count > 0 && bars.All(b => b is MeterBar) : bars.Count == 0, "The context bars are wrong at " + name + ": " + bars.Count);
                                if (live)
                                {
                                    liveSeen = true;
                                    var signs = Named(window, "ClaudeThreadSign").OfType<TextBlock>().Select(t => t.Text).ToList();
                                    Assert(signs.Any(t => t == "just now"), "No live row says just now at " + name + ": " + String.Join(",", signs));
                                    // A window drawn off screen is never IsVisible; a part in the tree has a width once laid out.
                                    var nows = Named(window, "ClaudeThreadNow").OfType<TextBlock>().Where(t => t.Parent != null && t.ActualWidth > 0).Select(t => t.Text).ToList();
                                    if (nows.Any(t => t.StartsWith("Bash"))) nowSeen = true;
                                    // v0.53.1: the crowd's long MCP tool ends in "…" on the row and its tip has the whole name.
                                    var cut = Named(window, "ClaudeThreadNow").OfType<TextBlock>().FirstOrDefault(t => t.Text.StartsWith(ClaudeView.SampleLongTool.Substring(0, 39)));
                                    if (cut != null)
                                    {
                                        Assert(cut.Text == ClaudeView.SampleLongTool.Substring(0, 39) + "…" && (cut.ToolTip as string ?? "").Contains(ClaudeView.SampleLongTool), "The long tool name isn't cut with … or its tip lost it: " + cut.Text + " / " + cut.ToolTip);
                                        longSeen = true;
                                    }
                                    foreach (var f in Named(window, "ClaudeThreadFor").OfType<TextBlock>()) if (f.Parent != null && f.ActualWidth > 0) Whole(f, "For at " + name);
                                }
                                // The plan row's time words fit (the Lead's 140% renders cut them at 1000 × 600).
                                Whole(window.FindName("ClaudeFiveHourPace") as TextBlock, "The tank's pace at " + name);
                                Whole(window.FindName("ClaudeFiveHourReset") as TextBlock, "The tank's reset at " + name);
                                var foot = window.FindName("ClaudeBurnFoot") as TextBlock; if (foot != null) Whole(foot, "The burn tile's foot at " + name);
                                var weekPace = window.FindName("ClaudeWeekPace") as TextBlock; if (weekPace != null) Whole(weekPace, "The week's pace at " + name);
                            }
                        }
                        finally { window.Close(); }
                    }
            Assert(liveSeen && nowSeen, "No render showed the live rows with a tool in Now.");
            Assert(longSeen, "No render showed the crowd's long tool name.");
        });

        check("Claude Medium fills its height with project rows at 100% and 140% (v0.53.1)", () => {
            // The Lead on v0.53.0 and v0.53.1: at 640 × 420 and 140% Medium showed one project and "+2 more" over a row's
            // room left empty. The room is measured from the body's bottom, not from the list, which sizes to its rows.
            int seen = 0;
            foreach (bool needs in new[] { true, false })
            foreach (double scale in new[] { 1.0, 1.4 })
                foreach (var size in new[] { new Size(640, 420), new Size(500, 400) })
                {
                    string name = "claude-medium-" + (needs ? "needs-" : "crowded-") + (int)(scale * 100) + "-" + size.Width + "x" + size.Height;
                    string path = Path.Combine(Fresh(directory, name), "settings.xml");
                    SettingsStore.Save(path, new AppSettings { ClaudePeriod = "Today", TextScale = scale });
                    var window = AppController.LoadWindow("ShellWindow");
                    try
                    {
                        using (var controller = new AppController(window, path))
                        {
                            controller.UseClaudeSample(needs, !needs);
                            string png = Path.Combine(Path.GetFullPath(Path.Combine(directory, "..", "renders")), name + ".png");
                            Directory.CreateDirectory(Path.GetDirectoryName(png));
                            AppController.Render(window, size, 1, png);
                            AppController.Render(window, size, 1, png);
                            var list = window.FindName("ClaudeGarages") as StackPanel;
                            var body = window.FindName("ClaudeBody") as Grid;
                            if (list == null || body == null) continue;
                            seen++;
                            double row = CardSizing.ClaudeMediumRow(scale), used = list.Children.OfType<FrameworkElement>().Sum(r => r.ActualHeight);
                            double room = body.ActualHeight - list.TransformToAncestor(body).Transform(new Point(0, 0)).Y;
                            bool more = window.FindName("ClaudeMoreProjects") != null;
                            int projects = 0; while (window.FindName("ClaudeProjectRow" + projects) != null) projects++;
                            Assert(used <= room + 1, "Medium's rows overflow at " + name + ": " + used + " of " + room);
                            Assert(!more || room - used < row - 0.5, "Medium leaves a row's room empty while projects wait at " + name + ": " + Math.Round(room - used) + " of a " + row + "-unit row.");
                            if (needs && scale > 1 && size.Width == 640)
                                Assert(projects >= 2, "Medium at " + name + " shows " + projects + " project row(s) in " + Math.Round(room) + " units; two and the more line fit.");
                        }
                    }
                    finally { window.Close(); }
                }
            Assert(seen >= 2, "Too few renders were Medium: " + seen);
        });
    }
}

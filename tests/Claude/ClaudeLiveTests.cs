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
            // Only a tool's name: an MCP tool's long name is cut, and nothing else about the call is kept.
            var fields = typeof(ClaudeSession).GetFields().Select(f => f.Name).ToList();
            Assert(!fields.Any(f => f.Contains("Input") || f.Contains("Command") || f.Contains("Args")), "A thread keeps a tool's details.");
        });

        check("Claude's plan row speaks in time: ≈ empty at, minutes before reset, ≈ % by reset", () => {
            var window5 = new ClaudeLimitWindow { Percent = 70, ResetsAt = Now.AddMinutes(100) };
            var forms = ClaudeText.PaceTimes(120, 63, window5, Now, false);
            string at = ClaudeText.When(Now.AddMinutes(63), Now, false);
            Assert(forms.Count == 2 && forms[0] == "≈ empty " + at + ", 37 min before reset" && forms[1] == "≈ empty " + at, "The pace in time is wrong: " + String.Join(" | ", forms));
            Assert(ClaudeText.PaceTimes(84.4, Double.NaN, window5, Now, false)[0] == "≈ 84% by reset" && ClaudeText.PaceTimes(Double.NaN, Double.NaN, window5, Now, false)[0] == "", "The projection's words are wrong.");
            Assert(ClaudeText.PaceBy(126) == "≈ 126% by reset" && ClaudeText.PaceBy(Double.NaN) == "", "The week's pace is wrong.");
            var resets = ClaudeText.FiveHourResets(window5, Now, false);
            Assert(resets.Count == 3 && resets[0].StartsWith("resets ") && resets[0].Contains(" · in 1 h 40 m") && resets[1] == "resets " + ClaudeText.When(window5.ResetsAt.Value, Now, false), "The reset's forms are wrong: " + String.Join(" | ", resets));
        });

        check("Claude's 5 h and Today ledger is live and Week keeps its columns; the time words fit at 140% (renders)", () => {
            string renders = Path.GetFullPath(Path.Combine(directory, "..", "renders")); Directory.CreateDirectory(renders);
            bool liveSeen = false, nowSeen = false;
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
        });
    }
}

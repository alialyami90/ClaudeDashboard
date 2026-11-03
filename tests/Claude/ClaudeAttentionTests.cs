using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ClaudeDashboard;

// Observability critique, issue 1 (v0.49.0): the attention rail. Projects are ranked Needs you > Stuck > Your turn >
// Working > the rest; the pit stop strip is a short queue with "1 of N" and Next; a clipped Large ledger keeps a footer
// that says what's out of view and scrolls to it; Medium counts the projects it can't show. All on samples.
internal static class ClaudeAttentionTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static string Fresh(string directory, string name)
    {
        string path = Path.Combine(directory, "claude-attention", name);
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
        return path;
    }
    public static void Run(string directory, Action<string, Action> check)
    {
        check("Claude ranks projects Needs you, Stuck, Your turn, Working, then the rest", () => {
            var view = ClaudeView.Fixture(Now, true, true);
            Assert(view.Sessions.Count == 25 && view.Sample, "The crowded sample doesn't have 25 threads: " + view.Sessions.Count);
            var groups = view.Groups(ClaudePeriod.Today);
            var ranks = groups.Select(g => g.Urgency).ToList();
            for (int i = 1; i < ranks.Count; i++) Assert(ranks[i - 1] <= ranks[i], "Projects aren't ranked by what waits on you: " + String.Join(", ", groups.Select(g => g.Name + "=" + g.Urgency)));
            Assert(groups[0].NeedsYou && groups[0].Urgency == 0, "The project that needs you isn't first: " + groups[0].Name);
            Assert(groups.Any(g => g.Urgency == 1) && groups.Any(g => g.Urgency == 2), "The sample has no stuck or your-turn project to rank.");
            var calm = ClaudeView.Fixture(Now, false, true).Groups(ClaudePeriod.Today);
            Assert(calm[0].Stuck && !calm[0].NeedsYou, "With nothing needing you, the stuck project isn't first: " + calm[0].Name);
        });

        check("Claude's attention queue holds needs, stuck, a long your-turn and a silent worker, in that order", () => {
            var view = ClaudeView.Fixture(Now, true, true);
            var queue = view.Attention(Now);
            var kinds = queue.Select(a => a.Kind).ToList();
            Assert(kinds.Contains(ClaudeAttention.Need) && kinds.Contains(ClaudeAttention.Stuck) && kinds.Contains(ClaudeAttention.Turn) && kinds.Contains(ClaudeAttention.Silent), "A kind is missing: " + String.Join(",", kinds));
            for (int i = 1; i < queue.Count; i++) Assert(ClaudeAttention.Rank(queue[i - 1].Kind) <= ClaudeAttention.Rank(queue[i].Kind), "The queue isn't in order: " + String.Join(",", kinds));
            Assert(queue.All(a => a.Thread.Running), "An ended thread is in the queue.");
            // A your-turn under 10 minutes and a worker heard from just now stay out.
            Assert(!queue.Any(a => a.Kind == ClaudeAttention.Turn && a.For < ClaudeAttention.TurnWaits) && !queue.Any(a => a.Kind == ClaudeAttention.Silent && a.For < ClaudeAttention.SilentAfter), "A short wait got in the queue.");
            var silent = queue.First(a => a.Kind == ClaudeAttention.Silent);
            string words = ClaudeText.AttentionReason(ClaudeAttention.Silent, silent.Thread, view, Now, false);
            Assert(words.StartsWith("Working, no sign of life for 8 min"), "The silent words are wrong: " + words);
            var turn = queue.First(a => a.Kind == ClaudeAttention.Turn);
            Assert(ClaudeText.AttentionReason(ClaudeAttention.Turn, turn.Thread, view, Now, false).StartsWith("Waiting for your reply · "), "The your-turn words are wrong.");
            Assert(ClaudeText.AttentionTitle(ClaudeAttention.Need, "Fix login", "api") == "Pit stop: Fix login · api" && ClaudeText.AttentionTitle(ClaudeAttention.Silent, "X", "") == "Silent: X", "The strip's titles are wrong.");
            Assert(ClaudeText.MoreRows(7, 0, 1, 1, 2) == "+7 below · 1 needs you · 1 stuck · 2 your turn" && ClaudeText.MoreRows(0, 3, 0, 0, 0) == "↑ 3 above" && ClaudeText.MoreRows(4, 0, 2, 0, 0) == "+4 below · 2 need you", "The footer words are wrong.");
            Assert(ClaudeText.MoreProjects(3, 0, 1, 1) == "+3 more projects · 1 stuck · 1 your turn" && ClaudeText.MoreProjects(1, 0, 0, 0) == "+1 more project", "Medium's more words are wrong.");
            Assert(ClaudeView.Fixture(Now, false, false).Attention(Now).All(a => a.Kind != ClaudeAttention.Need), "A calm sample has a thread that needs you.");
        });

        check("Claude's crowded sample: the strip queue, the ledger footer and Medium's count, rendered at four sizes", () => {
            string renders = Path.GetFullPath(Path.Combine(directory, "..", "renders")); Directory.CreateDirectory(renders);
            bool footerSeen = false, nextSeen = false, mediumSeen = false;
            foreach (bool needs in new[] { false, true })
                foreach (var size in new[] { new Size(360, 300), new Size(640, 420), new Size(1000, 600), new Size(1600, 900) })
                {
                    string name = "claude-crowded-" + (needs ? "needs-" : "") + size.Width + "x" + size.Height;
                    string path = Path.Combine(Fresh(directory, name), "settings.xml");
                    SettingsStore.Save(path, new AppSettings { ClaudePeriod = "Today" });
                    var window = AppController.LoadWindow("ShellWindow");
                    try
                    {
                        using (var controller = new AppController(window, path))
                        {
                            controller.UseClaudeSample(needs, true);
                            string png = Path.Combine(renders, name + ".png");
                            AppController.Render(window, size, 1, png);
                            Assert(File.Exists(png) && new FileInfo(png).Length > 0, name + " wasn't rendered.");
                            var card = controller.ClaudeCardView;
                            var queue = card.View.Attention(DateTime.UtcNow);
                            var ask = window.FindName("ClaudeAsk") as FrameworkElement;
                            if (ask != null)
                            {
                                var title = (TextBlock)window.FindName("ClaudeAskTitle");
                                Assert(title.Text.StartsWith(ClaudeText.AttentionWord(queue[0].Kind) + ": "), "The strip doesn't start with the first in the queue at " + size + ": " + title.Text);
                                Assert(!needs || title.Text.StartsWith("Pit stop: "), "Needs you isn't first on the strip at " + size + ".");
                                var count = window.FindName("ClaudeAskCount") as TextBlock;
                                var next = window.FindName("ClaudeAskNext") as Button;
                                Assert(count != null && count.Text == "1 of " + queue.Count && next != null, "The strip doesn't say 1 of " + queue.Count + " at " + size + ".");
                                string before = title.Text;
                                next.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                                AppController.Render(window, size, 1, png);
                                title = (TextBlock)window.FindName("ClaudeAskTitle"); count = (TextBlock)window.FindName("ClaudeAskCount");
                                Assert(count.Text == "2 of " + queue.Count && title.Text != before, "Next didn't move along the queue at " + size + ": " + count.Text + " / " + title.Text);
                                nextSeen = true;
                                AppController.Render(window, size, 1, png);
                            }
                            var ledger = window.FindName("ClaudeLedger") as ScrollViewer;
                            if (ledger != null && ledger.ExtentHeight > ledger.ViewportHeight + 1)
                            {
                                var more = (Button)window.FindName("ClaudeLedgerMore");
                                var text = (TextBlock)more.Content;
                                Assert(more.Visibility == Visibility.Visible && text.Text.StartsWith("+") && text.Text.Contains(" below"), "A clipped ledger has no footer at " + size + ": " + text.Text);
                                Assert(more.ActualHeight > 0 && more.TranslatePoint(new Point(0, 0), window).Y + more.ActualHeight <= size.Height + 1, "The footer is pushed out of the window at " + size + ".");
                                double offset = ledger.VerticalOffset;
                                more.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                                AppController.Render(window, size, 1, Path.Combine(renders, name + "-scrolled.png"));
                                Assert(ledger.VerticalOffset > offset, "The footer didn't scroll to what's out of view at " + size + ".");
                                footerSeen = true;
                            }
                            var medium = window.FindName("ClaudeMoreProjects") as TextBlock;
                            if (medium != null)
                            {
                                Assert(medium.Text.StartsWith("+") && medium.Text.Contains(" more project"), "Medium's count is wrong at " + size + ": " + medium.Text);
                                mediumSeen = true;
                                if (needs)
                                {
                                    var detail = window.FindName("ClaudeProjectDetail0") as TextBlock;
                                    Assert(detail != null && System.Text.RegularExpressions.Regex.IsMatch(detail.Text, @"^Needs you (now|\d+ (min|h)) · "), "Medium's Needs-you line doesn't say how long: " + (detail == null ? "missing" : detail.Text));
                                }
                            }
                        }
                    }
                    finally { window.Close(); }
                }
            Assert(footerSeen, "No render had a clipped ledger with its footer.");
            Assert(nextSeen, "No render had the strip's queue.");
            Assert(mediumSeen, "No render had Medium's more-projects row.");
        });
    }
}

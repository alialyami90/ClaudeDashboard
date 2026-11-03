using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using ClaudeDashboard;

// The lap chart's Reliability and Sources views (decision 030, package E, v0.58.0): the arithmetic behind the figures,
// the tools table and the stuck turns; what each says with telemetry off; the five sources with a state, freshness,
// what each gives and one action; layouts at three sizes and two text sizes that stay inside the width; and renders
// to artifacts\renders. Everything runs on fixtures in memory.
internal static class ReliabilitySourcesTests
{
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }
    private static string Texts(DependencyObject root)
    {
        return String.Join("\n", Descendants(root).OfType<TextBlock>().Where(t => t.IsVisible || t.Visibility == Visibility.Visible).Select(t => t.Text));
    }
    private static ClaudeToolRun Run(DateTime at, string tool, double ms, bool failed) { return new ClaudeToolRun { At = at, Session = "s1", Tool = tool, Ms = ms, Failed = failed }; }

    // Hosts a view in a themed window at a size and text scale, refreshed with a snapshot.
    private static void Host(IClaudeView view, ClaudeView snapshot, Size size, double scale, string theme, Action<Window, FrameworkElement> body)
    {
        var window = AppController.LoadWindow("ShellWindow");
        try
        {
            Palettes.Apply(window.Resources, theme, null, scale);
            var context = new ClaudeViewContext { TextScale = scale, Sample = snapshot != null && snapshot.Sample };
            var built = view.Build(context, size);
            view.Visible = true;
            var page = new Border { Child = built, Padding = new Thickness(16) };
            page.SetResourceReference(Border.BackgroundProperty, "Page");
            window.Content = page;
            view.Refresh(snapshot, DateTime.UtcNow);
            AppController.Lay(window, size);
            view.Refresh(snapshot, DateTime.UtcNow);
            AppController.Lay(window, size);
            body(window, built);
        }
        finally { window.Close(); }
    }
    // Nothing visible reaches past the view's right edge.
    private static void Inside(FrameworkElement root, string name)
    {
        double width = root.ActualWidth;
        foreach (var element in Descendants(root).OfType<FrameworkElement>().Where(e => e.IsVisible && e.ActualWidth > 0 && (e is TextBlock || e is Button)))
        {
            var box = element.TransformToAncestor(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            Assert(box.Right <= width + 1, name + ": '" + (element is TextBlock ? ((TextBlock)element).Text : "button") + "' reaches " + Math.Round(box.Right) + " past " + Math.Round(width) + ".");
        }
    }

    private static void Wait(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
    private static bool Within(DependencyObject child, DependencyObject parent)
    {
        for (var at = child; at != null; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at)) if (at == parent) return true;
        return false;
    }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Reliability: medians, longest, failures per tool and today's runs only", () => {
            Assert(ClaudeReliability.Median(new double[] { 5, 1, 3 }) == 3, "Odd median wrong.");
            Assert(ClaudeReliability.Median(new double[] { 4, 1, 3, 2 }) == 2.5, "Even median wrong.");
            Assert(Double.IsNaN(ClaudeReliability.Median(new double[0])), "An empty median should be unknown.");
            var now = DateTime.UtcNow;
            var runs = new List<ClaudeToolRun> {
                Run(now.AddMinutes(-5), "Bash", 1000, false), Run(now.AddMinutes(-4), "Bash", 3000, true), Run(now.AddMinutes(-3), "Bash", 9000, false),
                Run(now.AddMinutes(-2), "Read", 100, false), Run(now.AddMinutes(-1), "Read", 300, false),
                Run(ClaudeReliability.Today(now).AddMinutes(-1), "Grep", 50, true) };
            var tools = ClaudeReliability.Tools(runs, new Dictionary<string, int> { { "Bash", 2 } }, now);
            Assert(tools.Count == 2 && tools[0].Name == "Bash" && tools[1].Name == "Read", "Tools should be today's, busiest first: " + String.Join(",", tools.Select(t => t.Name)));
            Assert(tools[0].Runs == 3 && tools[0].Failed == 1 && tools[0].Typical == 3000 && tools[0].Longest == 9000 && tools[0].Asked == 2, "Bash's row is wrong.");
            Assert(tools[1].Typical == 200 && tools[1].Asked == 0, "Read's median or asks are wrong.");
            Assert(ClaudeReliability.Tools(runs, null, now)[0].Asked == -1, "Asks per tool should be unknown without them.");
            Assert(ClaudeReliability.Time(300) == "0.3 s" && ClaudeReliability.Time(6400) == "6.4 s" && ClaudeReliability.Time(11000) == "11 s"
                && ClaudeReliability.Time(130000) == "2 min 10 s" && ClaudeReliability.Time(180000) == "3 min" && ClaudeReliability.Time(3900000) == "1 h 5 min"
                && ClaudeReliability.Time(Double.NaN) == "—", "Run times read wrong.");
        });

        check("Reliability: the failure rate, API errors in the last hour only, and the figures without telemetry", () => {
            var now = DateTime.UtcNow;
            var input = new ReliabilityInput { Telemetry = true, Facts = new ClaudeFacts { Telemetry = true, Metrics = true, Runs = 312, RunFailures = 13, Asked = 41, Denied = 3, EditsAccepted = 188, EditsRejected = 8 },
                Runs = new List<ClaudeToolRun> { Run(now.AddMinutes(-3), "Bash", 100, true), Run(now.AddMinutes(-2), "Edit", 100, true), Run(now.AddMinutes(-1), "Bash", 100, true) },
                Errors = new List<ClaudeApiError> { new ClaudeApiError { At = now.AddMinutes(-70), Status = 500 }, new ClaudeApiError { At = now.AddMinutes(-20), Status = 429 }, new ClaudeApiError { At = now.AddMinutes(-10), Status = 429, Attempt = 2 }, new ClaudeApiError { At = now.AddMinutes(-5), Status = 529 } },
                Compactions = new List<ClaudeCompaction> { new ClaudeCompaction { Session = "s1", At = now.AddMinutes(-30) } } };
            var f = ClaudeReliability.Figures(input, now).ToDictionary(x => x.Key);
            Assert(f[ClaudeReliability.Failures].Value == "100%" && f[ClaudeReliability.Failures].Note.StartsWith("3 failed of 3 runs, 2 of them Bash"), "Failures read: " + f[ClaudeReliability.Failures].Value + " / " + f[ClaudeReliability.Failures].Note);
            Assert(f[ClaudeReliability.Errors].Value == "3" && f[ClaudeReliability.Errors].Note == "last hour · 429 ×2, 529 ×1" && f[ClaudeReliability.Errors].Brush == "Rose", "API errors read: " + f[ClaudeReliability.Errors].Note);
            Assert(f[ClaudeReliability.Asked].Value == "41" && f[ClaudeReliability.Asked].Note == "all asks: 38 allowed · 3 denied", "Asked read wrong.");
            Assert(f[ClaudeReliability.Compactions].Value == "1" && f[ClaudeReliability.Edits].Value == "196" && f[ClaudeReliability.Edits].Note == "188 accepted · 8 rejected", "Compactions or edits read wrong.");
            // Without telemetry's runs, the facts' counts (the hooks' are ≈).
            input.Runs = null; input.Facts.FailuresFromHooks = true;
            var hooks = ClaudeReliability.Figures(input, now)[0];
            Assert(hooks.Value == "≈ 4.2%" && hooks.Note.StartsWith("13 failed of 312 runs"), "Failures from the hooks read: " + hooks.Value + " / " + hooks.Note);

            var off = new ReliabilityInput { Telemetry = false, Facts = new ClaudeFacts { Tools = 57 } };
            foreach (var x in ClaudeReliability.Figures(off, now).Where(x => x.Key != ClaudeReliability.Compactions))
                Assert(x.Value == "—" && x.Note == "needs telemetry", x.Label + " should say it needs telemetry, not show " + x.Value + ".");
            Assert(ClaudeReliability.Figures(off, now).First(x => x.Key == ClaudeReliability.Compactions).Value == "—", "Unknown compactions should be a dash, not 0.");
        });

        check("Reliability: stuck turns split by a 10-minute gap, with reasons in plain words", () => {
            var now = DateTime.UtcNow;
            Assert(ClaudeReliability.Words(429) == "rate limited" && ClaudeReliability.Words(529) == "overloaded (529)" && ClaudeReliability.Words(503) == "server error (503)"
                && ClaudeReliability.Words(401) == "sign-in refused (401)" && ClaudeReliability.Words(400) == "request refused (400)" && ClaudeReliability.Words(418) == "API error (418)", "Status words wrong.");
            var thread = new ClaudeSession { Id = "s1", Title = "Retry flaky upload test", Running = true, State = ClaudeState.Working, Retrying = true, Since = now.AddMinutes(-90) };
            var input = new ReliabilityInput { Telemetry = true, Threads = new List<ClaudeSession> { thread }, Errors = new List<ClaudeApiError> {
                new ClaudeApiError { At = now.AddMinutes(-60), Session = "s1", Status = 529, Attempt = 1 }, new ClaudeApiError { At = now.AddMinutes(-59), Session = "s1", Status = 529, Attempt = 2 },
                new ClaudeApiError { At = now.AddMinutes(-3), Session = "s1", Status = 429, Attempt = 1 }, new ClaudeApiError { At = now.AddMinutes(-2), Session = "s1", Status = 429, Attempt = 2 }, new ClaudeApiError { At = now.AddMinutes(-1), Session = "s1", Status = 429, Attempt = 3 } } };
            // Errors before local midnight wouldn't count; keep the test inside today.
            if (now.AddMinutes(-61) < ClaudeReliability.Today(now)) return;
            var stuck = ClaudeReliability.Stuck(input, null, now, false);
            Assert(stuck.Count == 2, "Two episodes expected, got " + stuck.Count + ".");
            Assert(stuck[0].Kind == ReliabilityStuck.Retrying && stuck[0].Reason == "rate limited" && stuck[0].Tries == 3, "The current episode should be retrying, rate limited, 3 tries.");
            Assert(ClaudeReliability.Rest(stuck[0], false).StartsWith("rate limited, try 3 since "), "Retrying line reads: " + ClaudeReliability.Rest(stuck[0], false));
            Assert(stuck[1].Kind == ReliabilityStuck.Recovered && stuck[1].Reason == "overloaded (529)" && ClaudeReliability.Rest(stuck[1], false) == "overloaded (529), 2 tries", "The earlier episode should be recovered after 2 tries.");
            Assert(ClaudeReliability.Head(stuck[1], false).StartsWith("Recovered "), "Recovered episodes say when.");
        });

        check("Reliability reads the engine's runs, errors, compactions and asks per tool", () => {
            var now = DateTime.UtcNow;
            var view = ClaudeView.Fixture(now, true);
            var input = ReliabilityInput.From(view, now);
            Assert(input.Runs != null && input.Runs.Count == view.ToolRunsToday.Count && input.Errors != null && input.Compactions != null, "The engine's lists should come through.");
            Assert(input.AskedByTool != null && input.AskedByTool.Count == view.AskedToday.Count, "Asks per tool should be AskedToday.");
            var tools = ClaudeReliability.Tools(input.Runs, input.AskedByTool, now);
            foreach (var t in tools)
            {
                int asked; view.AskedToday.TryGetValue(t.Name, out asked);
                Assert(t.Asked == asked, t.Name + " asked " + t.Asked + ", the engine says " + asked + ".");
            }
        });

        check("Reliability's Tool failures card and the tools table count the same runs, on every sample", () => {
            foreach (bool needs in new[] { false, true })
                foreach (bool crowded in new[] { false, true })
                {
                    var now = DateTime.UtcNow;
                    var view = ClaudeView.Fixture(now, needs, crowded);
                    var input = ReliabilityInput.From(view, now);
                    var tools = ClaudeReliability.Tools(input.Runs, input.AskedByTool, now);
                    var card = ClaudeReliability.Figures(input, now).First(x => x.Key == ClaudeReliability.Failures);
                    int runs = tools.Sum(t => t.Runs), failed = tools.Sum(t => t.Failed);
                    Assert(runs > 0, "The sample should have tool runs today.");
                    Assert(card.Note.StartsWith(failed + " failed of " + runs + " runs") && card.Value == ClaudeReliability.Percent(failed, runs), "The card says " + card.Value + " · " + card.Note + " but the table has " + failed + " failed of " + runs + ".");
                }
        });

        check("Reliability's and Sources' pages show the app's 2-unit focus ring", () => {
            var style = CardParts.PageFocus;
            var template = (ControlTemplate)((Setter)style.Setters.First(x => ((Setter)x).Property == Control.TemplateProperty)).Value;
            var host = new Control { Template = template };
            host.ApplyTemplate();
            var ring = (Border)VisualTreeHelper.GetChild(host, 0);
            Assert(ring.BorderThickness == new Thickness(2), "The page's ring isn't 2 units.");
            foreach (var view in new IClaudeView[] { new ReliabilityView(), new SourcesView() })
                Host(view, ClaudeView.Fixture(DateTime.UtcNow, false), new Size(1000, 600), 1, "Midnight", (window, root) => {
                    var page = Descendants(root).OfType<ScrollViewer>().First(v => v.IsTabStop);
                    Assert(page.FocusVisualStyle == CardParts.PageFocus, view.Name + "'s page doesn't use the app's focus ring.");
                });
        });

        check("Sources' off buttons keep their words at Muted on every palette", () => {
            foreach (string theme in new[] { "Midnight", "Slate", "Warm", "Claude" })
                Host(new SourcesView(), ClaudeView.Fixture(DateTime.UtcNow, false), new Size(1000, 600), 1, theme, (window, root) => {
                    foreach (var b in Descendants(root).OfType<Button>())
                    {
                        Assert(!b.IsEnabled, "The sample's buttons should be off.");
                        Assert(b.Opacity == 1 && Descendants(b).OfType<Border>().First().Opacity == 1, theme + ": an off button is faded.");
                        var words = Descendants(b).OfType<ContentPresenter>().First();
                        var muted = (SolidColorBrush)window.Resources["Muted"];
                        var ink = TextElement.GetForeground(words) as SolidColorBrush;
                        Assert(ink != null && ink.Color == muted.Color, theme + ": an off button's words aren't Muted.");
                    }
                });
        });

        check("Reliability never shows a turn's own words", () => {
            var view = ClaudeView.Fixture(DateTime.UtcNow, true);
            foreach (var t in view.Threads) { t.ReasonText = "SECRET-REPLY-TEXT"; t.Message = "SECRET-PROMPT"; }
            view.Threads[0].State = ClaudeState.Stuck; view.Threads[0].Running = true; view.Threads[0].Reason = "rate_limit";
            view.Sample = false;
            var reliability = new ReliabilityView();
            Host(reliability, view, new Size(1000, 700), 1, "Midnight", (window, root) => {
                string all = Texts(root);
                Assert(!all.Contains("SECRET"), "A turn's own words reached the view.");
                Assert(reliability.StuckShown.Any(s => s.Kind == ReliabilityStuck.StuckNow), "A session stuck now should be listed.");
            });
        });

        check("Reliability with telemetry off says so and shows the logs' tool count", () => {
            var view = ClaudeView.Fixture(DateTime.UtcNow, false);
            view.Sample = false; view.ToolTimesKnown = false; view.Facts.Telemetry = false; view.FactsFor(ClaudePeriod.Today).Telemetry = false; view.FactsFor(ClaudePeriod.Today).Tools = 57;
            var reliability = new ReliabilityView();
            Host(reliability, view, new Size(1000, 700), 1, "Midnight", (window, root) => {
                string all = Texts(root);
                Assert(all.Contains("Tool calls in the logs today: 57."), "The logs' tool count should show with telemetry off:\n" + all);
                Assert(all.Contains("needs telemetry"), "The figures should say they need telemetry.");
            });
        });

        check("Sources: five rows, each with a state, freshness, what it gives and one action", () => {
            var view = ClaudeView.Fixture(DateTime.UtcNow, false);
            var rows = ClaudeSources.Of(view, DateTime.UtcNow, false, null);
            Assert(rows.Select(r => r.Name).SequenceEqual(new[] { ClaudeSources.Hooks, ClaudeSources.Logs, ClaudeSources.Telemetry, ClaudeSources.Plan, ClaudeSources.App }), "Rows out of order.");
            foreach (var r in rows)
                Assert(r.State.Length > 0 && r.Gives.Length > 0 && r.Where.Length > 0 && r.Action.Length > 0 && r.Button.Length > 0, r.Name + " is missing a part.");
            Assert(rows[2].Where.Contains("127.0.0.1") && rows[2].Where.Contains("loopback"), "Telemetry should say it's loopback only.");
            view.TelemetryWarning = "Telemetry off: port 43832 in use";
            var problem = ClaudeSources.Of(view, DateTime.UtcNow, false, null);
            Assert(problem[2].Level == ClaudeHealthLevel.Failed && problem[2].Action == ClaudeSources.UpdateConnection, "A port problem should fail Telemetry and offer Update connection.");
            view.Limits.UpdatedAt = DateTime.UtcNow.AddHours(-3);
            Assert(ClaudeSources.Of(view, DateTime.UtcNow, false, null)[3].Level == ClaudeHealthLevel.Old, "An old plan reading should be Old.");
            view.Connected = false;
            Assert(ClaudeSources.Of(view, DateTime.UtcNow, false, null)[0].Action == ClaudeSources.Connect, "Hooks off should offer Connect.");
        });

        check("Sources' buttons are off on the sample, and say why", () => {
            var view = ClaudeView.Fixture(DateTime.UtcNow, false);
            Host(new SourcesView(), view, new Size(1000, 700), 1, "Midnight", (window, root) => {
                var buttons = Descendants(root).OfType<Button>().ToList();
                Assert(buttons.Count == 6 && buttons.All(b => !b.IsEnabled && (b.ToolTip as string) == SourcesView.SampleRefusal), "The sample's buttons should be off with the refusal in their tip.");
            });
        });

        check("Sources buttons only call the host's Act, with the action's name", () => {
            var view = ClaudeView.Fixture(DateTime.UtcNow, false);
            view.Sample = false;
            var sources = new SourcesView();
            var asked = new List<string>();
            Host(sources, view, new Size(1000, 700), 1, "Midnight", (window, root) => {
                var buttons = Descendants(root).OfType<Button>().ToList();
                Assert(buttons.Count == 6, "Six buttons (Keep logs a year since v0.59.0) expected, got " + buttons.Count + ".");
                foreach (var b in buttons)
                    Assert(AutomationProperties.GetName(b).Length > 0 && b.Focusable, "Each button needs a name and the keyboard.");
                Assert(buttons.All(b => b.IsEnabled), "With Act the buttons should be on.");
                sources.Act = name => { asked.Add(name); return name == ClaudeSources.CheckPlan ? "Couldn't reach api.anthropic.com" : null; };
                foreach (var b in buttons) b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert(asked.Count == 6 && asked.Contains(ClaudeSources.KeepLogs) && asked.Contains(ClaudeSources.OpenLogs) && asked.Contains(ClaudeSources.CheckPlan), "Each button should call Act once with its action: " + String.Join(",", asked));
                // A failed action's words show above the rows, and the next action that works clears them.
                var plan = buttons.First(b => (b.Tag as string) == ClaudeSources.CheckPlan);
                plan.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert(Texts(root).Contains("Couldn't reach api.anthropic.com"), "Act's error should show.");
                buttons.First(b => (b.Tag as string) == ClaudeSources.OpenLogs).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert(!Texts(root).Contains("Couldn't reach api.anthropic.com"), "A working action should clear the last error.");
            });
        });

        check("Reliability and Sources stay inside their width at three sizes and two text sizes, and render", () => {
            string renders = Path.GetFullPath(Path.Combine(directory, "..", "renders")); Directory.CreateDirectory(renders);
            foreach (var size in new[] { new Size(640, 420), new Size(1000, 600), new Size(1600, 900) })
                foreach (double scale in new[] { 1.0, 1.4 })
                    foreach (string name in new[] { ClaudeViews.Reliability, ClaudeViews.Sources })
                    {
                        var view = ClaudeView.Fixture(DateTime.UtcNow, true);
                        var built = ClaudeViews.Create(name);
                        Assert(built != null && built.GetType().Name == name + "View", name + " wasn't found by its name.");
                        Host(built, view, size, scale, "Midnight", (window, root) => {
                            Inside(root, name + " " + size + " at " + scale);
                            foreach (var t in Descendants(root).OfType<TextBlock>().Where(t => t.IsVisible && t.Text.Length > 0))
                                Assert(t.FontSize >= 11 * scale - 0.11, name + ": '" + t.Text + "' is " + t.FontSize + " at text size " + scale + ".");
                            string png = Path.Combine(renders, "claude-" + name.ToLowerInvariant() + "-" + size.Width + "x" + size.Height + "-" + Math.Round(scale * 100) + ".png");
                            AppController.Render(window, size, 1, png);
                            Assert(File.Exists(png) && new FileInfo(png).Length > 0, png + " wasn't rendered.");
                        });
                    }
            var paper = new ReliabilityView();
            Host(paper, ClaudeView.Fixture(DateTime.UtcNow, true), new Size(1000, 600), 1, "Claude", (window, root) => {
                AppController.Render(window, new Size(1000, 600), 1, Path.Combine(renders, "claude-reliability-1000x600-claude.png"));
            });
        });

        check("Tab from the tab strip lands in Reliability's and Sources' pages, and the keys scroll them", () => {
            foreach (string name in new[] { ClaudeViews.Reliability, ClaudeViews.Sources })
            {
                string folder = Path.Combine(directory, "claude-reliability-keys", name);
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "settings.xml");
                SettingsStore.Save(path, new AppSettings { TextScale = 1, ClaudePeriod = "Today" });
                var window = AppController.LoadWindow("ShellWindow");
                try
                {
                    using (var controller = new AppController(window, path))
                    {
                        controller.UseClaudeSample(false); controller.ShowView(name);
                        window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -5000; window.Top = 0; window.Width = 1000; window.Height = 600;
                        window.Show(); window.Activate(); Wait(400);
                        var host = (FrameworkElement)window.FindName("ViewHost");
                        var page = Descendants(host).OfType<ScrollViewer>().FirstOrDefault(v => AutomationProperties.GetName(v) == name);
                        Assert(page != null, name + "'s page isn't in the window.");
                        Assert(page.Focusable && page.IsTabStop && page.IsEnabled, name + "'s page isn't a tab stop.");
                        // From the selected tab, Tab reaches the page (when the PC lets this off-screen window take the keyboard).
                        var tabs = (TabControl)window.FindName("ViewTabs");
                        var tab = tabs.ItemContainerGenerator.ContainerFromIndex(tabs.SelectedIndex) as UIElement;
                        if (tab != null && tab.Focus() && tab.IsKeyboardFocused)
                        {
                            bool landed = false;
                            for (int i = 0; i < 40 && !landed; i++)
                            {
                                var focused = Keyboard.FocusedElement as UIElement;
                                if (focused == null || !focused.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next))) break;
                                Wait(10);
                                landed = Keyboard.FocusedElement == page || Within(Keyboard.FocusedElement as DependencyObject, page);
                            }
                            Assert(landed, "Tab from the tab strip never reached " + name + "'s page.");
                        }
                        // The page's own keys scroll it.
                        page.UpdateLayout();
                        if (page.ScrollableHeight > 0)
                        {
                            var source = PresentationSource.FromVisual(page);
                            Action<Key> press = key => {
                                var down = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                                page.RaiseEvent(down);
                                if (!down.Handled) page.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.KeyDownEvent });
                                page.UpdateLayout();
                            };
                            press(Key.Down);
                            Assert(page.VerticalOffset > 0, "Down didn't scroll " + name + ".");
                            press(Key.Home);
                            Assert(page.VerticalOffset == 0, "Home didn't go back to the top of " + name + ".");
                            press(Key.PageDown);
                            Assert(page.VerticalOffset > 0, "Page Down didn't scroll " + name + ".");
                            press(Key.End);
                            Assert(Math.Abs(page.VerticalOffset - page.ScrollableHeight) < 1, "End didn't reach the bottom of " + name + ".");
                            press(Key.Home);
                            Assert(page.VerticalOffset == 0, "Home didn't go back to the top of " + name + ".");
                        }
                    }
                }
                finally { window.Close(); }
            }
        });
    }
}

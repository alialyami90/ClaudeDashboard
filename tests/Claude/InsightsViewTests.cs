using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClaudeDashboard;

// The Insights tab (decision 031, part B, v0.61.0): the view's model from the engine's ranges, the range row and the
// tools' model filter, honest words when something is missing, layouts that stay inside the width, and renders to
// artifacts\renders at 1600 × 900 and 1100 × 700 in a dark and a light palette. Everything runs on fixtures in memory.
internal static class InsightsViewTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc);
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
    private static List<string> Texts(DependencyObject root) { return Descendants(root).OfType<TextBlock>().Select(t => t.Text ?? "").ToList(); }

    private static void Host(IClaudeView view, ClaudeView snapshot, Size size, double scale, string theme, bool sample, Action<Window, FrameworkElement> body)
    {
        var window = AppController.LoadWindow("ShellWindow");
        try
        {
            Palettes.Apply(window.Resources, theme, null, scale);
            var context = new ClaudeViewContext { TextScale = scale, Sample = sample };
            var built = view.Build(context, size);
            view.Visible = true;
            var page = new Border { Child = built, Padding = new Thickness(16) };
            page.SetResourceReference(Border.BackgroundProperty, "Page");
            window.Content = page;
            view.Refresh(snapshot, Now);
            AppController.Lay(window, size);
            body(window, built);
        }
        finally { window.Close(); }
    }
    private static void Inside(FrameworkElement root, string name)
    {
        double width = root.ActualWidth;
        foreach (var element in Descendants(root).OfType<FrameworkElement>().Where(e => e.IsVisible && e.ActualWidth > 0 && (e is TextBlock || e is Button)))
        {
            var box = element.TransformToAncestor(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            Assert(box.Right <= width + 1, name + ": '" + (element is TextBlock ? ((TextBlock)element).Text : "button") + "' reaches " + Math.Round(box.Right) + " past " + Math.Round(width) + ".");
        }
    }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Insights is the 7th tab, Ctrl+7, and its package's view", () => {
            Assert(ClaudeViews.Names.Length == 7 && ClaudeViews.Names[6] == ClaudeViews.Insights, "Insights isn't the 7th tab");
            Assert(ClaudeViews.Create(ClaudeViews.Insights) is InsightsView, "Insights is a placeholder");
        });

        check("Insights' model takes the engine's ranges as they are", () => {
            var set = ClaudeInsightsSet.SampleSet(Now);
            foreach (var label in new[] { ClaudeInsights.Week, ClaudeInsights.Month, ClaudeInsights.All })
            {
                var ins = set.Pick(label);
                var m = InsightsModel.From(ins);
                Assert(m.Range == label && m.Tokens == ins.Tokens && m.Replies == ins.Replies && Math.Abs(m.Cost - ins.Cost) < 1e-9, label + ": totals");
                Assert(m.Models.Count == ins.Models.Count && (m.Models.Count == 0 || m.Models[0].Name == ins.Models[0].Name), label + ": models");
                Assert(m.Tools.Count == ins.Tools.Count && m.Projects.Count == ins.Projects.Count, label + ": tools and projects");
                Assert(m.PerDay.Count == ins.Series.Count, label + ": per day");
                Assert(m.HasPrevious == (label != ClaudeInsights.All), label + ": comparison");
                if (label == ClaudeInsights.All) Assert(m.Change(5, -1).Contains("everything kept") && m.ComparedLine().Contains("nothing to compare"), "All compares with something");
            }
            var week = InsightsModel.From(set.Week);
            Assert(week.Change(120, 100) == "↑ 20% on before" && week.Change(80, 100) == "↓ 20% on before" && week.Change(5, 0) == "new this period", "change words");
            var tools = week.ToolsFor("");
            for (int i = 1; i < tools.Count; i++) Assert(tools[i - 1].Calls > tools[i].Calls || (tools[i - 1].Calls == tools[i].Calls && String.Compare(tools[i - 1].Name, tools[i].Name, StringComparison.OrdinalIgnoreCase) <= 0), "tools aren't most used first, ties by name");
            if (week.Models.Count > 0)
            {
                string model = week.Models[0].Name;
                var filtered = week.ToolsFor(model);
                Assert(filtered.All(t => t.Calls > 0) && filtered.Sum(t => t.Calls) <= tools.Sum(t => t.Calls), "the model filter adds calls");
            }
        });

        check("Insights says why when the engine has nothing, and never shows Sample on live numbers", () => {
            var view = ClaudeView.Fixture(Now, false); view.Sample = false; view.Insights = null;
            var insights = new InsightsView();
            Host(insights, view, new Size(1100, 700), 1, "Midnight", false, (window, root) => {
                Assert(insights.Shown.Missing.Length > 0 && Texts(root).Any(t => t.Contains("Settings → Claude → History")), "no reason without the engine's ranges");
                Assert(!Texts(root).Any(t => t.StartsWith("SAMPLE")), "live numbers say Sample");
            });
            var empty = new ClaudeInsights { Label = ClaudeInsights.Week, Missing = "No day files in the last 7 days." };
            var m = InsightsModel.From(empty);
            Assert(m.Missing == empty.Missing, "the engine's reason is lost");
        });

        check("Insights' range row and model filter switch at once, without a new snapshot", () => {
            var insights = new InsightsView();
            Host(insights, ClaudeView.Fixture(Now, true), new Size(1100, 700), 1, "Midnight", true, (window, root) => {
                Assert(insights.Shown.Range == ClaudeInsights.Week, "doesn't start on 7 days");
                var month = Descendants(root).OfType<Button>().FirstOrDefault(b => System.Windows.Automation.AutomationProperties.GetName(b) == "30 days");
                Assert(month != null, "no 30 days button");
                month.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                AppController.Lay(window, new Size(1100, 700));
                Assert(insights.Shown.Range == ClaudeInsights.Month && insights.Range == ClaudeInsights.Month, "30 days didn't show");
                Assert(Texts(insights.Page).Any(t => t.Contains("compared with the previous 30 days")), "the comparison line didn't follow");
                insights.Choose(ClaudeInsights.All);
                Assert(insights.Shown.Range == ClaudeInsights.All && !insights.Shown.HasPrevious, "All didn't show");
                insights.Choose(ClaudeInsights.Week);
                if (insights.Shown.Models.Count > 1)
                {
                    string model = insights.Shown.Models[1].Name;
                    var filter = Descendants(insights.Page).OfType<Button>().FirstOrDefault(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Tools for " + model);
                    Assert(filter != null, "no filter button for " + model);
                    filter.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    Assert(Descendants(insights.Page).OfType<Button>().Any(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Tools for " + model + ", shown"), "the filter didn't take");
                }
                Assert(insights.Page.IsTabStop && insights.Page.FocusVisualStyle == CardParts.PageFocus, "the page isn't a tab stop with the shared ring");
            });
        });

        check("Insights stays inside its width and renders at 1600 × 900 and 1100 × 700, dark and light", () => {
            string renders = Path.GetFullPath(Path.Combine(directory, "..", "renders")); Directory.CreateDirectory(renders);
            foreach (var size in new[] { new Size(1600, 900), new Size(1100, 700), new Size(700, 500) })
                foreach (var theme in new[] { "Midnight", "Claude" })
                    foreach (double scale in new[] { 1.0, 1.4 })
                    {
                        if (scale > 1 && size.Width > 1500) continue;
                        LapChart.LastError = null;
                        var insights = new InsightsView();
                        Host(insights, ClaudeView.Fixture(Now, true), size, scale, theme, true, (window, root) => {
                            string name = "Insights " + size + " " + theme + " at " + scale;
                            Inside(root, name);
                            Assert(LapChart.LastError == null, name + ": a chart failed: " + LapChart.LastError);
                            Assert(Texts(root).Any(t => t.StartsWith("SAMPLE")), name + ": doesn't say Sample");
                            foreach (var heading in new[] { "Models", "Effort and thinking", "Tools", "Projects", "When", "Health" })
                                Assert(Texts(root).Contains(heading), name + ": no " + heading + " section");
                            if (scale == 1 && size.Width > 1000)
                            {
                                string png = Path.Combine(renders, "claude-insights-" + size.Width + "x" + size.Height + "-" + theme.ToLowerInvariant() + ".png");
                                AppController.Render(window, size, 1, png);
                                Assert(File.Exists(png) && new FileInfo(png).Length > 0, png + " wasn't rendered.");
                            }
                        });
                    }
        });
    }
}

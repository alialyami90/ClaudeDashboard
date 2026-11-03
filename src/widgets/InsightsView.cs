using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace ClaudeDashboard
{
    // The Insights tab (decision 031, part B): a detailed view of Claude Code's logs over 7 days, 30 days or everything
    // kept, compared with the period before. Six sections on one scrolling page: Models, Effort and thinking, Tools,
    // Projects, When, and Health. Numbers and names only (model ids, effort levels, tool, MCP server, skill and plugin
    // names, project folders), never prompts, replies, thinking, tool inputs or outputs.
    //
    // The engine works out all three ranges off the UI thread after each scan and puts them on the snapshot; this view
    // only picks one to draw, so switching range is instant. It starts no timers and reads no files.
    public sealed class InsightsView : IClaudeView
    {
        public string Name { get { return ClaudeViews.Insights; } }
        public bool Visible { get; set; }

        private ClaudeViewContext context = new ClaudeViewContext();
        private Size room;
        private Border root;
        private ScrollViewer page;
        private string shownKey;
        private ClaudeView last;
        private DateTime lastNow;
        // The range shown (Week, Month or All) and the tools' model filter ("" for all models). Kept across builds.
        private string range = InsightsModel.Week;
        private string toolModel = "";
        public ScrollViewer Page { get { return page; } }
        public InsightsModel Shown { get; private set; }
        public string Range { get { return range; } }

        public FrameworkElement Build(ClaudeViewContext context, Size room)
        {
            this.context = context ?? new ClaudeViewContext();
            this.room = room;
            shownKey = null;
            root = new Border();
            AutomationProperties.SetName(root, "Insights");
            page = LapParts.Page("Insights page");
            root.Child = page;
            if (last != null) Refresh(last, lastNow);
            return root;
        }

        // Picks a range (from the range row, or a check) and draws it at once from the last snapshot.
        public void Choose(string which)
        {
            if (which != InsightsModel.Week && which != InsightsModel.Month && which != InsightsModel.All) return;
            range = which; shownKey = null;
            if (last != null) Refresh(last, lastNow);
        }
        // Filters the tools by model ("" for all).
        public void FilterTools(string model)
        {
            toolModel = model ?? ""; shownKey = null;
            if (last != null) Refresh(last, lastNow);
        }

        public void Refresh(ClaudeView snapshot, DateTime now)
        {
            last = snapshot; lastNow = now;
            if (root == null || snapshot == null) return;
            bool sample = snapshot.Sample || context.Sample;
            string key = range + "|" + toolModel + "|" + snapshot.MadeAt.Ticks + "|" + now.ToString("yyyyMMddHH", CultureInfo.InvariantCulture) + "|" + sample + "|" + context.Use24Hour;
            if (key == shownKey) return;
            shownKey = key;
            var model = sample ? InsightsModel.SampleFor(range, now) : InsightsModel.From(snapshot, range);
            if (toolModel.Length > 0 && !model.Models.Any(m => m.Name == toolModel)) toolModel = "";
            Shown = model;
            double offset = page.VerticalOffset;
            page.Content = Draw(model);
            page.ScrollToVerticalOffset(offset);
        }

        // ---- Drawing ----

        private double S(double size) { return context.Scaled(size); }
        private bool Wide { get { return room.Width >= 1000; } }

        private FrameworkElement Draw(InsightsModel m)
        {
            var sheet = new StackPanel { Margin = new Thickness(14, 6, 14, 10) };
            if (m.Sample) sheet.Children.Add(LapParts.SampleFlag(S(12)));
            sheet.Children.Add(RangeRow(m));
            if (m.Missing.Length > 0)
            {
                var why = LapParts.Words(m.Missing, S(15), "Text"); why.Margin = new Thickness(0, 16, 0, 0);
                sheet.Children.Add(CardParts.Tile(why, 12, new Thickness(16)));
                return sheet;
            }
            sheet.Children.Add(Totals(m));
            var models = Section("Models", ModelsBody(m));
            var effort = Section("Effort and thinking", EffortBody(m));
            var tools = Section("Tools", ToolsBody(m));
            var projects = Section("Projects", ProjectsBody(m));
            var when = Section("When", WhenBody(m));
            var health = Section("Health", HealthBody(m));
            if (Wide)
            {
                sheet.Children.Add(Pair(models, effort));
                sheet.Children.Add(Pair(tools, projects));
                sheet.Children.Add(Pair(when, health));
            }
            else foreach (var s in new[] { models, effort, tools, projects, when, health }) sheet.Children.Add(s);
            var foot = LapParts.Words(m.FootLine(), S(12), "Muted"); foot.Margin = new Thickness(0, 12, 0, 0);
            sheet.Children.Add(foot);
            return sheet;
        }

        private static Grid Pair(FrameworkElement left, FrameworkElement right)
        {
            var grid = CardParts.Columns(CardParts.Star, CardParts.Star);
            left.Margin = new Thickness(0, 16, 8, 0); right.Margin = new Thickness(8, 16, 0, 0);
            grid.Children.Add(CardParts.At(left, 0, 0)); grid.Children.Add(CardParts.At(right, 0, 1));
            return grid;
        }

        private Border Section(string title, FrameworkElement body)
        {
            var stack = new StackPanel();
            var heading = LapParts.Heading(title, S(18));
            stack.Children.Add(heading);
            body.Margin = new Thickness(0, 10, 0, 0);
            stack.Children.Add(body);
            var tile = CardParts.Tile(stack, 12, new Thickness(16));
            tile.Margin = new Thickness(0, 16, 0, 0);
            AutomationProperties.SetName(tile, title);
            return tile;
        }

        // 7 days · 30 days · All, and what it's compared with.
        private FrameworkElement RangeRow(InsightsModel m)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            foreach (var r in new[] { InsightsModel.Week, InsightsModel.Month, InsightsModel.All })
            {
                string which = r;
                var button = InsightsParts.Choice(InsightsModel.RangeName(r), r == range, S(13));
                AutomationProperties.SetName(button, InsightsModel.RangeName(r) + (r == range ? ", shown" : ""));
                button.Click += (sender, e) => Choose(which);
                row.Children.Add(button);
            }
            if (m.Missing.Length > 0) return row;
            var compared = LapParts.Words(m.ComparedLine(), S(13), "Muted"); compared.VerticalAlignment = VerticalAlignment.Center; compared.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(compared);
            return row;
        }

        // The range's totals beside the period before.
        private FrameworkElement Totals(InsightsModel m)
        {
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = Wide ? 5 : 3, Margin = new Thickness(0, 8, 0, 0) };
            foreach (var t in m.TotalTiles())
            {
                var stack = new StackPanel();
                stack.Children.Add(LapParts.Caption(t.Name, S(11)));
                var value = CardParts.Reading(t.Value, S(28)); value.SetResourceReference(TextBlock.ForegroundProperty, "Text"); value.Margin = new Thickness(0, 4, 0, 2);
                stack.Children.Add(value);
                stack.Children.Add(LapParts.Words(t.Change, S(12), "Muted"));
                var tile = CardParts.Tile(stack, 12, new Thickness(14, 10, 14, 12)); tile.Margin = new Thickness(0, 0, 10, 10);
                AutomationProperties.SetName(tile, t.Name + " " + t.Value + ", " + t.Change);
                grid.Children.Add(tile);
            }
            return grid;
        }

        private FrameworkElement ModelsBody(InsightsModel m)
        {
            var stack = new StackPanel();
            if (m.Models.Count == 0) { stack.Children.Add(LapParts.Words("No replies in this range.", S(13), "Muted")); return stack; }
            var share = new LapChart { Height = Math.Round(S(14)) };
            var parts = m.Models.Select(x => x.Share).ToList();
            share.Paint = (dc, size, c) => InsightsParts.PaintSplit(dc, size, c, parts);
            AutomationProperties.SetName(share, "Share of tokens by model: " + String.Join(", ", m.Models.Select(x => InsightsModel.ModelName(x.Name) + " " + InsightsModel.Percent(x.Share))));
            stack.Children.Add(share);
            stack.Children.Add(InsightsParts.Legend(m.Models.Select(x => InsightsModel.ModelName(x.Name) + " " + InsightsModel.Percent(x.Share)).ToList(), S(12)));

            var table = InsightsParts.Table(new[] { "Model", "Replies", "Tokens", "≈ $", "Thinking", "Share of tokens", "Share of replies" }, m.Models.Select((x, i) => new[] {
                InsightsModel.ModelName(x.Name), InsightsModel.Count(x.Replies), ClaudeUsage.Format(x.Tokens), InsightsModel.Money(x.Cost, x.Unpriced), x.ThinkingTokens > 0 ? ClaudeUsage.Format(x.ThinkingTokens) : "—", InsightsModel.Percent(x.Share),
                m.DetailReplies > 0 ? InsightsModel.Percent((double)x.Replies / m.DetailReplies) : "—" }).ToList(), S(13), 0);
            table.Margin = new Thickness(0, 10, 0, 0);
            stack.Children.Add(table);

            var caption = LapParts.Caption("Tokens per day, by model", S(11)); caption.Margin = new Thickness(0, 14, 0, 6);
            stack.Children.Add(caption);
            var perDay = new LapChart { Height = Math.Round(S(110) + S(18)) };
            double label = S(11);
            perDay.Paint = (dc, size, c) => InsightsParts.PaintStackedDays(dc, size, c, m, label);
            AutomationProperties.SetName(perDay, m.PerDayWords());
            stack.Children.Add(perDay);
            return stack;
        }

        private FrameworkElement EffortBody(InsightsModel m)
        {
            var stack = new StackPanel();
            if (m.Effort.Count == 0) stack.Children.Add(LapParts.Words(m.EffortWhy.Length > 0 ? m.EffortWhy : "No effort levels in this range.", S(13), "Muted"));
            else
            {
                var levels = m.EffortLevels();
                foreach (var e in m.Effort)
                {
                    var row = CardParts.Columns(new GridLength(S(110)), CardParts.Star, CardParts.Auto); row.Margin = new Thickness(0, 3, 0, 3);
                    var name = CardParts.Text(InsightsModel.ModelName(e.Model), S(13), "Text"); name.ToolTip = e.Model; name.TextTrimming = TextTrimming.CharacterEllipsis;
                    row.Children.Add(CardParts.At(name, 0, 0));
                    int total = e.Levels.Values.Sum();
                    var split = levels.Select(l => { int n; return e.Levels.TryGetValue(l, out n) && total > 0 ? (double)n / total : 0.0; }).ToList();
                    var bar = new LapChart { Height = Math.Round(S(12)), VerticalAlignment = VerticalAlignment.Center };
                    bar.Paint = (dc, size, c) => InsightsParts.PaintSplit(dc, size, c, split);
                    row.Children.Add(CardParts.At(bar, 0, 1));
                    var count = CardParts.Text(InsightsModel.Count(total), S(13), "Muted"); count.Margin = new Thickness(10, 0, 0, 0);
                    row.Children.Add(CardParts.At(count, 0, 2));
                    AutomationProperties.SetName(row, InsightsModel.ModelName(e.Model) + ": " + String.Join(", ", levels.Select((l, i) => l + " " + InsightsModel.Percent(split[i]))));
                    stack.Children.Add(row);
                }
                stack.Children.Add(InsightsParts.Legend(levels, S(12)));
            }
            var thinking = CardParts.Columns(CardParts.Star, CardParts.Star, CardParts.Star); thinking.Margin = new Thickness(0, 14, 0, 0);
            var figures = m.ThinkingFigures();
            for (int i = 0; i < figures.Count; i++)
            {
                var cell = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
                cell.Children.Add(LapParts.Caption(figures[i].Name, S(11)));
                var value = CardParts.Text(figures[i].Value, S(20), "Text"); Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular); value.Margin = new Thickness(0, 2, 0, 0);
                cell.Children.Add(value);
                if (figures[i].Change.Length > 0) cell.Children.Add(LapParts.Words(figures[i].Change, S(12), "Muted"));
                thinking.Children.Add(CardParts.At(cell, 0, i));
            }
            stack.Children.Add(thinking);
            return stack;
        }

        private FrameworkElement ToolsBody(InsightsModel m)
        {
            var stack = new StackPanel();
            if (m.Models.Count > 1)
            {
                var filter = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
                var label = LapParts.Words("Model:", S(13), "Muted"); label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new Thickness(0, 0, 6, 0);
                filter.Children.Add(label);
                foreach (var name in new[] { "" }.Concat(m.Models.Select(x => x.Name)))
                {
                    string which = name;
                    string shown = InsightsModel.ModelName(name);
                    var button = InsightsParts.Choice(name.Length == 0 ? "All" : shown, name == toolModel, S(12));
                    AutomationProperties.SetName(button, "Tools for " + (name.Length == 0 ? "all models" : shown) + (name == toolModel ? ", shown" : ""));
                    button.Click += (sender, e) => FilterTools(which);
                    filter.Children.Add(button);
                }
                stack.Children.Add(filter);
            }
            var tools = m.ToolsFor(toolModel);
            if (tools.Count == 0) { stack.Children.Add(LapParts.Words("No tool calls in this range" + (toolModel.Length > 0 ? " for " + InsightsModel.ModelName(toolModel) : "") + ".", S(13), "Muted")); return stack; }
            var top = tools.Take(10).ToList();
            stack.Children.Add(LapParts.Caption("Most used", S(11)));
            stack.Children.Add(ToolTable(top, tools[0].Calls));
            var least = tools.Count > 10 ? tools.Skip(Math.Max(10, tools.Count - 5)).Reverse().ToList() : new List<InsightsTool>();
            if (least.Count > 0)
            {
                var caption = LapParts.Caption("Least used", S(11)); caption.Margin = new Thickness(0, 12, 0, 0);
                stack.Children.Add(caption);
                stack.Children.Add(ToolTable(least, tools[0].Calls));
            }
            if (tools.Any(t => Double.IsNaN(t.MedianMs)))
            {
                var why = LapParts.Words("— in ≈ median: tool times need both the call and its result in the log.", S(12), "Muted"); why.Margin = new Thickness(0, 6, 0, 0);
                stack.Children.Add(why);
            }
            var extras = CardParts.Columns(CardParts.Star, CardParts.Star); extras.Margin = new Thickness(0, 14, 0, 0);
            extras.Children.Add(CardParts.At(NameCounts("MCP servers", m.Mcp, "calls", "No MCP calls in this range."), 0, 0));
            var skills = NameCounts("Skills and plugins", m.Skills, "replies", "No skill or plugin replies in this range."); skills.Margin = new Thickness(10, 0, 0, 0);
            extras.Children.Add(CardParts.At(skills, 0, 1));
            stack.Children.Add(extras);
            return stack;
        }

        private FrameworkElement ToolTable(IList<InsightsTool> tools, long most)
        {
            var grid = CardParts.Columns(new GridLength(S(170)), CardParts.Star, CardParts.Auto, CardParts.Auto, CardParts.Auto);
            grid.Margin = new Thickness(0, 4, 0, 0);
            string[] heads = { "", "Calls", "Errors", "Error %", "≈ median" };
            grid.RowDefinitions.Add(new RowDefinition { Height = CardParts.Auto });
            for (int i = 1; i < heads.Length; i++)
            {
                var head = CardParts.Text(heads[i], S(11), "Muted"); head.FontWeight = FontWeights.SemiBold; head.Margin = new Thickness(10, 0, 0, 2);
                head.HorizontalAlignment = i == 1 ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                grid.Children.Add(CardParts.At(head, 0, i));
            }
            for (int r = 0; r < tools.Count; r++)
            {
                var t = tools[r];
                grid.RowDefinitions.Add(new RowDefinition { Height = CardParts.Auto });
                var name = CardParts.Text(InsightsModel.ToolName(t.Name), S(13), "Text"); name.TextWrapping = TextWrapping.Wrap; name.ToolTip = t.Name; name.Margin = new Thickness(0, 3, 0, 3);
                grid.Children.Add(CardParts.At(name, r + 1, 0));
                var bar = CardParts.Columns(CardParts.Star, CardParts.Auto); bar.Margin = new Thickness(10, 0, 0, 0);
                bar.Children.Add(CardParts.At(LapParts.Bar(most > 0 ? (double)t.Calls / most : 0, 8, false), 0, 0));
                var calls = CardParts.Text(InsightsModel.Count(t.Calls), S(13), "Text"); calls.Margin = new Thickness(8, 0, 0, 0); Typography.SetNumeralAlignment(calls, FontNumeralAlignment.Tabular);
                bar.Children.Add(CardParts.At(calls, 0, 1));
                grid.Children.Add(CardParts.At(bar, r + 1, 1));
                bool warns = t.ErrorShare >= 0.2 && t.Errors >= 3;
                var cells = new[] { InsightsModel.Count(t.Errors), t.Calls > 0 ? InsightsModel.Percent(t.ErrorShare) : "—", Double.IsNaN(t.MedianMs) ? "—" : "≈ " + InsightsModel.Duration(t.MedianMs) };
                for (int i = 0; i < 3; i++)
                {
                    var cell = CardParts.Text(cells[i], S(13), i < 2 && warns ? "Warning" : i == 2 ? "Muted" : "Text");
                    cell.Margin = new Thickness(10, 3, 0, 3); cell.HorizontalAlignment = HorizontalAlignment.Right; Typography.SetNumeralAlignment(cell, FontNumeralAlignment.Tabular);
                    grid.Children.Add(CardParts.At(cell, r + 1, i + 2));
                }
                AutomationProperties.SetName(name, t.Name + ": " + t.Calls + " calls, " + t.Errors + " errors" + (Double.IsNaN(t.MedianMs) ? "" : ", about " + InsightsModel.Duration(t.MedianMs) + " median"));
            }
            return grid;
        }

        private FrameworkElement NameCounts(string title, IList<KeyValuePair<string, long>> list, string unit, string none)
        {
            var stack = new StackPanel();
            stack.Children.Add(LapParts.Caption(title, S(11)));
            if (list.Count == 0) { stack.Children.Add(LapParts.Words(none, S(12), "Muted")); return stack; }
            foreach (var p in list.Take(6))
            {
                var row = CardParts.Columns(CardParts.Star, CardParts.Auto); row.Margin = new Thickness(0, 2, 0, 2);
                var name = CardParts.Text(InsightsModel.ServerName(p.Key), S(13), "Text"); name.TextTrimming = TextTrimming.CharacterEllipsis; name.ToolTip = p.Key;
                AutomationProperties.SetName(name, p.Key);
                var value = CardParts.Text(InsightsModel.Count(p.Value) + " " + unit, S(13), "Muted"); value.Margin = new Thickness(8, 0, 0, 0);
                row.Children.Add(CardParts.At(name, 0, 0)); row.Children.Add(CardParts.At(value, 0, 1));
                stack.Children.Add(row);
            }
            if (list.Count > 6) stack.Children.Add(LapParts.Words("and " + (list.Count - 6) + " more", S(12), "Muted"));
            return stack;
        }

        private FrameworkElement ProjectsBody(InsightsModel m)
        {
            var stack = new StackPanel();
            if (m.Projects.Count == 0) { stack.Children.Add(LapParts.Words("No project tokens in this range.", S(13), "Muted")); return stack; }
            var rows = m.Projects.Take(10).Select(p => new[] { p.Name, ClaudeUsage.Format(p.Tokens), InsightsModel.Count(p.Replies), InsightsModel.Money(p.Cost, 0), p.Sessions > 0 ? InsightsModel.Count(p.Sessions) : "—",
                p.Added + p.Removed > 0 ? "+" + InsightsModel.Count(p.Added) + " −" + InsightsModel.Count(p.Removed) : "—" }).ToList();
            stack.Children.Add(InsightsParts.Table(new[] { "Project", "Tokens", "Replies", "≈ $", "Sessions", "Lines" }, rows, S(13), 0));
            var line = LapParts.Words(m.ProjectsLine(), S(12), "Muted"); line.Margin = new Thickness(0, 8, 0, 0);
            stack.Children.Add(line);
            return stack;
        }

        private FrameworkElement WhenBody(InsightsModel m)
        {
            var stack = new StackPanel();
            stack.Children.Add(LapParts.Words("tokens by hour and weekday · darker is quieter", S(12), "Muted"));
            var heat = new LapChart { Height = Math.Round(S(14) + 7 * (S(16) + 3)), Margin = new Thickness(0, 8, 0, 0) };
            double label = S(11); bool use24 = context.Use24Hour;
            heat.Paint = (dc, size, c) => InsightsParts.PaintHeat(dc, size, c, m, label, use24);
            AutomationProperties.SetName(heat, m.HeatWords());
            stack.Children.Add(heat);
            var caption = LapParts.Caption("Sessions per day", S(11)); caption.Margin = new Thickness(0, 14, 0, 6);
            stack.Children.Add(caption);
            var sessions = new LapChart { Height = Math.Round(S(70) + S(18)) };
            sessions.Paint = (dc, size, c) => InsightsParts.PaintSessions(dc, size, c, m, label);
            AutomationProperties.SetName(sessions, m.SessionWords());
            stack.Children.Add(sessions);
            return stack;
        }

        private FrameworkElement HealthBody(InsightsModel m)
        {
            var stack = new StackPanel();
            var figures = CardParts.Columns(CardParts.Star, CardParts.Star, CardParts.Star);
            var list = m.HealthFigures();
            for (int i = 0; i < list.Count; i++)
            {
                var cell = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
                cell.Children.Add(LapParts.Caption(list[i].Name, S(11)));
                var value = CardParts.Text(list[i].Value, S(20), list[i].Warns ? "Warning" : "Text"); Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
                cell.Children.Add(value);
                cell.Children.Add(LapParts.Words(list[i].Change, S(12), "Muted"));
                figures.Children.Add(CardParts.At(cell, 0, i));
            }
            stack.Children.Add(figures);
            stack.Children.Add(Split("Permissions", m.Permissions, "No permission decisions in the logs for this range."));
            stack.Children.Add(Split("How replies ended", m.Stops, "No stop reasons in this range."));
            return stack;
        }

        private FrameworkElement Split(string title, IList<KeyValuePair<string, long>> parts, string none)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            stack.Children.Add(LapParts.Caption(title, S(11)));
            long total = parts.Sum(p => p.Value);
            if (total <= 0) { stack.Children.Add(LapParts.Words(none, S(12), "Muted")); return stack; }
            var shares = parts.Select(p => (double)p.Value / total).ToList();
            var bar = new LapChart { Height = Math.Round(S(12)), Margin = new Thickness(0, 6, 0, 0) };
            bar.Paint = (dc, size, c) => InsightsParts.PaintSplit(dc, size, c, shares);
            AutomationProperties.SetName(bar, title + ": " + String.Join(", ", parts.Select(p => p.Key + " " + p.Value)));
            stack.Children.Add(bar);
            stack.Children.Add(InsightsParts.Legend(parts.Select(p => p.Key + " " + InsightsModel.Count(p.Value)).ToList(), S(12)));
            return stack;
        }
    }

    // The Insights tab's own parts: series colours, choice buttons, tables, legends and charts.
    internal static class InsightsParts
    {
        // Series colours from the palette, never Amber (kept for warnings): the accent and Glacier, then each at half
        // strength, then Muted.
        public static string SeriesKey(int i) { return i >= 4 ? "Muted" : i % 2 == 0 ? "Accent" : "Turn"; }
        public static double SeriesOpacity(int i) { return i < 2 ? 1 : i < 4 ? 0.5 : 0.8; }
        public static Brush SeriesInk(LapChart c, int i) { return i >= 4 ? c.MutedInk : i % 2 == 0 ? c.AccentInk : c.TurnInk; }

        public static void PaintSplit(DrawingContext dc, Size size, LapChart c, IList<double> shares)
        {
            dc.DrawRoundedRectangle(c.TrackInk, null, new Rect(size), size.Height / 2, size.Height / 2);
            double x = 0;
            for (int i = 0; i < shares.Count; i++)
            {
                double w = size.Width * Math.Max(0, shares[i]);
                if (w <= 0) continue;
                dc.PushOpacity(SeriesOpacity(i));
                dc.DrawRectangle(SeriesInk(c, i), null, new Rect(x, 0, Math.Max(1, w - (i < shares.Count - 1 ? 1 : 0)), size.Height));
                dc.Pop();
                x += w;
            }
        }

        public static FrameworkElement Legend(IList<string> names, double size)
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            for (int i = 0; i < names.Count; i++)
            {
                var swatch = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Opacity = SeriesOpacity(i), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
                swatch.SetResourceReference(Border.BackgroundProperty, SeriesKey(i));
                var words = CardParts.Text(names[i], size, "Muted");
                var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 2) };
                item.Children.Add(swatch); item.Children.Add(words);
                wrap.Children.Add(item);
            }
            return wrap;
        }

        // A plain table: the first column left, the rest right-aligned with tabular figures.
        public static Grid Table(IList<string> heads, IList<string[]> rows, double size, int bold)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            for (int i = 1; i < heads.Count; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = CardParts.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = CardParts.Auto });
            for (int i = 0; i < heads.Count; i++)
            {
                var head = CardParts.Text(heads[i], Math.Round(size * 0.85, 1), "Muted"); head.FontWeight = FontWeights.SemiBold;
                head.Margin = new Thickness(i == 0 ? 0 : 12, 0, 0, 4); head.HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                grid.Children.Add(CardParts.At(head, 0, i));
            }
            for (int r = 0; r < rows.Count; r++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = CardParts.Auto });
                for (int i = 0; i < heads.Count && i < rows[r].Length; i++)
                {
                    var cell = CardParts.Text(rows[r][i], size, "Text");
                    if (i == 0) { cell.TextTrimming = TextTrimming.CharacterEllipsis; cell.ToolTip = rows[r][i]; }
                    cell.Margin = new Thickness(i == 0 ? 0 : 12, 3, 0, 3); cell.HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                    Typography.SetNumeralAlignment(cell, FontNumeralAlignment.Tabular);
                    grid.Children.Add(CardParts.At(cell, r + 1, i));
                }
            }
            return grid;
        }

        // A choice in a row (a range, a model filter): a plain button, filled when chosen, with the app's 2-unit ring
        // when the keyboard brings focus to it.
        public static Button Choice(string text, bool chosen, double size)
        {
            var words = CardParts.Text(text, size, chosen ? "OnAccent" : "Text");
            if (chosen) words.FontWeight = FontWeights.SemiBold;
            var face = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 4, 10, 4), Child = words, BorderThickness = new Thickness(1) };
            face.SetResourceReference(Border.BackgroundProperty, chosen ? "Accent" : "Raised");
            face.SetResourceReference(Border.BorderBrushProperty, chosen ? "Accent" : "ControlBorder");
            var button = new Button { Content = face, Style = ChoiceStyle, Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand };
            return button;
        }
        private static Style choiceStyle;
        private static Style ChoiceStyle
        {
            get
            {
                if (choiceStyle != null) return choiceStyle;
                var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
                var ring = new FrameworkElementFactory(typeof(Border));
                ring.SetValue(Border.BorderThicknessProperty, new Thickness(2));
                ring.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
                ring.SetValue(FrameworkElement.MarginProperty, new Thickness(-3));
                ring.SetResourceReference(Border.BorderBrushProperty, "Text");
                var focus = new Style(typeof(Control));
                focus.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Control)) { VisualTree = ring }));
                var style = new Style(typeof(Button));
                style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Button)) { VisualTree = presenter }));
                style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, focus));
                style.Seal();
                return choiceStyle = style;
            }
        }

        // Tokens per day, stacked by model in the same order and colours as the share bar.
        public static void PaintStackedDays(DrawingContext dc, Size size, LapChart c, InsightsModel m, double label)
        {
            int n = m.PerDay.Count; if (n == 0) return;
            double axis = label * 1.6, bottom = size.Height - axis, gap = n > 40 ? 1 : 3, width = (size.Width - gap * (n - 1)) / n;
            if (width < 1) return;
            long most = Math.Max(1, m.PerDay.Max(d => d.Total));
            for (int i = 0; i < n; i++)
            {
                var d = m.PerDay[i]; double x = i * (width + gap), y = bottom;
                for (int k = 0; k < m.Models.Count; k++)
                {
                    long t; if (!d.ByModel.TryGetValue(m.Models[k].Name, out t) || t <= 0) continue;
                    double h = (bottom - 2) * t / most;
                    dc.PushOpacity(SeriesOpacity(k)); dc.DrawRectangle(SeriesInk(c, k), null, new Rect(x, y - h, width, h)); dc.Pop();
                    y -= h;
                }
                long other = d.Total - m.Models.Sum(x2 => { long t; return d.ByModel.TryGetValue(x2.Name, out t) ? t : 0; });
                if (other > 0) { double h = (bottom - 2) * other / most; dc.DrawRectangle(c.TrackInk, null, new Rect(x, y - h, width, h)); }
            }
            Ends(dc, c, m.PerDay[0].Date, m.PerDay[n - 1].Date, size, bottom, label);
        }

        public static void PaintSessions(DrawingContext dc, Size size, LapChart c, InsightsModel m, double label)
        {
            int n = m.PerDay.Count; if (n == 0) return;
            double axis = label * 1.6, bottom = size.Height - axis, gap = n > 40 ? 1 : 3, width = (size.Width - gap * (n - 1)) / n;
            if (width < 1) return;
            int most = Math.Max(1, m.PerDay.Max(d => d.Sessions));
            for (int i = 0; i < n; i++)
            {
                var d = m.PerDay[i]; if (d.Sessions <= 0) continue;
                double h = Math.Max(2, (bottom - label * 1.4) * d.Sessions / most);
                dc.PushOpacity(0.55); dc.DrawRectangle(c.AccentInk, null, new Rect(i * (width + gap), bottom - h, width, h)); dc.Pop();
            }
            var peak = LapParts.Ink(c, "most " + most, label, c.MutedInk, false);
            dc.DrawText(peak, new Point(size.Width - peak.Width, 0));
            Ends(dc, c, m.PerDay[0].Date, m.PerDay[n - 1].Date, size, bottom, label);
        }

        private static void Ends(DrawingContext dc, LapChart c, DateTime first, DateTime last, Size size, double bottom, double label)
        {
            var from = LapParts.Ink(c, first.ToString("d MMM", CultureInfo.InvariantCulture), label, c.MutedInk, false);
            dc.DrawText(from, new Point(0, bottom + 3));
            var to = LapParts.Ink(c, last.ToString("d MMM", CultureInfo.InvariantCulture), label, c.MutedInk, false);
            dc.DrawText(to, new Point(Math.Max(from.Width + 8, size.Width - to.Width), bottom + 3));
        }

        // Hour across, Monday at the top; four steps of the accent by share of the busiest cell.
        public static void PaintHeat(DrawingContext dc, Size size, LapChart c, InsightsModel m, double label, bool use24)
        {
            double labels = 34, head = label * 1.4, gap = 2;
            double width = (size.Width - labels - gap * 23) / 24, height = (size.Height - head - gap * 6) / 7;
            if (width < 2 || height < 2) return;
            string[] days = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            long most = 0; for (int d = 0; d < 7; d++) for (int h = 0; h < 24; h++) most = Math.Max(most, m.Heat[d, h]);
            for (int d = 0; d < 7; d++)
            {
                var name = LapParts.Ink(c, days[d], label, c.MutedInk, false);
                dc.DrawText(name, new Point(0, head + d * (height + gap) + (height - name.Height) / 2));
                for (int h = 0; h < 24; h++)
                {
                    var rect = new Rect(labels + h * (width + gap), head + d * (height + gap), width, height);
                    long v = m.Heat[d, h];
                    if (v <= 0 || most <= 0) { dc.DrawRoundedRectangle(c.TrackInk, null, rect, 2, 2); continue; }
                    int level = Math.Max(1, Math.Min(4, (int)Math.Ceiling(4.0 * v / most)));
                    dc.PushOpacity(HistoryModel.Shades[level]); dc.DrawRoundedRectangle(c.AccentInk, null, rect, 2, 2); dc.Pop();
                }
            }
            foreach (int h in new[] { 0, 6, 12, 18 })
            {
                var words = LapParts.Ink(c, HistoryModel.HourName(h, use24), label, c.MutedInk, false);
                dc.DrawText(words, new Point(labels + h * (width + gap), 0));
            }
        }
    }

    // ---- The view's data ----

    public sealed class InsightsModelRow
    {
        public string Name = "";
        public long Replies, Tokens, ThinkingTokens, Unpriced;
        public double Cost, Share;
    }
    public sealed class InsightsDay { public DateTime Date; public long Total; public int Sessions; public Dictionary<string, long> ByModel = new Dictionary<string, long>(); }
    public sealed class InsightsEffort { public string Model = ""; public Dictionary<string, int> Levels = new Dictionary<string, int>(); }
    public sealed class InsightsTool
    {
        public string Name = "";
        public long Calls, Errors;
        // ≈ median time from the gap between the call's line and its result's (NaN: no pairs).
        public double MedianMs = Double.NaN;
        public Dictionary<string, long> ByModel = new Dictionary<string, long>();
        // Per model, from the engine: errors, and the ≈ median time (NaN: that model has no timed calls).
        public Dictionary<string, long> ErrorsByModel = new Dictionary<string, long>();
        public Dictionary<string, double> MedianByModel = new Dictionary<string, double>();
        public double ErrorShare { get { return Calls > 0 ? (double)Errors / Calls : 0; } }
    }
    public sealed class InsightsProject { public string Name = "", Folder = ""; public long Tokens, Replies, Added, Removed; public int Sessions; public double Cost; }
    public sealed class InsightsFigure { public string Name = "", Value = "", Change = ""; public bool Warns; }

    // What the Insights tab shows for one range, beside the period before (for the view and the checks).
    public sealed class InsightsModel
    {
        public const string Week = ClaudeInsights.Week, Month = ClaudeInsights.Month, All = ClaudeInsights.All;
        public static string RangeName(string range) { return range; }

        public bool Sample;
        public string Missing = "";
        public string Range = Week;
        public DateTime Start, End;
        // Days in this range rebuilt from Claude Code's logs (History's backfill), and whether the period before exists.
        public int FromLogs;
        public bool HasPrevious;
        public long Tokens, Replies, ThinkingReplies, PrevTokens, PrevReplies;
        // Days in the period before with any replies (-1: the engine doesn't say yet, so only the replies count).
        public int PrevActiveDays = -1;
        public double Cost, PrevCost;
        public int Sessions, PrevSessions;
        // Replies in the days with detail: ratios over the detail (thinking, share of replies) divide by this.
        public long DetailReplies;
        // The engine's note for the footer (today's detail still pending, and such).
        public string Note = "";
        public bool TodayPending;
        // Today's replies while today's detail is still being read: the Replies tile leaves them out, so it agrees with
        // the models table, and says how many are on their way.
        public long TodayReplies;
        public List<InsightsModelRow> Models = new List<InsightsModelRow>();
        public List<InsightsDay> PerDay = new List<InsightsDay>();
        public List<InsightsEffort> Effort = new List<InsightsEffort>();
        public string EffortWhy = "";
        // Days in the range with a day file, and with the per-model detail (days kept before v0.60.0 have none).
        public int DaysWithFiles, DaysWithDetail;
        public double ThinkingMedianMs = Double.NaN, ThinkingLongestMs = Double.NaN, PrevThinkingShare = Double.NaN;
        public List<InsightsTool> Tools = new List<InsightsTool>();
        public List<KeyValuePair<string, long>> Mcp = new List<KeyValuePair<string, long>>(), Skills = new List<KeyValuePair<string, long>>();
        public List<InsightsProject> Projects = new List<InsightsProject>();
        public long[,] Heat = new long[7, 24];
        public long ApiErrors, Retries, Compactions, PrevApiErrors = -1, PrevRetries = -1, PrevCompactions = -1;
        public List<KeyValuePair<string, long>> Permissions = new List<KeyValuePair<string, long>>(), Stops = new List<KeyValuePair<string, long>>();

        // ---- Words ----

        public static string Count(long n) { return n.ToString("#,0", CultureInfo.InvariantCulture); }
        public static string Percent(double share) { double p = share * 100; return Double.IsNaN(p) ? "—" : p > 0 && p < 1 ? "<1%" : Math.Round(p).ToString(CultureInfo.InvariantCulture) + "%"; }
        public static string Money(double cost, long unpriced) { return cost <= 0 ? (unpriced > 0 ? "unpriced" : "—") : (unpriced > 0 ? "≥ " : "") + cost.ToString("0.00", CultureInfo.InvariantCulture); }
        // "claude-<family>-<major>-<minor>" → "Family major.minor" (also the older "claude-3-5-sonnet"), dropping a "[1m]" or
        // date suffix, so models that don't exist yet read right too; anything else as it is.
        public static string ModelName(string id)
        {
            if (String.IsNullOrEmpty(id)) return id ?? "";
            string bare = id.Trim();
            int bracket = bare.IndexOf('[');
            if (bracket > 0 && bare.EndsWith("]", StringComparison.Ordinal)) bare = bare.Substring(0, bracket);
            var parts = bare.ToLowerInvariant().Split('-').ToList();
            if (parts.Count < 3 || parts[0] != "claude") return id;
            parts.RemoveAt(0);
            if (parts.Count > 0 && parts[parts.Count - 1].Length == 8 && parts[parts.Count - 1].All(Char.IsDigit)) parts.RemoveAt(parts.Count - 1);
            var words = parts.Where(x => x.Length > 0 && x.All(Char.IsLetter)).ToList();
            if (words.Count != 1) return id;
            var numbers = parts.Where(x => x != words[0]).ToList();
            if (numbers.Count == 0 || numbers.Count > 2 || !numbers.All(x => x.Length > 0 && x.Length <= 2 && x.All(Char.IsDigit))) return id;
            return Char.ToUpperInvariant(words[0][0]) + words[0].Substring(1) + " " + String.Join(".", numbers);
        }
        // An MCP server's name; a bare id (a connector's uuid) reads "Connector 1a59c9".
        public static string ServerName(string server)
        {
            if (String.IsNullOrEmpty(server)) return server ?? "";
            string s = server;
            int dash = s.IndexOf('-');
            bool id = s.Length >= 32 && dash == 8 && s.Replace("-", "").All(c => Uri.IsHexDigit(c));
            return id ? "Connector " + s.Substring(0, 6) : s;
        }
        // "mcp__server__tool" → "server · tool"; other tools as they are.
        public static string ToolName(string name)
        {
            if (String.IsNullOrEmpty(name) || !name.StartsWith("mcp__", StringComparison.Ordinal)) return name ?? "";
            string rest = name.Substring(5);
            int cut = rest.IndexOf("__", StringComparison.Ordinal);
            if (cut <= 0 || cut + 2 >= rest.Length) return name;
            return ServerName(rest.Substring(0, cut)) + " · " + rest.Substring(cut + 2);
        }
        public static string Duration(double ms)
        {
            if (Double.IsNaN(ms)) return "—";
            if (ms < 1000) return Math.Round(ms) + " ms";
            if (ms < 60000) return (ms / 1000).ToString(ms < 10000 ? "0.0" : "0", CultureInfo.InvariantCulture) + " s";
            return Math.Round(ms / 60000) + " min";
        }
        // "↑ 12% on the previous 7 days", "↓ 3%", "new", or why there's no comparison.
        public string Change(double now, double before)
        {
            if (!HasPrevious || before < 0) return Range == All ? "everything kept" : "no earlier days to compare";
            if (!EnoughBefore) return "too little before";
            if (before == 0) return now > 0 ? "new this period" : "none before either";
            double change = (now - before) / before;
            if (Math.Abs(change) < 0.005) return "same as before";
            return (change > 0 ? "↑ " : "↓ ") + Percent(Math.Abs(change)) + " on before";
        }
        // A change is shown only when the period before has enough to compare with: 3 active days and 50 replies.
        public const int EnoughDays = 3, EnoughReplies = 50;
        public bool EnoughBefore { get { return HasPrevious && PrevReplies >= EnoughReplies && (PrevActiveDays < 0 || PrevActiveDays >= EnoughDays); } }
        public string ComparedLine()
        {
            string span = Start.ToString("d MMM", CultureInfo.InvariantCulture) + " to " + End.ToString("d MMM", CultureInfo.InvariantCulture);
            if (Range == All) return span + " · everything kept, so nothing to compare with";
            return span + " · compared with the previous " + (Range == Week ? "7" : "30") + " days" + (!HasPrevious ? " (none kept)" : EnoughBefore ? "" : " (too little before to compare: " + (PrevActiveDays >= 0 ? PrevActiveDays + " active day" + (PrevActiveDays == 1 ? "" : "s") + ", " : "") + Count(PrevReplies) + " replies)");
        }
        public string FootLine()
        {
            string logs = FromLogs > 0 ? FromLogs + " day" + (FromLogs == 1 ? "" : "s") + " rebuilt from Claude Code's logs. " : "";
            string note = Note.Length > 0 ? Note.TrimEnd() + (Note.TrimEnd().EndsWith(".") ? " " : ". ") : "";
            return note + logs + "≈ $ at API prices; ≈ tool times from the gap between a call and its result. Numbers and names only.";
        }
        public List<InsightsFigure> TotalTiles()
        {
            return new List<InsightsFigure> {
                new InsightsFigure { Name = "Tokens", Value = ClaudeUsage.Format(Tokens), Change = Change(Tokens, HasPrevious ? PrevTokens : -1) },
                TodayReplies > 0
                    ? new InsightsFigure { Name = "Replies", Value = Count(Replies - TodayReplies), Change = "+" + Count(TodayReplies) + " today, still being read" }
                    : new InsightsFigure { Name = "Replies", Value = Count(Replies), Change = Change(Replies, HasPrevious ? PrevReplies : -1) },
                new InsightsFigure { Name = "≈ $ at API prices", Value = "≈ " + Cost.ToString("0.00", CultureInfo.InvariantCulture), Change = Change(Cost, HasPrevious ? PrevCost : -1) },
                new InsightsFigure { Name = "Sessions", Value = Count(Sessions), Change = Change(Sessions, HasPrevious ? PrevSessions : -1) },
                new InsightsFigure { Name = "Models", Value = Count(Models.Count), Change = Models.Count == 0 ? "" : "most: " + ModelName(Models[0].Name) } };
        }
        public List<string> EffortLevels()
        {
            string[] order = { "low", "medium", "high", "xhigh", "max" };
            var seen = Effort.SelectMany(e => e.Levels.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return seen.OrderBy(l => { int i = Array.IndexOf(order, l.ToLowerInvariant()); return i < 0 ? 99 : i; }).ThenBy(l => l, StringComparer.OrdinalIgnoreCase).ToList();
        }
        public List<InsightsFigure> ThinkingFigures()
        {
            double share = DetailReplies > 0 ? (double)ThinkingReplies / DetailReplies : Double.NaN;
            return new List<InsightsFigure> {
                new InsightsFigure { Name = "Replies with thinking", Value = Percent(share), Change = Double.IsNaN(PrevThinkingShare) || !HasPrevious ? "" : !EnoughBefore ? "too little before" : "was " + Percent(PrevThinkingShare) },
                new InsightsFigure { Name = "Thinking, ≈ median", Value = Double.IsNaN(ThinkingMedianMs) ? "—" : "≈ " + Duration(ThinkingMedianMs), Change = Double.IsNaN(ThinkingMedianMs) ? "no thinking times logged" : "" },
                new InsightsFigure { Name = "Thinking, longest", Value = Double.IsNaN(ThinkingLongestMs) ? "—" : Duration(ThinkingLongestMs) } };
        }
        public List<InsightsFigure> HealthFigures()
        {
            return new List<InsightsFigure> {
                new InsightsFigure { Name = "API errors", Value = Count(ApiErrors), Change = Change(ApiErrors, HasPrevious ? PrevApiErrors : -1), Warns = EnoughBefore && ApiErrors > 0 && PrevApiErrors >= 0 && ApiErrors > 2 * Math.Max(1, PrevApiErrors) },
                new InsightsFigure { Name = "Retries", Value = Count(Retries), Change = Change(Retries, HasPrevious ? PrevRetries : -1) },
                new InsightsFigure { Name = "Compactions", Value = Count(Compactions), Change = Change(Compactions, HasPrevious ? PrevCompactions : -1) } };
        }
        // The tools for one model ("" for all), most calls first, ties by name.
        public List<InsightsTool> ToolsFor(string model)
        {
            if (String.IsNullOrEmpty(model)) return Tools.Where(t => t.Calls > 0).OrderByDescending(t => t.Calls).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
            return Tools.Select(t => { long n; t.ByModel.TryGetValue(model, out n); long e; t.ErrorsByModel.TryGetValue(model, out e); double ms; if (!t.MedianByModel.TryGetValue(model, out ms)) ms = Double.NaN;
                    return new InsightsTool { Name = t.Name, Calls = n, Errors = e, MedianMs = ms }; })
                .Where(t => t.Calls > 0).OrderByDescending(t => t.Calls).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        public string ProjectsLine()
        {
            if (Projects.Count == 0) return "";
            var most = Projects[0]; var least = Projects[Projects.Count - 1];
            return "Most active: " + most.Name + " (" + ClaudeUsage.Format(most.Tokens) + ")" + (Projects.Count > 1 ? " · least active: " + least.Name + " (" + ClaudeUsage.Format(least.Tokens) + ")" : "");
        }
        public string PerDayWords() { return "Tokens per day by model, " + PerDay.Count + " days; busiest " + (PerDay.Count == 0 ? "none" : PerDay.OrderByDescending(d => d.Total).First().Date.ToString("ddd d MMM", CultureInfo.InvariantCulture)) + "."; }
        public string HeatWords()
        {
            long most = -1; int day = 0, hour = 0;
            for (int d = 0; d < 7; d++) for (int h = 0; h < 24; h++) if (Heat[d, h] > most) { most = Heat[d, h]; day = d; hour = h; }
            string[] days = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
            return most <= 0 ? "No tokens by hour in this range." : "Tokens by hour and weekday; busiest " + days[day] + " from " + HistoryModel.HourName(hour, false) + ".";
        }
        public string SessionWords() { return "Sessions per day: " + Sessions + " in all, most " + (PerDay.Count == 0 ? 0 : PerDay.Max(d => d.Sessions)) + " in a day."; }

        // ---- From the engine's ranges ----

        public static InsightsModel From(ClaudeView view, string range)
        {
            if (view.Insights == null) return new InsightsModel { Range = range, Missing = "Insights are worked out after Claude Code's logs are scanned. They show once History has day files (Settings → Claude → History)." };
            var ins = view.Insights.Pick(range);
            if (ins == null) return new InsightsModel { Range = range, Missing = "This range hasn't been worked out yet." };
            return From(ins);
        }

        public static InsightsModel SampleFor(string range, DateTime now)
        {
            var m = From(ClaudeInsightsSet.SampleSet(now).Pick(range));
            m.Sample = true;
            return m;
        }

        public static InsightsModel From(ClaudeInsights ins)
        {
            var d = ins.Detail ?? new ClaudeDayDetail(); var prior = ins.PriorDetail ?? new ClaudeDayDetail();
            var m = new InsightsModel { Sample = ins.Sample, Missing = ins.Missing ?? "", Range = ins.Label, Start = ins.Start, End = ins.End, HasPrevious = ins.PriorStart.HasValue,
                FromLogs = ins.DaysFromLogs, DaysWithFiles = ins.DaysWithFiles, DaysWithDetail = ins.DaysWithDetail,
                Tokens = ins.Tokens, Replies = ins.Replies, Cost = ins.Cost, PrevTokens = ins.PriorTokens, PrevReplies = ins.PriorReplies, PrevCost = ins.PriorCost,
                Sessions = ins.Sessions, PrevSessions = ins.PriorSessions, PrevActiveDays = ins.PriorActiveDays, ThinkingReplies = d.ThinkingReplies, DetailReplies = d.Replies,
                Note = ins.Note ?? "", TodayPending = ins.TodayPending };
            if (ins.TodayPending)
            {
                var today = ins.Series.LastOrDefault(day => day.Date.Date == ins.End.Date);
                if (today != null && today.Detail == null) m.TodayReplies = today.Replies;
            }
            if (m.Missing.Length > 0) return m;

            foreach (var r in ins.Models)
            {
                ClaudeModelUse use; d.Models.TryGetValue(r.Name, out use);
                m.Models.Add(new InsightsModelRow { Name = r.Name, Tokens = r.Value, Replies = r.Replies, Cost = r.Cost, ThinkingTokens = r.Thinking, Share = r.Share, Unpriced = use == null ? 0 : use.Unpriced });
            }
            foreach (var day in ins.Series)
            {
                var row = new InsightsDay { Date = day.Date, Total = day.Tokens, Sessions = day.Detail == null ? 0 : day.Detail.Sessions };
                if (day.Detail != null) foreach (var p in day.Detail.Models) row.ByModel[p.Key] = p.Value.Tokens;
                m.PerDay.Add(row);
            }
            foreach (var r in ins.Models)
            {
                Dictionary<string, int> levels;
                if (d.Effort.TryGetValue(r.Name, out levels) && levels.Values.Sum() > 0) m.Effort.Add(new InsightsEffort { Model = r.Name, Levels = new Dictionary<string, int>(levels) });
            }
            if (m.Effort.Count == 0) m.EffortWhy = ins.DaysWithDetail == 0 ? "Effort levels come from the logs' detail, which this range's day files don't have yet." : "Claude Code logged no effort levels in this range.";
            var thinking = ClaudeDayDetail.Median(d.ThinkingBuckets);
            m.ThinkingMedianMs = thinking.HasValue ? thinking.Value : Double.NaN;
            m.ThinkingLongestMs = d.LongestThinkingMs > 0 ? d.LongestThinkingMs : Double.NaN;
            m.PrevThinkingShare = prior.Replies > 0 ? (double)prior.ThinkingReplies / prior.Replies : Double.NaN;
            foreach (var r in ins.Tools)
            {
                var t = new InsightsTool { Name = r.Name, Calls = r.Value, Errors = r.Errors, MedianMs = r.MedianMs.HasValue ? r.MedianMs.Value : Double.NaN };
                foreach (var p in r.Split) t.ByModel[p.Key] = p.Value;
                foreach (var p in r.ErrorSplit) t.ErrorsByModel[p.Key] = p.Value;
                foreach (var p in r.MedianByModel) t.MedianByModel[p.Key] = p.Value.HasValue ? p.Value.Value : Double.NaN;
                m.Tools.Add(t);
            }
            m.Mcp = ins.Mcp.Where(r => r.Value > 0).Select(r => new KeyValuePair<string, long>(r.Name, r.Value)).ToList();
            m.Skills = ins.Skills.Concat(ins.Plugins).Where(r => r.Value > 0).OrderByDescending(r => r.Value).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .Select(r => new KeyValuePair<string, long>(r.Name, r.Value)).ToList();
            foreach (var r in ins.Projects)
                m.Projects.Add(new InsightsProject { Name = r.Name, Folder = r.Folder, Tokens = r.Value, Replies = r.Replies, Cost = r.Cost, Sessions = r.Sessions, Added = r.LinesAdded, Removed = r.LinesRemoved });
            m.Heat = ins.Heat ?? new long[7, 24];
            m.ApiErrors = d.ApiErrors; m.Retries = d.Retries; m.Compactions = d.Compactions;
            if (m.HasPrevious) { m.PrevApiErrors = prior.ApiErrors; m.PrevRetries = prior.Retries; m.PrevCompactions = prior.Compactions; }
            m.Permissions = new List<KeyValuePair<string, long>> {
                new KeyValuePair<string, long>("allowed by rule", d.PermRule), new KeyValuePair<string, long>("allowed by a hook", d.PermHook),
                new KeyValuePair<string, long>("asked you", d.PermAsked), new KeyValuePair<string, long>("denied", d.PermDenied) };
            int toolUse, endTurn; d.Stops.TryGetValue("tool_use", out toolUse); d.Stops.TryGetValue("end_turn", out endTurn);
            int other = d.Stops.Where(p => p.Key != "tool_use" && p.Key != "end_turn").Sum(p => p.Value);
            m.Stops = new List<KeyValuePair<string, long>> { new KeyValuePair<string, long>("tool use", toolUse), new KeyValuePair<string, long>("end of turn", endTurn), new KeyValuePair<string, long>("other", other) };
            return m;
        }
    }
}

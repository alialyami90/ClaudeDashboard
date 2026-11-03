using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ClaudeDashboard
{
    // One source's row on the Sources view: its name and where it reads from, its state (ClaudeHealth's levels), how
    // fresh it is, what it gives, and the one thing to do about it. Action is a name the host's Act understands.
    public sealed class SourceRow
    {
        public string Name = "", Where = "", State = "", Fresh = "", Gives = "", Tip = "", Action = "", Button = "";
        public ClaudeHealthLevel Level;
        public string Glyph { get { return ClaudeHealth.Glyph(Level); } }
    }

    public static class ClaudeSources
    {
        public const string Hooks = "Hooks", Logs = "Logs", Telemetry = "Telemetry", Plan = "Plan meters", App = "Claude app threads";
        // The actions Sources asks the host for (decision 030). The view never changes Claude Code's settings itself.
        public const string Connect = "Connect", Disconnect = "Disconnect", Reconnect = "Reconnect", OpenLogs = "OpenLogs",
            UpdateConnection = "UpdateConnection", CheckPlan = "CheckPlan", SignIn = "SignIn", ShowArchived = "ShowArchived", HideArchived = "HideArchived";

        public static string Word(ClaudeHealthLevel level)
        {
            switch (level)
            {
                case ClaudeHealthLevel.Ok: return "Live";
                case ClaudeHealthLevel.Waiting: return "Waiting";
                case ClaudeHealthLevel.Old: return "Old";
                case ClaudeHealthLevel.Off: return "Off";
                default: return "Failed";
            }
        }
        public static string Brush(ClaudeHealthLevel level)
        {
            return level == ClaudeHealthLevel.Ok ? "Accent" : level == ClaudeHealthLevel.Old || level == ClaudeHealthLevel.Failed ? "Warning" : "Muted";
        }

        private static string Ago(DateTime at, DateTime now)
        {
            var age = now - at;
            if (age < TimeSpan.Zero) age = TimeSpan.Zero;
            if (age < TimeSpan.FromMinutes(1)) return Math.Max(0, (int)age.TotalSeconds) + " s ago";
            return ClaudeText.Duration(age) + " ago";
        }

        // The five rows, in the order the prototype lists them.
        public static List<SourceRow> Of(ClaudeView view, DateTime now, bool use24, ClaudeLinks links)
        {
            if (links == null) links = view == null ? null : new ClaudeLinks { TelemetrySetUp = true, TelemetryProblem = view.TelemetryWarning ?? "" };
            var health = ClaudeHealth.Of(view, now, use24, links);
            var threads = view == null ? new List<ClaudeSession>() : view.Threads.ToList();
            var rows = new List<SourceRow>();

            // Hooks.
            var h = health[0];
            int writing = threads.Count(t => t.Running && t.LastSign > DateTime.MinValue);
            var hooks = Row(Hooks, h, "ClaudeHook.exe" + (writing > 0 ? " · " + writing + (writing == 1 ? " session" : " sessions") + " writing" : ""),
                "each session's state, what it asks, its window, tasks, helpers and context");
            var last = threads.Where(t => t.LastSign > DateTime.MinValue).Select(t => t.LastSign).DefaultIfEmpty(DateTime.MinValue).Max();
            hooks.Fresh = view == null ? "" : last > DateTime.MinValue ? "last event " + Ago(last, now) : view.Connected ? "no event yet" : "not connected";
            if (view != null && view.Connected && links != null && links.HooksElsewhere != null) { hooks.Action = Reconnect; hooks.Button = "Reconnect"; }
            else if (view != null && view.Connected) { hooks.Action = Disconnect; hooks.Button = "Disconnect"; }
            else { hooks.Action = Connect; hooks.Button = "Connect"; }
            rows.Add(hooks);

            // Logs.
            var l = health[1];
            var logs = Row(Logs, l, ClaudeLogsPlace(), "tokens per reply, models, tool names, lines changed; read-only");
            logs.Fresh = view == null ? "" : view.LogsFailed ? "couldn't be read" : view.LogsEmpty ? "no logs yet" : view.LogsReadAt > DateTime.MinValue ? "read " + Ago(view.LogsReadAt, now) : view.LogsRead ? "read " + Ago(view.MadeAt, now) : "reading…";
            logs.Action = OpenLogs; logs.Button = "Open folder";
            rows.Add(logs);

            // Telemetry.
            var t0 = health[2];
            int port = view == null ? 0 : view.TelemetryPort;
            var telemetry = Row(Telemetry, t0, (port > 0 ? "127.0.0.1:" + port : "127.0.0.1, not listening") + " · loopback only", "costs, tool run times and failures, API errors, asks and denials, commits");
            telemetry.Fresh = view == null ? "" : view.TelemetryAt > DateTime.MinValue ? "last event " + Ago(view.TelemetryAt, now) : view.Facts.Telemetry ? "events today" : t0.Level == ClaudeHealthLevel.Ok || t0.Level == ClaudeHealthLevel.Waiting ? "nothing yet" : "not arriving";
            telemetry.Action = UpdateConnection; telemetry.Button = t0.Level == ClaudeHealthLevel.Failed ? "Change port" : t0.Level == ClaudeHealthLevel.Off ? "Turn on" : "Change port";
            if (view != null && !view.Connected) { telemetry.Action = Connect; telemetry.Button = "Connect"; }
            rows.Add(telemetry);

            // Plan meters.
            var p = health[3];
            var plan = Row(Plan, p, "Sign in with Claude · api.anthropic.com", "the 5-hour and weekly limits, per-model windows and extra usage");
            var limits = view == null ? null : view.Limits;
            if (limits != null && limits.Known)
            {
                plan.State = (p.Level == ClaudeHealthLevel.Ok ? "As of " : "Old, as of ") + ClaudeText.Clock(limits.UpdatedAt, use24);
                plan.Fresh = limits.Source == "online" ? "checked " + Ago(limits.UpdatedAt, now) : "from the status line";
            }
            else plan.Fresh = view == null ? "" : "no numbers yet";
            if (view != null && view.PlanNextCheck.HasValue) plan.Fresh += (plan.Fresh.Length > 0 ? " · " : "") + "next check " + ClaudeText.Clock(view.PlanNextCheck.Value > now ? view.PlanNextCheck.Value : now, use24);
            if (view != null && (view.Plan == ClaudePlanState.Expired || view.Plan == ClaudePlanState.SignedOut)) { plan.Action = SignIn; plan.Button = "Sign in"; }
            else { plan.Action = CheckPlan; plan.Button = "Check now"; }
            rows.Add(plan);

            // The Claude app's thread records: names, folders, archived flags. Read with the snapshot, so as fresh as it.
            int fromApp = view == null ? 0 : view.AppThreadCount;
            int archived = threads.Count(t => t.Archived);
            var app = new SourceRow { Name = App, Where = "the Claude app's thread records", Gives = "thread names, folders, archived flags and last activity" };
            if (view == null) { app.Level = ClaudeHealthLevel.Waiting; app.State = "Waiting"; app.Tip = "Reading Claude Code…"; }
            else if (fromApp > 0) { app.Level = ClaudeHealthLevel.Ok; app.State = "Live"; app.Fresh = "read " + Ago(view.AppThreadsReadAt > DateTime.MinValue ? view.AppThreadsReadAt : view.MadeAt, now) + " · " + fromApp + (fromApp == 1 ? " thread" : " threads"); app.Tip = "Thread names and archived flags come from the Claude app's own records, read-only."; }
            else { app.Level = ClaudeHealthLevel.Waiting; app.State = "None found"; app.Fresh = view.AppThreadsReadAt > DateTime.MinValue ? "checked " + Ago(view.AppThreadsReadAt, now) : "not read yet"; app.Tip = "No Claude app threads on this PC yet, or the app isn't installed. Sessions still show from Claude Code's files."; }
            bool showing = view != null && view.ShowArchived;
            app.Action = showing ? HideArchived : ShowArchived; app.Button = showing ? "Hide archived" : "Show archived" + (archived > 0 ? " (" + archived + ")" : "");
            rows.Add(app);
            return rows;
        }

        // v0.59.0, History from the logs: how long Claude Code keeps its logs, how far the past days' backfill has got
        // and the oldest day the logs hold, and the one button (Keep logs a year, or Undo).
        public const string Exempt = "Sessions from the Claude desktop app are already exempt from Claude Code's cleanup.";
        public const string KeepLogs = "KeepLogs", UndoKeepLogs = "UndoKeepLogs";
        public static string LogsKept(ClaudeView view)
        {
            int? days = view == null ? null : view.LogsKeptDays;
            if (!days.HasValue) return "Claude Code keeps its logs " + ClaudeConnector.LogsDefault + " days (Claude Code's default)";
            return "Claude Code keeps its logs " + days.Value + (days.Value == 1 ? " day" : " days") + (view.LogsKeptByUs && days.Value == ClaudeConnector.LogsYear ? " (set by Claude Dashboard)" : "");
        }
        public static string Backfill(ClaudeView view)
        {
            if (view == null || view.Sample) return "";
            var c = System.Globalization.CultureInfo.InvariantCulture;
            string oldest = view.OldestLog.HasValue ? " · logs from " + view.OldestLog.Value.ToString("d MMM yyyy", c) : "";
            if (!view.BackfillOn) return "History is off, so no past days are built from the logs.";
            if (view.BackfillDone) return "Past days built from the logs: " + view.BackfillDays + (view.BackfillDays == 1 ? " day" : " days") + oldest;
            if (view.BackfillTotal == 0) return "Past days from the logs: waiting for the next log scan";
            return "Past days from the logs: reading " + view.BackfillRead + " of " + view.BackfillTotal + (view.BackfillTotal == 1 ? " log" : " logs") + oldest;
        }
        // The button: Keep logs a year, or Undo while the dashboard's 365 days are set; none when a year is already kept.
        public static string[] LogsButton(ClaudeView view)
        {
            if (view != null && view.LogsKeptByUs) return new[] { "Undo", UndoKeepLogs };
            if (view != null && view.LogsKeptDays.HasValue && view.LogsKeptDays.Value >= ClaudeConnector.LogsYear) return new[] { "", "" };
            return new[] { "Keep logs a year", KeepLogs };
        }

        private static SourceRow Row(string name, ClaudeHealthItem item, string where, string gives)
        {
            string state = item.Level == ClaudeHealthLevel.Ok ? "Live" : item.Long.StartsWith(item.Name, StringComparison.Ordinal) && item.Long.Length > item.Name.Length ? item.Long.Substring(item.Name.Length).TrimStart(':', ' ') : item.Long;
            if (state.Length > 0) state = Char.ToUpperInvariant(state[0]) + state.Substring(1);
            return new SourceRow { Name = name, Where = where, Gives = gives, Level = item.Level, State = state, Tip = item.Tip };
        }

        // Where Claude Code's logs are: CLAUDE_CONFIG_DIR's projects, else %USERPROFILE%\.claude\projects. Names only.
        private static string ClaudeLogsPlace()
        {
            string config = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            return String.IsNullOrWhiteSpace(config) ? @"%USERPROFILE%\.claude\projects" : @"CLAUDE_CONFIG_DIR\projects";
        }
    }

    // Sources (decision 030, package E): where every number comes from and how fresh it is, one row per source with
    // its state, freshness, what it gives and one action. The action goes through the host's Act; the view never
    // writes Claude Code's settings.json itself.
    public sealed class SourcesView : IClaudeView
    {
        public string Name { get { return ClaudeViews.Sources; } }
        public bool Visible { get; set; }

        private ClaudeViewContext context = new ClaudeViewContext();
        private Size room;
        private Grid root;
        private TextBlock sample, error, logsKept, backfill;
        private Button logsButton;
        private Border logsTile;
        // For the checks: the History from the logs tile's words and button.
        public string LogsKeptShown { get { return logsKept == null ? "" : logsKept.Text; } }
        public string BackfillShown { get { return backfill == null ? "" : backfill.Text; } }
        public Button LogsButton { get { return logsButton; } }
        private readonly List<Cells> cells = new List<Cells>();
        private ClaudeView last;
        // For the checks: the rows last shown.
        public List<SourceRow> RowsShown { get; private set; }

        private sealed class Cells { public TextBlock Name, Where, Glyph, State, Fresh, Gives; public Button Button; public FrameworkElement Row; }

        private double S(double size) { return context.Scaled(size); }

        // Set by the checks in place of the host's Act (the same path as Settings → Claude's buttons, confirmations
        // included).
        public Func<string, string> Act;
        // The sample's buttons are off: there's nothing behind them to change (the window's Act says the same).
        public const string SampleRefusal = "These are sample numbers, so there's nothing to change.";
        private Func<string, string> Acts { get { return Act ?? context.Act; } }

        public FrameworkElement Build(ClaudeViewContext context, Size room)
        {
            this.context = context ?? new ClaudeViewContext(); this.room = room;
            cells.Clear();
            bool wide = room.Width >= 1000;
            var stack = new StackPanel { Margin = new Thickness(0, 0, 4, 8) };
            var head = CardParts.Columns(CardParts.Star, CardParts.Auto);
            var title = CardParts.Text("Sources", S(18), "Text"); title.FontWeight = FontWeights.SemiBold;
            head.Children.Add(title);
            sample = CardParts.Text("Sample", S(12), "Muted"); sample.FontWeight = FontWeights.SemiBold; sample.VerticalAlignment = VerticalAlignment.Center; sample.Visibility = this.context.Sample ? Visibility.Visible : Visibility.Collapsed;
            head.Children.Add(CardParts.At(sample, 0, 1));
            stack.Children.Add(head);
            var intro = CardParts.Text("Where every number comes from, and how fresh it is. A source that stops shows here and on the Live header at once.", S(13), "Muted");
            intro.TextWrapping = TextWrapping.Wrap; intro.Margin = new Thickness(0, 4, 0, 10);
            stack.Children.Add(intro);
            error = CardParts.Text("", S(13), "Warning"); error.TextWrapping = TextWrapping.Wrap; error.Visibility = Visibility.Collapsed; error.Margin = new Thickness(0, 0, 0, 8);
            stack.Children.Add(error);
            foreach (string name in new[] { ClaudeSources.Hooks, ClaudeSources.Logs, ClaudeSources.Telemetry, ClaudeSources.Plan, ClaudeSources.App })
            {
                var c = new Cells();
                c.Name = CardParts.Text(name, S(15), "Text"); c.Name.FontWeight = FontWeights.SemiBold; c.Name.TextWrapping = TextWrapping.Wrap;
                c.Where = Small("", "Muted");
                c.Glyph = CardParts.Text("", S(13), "Muted"); c.Glyph.FontFamily = new FontFamily("Segoe MDL2 Assets"); c.Glyph.Margin = new Thickness(0, 2, 6, 0); c.Glyph.VerticalAlignment = VerticalAlignment.Top;
                c.State = CardParts.Text("", S(14), "Text"); c.State.TextWrapping = TextWrapping.Wrap;
                c.Fresh = Small("", "Muted");
                c.Gives = Small("", "Muted");
                c.Button = new Button { MinWidth = S(120), Padding = new Thickness(S(10), S(4), S(10), S(4)), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, FontSize = S(13), Template = ButtonLook() };
                var button = c.Button;
                button.Click += (s, e) => Run(button.Tag as string);

                var namePart = new StackPanel(); namePart.Children.Add(c.Name); namePart.Children.Add(c.Where);
                var statePart = new StackPanel();
                var stateLine = CardParts.Columns(CardParts.Auto, CardParts.Star); stateLine.Children.Add(c.Glyph); stateLine.Children.Add(CardParts.At(c.State, 0, 1));
                statePart.Children.Add(stateLine); statePart.Children.Add(c.Fresh);
                Grid row;
                if (wide)
                {
                    row = CardParts.Columns(new GridLength(S(220)), new GridLength(S(170)), CardParts.Star, CardParts.Auto);
                    namePart.Margin = new Thickness(0, 0, 12, 0); statePart.Margin = new Thickness(0, 0, 12, 0); c.Gives.Margin = new Thickness(0, 0, 12, 0); c.Gives.VerticalAlignment = VerticalAlignment.Center;
                    row.Children.Add(namePart); row.Children.Add(CardParts.At(statePart, 0, 1)); row.Children.Add(CardParts.At(c.Gives, 0, 2)); row.Children.Add(CardParts.At(c.Button, 0, 3));
                }
                else
                {
                    // Narrow: name, where and what it gives on the left; state and freshness, then the button, on the right.
                    row = CardParts.Columns(CardParts.Star, new GridLength(Math.Min(S(170), Math.Max(120, room.Width * 0.32))));
                    c.Gives.Margin = new Thickness(0, 4, 0, 0); namePart.Children.Add(c.Gives); namePart.Margin = new Thickness(0, 0, 12, 0);
                    c.Button.Margin = new Thickness(0, 8, 0, 0); c.Button.HorizontalAlignment = HorizontalAlignment.Stretch; c.Button.MinWidth = 0;
                    statePart.Children.Add(c.Button);
                    row.Children.Add(namePart); row.Children.Add(CardParts.At(statePart, 0, 1));
                }
                var tile = CardParts.Tile(row, 12, new Thickness(16, 12, 16, 12)); tile.Margin = new Thickness(0, 0, 10, 10);
                c.Row = tile;
                cells.Add(c);
                stack.Children.Add(tile);
            }
            // v0.59.0: History from the logs, and Keep logs a year.
            var logsPart = new StackPanel();
            var logsTitle = CardParts.Text("History from the logs", S(15), "Text"); logsTitle.FontWeight = FontWeights.SemiBold; logsPart.Children.Add(logsTitle);
            logsKept = CardParts.Text("", S(14), "Text"); logsKept.TextWrapping = TextWrapping.Wrap; logsKept.Margin = new Thickness(0, 4, 0, 0); logsPart.Children.Add(logsKept);
            backfill = Small("", "Muted"); logsPart.Children.Add(backfill);
            logsPart.Children.Add(Small(ClaudeSources.Exempt, "Muted"));
            logsButton = new Button { MinWidth = S(120), Padding = new Thickness(S(10), S(4), S(10), S(4)), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, FontSize = S(13), Template = ButtonLook() };
            var keep = logsButton; keep.Click += (s, e) => Run(keep.Tag as string);
            var logsRow = CardParts.Columns(CardParts.Star, CardParts.Auto); logsPart.Margin = new Thickness(0, 0, 12, 0);
            logsRow.Children.Add(logsPart); logsRow.Children.Add(CardParts.At(logsButton, 0, 1));
            logsTile = CardParts.Tile(logsRow, 12, new Thickness(16, 12, 16, 12)); logsTile.Margin = new Thickness(0, 0, 10, 10);
            AutomationProperties.SetName(logsTile, "History from the logs");
            stack.Children.Add(logsTile);
            var note = Small("The dashboard reads these and changes Claude Code's settings only from a button: Connect, Disconnect and Update connection in Settings → Claude, and Keep logs a year here, each with dated backups.", "Muted");
            stack.Children.Add(note);
            var scroll = ClaudeReliability.Page(stack, "Sources");
            root = new Grid(); root.Children.Add(scroll);
            return root;
        }

        // The app's button (ShellStyles), except that an off button keeps its words at Muted's contrast with a Muted
        // outline instead of fading the whole button to 35%, which was too faint on the Claude palette.
        private static ControlTemplate look;
        public static ControlTemplate ButtonLook()
        {
            if (look != null) return look;
            var chrome = new FrameworkElementFactory(typeof(Border), "Chrome");
            chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            chrome.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            chrome.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            chrome.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            chrome.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var grid = new FrameworkElementFactory(typeof(Grid));
            var hover = new FrameworkElementFactory(typeof(Border), "Hover");
            hover.SetResourceReference(Border.BackgroundProperty, "Text");
            hover.SetValue(UIElement.OpacityProperty, 0.0); hover.SetValue(Border.CornerRadiusProperty, new CornerRadius(6)); hover.SetValue(UIElement.IsHitTestVisibleProperty, false);
            var words = new FrameworkElementFactory(typeof(ContentPresenter), "Words");
            words.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); words.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            grid.AppendChild(hover); grid.AppendChild(words); chrome.AppendChild(grid);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = chrome };
            var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true }; over.Setters.Add(new Setter(UIElement.OpacityProperty, 0.06, "Hover"));
            var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true }; pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.13, "Hover"));
            var focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focused.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Text"), "Chrome")); focused.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "Chrome"));
            var off = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            off.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.Transparent, "Chrome"));
            off.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Muted"), "Chrome"));
            off.Setters.Add(new Setter(TextElement.ForegroundProperty, new DynamicResourceExtension("Muted"), "Words"));
            template.Triggers.Add(over); template.Triggers.Add(pressed); template.Triggers.Add(focused); template.Triggers.Add(off);
            template.Seal();
            look = template;
            return look;
        }

        private TextBlock Small(string text, string brush)
        {
            var block = CardParts.Text(text, S(13), brush); block.TextWrapping = TextWrapping.Wrap;
            return block;
        }

        private void Run(string action)
        {
            var act = Acts;
            if (act == null || String.IsNullOrEmpty(action)) return;
            string problem = null;
            try { problem = act(action); }
            catch (Exception ex) { problem = ex.Message; }
            error.Text = problem ?? ""; error.Visibility = String.IsNullOrEmpty(problem) ? Visibility.Collapsed : Visibility.Visible;
            if (last != null) Refresh(last, DateTime.UtcNow);
        }

        public void Refresh(ClaudeView snapshot, DateTime now)
        {
            if (root == null) return;
            last = snapshot;
            sample.Visibility = context.Sample || (snapshot != null && snapshot.Sample) ? Visibility.Visible : Visibility.Collapsed;
            RowsShown = ClaudeSources.Of(snapshot, now, context.Use24Hour, null);
            for (int i = 0; i < cells.Count && i < RowsShown.Count; i++)
            {
                var r = RowsShown[i]; var c = cells[i];
                c.Where.Text = r.Where; c.Glyph.Text = r.Glyph; c.State.Text = r.State; c.Fresh.Text = r.Fresh; c.Gives.Text = r.Gives;
                string brush = ClaudeSources.Brush(r.Level);
                c.Glyph.SetResourceReference(TextBlock.ForegroundProperty, brush);
                c.State.SetResourceReference(TextBlock.ForegroundProperty, r.Level == ClaudeHealthLevel.Ok ? "Text" : brush == "Muted" ? "Text" : brush);
                c.Button.Content = r.Button; c.Button.Tag = r.Action;
                bool sampled = context.Sample || (snapshot != null && snapshot.Sample);
                c.Button.IsEnabled = !sampled;
                ToolTipService.SetShowOnDisabled(c.Button, true);
                c.Button.ToolTip = sampled ? SampleRefusal : null;
                AutomationProperties.SetName(c.Button, r.Button + ", " + r.Name);
                AutomationProperties.SetName(c.Row, r.Name + ": " + r.State + (r.Fresh.Length > 0 ? ", " + r.Fresh : "") + ". Gives " + r.Gives + ".");
                AutomationProperties.SetHelpText(c.Row, r.Tip);
                c.Row.ToolTip = r.Tip;
            }
            bool sample0 = context.Sample || (snapshot != null && snapshot.Sample);
            logsKept.Text = ClaudeSources.LogsKept(snapshot);
            backfill.Text = ClaudeSources.Backfill(snapshot);
            backfill.Visibility = backfill.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            var b = ClaudeSources.LogsButton(snapshot);
            logsButton.Content = b[0]; logsButton.Tag = b[1];
            logsButton.Visibility = b[0].Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            logsButton.IsEnabled = !sample0;
            ToolTipService.SetShowOnDisabled(logsButton, true);
            logsButton.ToolTip = sample0 ? SampleRefusal : b[1] == ClaudeSources.KeepLogs ? "Sets cleanupPeriodDays to 365 in Claude Code's settings.json, with a dated backup. Undo puts back what was there." : "Puts back how long Claude Code kept its logs before.";
            AutomationProperties.SetName(logsButton, b[0] + ", Claude Code's logs");
            AutomationProperties.SetHelpText(logsTile, logsKept.Text + ". " + backfill.Text + " " + ClaudeSources.Exempt);
        }
    }
}

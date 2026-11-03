using System;
using System.Collections.Generic;

namespace ClaudeDashboard
{
    // The source-health line under the dashboard (observability critique, issue 3): where every number comes from and
    // whether that source is working. Four sources, always in this order: Claude Code's hooks (live states), its logs
    // (tokens, replies, lines), its telemetry (asked, denied, errors, costs) and the plan check (the meters). Each is
    // ok, still waiting, old, off or failed, and says why in words.
    public enum ClaudeHealthLevel { Ok, Waiting, Old, Off, Failed }

    public sealed class ClaudeHealthItem
    {
        public string Name = "", Long = "", Short = "", Tip = "";
        public ClaudeHealthLevel Level;
        // Old and Failed are warnings (Amber); Waiting and Off are calm, since nothing is wrong yet or the user chose it.
        public bool Warns { get { return Level == ClaudeHealthLevel.Old || Level == ClaudeHealthLevel.Failed; } }
        // Segoe MDL2: CheckMark, Sync, Recent, Cancel, Warning. A different glyph for each level, so the state never
        // depends on colour alone.
        public string Glyph { get { return ClaudeHealth.Glyph(Level); } }
        public string Spoken { get { return Long + ". " + Tip; } }
    }

    // What the dashboard knows about the connection beyond the view, read with it every 10 s (decision 028): whether
    // settings.json sends telemetry here, the bridge Claude Code's hooks run when Widget Screen took them back, and
    // why telemetry doesn't arrive ("Telemetry goes to Widget Screen while it's open", "Telemetry off: port 43832 in
    // use"). Null in Of: taken from the view (the fixture, and the checks).
    public sealed class ClaudeLinks
    {
        public bool TelemetrySetUp;
        public string HooksElsewhere;
        public string TelemetryProblem = "";
        public bool WidgetScreenHasTelemetry;
        public static ClaudeLinks From(ClaudeView view)
        {
            // The hooks' Widget Screen warning belongs on the Hooks row, not Telemetry's (v0.59.0).
            return new ClaudeLinks { TelemetrySetUp = view == null || view.Sample, TelemetryProblem = view == null ? "" : view.TelemetryProblem ?? view.TelemetryWarning ?? "", HooksElsewhere = view == null ? null : view.HooksElsewhere };
        }
    }

    public static class ClaudeHealth
    {
        public const string Hooks = "Hooks", Logs = "Logs", Telemetry = "Telemetry", Plan = "Plan";

        public static string Glyph(ClaudeHealthLevel level)
        {
            switch (level)
            {
                case ClaudeHealthLevel.Ok: return "";
                case ClaudeHealthLevel.Waiting: return "";
                case ClaudeHealthLevel.Old: return "";
                case ClaudeHealthLevel.Off: return "";
                default: return "";
            }
        }

        // The four sources for a view.
        public static List<ClaudeHealthItem> Of(ClaudeView view, DateTime now, bool use24, ClaudeLinks links)
        {
            links = links ?? ClaudeLinks.From(view);
            if (view == null)
                return new List<ClaudeHealthItem> { Waiting(Hooks, "Reading Claude Code…"), Waiting(Logs, "Reading Claude Code…"), Waiting(Telemetry, "Reading Claude Code…"), Waiting(Plan, "Reading Claude Code…") };
            return new List<ClaudeHealthItem> { HooksItem(view, links), LogsItem(view), TelemetryItem(view, links), PlanItem(view, now, use24) };
        }

        private static ClaudeHealthItem Item(string name, ClaudeHealthLevel level, string longText, string shortText, string tip)
        {
            return new ClaudeHealthItem { Name = name, Level = level, Long = longText, Short = shortText, Tip = tip };
        }
        private static ClaudeHealthItem Waiting(string name, string tip) { return Item(name, ClaudeHealthLevel.Waiting, name, name, tip); }

        private static ClaudeHealthItem HooksItem(ClaudeView view, ClaudeLinks links)
        {
            // Widget Screen's Update connection pointed the hooks at its own bridge (decision 028): live states reach
            // it, not this app, until Reconnect.
            if (view.Connected && links.HooksElsewhere != null)
                return Item(Hooks, ClaudeHealthLevel.Failed, "Hooks with Widget Screen · Reconnect", "Hooks with Widget Screen", "Claude Code's hooks run Widget Screen's bridge, so live states and Needs you reach Widget Screen only. Sessions still show here from Claude Code's own files. Settings → Claude → Reconnect brings them back; Claude Dashboard's bridge keeps Widget Screen up to date too.");
            if (!view.Connected)
                return Item(Hooks, ClaudeHealthLevel.Off, "Hooks off: not connected", "Hooks off", "Claude Code isn't connected, so states come only from its session list and logs and Needs you can't show. Settings → Claude → Connect adds the hooks.");
            return Item(Hooks, ClaudeHealthLevel.Ok, Hooks, Hooks, "Claude Code's hooks report each session's state as it changes.");
        }

        private static ClaudeHealthItem LogsItem(ClaudeView view)
        {
            if (view.LogsFailed)
                return Item(Logs, ClaudeHealthLevel.Failed, "Logs unreadable", "Logs unreadable", ClaudeText.NoLogs + " Tokens, replies and lines show — until they can be read.");
            if (view.LogsEmpty)
                return Item(Logs, ClaudeHealthLevel.Waiting, "Logs: none yet", Logs, "Claude Code hasn't written a session log on this PC yet. Tokens, replies and lines start counting with its first session.");
            if (!view.LogsRead)
                return Item(Logs, ClaudeHealthLevel.Waiting, "Logs counting…", "Logs counting", "Claude Code's logs are still being read, so numbers marked ≈ will grow.");
            return Item(Logs, ClaudeHealthLevel.Ok, Logs, Logs, "Tokens, replies, tool calls and lines come from Claude Code's logs.");
        }

        private static ClaudeHealthItem TelemetryItem(ClaudeView view, ClaudeLinks links)
        {
            const string port = "Claude Dashboard couldn't listen on the port Claude Code sends telemetry to. Settings → Claude → Update connection picks a free one.";
            string problem = links.TelemetryProblem ?? "";
            // Widget Screen open with the port: not off and not an error, since it hands the port back within about
            // 10 s of closing (decision 028).
            if (problem.Length > 0 && (links.WidgetScreenHasTelemetry || problem.StartsWith("Telemetry goes to Widget Screen", StringComparison.Ordinal)))
                return Item(Telemetry, ClaudeHealthLevel.Waiting, "Telemetry with Widget Screen", "Telemetry with Widget Screen", "Telemetry goes to Widget Screen while it's open, and comes here within about 10 seconds of closing it.");
            if (problem.Length > 0) return Item(Telemetry, ClaudeHealthLevel.Failed, problem, "Telemetry off", port);
            if (!view.Connected) return Item(Telemetry, ClaudeHealthLevel.Off, "Telemetry off: not connected", "Telemetry off", "Connect Claude Code in Settings → Claude to get its telemetry.");
            if (!links.TelemetrySetUp && !view.Sample) return Item(Telemetry, ClaudeHealthLevel.Off, "Telemetry off: not set up", "Telemetry off", "Claude Code doesn't send telemetry here. Settings → Claude → Update connection turns it on; until then asked, denied and API errors are unknown.");
            if (!view.Facts.Telemetry) return Item(Telemetry, ClaudeHealthLevel.Waiting, "Telemetry: none yet", Telemetry, "Listening. Claude Code sends telemetry while a session runs.");
            return Item(Telemetry, ClaudeHealthLevel.Ok, Telemetry, Telemetry, "Asked, denied, API errors and costs come from Claude Code's telemetry.");
        }

        private static ClaudeHealthItem PlanItem(ClaudeView view, DateTime now, bool use24)
        {
            var limits = view.Limits;
            if (limits != null && limits.Known)
            {
                string at = ClaudeText.Clock(limits.UpdatedAt, use24);
                if (ClaudeText.Old(view, now))
                {
                    TimeSpan age = now - limits.UpdatedAt;
                    string why = view.Plan == ClaudePlanState.Expired ? ClaudeText.SignInEnded : view.Plan == ClaudePlanState.Failed ? "The last plan check didn't go through. Trying again soon." : "No newer plan numbers have come in.";
                    string text = age < TimeSpan.FromMinutes(1) ? "Plan check failed" : "Plan " + ClaudeText.Duration(age) + " old";
                    return Item(Plan, ClaudeHealthLevel.Old, text, age < TimeSpan.FromMinutes(1) ? "Plan old" : text, "The meters show your plan's use as of " + at + ". " + why);
                }
                return Item(Plan, ClaudeHealthLevel.Ok, Plan, Plan, "The meters show your plan's use as of " + at + ", " + (limits.Source == "online" ? "checked online" : "from Claude Code's status line") + ".");
            }
            string tip = ClaudeText.LimitsMissing(view);
            switch (view.Plan)
            {
                case ClaudePlanState.Reading: return Item(Plan, ClaudeHealthLevel.Waiting, "Plan reading…", "Plan reading", tip);
                case ClaudePlanState.Expired: return Item(Plan, ClaudeHealthLevel.Failed, "Plan off: sign-in ended", "Plan off", tip);
                case ClaudePlanState.Failed: return Item(Plan, ClaudeHealthLevel.Failed, "Plan off: check failed", "Plan off", tip);
                case ClaudePlanState.NoMeters: return Item(Plan, ClaudeHealthLevel.Off, "Plan: no meters", "Plan off", tip);
                case ClaudePlanState.Current:
                    return view.NoPlan ? Item(Plan, ClaudeHealthLevel.Off, "Plan off: no Pro or Max login", "Plan off", tip) : Item(Plan, ClaudeHealthLevel.Waiting, "Plan waiting", "Plan waiting", tip);
                default: return Item(Plan, ClaudeHealthLevel.Off, "Plan off: not signed in", "Plan off", tip);
            }
        }
    }
}

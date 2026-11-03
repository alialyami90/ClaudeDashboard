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
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ClaudeDashboard
{
    // The Claude card's words (docs/design/claude-watcher.md and claude-watcher-v2.html), the same as ClaudeText.java on
    // the phone.
    public static class ClaudeText
    {
        public static string State(ClaudeState state)
        {
            return state == ClaudeState.NeedsYou ? "Needs you" : state == ClaudeState.Working ? "Working" : state == ClaudeState.Done ? "Your turn" : state == ClaudeState.Stuck ? "Stuck" : "Ready";
        }
        // Why a session is stuck (decision 023), from StopFailure's error: "Rate limited, resets 14:05".
        public static string StuckReason(ClaudeSession thread, ClaudeView view, DateTime now, bool use24)
        {
            switch (thread == null ? "" : thread.Reason ?? "")
            {
                case "rate_limit":
                    {
                        var limits = view == null ? null : view.Limits;
                        var window = limits == null ? null : limits.FiveHour != null && limits.FiveHour.Current(now) && limits.FiveHour.Percent >= 100 ? limits.FiveHour : limits.Week != null && limits.Week.Current(now) && limits.Week.Percent >= 100 ? limits.Week : limits.FiveHour;
                        return window != null && window.ResetsAt.HasValue && window.Current(now) ? "Rate limited, resets " + When(window.ResetsAt.Value, now, use24) : "Rate limited";
                    }
                case "overloaded": return "Overloaded, try again";
                case "authentication_failed": case "oauth_org_not_allowed": return "Sign in again";
                case "cloud_credential_error": return "Cloud credentials failed";
                case "account_on_hold": return "Account on hold";
                case "billing_error": return "Billing problem";
                case "invalid_request": return "Request refused";
                case "model_not_found": return "Model not available";
                case "server_error": return "Server error, try again";
                case "max_output_tokens": return "Reply too long";
                case ClaudeBridge.QuotaDisabled: return "Stopped waiting for the usage limit";
                default: return "API error";
            }
        }
        // A thread's chip word: a lasting Stuck says so; a working car spinning after an API error is retrying.
        public static string Chip(ClaudeSession thread)
        {
            return thread != null && thread.Pit == PitState.Stuck && thread.State == ClaudeState.Stuck ? "Stuck" : Chip(thread == null ? PitState.Idle : thread.Pit);
        }
        // The car's context ring tooltip: "Context 82% used, compaction soon".
        public static string ContextTip(double percent)
        {
            int level = ClaudePace.ContextLevel(percent);
            if (level == 0) return "";
            string used = "Context " + Math.Round(percent) + "% used";
            return level == 1 ? used : level == 2 ? used + ", compaction soon" : used + ", compaction next";
        }
        // "now", "2 min", "3 h".
        public static string Duration(TimeSpan time)
        {
            if (time < TimeSpan.FromMinutes(1)) return "now";
            if (time < TimeSpan.FromHours(1)) return (int)time.TotalMinutes + " min";
            return (int)time.TotalHours + " h";
        }
        // "Needs you · 2 min" always says how long; the others only when asked.
        public static string StateWords(ClaudeState state, DateTime since, DateTime now, bool withTime)
        {
            return state == ClaudeState.NeedsYou || (withTime && state != ClaudeState.Ready) ? State(state) + " · " + Duration(now - since) : State(state);
        }
        // "3:40 pm" within a day, otherwise the day ("Thu").
        public static string When(DateTime resetsAt, DateTime now, bool use24)
        {
            var culture = CultureInfo.InvariantCulture; var local = resetsAt.ToLocalTime();
            if (resetsAt - now < TimeSpan.FromHours(24)) return use24 ? local.ToString("HH:mm", culture) : local.ToString("h:mm tt", culture).ToLowerInvariant();
            return local.ToString("ddd", culture);
        }
        // Under a meter: "resets 3:40 pm", or "Limit reached · back 3:40 pm" at 100%.
        public static string Reset(ClaudeLimitWindow window, DateTime now, bool use24)
        {
            if (window == null) return "";
            string when = window.ResetsAt.HasValue ? When(window.ResetsAt.Value, now, use24) : "";
            if (window.Percent >= 100) return when.Length == 0 ? "Limit reached" : "Limit reached · back " + when;
            return when.Length == 0 ? "" : "resets " + when;
        }
        // The week's reset with its time: "resets Wed 9:00 am".
        public static string WeekReset(ClaudeLimitWindow window, bool use24)
        {
            if (window == null || !window.ResetsAt.HasValue) return "";
            var local = window.ResetsAt.Value.ToLocalTime();
            return "resets " + local.ToString("ddd", CultureInfo.InvariantCulture) + " " + local.ToString(use24 ? "HH:mm" : "h:mm tt", CultureInfo.InvariantCulture).ToLowerInvariant();
        }
        // "resets 4:18 pm · in 1 h 38 m".
        public static string FiveHourReset(ClaudeLimitWindow window, DateTime now, bool use24)
        {
            if (window == null || !window.ResetsAt.HasValue || !window.Current(now)) return window != null && window.ResetsAt.HasValue ? "reset" : "";
            if (window.Percent >= 100) return Reset(window, now, use24);
            var left = window.ResetsAt.Value - now;
            string inText = left.TotalHours >= 1 ? (int)left.TotalHours + " h " + left.Minutes + " m" : Math.Max(1, (int)Math.Ceiling(left.TotalMinutes)) + " m";
            return "resets " + When(window.ResetsAt.Value, now, use24) + " · in " + inText;
        }
        // "on pace for 92%", or nothing early in a window.
        public static string Pace(double projection) { return Double.IsNaN(projection) ? "" : "on pace for " + Math.Round(projection) + "%"; }
        // The hook's message, shorter: "Claude needs your permission to use Bash" reads "Wants to use Bash".
        public static string Ask(string message)
        {
            const string permission = "Claude needs your permission to use ";
            if (String.IsNullOrWhiteSpace(message)) return "";
            message = message.Trim();
            return message.StartsWith(permission, StringComparison.OrdinalIgnoreCase) && message.Length > permission.Length ? "Wants to use " + message.Substring(permission.Length) : message;
        }
        // A meter turns Amber from 80%.
        public static bool Warns(double percent) { return percent >= 80; }
        public static double Percent(ClaudeLimitWindow window, DateTime now) { return window == null || !window.Current(now) ? 0 : window.Percent; }
        // The heading's live summary: "1 needs you · 2 working · 6 threads today". "Working" is the state, as on the
        // chips and under a project's name; "running" means a session that is open (observability critique, issue 3).
        // The window header's summary when its line is narrow: the two most urgent parts, then the thread count without
        // the period's words ("1 needs you · 2 working · 8 threads"), so it stays whole words.
        // Narrower still (a 360-wide window at 140% text): most = 1 keeps the most urgent part, and threads = false
        // leaves out the count ("1 needs you").
        public static string ShortSummary(ClaudeView view, ClaudePeriod period) { return ShortSummary(view, period, 2, true); }
        public static string ShortSummary(ClaudeView view, ClaudePeriod period, int most, bool threads)
        {
            if (!view.Connected && view.Threads.Count == 0) return "Not connected";
            var parts = new List<string>();
            if (view.NeedsYou > 0) parts.Add(view.NeedsYou + " needs you");
            if (view.Stuck > 0) parts.Add(view.Stuck + " stuck");
            int turn = view.Sessions.Count(s => s.Pit == PitState.Done);
            if (turn > 0) parts.Add(turn + " your turn");
            if (view.Working > 0) parts.Add(view.Working + " working");
            parts = parts.Take(most).ToList();
            if (threads || parts.Count == 0) parts.Add(Threads(view.Groups(period).Sum(g => g.Threads.Count)));
            return String.Join(" · ", parts);
        }
        public static string Summary(ClaudeView view, ClaudePeriod period, bool threads)
        {
            if (!view.Connected && view.Threads.Count == 0) return "Not connected";
            var parts = new List<string>();
            if (view.NeedsYou > 0) parts.Add(view.NeedsYou + " needs you");
            if (view.Stuck > 0) parts.Add(view.Stuck + " stuck");
            int turn = view.Sessions.Count(s => s.Pit == PitState.Done);
            if (turn > 0) parts.Add(turn + " your turn");
            if (view.Working > 0) parts.Add(view.Working + " working");
            if (threads)
            {
                int count = view.Groups(period).Sum(g => g.Threads.Count);
                parts.Add(Threads(count) + (period == ClaudePeriod.FiveHour ? " in 5 h" : period == ClaudePeriod.Today ? " today" : " this week"));
            }
            if (parts.Count == 0) parts.Add(view.Sessions.Count == 0 ? "Nothing running" : "All quiet");
            // Medium and Small keep the two most urgent parts, so the line fits beside the flag (review fix 3).
            if (!threads && parts.Count > 2) parts = parts.Take(2).ToList();
            return String.Join(" · ", parts);
        }
        // The flag at the header's right: when the plan numbers are from, "PLAN · 2:17 AM" (the spec's freshness stamp,
        // review fix 10). Never "LIVE": numbers up to 15 minutes old aren't live, and how old they are is on the
        // source-health line ("Plan 12 min old"), as is "Sample" (observability critique, issue 3). always: the window's
        // header shows it whenever there are numbers; otherwise only when they're old.
        public static string Flag(ClaudeView view, DateTime now, bool use24, bool always = false)
        {
            if (view.Limits == null || !view.Limits.Known || !(always || Old(view, now))) return "";
            return "PLAN · " + Clock(view.Limits.UpdatedAt, use24).ToUpperInvariant();
        }
        // The meters show numbers that may have moved since: older than 15 minutes, or the last check didn't go through.
        public static bool Old(ClaudeView view, DateTime now)
        {
            return view.Limits != null && view.Limits.Known && (now - view.Limits.UpdatedAt > ClaudeService.Stale || view.Plan == ClaudePlanState.Failed || view.Plan == ClaudePlanState.Expired);
        }
        public static string PlanName(ClaudePlanState plan)
        {
            return plan == ClaudePlanState.Current ? "current" : plan == ClaudePlanState.Reading ? "reading" : plan == ClaudePlanState.Failed ? "failed" : plan == ClaudePlanState.Expired ? "expired" : plan == ClaudePlanState.NoMeters ? "noMeters" : "signedOut";
        }
        public static string Clock(DateTime time, bool use24) { return time.ToLocalTime().ToString(use24 ? "HH:mm" : "h:mm tt", CultureInfo.InvariantCulture).ToLowerInvariant(); }
        // The meters' empty states: the widget's own sign-in is what fills them (decision 015).
        public static string LimitsMissing(ClaudeView view)
        {
            switch (view.Plan)
            {
                case ClaudePlanState.Expired: return SignInEnded;
                case ClaudePlanState.NoMeters: return NoMeters;
                case ClaudePlanState.Reading: return "Reading your plan's limits…";
                case ClaudePlanState.Failed: return "Couldn't read your plan's limits. Trying again soon.";
                case ClaudePlanState.Current: return view.NoPlan ? "Plan limits need a Claude Pro or Max login." : "Plan limits show after Claude Code's next reply.";
                default: return SignIn;
            }
        }
        public const string SignIn = "Sign in to Claude in Settings → Claude to see plan limits.", SignInEnded = "Your Claude sign-in has ended. Sign in again in Settings → Claude.";
        public const string NoMeters = "Your plan has no usage meters.";
        public const string NotConnected = "Connect Claude Code in Settings → Claude.", NoSessions = "No Claude Code sessions running.", NoLogs = "Can't read Claude Code's logs.";
        // The plan reports no 5-hour window (only weekly ones), as Anthropic's usage answer can.
        public const string NoFiveHour = "No 5-hour limit on your plan right now";
        public const string CacheTip = "Tokens Claude read back from the prompt cache. They're cheap and not counted in Tokens.";
        public const string RepliesTip = "Claude's replies in this period, one per request to Claude.";
        public static string Replies(int count) { return count == 1 ? "1 reply" : count + " replies"; }
        public const string ApiValueTip = "What the same use would cost on Anthropic's API at list prices, cache reads included. It isn't what your Claude plan charges: the plan meters show your plan's use.";
        // The tools fact (review fix 2): tool calls from the logs; "asked" from telemetry, marked partial when telemetry
        // saw fewer tool calls than the logs.
        public static string ToolsKey(ClaudeFacts f) { return !f.Telemetry ? "TOOLS" : "TOOLS · " + f.Asked + " ASKED" + (f.AskedPartial ? " · PARTIAL" : ""); }
        public static string ToolsTip(ClaudeFacts f)
        {
            string calls = "Tool calls today, from Claude Code's logs.";
            if (!f.Telemetry) return calls + " Which ones asked you comes from Claude Code's telemetry (Settings → Claude → Update connection).";
            return calls + " " + f.Asked + " asked you and " + f.Denied + " were denied" + (f.AskedPartial ? ", from the sessions that sent telemetry (not every session does, so this is partial)." : ".");
        }
        public const string ForTip = "How long the thread has been in its state (Working, Your turn, Needs you…).";
        public const string SignTip = "The last hook event or reply from the thread. A working thread quiet for 5 minutes or more says \"silent\".";
        public const string NowHeadTip = "The tool a working thread is in now, by name only, from Claude Code's telemetry.";
        public const string TokensTip = "Fresh input, cache writes and output. Cache reads aren't counted here (see Cache hits).";
        // The attention queue's words (issue 1). "Pit stop: Fix login · api-server", "Stuck: …", "Your turn: …", "Silent: …".
        public static string AttentionWord(string kind) { return kind == ClaudeAttention.Need ? "Pit stop" : kind == ClaudeAttention.Stuck ? "Stuck" : kind == ClaudeAttention.Turn ? "Your turn" : "Silent"; }
        public static string AttentionTitle(string kind, string title, string project) { return AttentionWord(kind) + ": " + title + (String.IsNullOrEmpty(project) ? "" : " · " + project); }
        // Why it's in the queue and for how long: "Wants to use Bash · waiting 2 min · Claude app", "Overloaded, try
        // again · stuck 4 min", "Waiting for your reply · 15 min", "Working, no sign of life for 8 min".
        public static string AttentionReason(string kind, ClaudeSession t, ClaudeView view, DateTime now, bool use24)
        {
            var parts = new List<string>();
            if (kind == ClaudeAttention.Need) { string ask = Ask(t.Message); if (ask.Length > 0) parts.Add(ask); parts.Add("waiting " + Duration(now - t.Since)); }
            else if (kind == ClaudeAttention.Stuck) { string why = StuckReason(t, view, now, use24); if (!String.IsNullOrEmpty(why)) parts.Add(why); parts.Add("stuck " + Duration(now - t.Since)); }
            else if (kind == ClaudeAttention.Turn) parts.Add("Waiting for your reply · " + Duration(now - t.Since));
            else if (t.Tool.Length > 0 && t.ToolSince > DateTime.MinValue) parts.Add("In " + ToolName(t.Tool) + " for " + Duration(now - t.ToolSince));
            else parts.Add(t.Pit == PitState.Rev ? "In a tool for " + Duration(now - t.LastSign) : "Working, no sign of life for " + Duration(now - t.LastSign));
            if (t.Where.Length > 0) parts.Add(t.Where == "Desktop" ? "Claude app" : t.Where);
            return String.Join(" · ", parts);
        }
        // The clipped ledger's footer: "+7 below · 1 stuck · 2 your turn", with what waits above after an arrow
        // ("+7 below · 2 your turn · ↑ 1 needs you"), or "↑ 3 above · 1 needs you" at the bottom. Each count says
        // where its rows are.
        public static string MoreRows(int below, int needs, int stuck, int turn, int above, int upNeeds, int upStuck, int upTurn)
        {
            var parts = new List<string>();
            if (below > 0) { parts.Add("+" + below + " below"); parts.AddRange(Waiting(needs, stuck, turn)); }
            var up = Waiting(upNeeds, upStuck, upTurn);
            if (below <= 0) { parts.Add("\u2191 " + above + " above"); parts.AddRange(up); }
            else if (up.Count > 0) parts.Add("\u2191 " + String.Join(" · ", up));
            return String.Join(" · ", parts);
        }
        private static List<string> Waiting(int needs, int stuck, int turn)
        {
            var parts = new List<string>();
            if (needs > 0) parts.Add(needs + (needs == 1 ? " needs you" : " need you"));
            if (stuck > 0) parts.Add(stuck + " stuck");
            if (turn > 0) parts.Add(turn + " your turn");
            return parts;
        }
        // Medium's last row when projects don't fit: "+3 more projects · 1 stuck · 1 your turn".
        public static string MoreProjects(int count, int needs, int stuck, int turn)
        {
            var parts = new List<string> { "+" + count + (count == 1 ? " more project" : " more projects") };
            parts.AddRange(Waiting(needs, stuck, turn));
            return String.Join(" · ", parts);
        }

        // v0.53.0, the plan row speaks in time. The tank's pace, longest first: "≈ empty 3:21 am, 37 min before reset",
        // "≈ empty 3:21 am"; or, when it lasts the window, "≈ 84% by reset" ("≈ 84%"). Estimates say ≈.
        public static List<string> PaceTimes(double projection, double etaMinutes, ClaudeLimitWindow window, DateTime now, bool use24) { return PaceTimes(projection, etaMinutes, window, ClaudePace.FiveHours, now, use24); }
        // Over 100% never reads as a percentage by reset (the Lead on #17): the window runs dry first, so it says when.
        // The ETA from the last half hour wins; else the window's own pace so far (its projection) gives the time; else
        // "≈ empty before reset".
        public static List<string> PaceTimes(double projection, double etaMinutes, ClaudeLimitWindow window, TimeSpan length, DateTime now, bool use24)
        {
            var forms = new List<string>();
            DateTime? empty = null;
            if (!Double.IsNaN(etaMinutes)) empty = now.AddMinutes(etaMinutes);
            else if (!Double.IsNaN(projection) && projection > 100 && window != null && window.ResetsAt.HasValue)
            {
                var at = window.ResetsAt.Value - TimeSpan.FromTicks((long)(length.Ticks * (1 - 100 / projection)));
                if (at > now) empty = at;
            }
            if (empty.HasValue)
            {
                string at = "≈ empty " + When(empty.Value, now, use24);
                if (window != null && window.ResetsAt.HasValue && window.ResetsAt.Value > empty.Value) forms.Add(at + ", " + Duration(window.ResetsAt.Value - empty.Value) + " before reset");
                forms.Add(at);
            }
            else if (!Double.IsNaN(projection) && projection > 100) forms.Add("≈ empty before reset");
            else if (!Double.IsNaN(projection)) { forms.Add(PaceBy(projection)); forms.Add("≈ " + Math.Round(projection) + "%"); }
            else forms.Add("");
            return forms;
        }
        // "≈ 84% by reset", "≈ empty before reset" over 100%, or nothing early in a window.
        public static string PaceBy(double projection) { return Double.IsNaN(projection) ? "" : projection > 100 ? "≈ empty before reset" : "≈ " + Math.Round(projection) + "% by reset"; }
        // The tank's reset, longest first: "resets 9:54 am · in 1 h 20 m", "resets 9:54 am", "9:54 am".
        public static List<string> FiveHourResets(ClaudeLimitWindow window, DateTime now, bool use24)
        {
            var forms = new List<string> { FiveHourReset(window, now, use24) };
            if (window != null && window.ResetsAt.HasValue && window.Current(now) && window.Percent < 100) { forms.Add("resets " + When(window.ResetsAt.Value, now, use24)); forms.Add(When(window.ResetsAt.Value, now, use24)); }
            return forms;
        }

        // v0.53.0, the live rows (5 h and Today). How long the thread has been in its state ("For"): "now", "12 min".
        public static string StateFor(ClaudeSession t, DateTime now) { return t.Running ? Duration(now - t.Since) : ""; }
        // Its last sign of life: "just now", "3 min ago", "silent 8 min" for a working thread quiet 5 minutes or more
        // (the attention queue's Silent), "2 h ago" for an ended one.
        public static string Sign(ClaudeSession t, DateTime now)
        {
            if (!t.Running) return t.LastActive == DateTime.MinValue ? "" : Ago(now - t.LastActive);
            if (t.LastSign == DateTime.MinValue) return "";
            if (Silent(t, now)) return "silent " + Duration(now - t.LastSign);
            return Ago(now - t.LastSign);
        }
        public static bool Silent(ClaudeSession t, DateTime now) { return t.Running && t.State == ClaudeState.Working && t.LastSign > DateTime.MinValue && now - t.LastSign >= ClaudeAttention.SilentAfter; }
        private static string Ago(TimeSpan span) { return span < TimeSpan.FromMinutes(1) ? "just now" : Duration(span) + " ago"; }
        // What it's in now, by tool name only (telemetry's open call): "Bash", "Bash · 2 min"; "a tool" when telemetry
        // only says a call runs long; "—" for a working thread with no tool known.
        public static string NowWords(ClaudeSession t, DateTime now)
        {
            if (!t.Running) return "";
            if (t.Tool.Length > 0) { string tool = ToolName(t.Tool); return now - t.ToolSince >= TimeSpan.FromMinutes(1) && t.ToolSince > DateTime.MinValue ? tool + " · " + Duration(now - t.ToolSince) : tool; }
            if (t.Pit == PitState.Rev) return "a tool";
            return t.State == ClaudeState.Working ? "—" : "";
        }
        // A tool's name for the row: 40 characters, then "…" (the tip has the whole name).
        public static string ToolName(string tool) { return tool.Length > 40 ? tool.Substring(0, 39) + "…" : tool; }
        public static string NowTip(ClaudeSession t, bool telemetry)
        {
            if (t.Tool.Length > 0) return "In " + t.Tool + " now (its name only, from Claude Code's telemetry).";
            return telemetry ? "No tool running now, by Claude Code's telemetry." : "Which tool runs comes from Claude Code's telemetry (Settings → Claude → Update connection).";
        }
        public static string ContextTip(ClaudeSession t) { return Double.IsNaN(t.Context) ? "Context: not known yet (it comes from Claude Code's status line)." : "Context " + Math.Round(t.Context) + "% full"; }

        // Package 4.6, the plain list's words. The plan: "5-hour 23%, resets 3:40 pm · Week 41%, resets Thu 9:00 am".
        public static string PlainPlan(ClaudeView view, DateTime now, bool use24)
        {
            var limits = view == null ? null : view.Limits;
            string text;
            if (limits == null || !limits.Known) text = LimitsMissing(view);
            else
            {
                var parts = new List<string>();
                if (limits.FiveHour != null) { string reset = FiveHourReset(limits.FiveHour, now, use24); parts.Add("5-hour " + Math.Round(Percent(limits.FiveHour, now)).ToString(CultureInfo.InvariantCulture) + "%" + (reset.Length > 0 ? ", " + reset : "")); }
                else parts.Add(NoFiveHour);
                if (limits.Week != null) parts.Add("Week " + Math.Round(Percent(limits.Week, now)).ToString(CultureInfo.InvariantCulture) + "%" + (limits.Week.Current(now) && limits.Week.ResetsAt.HasValue ? ", " + WeekReset(limits.Week, use24) : ", reset"));
                text = String.Join(" · ", parts);
            }
            if (view != null && view.NeedsYou > 0) text = (view.NeedsYou == 1 ? "1 thread needs you. " : view.NeedsYou + " threads need you. ") + text;
            return view != null && view.Sample ? "Sample. " + text : text;
        }
        public static string PlainProject(ClaudeProjectGroup group, bool counted)
        {
            int n = group.Threads.Count;
            return n + (n == 1 ? " thread" : " threads") + " · " + (counted ? "" : "≈") + ClaudeUsage.Format(group.Tokens) + " tokens";
        }
        // Under the thread's name: what it waits on or why it stopped, else since when.
        public static string PlainDetail(ClaudeSession thread, ClaudeView view, DateTime now, bool use24)
        {
            if (thread.State == ClaudeState.Stuck) { string why = StuckReason(thread, view, now, use24); if (!String.IsNullOrEmpty(why)) return why; }
            if (thread.State == ClaudeState.NeedsYou && !String.IsNullOrWhiteSpace(thread.Message)) return Ask(thread.Message);
            DateTime at = thread.Running ? thread.Since : thread.LastActive;
            return at == DateTime.MinValue ? (thread.Running ? "Running" : "Ended") : (thread.Running ? "Since " : "Last active ") + Ago(at, now, use24);
        }
        public static string PlainNumbers(ClaudeSession thread, ClaudePeriod period, bool counted)
        {
            int replies = thread.RepliesFor(period);
            return (counted ? "" : "≈") + ClaudeUsage.Format(thread.TokensFor(period)) + " tokens · " + replies + (replies == 1 ? " reply" : " replies");
        }

        // Package 4: the plan row's 7-day bars, in words.
        public static string DaysTip(long[] days, long? lastWeek, bool sample)
        {
            if (days == null || days.Length == 0) return null;
            long today = days[days.Length - 1];
            string tip = "Tokens a day, last " + days.Length + " days (from History): today " + ClaudeUsage.Format(today) + ", busiest " + ClaudeUsage.Format(days.Max()) + ".";
            tip += lastWeek.HasValue ? " By this time last week: " + ClaudeUsage.Format(lastWeek.Value) + " (the tick)." : " No file for this day last week yet.";
            return sample ? "Sample. " + tip : tip;
        }
        public static string PeriodName(ClaudePeriod period) { return period == ClaudePeriod.FiveHour ? "5 h" : period == ClaudePeriod.Today ? "Today" : "Week"; }
        // "Plan limits as of 10:42" when a check failed, and the sign-in's end when it has.
        public static string Source(ClaudeView view, bool use24)
        {
            var limits = view.Limits;
            if (limits == null || !limits.Known) return "";
            if (view.Plan == ClaudePlanState.Expired) return "as of " + Clock(limits.UpdatedAt, use24) + " · sign in again in Settings → Claude";
            if (view.Plan == ClaudePlanState.Failed) return "Plan limits as of " + Clock(limits.UpdatedAt, use24);
            return "as of " + Clock(limits.UpdatedAt, use24) + " · " + (limits.Source == "online" ? "checked online" : "from Claude Code");
        }
        // A project row's or thread's usage: "1.8M · ≈ $2.01".
        public static string Usage(long tokens, double cost) { return ClaudeUsage.Format(tokens) + " · " + ClaudeUsage.Money(cost); }
        // A wait: "30 s", "5 min".
        public static string Wait(TimeSpan time) { return time < TimeSpan.FromMinutes(1) ? (int)Math.Ceiling(time.TotalSeconds) + " s" : Duration(time); }
        public static string Threads(int count) { return count == 1 ? "1 thread" : count + " threads"; }
        public static string Cars(int count) { return count == 1 ? "1 car" : count + " cars"; }
        // Under a project's name: "3 threads · 2 working · Claude app", "working" as in the header's summary (Lead's
        // review of v0.50.0). Compact (Medium) leaves out where it runs, so
        // "+N hidden" still fits (review fix 3).
        public static string GroupDetail(ClaudeProjectGroup group, bool compact = false)
        {
            // A project with no thread in the period: when it was last used.
            if (group.Threads.Count == 0) return (group.Hidden > 0 ? group.Hidden + " hidden · " : "") + (group.Seen > DateTime.MinValue ? "Quiet · last used " + Day(group.Seen) : "Quiet");
            var parts = new List<string> { Threads(group.Threads.Count) };
            int working = group.Threads.Count(t => t.Running && t.State == ClaudeState.Working);
            if (working > 0) parts.Add(working + " working");
            var where = group.Threads.Where(t => t.Running && t.Where.Length > 0).Select(t => t.Where).Distinct().ToList();
            if (where.Count == 1) { if (!compact) parts.Add(where[0] == "Desktop" ? "Claude app" : where[0]); }
            else if (group.Asleep) parts.Add("ended " + Duration(DateTime.UtcNow - group.LastActive) + " ago");
            if (group.Hidden > 0) parts.Add("+" + group.Hidden + " hidden");
            return String.Join(" · ", parts);
        }
        // "Oct 25".
        public static string Day(DateTime at) { return at.ToLocalTime().ToString("MMM d", CultureInfo.InvariantCulture); }
        // "3:40 pm" today, otherwise the day ("Thu").
        public static string Ago(DateTime at, DateTime now, bool use24)
        {
            return at.ToLocalTime().Date == now.ToLocalTime().Date ? Clock(at, use24) : at.ToLocalTime().ToString("ddd", CultureInfo.InvariantCulture);
        }
        // A thread's name (decision 018): the one ClaudeService picked (a rename, a generated title or the first line
        // of its first request; never a placeholder), else a label from its id.
        public static string ThreadTitle(ClaudeSession thread, DateTime now, bool use24)
        {
            return !ClaudeTitles.Placeholder(thread.Title) ? thread.Title.Trim() : ClaudeTitles.IdLabel(thread.Id);
        }
        // Where a thread runs and for how long in its state ("Remote Control · 6 min"), or when it ended ("Remote
        // Control · ended 1 h ago").
        // Where a thread runs, one name everywhere: the Claude app's Code tab ("Desktop" in its records) is "Claude app".
        public static string Place(ClaudeSession thread) { return thread.Where == "Desktop" ? "Claude app" : thread.Where; }
        public static string Where(ClaudeSession thread, DateTime now)
        {
            string place = Place(thread);
            if (!thread.Running) return (place.Length > 0 ? place + " · " : "") + "ended " + Duration(now - thread.LastActive) + " ago";
            string age = Duration(now - thread.Since);
            return place.Length > 0 ? place + " · " + age : age;
        }
        // A thread's chip: what its car is doing.
        public static string Chip(PitState state)
        {
            switch (state)
            {
                case PitState.Need: return "Needs you";
                case PitState.Work: return "Working";
                case PitState.Rev: return "Tool run";
                case PitState.Pit: return "Compacting";
                case PitState.Stuck: return "Retrying";
                case PitState.Done: return "Your turn";
                case PitState.End: return "Ended";
                case PitState.Archived: return "Archived";
                case PitState.Resolved: return "Resolved";
                default: return "Idle";
            }
        }
        // A thread's tooltip: the full title, its state or when it was last active, its usage and its model.
        public static string ThreadTip(ClaudeSession thread, ClaudePeriod period, DateTime now, bool use24, bool counted, bool apiValue = false)
        {
            var lines = new List<string> { ThreadTitle(thread, now, use24) };
            lines.Add(thread.Running ? StateWords(thread.State, thread.Since, now, true) + (thread.State == ClaudeState.NeedsYou && !String.IsNullOrWhiteSpace(thread.Message) ? " · " + Ask(thread.Message) : "") : "Ended · last active " + Ago(thread.LastActive, now, use24));
            if (thread.Running && thread.State == ClaudeState.Stuck)
                lines.Add(StuckReason(thread, null, now, use24) + (String.IsNullOrWhiteSpace(thread.ReasonText) ? "" : ": " + thread.ReasonText.Trim()));
            if (thread.Running && thread.Pit == PitState.Stuck && thread.State != ClaudeState.Stuck && Retrying(thread).Length > 0) lines.Add(Retrying(thread));
            string context = thread.Running ? ContextTip(thread.Context) : "";
            if (context.Length > 0) lines.Add(context);
            // Package 3: its pit crew (helpers running now) and the task list.
            string crew = thread.Running ? Crew(thread) : "";
            if (crew.Length > 0) lines.Add(crew);
            string endedBy = thread.Running ? "" : EndedBy(thread.EndReason);
            if (endedBy.Length > 0) lines.Add(endedBy);
            // Package 2: helpers' tokens apart from the thread's own, Claude Code's own cost, its mode and setup.
            long helpers = thread.HelperTokensFor(period);
            if (counted && helpers > 0) lines.Add("Own " + ClaudeUsage.Format(Math.Max(0, thread.TokensFor(period) - helpers)) + " · helpers " + ClaudeUsage.Format(helpers) + " tokens " + PeriodName(period).ToLowerInvariant());
            var setup = new List<string>();
            if (thread.Running && thread.Mode.Length > 0) setup.Add("Mode " + thread.Mode);
            if (thread.Agent.Length > 0) setup.Add("Agent " + thread.Agent);
            if (thread.Worktree.Length > 0) setup.Add("Worktree " + thread.Worktree);
            if (thread.Style.Length > 0 && !String.Equals(thread.Style, "default", StringComparison.OrdinalIgnoreCase)) setup.Add("Style " + thread.Style);
            if (apiValue && !Double.IsNaN(thread.CostUsd)) setup.Add("Claude Code counts $" + Dollars(thread.CostUsd));
            if (setup.Count > 0) lines.Add(String.Join(" · ", setup));
            var detail = new List<string>();
            if (counted) detail.Add(ClaudeUsage.Format(thread.TokensFor(period)) + " tokens · " + Replies(thread.RepliesFor(period)) + (apiValue ? " · " + ClaudeUsage.Money(thread.CostFor(period)) + " API value" : "") + " " + PeriodName(period).ToLowerInvariant());
            if (!String.IsNullOrWhiteSpace(thread.Model)) detail.Add(thread.Model);
            if (!String.IsNullOrWhiteSpace(thread.Where)) detail.Add(thread.Where);
            if (detail.Count > 0) lines.Add(String.Join(" · ", detail));
            if (thread.Running && thread.CanFocus) lines.Add("Click to show its window.");
            return String.Join("\n", lines);
        }
        // "2 helpers working · tasks 3 / 5" (package 3), or "".
        public static string Crew(ClaudeSession thread)
        {
            var parts = new List<string>();
            if (thread.Workers > 0) parts.Add(thread.Workers == 1 ? "1 helper working" : thread.Workers + " helpers working");
            if (thread.TasksTotal > 0) parts.Add("tasks " + Tasks(thread));
            return String.Join(" · ", parts);
        }
        // The car's task chip: "3 / 5".
        public static string Tasks(ClaudeSession thread) { return thread.TasksDone + " / " + thread.TasksTotal; }
        // Why Claude Code ended a thread it kept (package 3).
        public static string EndedBy(string reason)
        {
            return reason == "prompt_input_exit" ? "Closed in Claude Code" : reason == "logout" ? "Ended when Claude Code logged out" : reason == "clear" ? "Cleared with /clear" : "";
        }
        public static string NoThreads(ClaudePeriod period) { return "No Claude Code threads " + (period == ClaudePeriod.FiveHour ? "in the last 5 hours." : period == ClaudePeriod.Today ? "today." : "this week."); }
        // "+1.3k" and "−216": lines added and removed.
        public static string Added(long lines) { return "+" + ClaudeUsage.Format(lines); }
        public static string Removed(long lines) { return "−" + ClaudeUsage.Format(lines); }
        // A ledger's dollars without the "≈" (the column says it): "7.10", "12", "1.2k".
        public static string Dollars(double dollars) { string money = ClaudeUsage.Money(dollars); return money.Substring(money.IndexOf('$') + 1); }
        // The model mix in words, for its tooltip: "Opus 80% · Sonnet 20%".
        public static string Mix(double[] mix)
        {
            var names = new[] { "Opus", "Sonnet", "Haiku" };
            var parts = Enumerable.Range(0, 3).Where(i => mix[i] > 0.005).Select(i => names[i] + " " + Math.Round(mix[i] * 100) + "%").ToList();
            return parts.Count == 0 ? "No replies in this period" : String.Join(" · ", parts);
        }
        // ----- Package 2 -----

        // "1 h 40", "25 min", "40 s".
        public static string Span(TimeSpan time)
        {
            if (time < TimeSpan.FromMinutes(1)) return Math.Max(0, (int)time.TotalSeconds) + " s";
            if (time < TimeSpan.FromHours(1)) return (int)time.TotalMinutes + " min";
            return (int)time.TotalHours + " h" + (time.Minutes > 0 ? " " + time.Minutes.ToString("00", CultureInfo.InvariantCulture) : "");
        }
        // The 5-hour tank's time to the limit: "full in 1 h 40 at this pace" (nothing when it lasts to the reset).
        public static string Eta(double minutes) { return Double.IsNaN(minutes) ? "" : "full in " + Span(TimeSpan.FromMinutes(minutes)) + " at this pace"; }
        // The tank's pace line: the projection and, when it fills before the reset, when.
        public static string PaceLine(double projection, double etaMinutes)
        {
            string pace = Pace(projection), eta = Eta(etaMinutes);
            return pace.Length == 0 ? eta : eta.Length == 0 ? pace : pace + " · " + eta;
        }
        // "~" before an API value that leaves out replies from a model with no known price, and what the tip says.
        public static string Unpriced(List<string> models) { return models == null || models.Count == 0 ? "" : "~"; }
        public static string UnpricedTip(List<string> models)
        {
            if (models == null || models.Count == 0) return "";
            return "Price unknown for " + String.Join(", ", models.Take(3)) + (models.Count > 3 ? " and " + (models.Count - 3) + " more" : "") + ": those replies count $0 here unless Claude Code's status line gave the session's cost.";
        }
        // The API value's tip: what it is, where its numbers came from, and Claude Code's own count for the running
        // sessions beside the widget's.
        public static string ApiValue(ClaudeView view, ClaudePeriod period)
        {
            var lines = new List<string> { ApiValueTip };
            string source = view.CostSourceFor(period);
            lines.Add(source == "claude" ? "Costs from Claude Code's telemetry." : source == "statusline" ? "Costs from Claude Code's status line, shared out by reply size." : source == "mixed" ? "Costs from Claude Code where it sent them, else from list prices." : "Costs from list prices.");
            if (!Double.IsNaN(view.ClaudeCodeCost)) lines.Add("Running sessions: Claude Code counts $" + Dollars(view.ClaudeCodeCost) + " in all; Claude Dashboard counts $" + Dollars(view.EstimateCost) + " for them this week.");
            string unknown = UnpricedTip(view.UnpricedFor(period));
            if (unknown.Length > 0) lines.Add(unknown);
            return String.Join("\n", lines);
        }
        // An API error telemetry saw on a working thread: "Retrying after an API error · HTTP 529 · attempt 3".
        public static string Retrying(ClaudeSession thread)
        {
            if (thread.ErrorAt == DateTime.MinValue) return "";
            return "Retrying after an API error" + (thread.ErrorStatus > 0 ? " · HTTP " + thread.ErrorStatus : "") + (thread.ErrorAttempt > 0 ? " · attempt " + thread.ErrorAttempt : "");
        }
        // A side-panel fact (package 2): its value, the words under it and its tip, for the period.
        public static string[] Fact(string kind, ClaudeView view, ClaudePeriod period, bool counted, bool showApi)
        {
            var f = view.FactsFor(period);
            string when = PeriodName(period).ToUpperInvariant(), lower = PeriodName(period).ToLowerInvariant();
            var c = CultureInfo.InvariantCulture;
            switch (kind)
            {
                case ClaudeFactKinds.Cache:
                    return new[] { Percent(f.CacheHit), "CACHE HITS", ClaudeUsage.Format(f.CacheRead) + " tokens read from the cache " + lower + " (not counted in the token totals)" };
                case ClaudeFactKinds.Tools:
                    return new[] { counted ? f.Tools.ToString(c) : view.LogsFailed ? "—" : "≈" + f.Tools.ToString(c), view.LogsFailed ? "TOOLS · LOGS UNREADABLE" : ToolsKey(f), view.LogsFailed ? NoLogs : ToolsTip(f) };
                case ClaudeFactKinds.Lines:
                    return new[] { f.Added > 0 || f.Removed > 0 ? Added(f.Added) : "—", f.Added > 0 || f.Removed > 0 ? Removed(f.Removed) + " · " + Threads(f.LinesThreads).ToUpperInvariant() : "LINES", "Lines added and removed " + lower + ", from Claude Code's logs" };
                case ClaudeFactKinds.Slowest:
                    return new[] { Double.IsNaN(f.SlowMs) ? "—" : Span(TimeSpan.FromMilliseconds(f.SlowMs)), f.SlowTool.Length > 0 ? "SLOWEST · " + f.SlowTool.ToUpperInvariant() : "SLOWEST TOOL", f.Telemetry ? "The tool with the longest average run " + lower + ", from Claude Code's telemetry." : NeedsTelemetry };
                case ClaudeFactKinds.Failures:
                    return new[] { Percent(f.FailureRate), f.Runs > 0 ? f.RunFailures + " OF " + f.Runs + " FAILED" : "TOOL FAILURES",
                        f.FailuresFromHooks ? "Tool calls that failed: the sessions' failure counts from Claude Code's hooks over the logs' tool calls " + lower + "." : "Tool runs that failed " + lower + ", from Claude Code's telemetry." };
                case ClaudeFactKinds.Errors:
                    return new[] { f.Telemetry ? f.Errors.ToString(c) : "—", "API ERRORS · LAST HOUR", f.Telemetry ? "Requests to Claude that failed in the last hour (Claude Code retries most by itself)." : NeedsTelemetry };
                case ClaudeFactKinds.Helpers:
                    return new[] { Percent(f.HelperShare), "HELPERS · " + when, "The subagents' share of the tokens " + lower + (f.Helpers > 0 ? "; " + f.Helpers + " finished" : "") + "." };
                case ClaudeFactKinds.Edits:
                    return new[] { f.Metrics ? f.EditsAccepted.ToString(c) : "—", f.Metrics ? "EDITS OK · " + f.EditsRejected + " REJECTED" : "EDITS", f.Metrics ? "Edits, writes and notebook edits you accepted and rejected " + lower + "." : NeedsTelemetry };
                case ClaudeFactKinds.Commits:
                    return new[] { f.Metrics ? f.Commits.ToString(c) : "—", f.Metrics ? "COMMITS · " + f.PullRequests + " PRS" : "COMMITS", f.Metrics ? "Git commits and pull requests Claude Code made " + lower + "." : NeedsTelemetry };
                case ClaudeFactKinds.Active:
                    return new[] { f.Metrics ? Span(TimeSpan.FromSeconds(f.ActiveCli)) : "—", f.Metrics ? "CLAUDE · YOU " + Span(TimeSpan.FromSeconds(f.ActiveUser)).ToUpperInvariant() : "ACTIVE TIME", f.Metrics ? "Time Claude worked (tools and replies) and time you were active (typing and reading) " + lower + ", from Claude Code's telemetry." : NeedsTelemetry };
                default:
                    string unknown = Unpriced(view.UnpricedFor(period));
                    // While the logs are still being read the count so far shows with "≈"; when they can't be read, "—" and
                    // why, so loading and unavailable look different (observability critique, issue 3).
                    if (view.LogsFailed) return new[] { "—", when + " · LOGS UNREADABLE", NoLogs };
                    return new[] { Tokens(f.Tokens, counted), !counted ? when + " · COUNTING" : showApi ? when + " · " + unknown + "$" + Dollars(f.Cost) + " API VALUE" : when + " · " + f.Replies + " REPLIES", showApi ? ApiValue(view, period) : TokensTip };
            }
        }
        // The plan's chips (package 2): the narrower weekly windows the week tile has no room for (it names two),
        // extra usage and a gateway's spend limit, with whether it warns (80% and over, as the meters do).
        public const int MostPlanChips = 6;
        public static List<KeyValuePair<string, bool>> PlanChips(ClaudeLimits limits, DateTime now, bool use24)
        {
            var chips = new List<KeyValuePair<string, bool>>();
            if (limits == null) return chips;
            foreach (var p in limits.Extra.Skip(2).Take(4))
            {
                double pct = Percent(p.Value, now);
                chips.Add(new KeyValuePair<string, bool>(p.Key + " " + Math.Round(pct) + "%" + (p.Value.ResetsAt.HasValue && p.Value.Current(now) ? " · resets " + When(p.Value.ResetsAt.Value, now, use24) : ""), Warns(pct)));
            }
            var extra = limits.ExtraUsage;
            if (extra != null)
            {
                var c = CultureInfo.InvariantCulture;
                string amounts = !Double.IsNaN(extra.Used) && !Double.IsNaN(extra.Limit) ? " · " + extra.Used.ToString("0.##", c) + " of " + extra.Limit.ToString("0.##", c) : "";
                chips.Add(new KeyValuePair<string, bool>(!extra.Enabled ? "Extra usage off" : "Extra usage " + (Double.IsNaN(extra.Percent) ? "on" : Math.Round(extra.Percent) + "%") + amounts, extra.Enabled && !Double.IsNaN(extra.Percent) && Warns(extra.Percent)));
            }
            var spend = limits.Spend;
            if (spend != null)
            {
                string dollars = !Double.IsNaN(spend.UsedUsd) && !Double.IsNaN(spend.LimitUsd) ? " · $" + Dollars(spend.UsedUsd) + " of $" + Dollars(spend.LimitUsd) : "";
                chips.Add(new KeyValuePair<string, bool>("Spend limit " + Math.Round(spend.Percent) + "%" + dollars + (spend.Period.Length > 0 ? " " + spend.Period : ""), Warns(spend.Percent)));
            }
            return chips.Take(MostPlanChips).ToList();
        }
        public const string NeedsTelemetry = "Comes from Claude Code's telemetry, which hasn't sent any yet (Settings → Claude → Update connection).";

        // The narrower weekly windows in one line: "Opus only 18% · Sonnet 9%".
        public static string Extras(ClaudeLimits limits, DateTime now)
        {
            if (limits == null || limits.Extra.Count == 0) return "";
            var parts = limits.Extra.Take(2).Select((p, i) => p.Key.Replace(" week", "") + (i == 0 ? " only " : " ") + Math.Round(Percent(p.Value, now)) + "%");
            return String.Join(" · ", parts);
        }
        // The lap counter: "Week · lap 5 of 7".
        public static string Lap(int lap) { return lap < 0 ? "WEEK" : "WEEK · LAP " + (lap + 1) + " OF 7"; }
        // The day names under the laps, from the window's start.
        public static string[] LapDays(ClaudeLimitWindow week)
        {
            var days = new string[7];
            DateTime start = week != null && week.ResetsAt.HasValue ? week.ResetsAt.Value - ClaudePace.Week : DateTime.UtcNow.AddDays(-6);
            for (int i = 0; i < 7; i++) days[i] = start.AddDays(i).ToLocalTime().ToString("ddd", CultureInfo.InvariantCulture);
            return days;
        }
        // A token count: "1.8M", "≈1.2M" while the logs are still being read.
        public static string Tokens(long tokens, bool counted) { return (counted ? "" : "≈") + ClaudeUsage.Format(tokens); }
        // The cache hit rate: "91%", or "—" with nothing read today.
        public static string Percent(double share) { return Double.IsNaN(share) ? "—" : Math.Round(share * 100) + "%"; }
    }

    // Claude Code as a pit wall (docs/design/claude-watcher-v2.html; decision 017), in three designs that follow the
    // card's size (CardSizing.Claude). Each project is a pit garage and each thread a pixel-art race car drawn in code
    // (PitArt): its class follows the model it used most, its colour the project's, and its status shows in what it
    // does. Large: the heading with a live summary and the period switch; the plan row (the 5-hour tank gauge with
    // pace, the week as seven laps, the rev counter with the hour's burn); a pit stop strip for the thread waiting on
    // you; and the ledger of garages and cars beside the 5-hour lanes and today's facts. Medium: the two plan meters
    // with pace and the top garages with their cars. Small: the car that needs you (or the busiest) and the 5-hour
    // meter. Clicking a garage (or Enter or Space on it) rolls its cars in (they back out of their rows and drive into the bay, tail lights toward
    // you) or out (headlights toward you, then into their rows); Quiet mode swaps states without motion. The phone's
    // ClaudeWidget.java draws the same.
    public sealed class ClaudeCard : CardParts
    {
        public static readonly TimeSpan Roll = TimeSpan.FromMilliseconds(1200);
        private readonly Grid host;
        private readonly Border card;
        private readonly PartNames names;
        private readonly Func<string, string> focus;
        private readonly Action<string, bool> collapse;
        private readonly Action<ClaudePeriod> choosePeriod;
        private readonly PitClock clock;
        private ClaudeView view;
        private Rect bounds = Rect.Empty;
        private double textScale = 1;
        private string built = "";
        private CardSizing.Design design;
        private ClaudePeriod period = ClaudePeriod.Today;
        private bool quiet, use24, live;
        private RollState rolling;
        private string rollOut;
        private readonly HashSet<string> collapsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Each thread's last car state, so Done and Ended play their once-only frames when a car changes into them.
        private readonly Dictionary<string, PitState> seen = new Dictionary<string, PitState>();
        private readonly List<CarPart> cars = new List<CarPart>();
        private readonly List<GaragePart> garages = new List<GaragePart>();
        private readonly List<Action<DateTime>> fills = new List<Action<DateTime>>();
        private Canvas layer;
        private Border askStrip;
        // The ledger's scrolling list on Large: the roll only moves cars that show in it.
        private ScrollViewer ledgerScroll;

        // A thread's car (in the ledger, inline on Medium, Small's or the pit stop strip's) and its row.
        // A car: its sprite, the context ring on its corner (decision 023) and the two together (View), which is what
        // goes into a row. OnlyWorst: on Medium, only the fullest context in its garage shows a ring.
        private sealed class CarPart { public string Id, Folder; public PitSprite Sprite; public ContextRing Ring; public PitCrew Crew; public FrameworkElement View, Row; public double Width; public bool Dim, OnlyWorst; }
        // A project's garage and its cars.
        private sealed class GaragePart { public string Folder; public PitSprite Sprite; public FrameworkElement Tap; public readonly List<CarPart> Cars = new List<CarPart>(); }

        public ClaudeCard(FrameworkElement scope, Grid host, Border card, Func<string, string> focus, Action<string, bool> collapse = null, Action<ClaudePeriod> choosePeriod = null)
        {
            this.host = host; this.card = card; this.focus = focus; this.collapse = collapse; this.choosePeriod = choosePeriod; names = new PartNames(scope);
            clock = new PitClock(host.Dispatcher);
            host.IsVisibleChanged += (sender, e) => clock.SetLive(live && host.IsVisible);
        }

        public CardSizing.Design Current { get { return design; } }
        public ClaudeView View { get { return view; } }
        public ClaudePeriod Period { get { return period; } }
        // Claude Dashboard's window (phase 4): the window's own header shows the summary, the flag and the period
        // switch, so the card has no heading, and the Needs-you and Your-turn edge goes on the border passed in as the
        // card: the window's frame. Rendered tells the window's header to follow, after every fill.
        public event Action Rendered;
        // The edge the card asks for: Warning while a session needs you, Turn while one waits for your reply, otherwise
        // none.
        public string Edge { get; private set; }
        // A new palette: the card's tints are worked out again.
        public void Restyle() { built = ""; Rebuild(); }
        public bool Animating { get { return rolling != null; } }
        public PitClock Clock { get { return clock; } }
        // For the checks: each shown garage and its cars, in card coordinates.
        public List<KeyValuePair<FrameworkElement, List<FrameworkElement>>> Garages() { return garages.Select(g => new KeyValuePair<FrameworkElement, List<FrameworkElement>>(g.Tap, g.Cars.Select(c => (FrameworkElement)c.Sprite).ToList())).ToList(); }
        public IEnumerable<PitSprite> Sprites { get { return cars.Select(c => c.Sprite).Concat(garages.Select(g => g.Sprite)); } }

        public void Fit(Rect next, double scale) { bounds = next; textScale = scale; Rebuild(); }
        public void Update(ClaudeView next)
        {
            view = next;
            if (rolling == null && next != null) { collapsed.Clear(); collapsed.UnionWith(next.Collapsed); }
            Rebuild();
        }
        public void Configure(bool use24Hour, ClaudePeriod chosen, bool apiValue = false, string[] facts = null)
        {
            use24 = use24Hour;
            var kinds = ClaudeFactKinds.Clean(facts);
            if (chosen != period || apiValue != showApi || !kinds.SequenceEqual(factKinds)) { period = chosen; showApi = apiValue; factKinds = kinds; built = ""; Rebuild(); } else Render();
        }
        // Settings → Claude → the side panel's four facts (package 2).
        private string[] factKinds = ClaudeFactKinds.Default;
        public string[] FactKinds { get { return factKinds; } }
        // Settings → Claude → Show what the tokens would cost at API list prices (off by default; decision 019): the
        // ledger's third number column shows that, or else each thread's replies.
        private bool showApi;
        public bool ShowsApiValue { get { return showApi; } }
        // The heartbeat: the "2 min" counts, and whether the cars keep still (Quiet mode with Settings → Claude → Keep
        // the cars still in Quiet mode; decision 019). The cars move on the card's own frame clock.
        public void Tick(bool still, bool render = true)
        {
            quiet = still;
            if (render) Render();
        }
        // Whether the card may move: shown on the page, the window not minimised, and the cars not kept still.
        public void SetLive(bool shown)
        {
            live = shown && !quiet;
            clock.SetLive(live && host.IsVisible);
        }
        // Package 4.6: Settings → Claude → Plain list. The same threads, states and numbers as a list of text rows, with no
        // cars or garages to animate; the screen-reader path, since every row is plain words.
        private bool plain;
        public bool Plain
        {
            get { return plain; }
            set { if (value == plain) return; plain = value; built = ""; if (plain) EndRoll(false); Rebuild(); }
        }
        public void ShowPeriod(ClaudePeriod next) { period = next; if (choosePeriod != null) choosePeriod(next); Rebuild(); }

        // Folds a project's cars into its garage or rolls them out (a tap on the garage). Returns whether it's folded now.
        public bool Toggle(string folder)
        {
            if (rolling != null || folder == null) return collapsed.Contains(folder ?? "");
            bool fold = !collapsed.Contains(folder);
            var garage = garages.FirstOrDefault(g => String.Equals(g.Folder, folder, StringComparison.OrdinalIgnoreCase));
            bool motion = !quiet && live && host.IsLoaded && !Program.Rendering;
            if (fold && motion && garage != null && garage.Cars.Count > 0) { StartRoll(garage, false, () => SetCollapsed(folder, true)); return true; }
            if (!fold && motion) rollOut = folder;
            SetCollapsed(folder, fold);
            return fold;
        }
        private void SetCollapsed(string folder, bool fold)
        {
            if (fold) collapsed.Add(folder); else collapsed.Remove(folder);
            if (view != null) { if (fold) view.Collapsed.Add(folder); else view.Collapsed.Remove(folder); }
            if (collapse != null) collapse(folder, fold);
            Rebuild();
        }

        private void Rebuild()
        {
            if (bounds.IsEmpty || rolling != null) return;
            string key = Math.Round(bounds.Width) + "x" + Math.Round(bounds.Height) + "|" + textScale + "|" + period + "|" + plain + "|" + Layout();
            if (key != built) { built = key; Build(); }
            Render();
            if (rollOut != null)
            {
                string folder = rollOut; rollOut = null;
                var garage = garages.FirstOrDefault(g => String.Equals(g.Folder, folder, StringComparison.OrdinalIgnoreCase));
                if (garage != null && garage.Cars.Count > 0)
                {
                    foreach (var c in garage.Cars) c.Sprite.Visibility = Visibility.Hidden;
                    garage.Sprite.Visibility = Visibility.Hidden;
                    host.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => StartRoll(garage, true, null)));
                }
            }
        }
        // What decides the design's parts: which threads and projects, which areas have something.
        private string Layout()
        {
            if (view == null) return "-";
            var groups = String.Join(";", view.Groups(period).Select(g => g.Folder + (collapsed.Contains(g.Folder) ? "!" : "") + ":" + String.Join(",", g.Threads.Select(t => t.Id + (t.Running && t.CanFocus ? "*" : "")))));
            // The attention queue's threads and kinds (issue 1): a thread joining or leaving it rebuilds the strip.
            var attention = String.Join(",", view.Attention(DateTime.UtcNow).Select(a => a.Thread.Id + ":" + a.Kind + (a.Thread.CanFocus ? "*" : "")));
            return view.Connected + "|" + view.Sample + "|" + String.Join(",", view.Sessions.Select(s => s.Id + ":" + s.CanFocus)) + "|" + groups + "|" + attention + "|" + attentionId
                + "|" + (view.Limits != null && view.Limits.Known) + view.NoPlan + view.Plan + "|" + ClaudeText.PlanChips(view.Limits, DateTime.UtcNow, use24).Count + "|" + view.LogsFailed + "|" + String.Join(",", view.Lanes.Select(l => l.Folder)) + "|" + view.Facts.Telemetry + "|" + (view.Days != null);
        }

        private void Build()
        {
            EndRoll(false);
            host.Children.Clear(); names.Clear(); cars.Clear(); garages.Clear(); fills.Clear(); clock.Clear(); askStrip = null; ledgerScroll = null;
            double width = Math.Max(1, bounds.Width - 48), height = Math.Max(1, bounds.Height - 48);
            design = CardSizing.Claude(bounds.Width, bounds.Height);
            FrameworkElement content;
            if (view == null) content = Caption("Reading Claude Code…");
            else if (plain) content = BuildPlain(width);
            else content = design == CardSizing.Design.Small ? BuildSmall(width, height) : design == CardSizing.Design.Medium ? BuildMedium(width, height) : BuildLarge(width, height);
            host.Children.Add(names.Add(content, "ClaudeView"));
            layer = new Canvas { IsHitTestVisible = false, ClipToBounds = false };
            // Under the card's content, so cars rolling past a row go under its chip and words (review fix 8).
            host.Children.Insert(0, layer);
        }

        // ----- Parts -----

        // Every size of words on the card follows Settings' text size, 100 to 140% (critique issue 4).
        private double Scaled(double size) { return Math.Round(size * textScale); }
        private new TextBlock Caption(string text) { return Text(text, Scaled(13), "Muted"); }
        private static Run Colored(string text, string brush) { var run = new Run(text); run.SetResourceReference(TextElement.ForegroundProperty, brush); return run; }
        // Whether words fit a width at a block's font, measured as WPF would lay them out.
        private static bool Fits(TextBlock block, string text, double room)
        {
            if (String.IsNullOrEmpty(text)) return true;
            var probe = new TextBlock { Text = text, FontFamily = block.FontFamily, FontSize = block.FontSize, FontWeight = block.FontWeight };
            probe.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            return probe.DesiredSize.Width <= room;
        }
        private TextBlock Trimmed(TextBlock block) { block.TextTrimming = TextTrimming.CharacterEllipsis; return block; }
        private static TextBlock Right(TextBlock block) { block.TextAlignment = TextAlignment.Right; block.HorizontalAlignment = HorizontalAlignment.Stretch; block.VerticalAlignment = VerticalAlignment.Center; return block; }
        private static TextBlock Upper(string text, double size) { var block = Text(text.ToUpperInvariant(), size, "Muted"); block.TextTrimming = TextTrimming.CharacterEllipsis; return block; }

        // A car sprite for a thread, registered for its frames.
        private CarPart Car(ClaudeSession thread, string folder, double width, string name, bool dim)
        {
            var sprite = names.Add(new PitSprite { Width = width, Height = Math.Round(width * 60 / 140), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, name);
            double ringSize = Math.Max(10, Math.Min(24, Math.Round(width * 0.26)));
            var ring = names.Add(new ContextRing { Width = ringSize, Height = ringSize, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Margin = new Thickness(0, -ringSize / 3, -ringSize / 4, 0) }, name + "Context");
            // The ring hides with its car while a garage rolls the cars in or out.
            ring.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding("Visibility") { Source = sprite });
            // Package 3: the pit crew and task stickers, hidden with the car like its ring.
            var crew = names.Add(new PitCrew { IsHitTestVisible = false, ShowTasks = width >= 40 }, name + "Crew");
            crew.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding("Visibility") { Source = sprite });
            var both = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            both.Children.Add(sprite); both.Children.Add(crew); both.Children.Add(ring);
            var part = new CarPart { Id = thread == null ? null : thread.Id, Folder = folder, Sprite = sprite, Ring = ring, Crew = crew, View = both, Width = width, Dim = dim };
            cars.Add(part); clock.Add(sprite);
            return part;
        }
        // A thread as a button (critique issue 4): Tab reaches every row and car, a screen reader reads its name, and
        // when its window is known a click, Enter or Space brings it forward. A row with no window to show says so.
        private Button Tappable(FrameworkElement child, ClaudeSession session)
        {
            var area = Pressable(child);
            if (session == null) { area.Focusable = false; return area; }
            bool live = session.Running && session.CanFocus;
            string key = session.Id;
            AutomationProperties.SetName(area, ThreadName(session, DateTime.UtcNow));
            fills.Add(now => {
                var t = view.Threads.FirstOrDefault(x => x.Id == key) ?? view.Sessions.FirstOrDefault(x => x.Id == key);
                if (t != null) AutomationProperties.SetName(area, ThreadName(t, now));
            });
            if (live) { string id = session.Id; area.Cursor = Cursors.Hand; area.Click += (sender, e) => { e.Handled = true; focus(id); }; }
            return area;
        }
        // What a screen reader says for a thread's row or car, ended and idle ones too.
        private string ThreadName(ClaudeSession t, DateTime now)
        {
            string title = ClaudeText.ThreadTitle(t, now, use24);
            string state = t.Running ? ClaudeText.Chip(t) : "Ended, last active " + ClaudeText.Ago(t.LastActive, now, use24);
            return title + ", " + t.ProjectName + ". " + state + (t.Running && t.CanFocus ? ". Click to show its window." : ". No window to show.");
        }
        // A plain button around a row, car or garage: no chrome of its own, the theme's text and font, and a 2-unit Text
        // outline only when the keyboard brings focus to it.
        private static Button Pressable(FrameworkElement child)
        {
            return new Button { Style = PressStyle, Content = child };
        }
        private static Style pressStyle;
        private static Style PressStyle
        {
            get
            {
                if (pressStyle != null) return pressStyle;
                var chrome = new FrameworkElementFactory(typeof(Border));
                chrome.SetValue(Border.BackgroundProperty, Brushes.Transparent);
                chrome.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
                var ring = new FrameworkElementFactory(typeof(Border));
                ring.SetValue(Border.BorderThicknessProperty, new Thickness(2));
                ring.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
                ring.SetValue(FrameworkElement.MarginProperty, new Thickness(-2));
                ring.SetResourceReference(Border.BorderBrushProperty, "Text");
                var focusStyle = new Style(typeof(Control));
                focusStyle.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Control)) { VisualTree = ring }));
                var style = new Style(typeof(Button));
                style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Button)) { VisualTree = chrome }));
                style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, focusStyle));
                style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Text")));
                style.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily("Segoe UI")));
                style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
                style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch));
                style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Arrow));
                style.Seal();
                return pressStyle = style;
            }
        }
        // A thread's tip, written when it opens from the view shown then (package 3), so it never shows the numbers of
        // the view the row was built from.
        private void LiveTip(FrameworkElement area, string id)
        {
            area.ToolTip = " ";
            area.ToolTipOpening += (sender, e) => {
                var t = view == null ? null : view.Threads.FirstOrDefault(x => x.Id == id);
                if (t == null) { e.Handled = true; return; }
                area.ToolTip = ClaudeText.ThreadTip(t, period, DateTime.UtcNow, use24, Counted, showApi);
            };
        }
        // For the checks: the tip a thread's row would show now.
        public string TipFor(string id)
        {
            var t = view == null ? null : view.Threads.FirstOrDefault(x => x.Id == id);
            return t == null ? null : ClaudeText.ThreadTip(t, period, DateTime.UtcNow, use24, Counted, showApi);
        }
        // A project's garage: a tap rolls its cars in or out.
        private GaragePart Garage(ClaudeProjectGroup group, double size, string name)
        {
            var sprite = names.Add(new PitSprite { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, name);
            var tap = Pressable(sprite); tap.Cursor = Cursors.Hand; tap.MinWidth = 48; tap.MinHeight = 48;
            string folder = group.Folder;
            bool folded = collapsed.Contains(folder);
            tap.ToolTip = folded ? "Roll " + group.Name + "'s cars out" : "Roll " + group.Name + "'s cars into the garage";
            AutomationProperties.SetName(tap, group.Name + ", " + ClaudeText.Threads(group.Threads.Count) + (folded ? ", in the garage. Click to roll them out." : ". Click to roll them into the garage."));
            tap.Click += (sender, e) => { e.Handled = true; Toggle(folder); };
            var part = new GaragePart { Folder = folder, Sprite = sprite, Tap = names.Add(tap, name + "Tap") };
            garages.Add(part); clock.Add(sprite);
            return part;
        }
        private FrameworkElement Dot(bool warn)
        {
            var dot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            dot.SetResourceReference(Shape.FillProperty, warn ? "Warning" : "Accent");
            return dot;
        }
        // ----- Large -----

        private FrameworkElement BuildLarge(double width, double height)
        {
            var attention = view.Attention(DateTime.UtcNow);
            bool asking = attention.Count > 0;
            // Rows 0 and 1 were the heading, which the window's header took over (phase 4).
            var root = Rows(new GridLength(0), new GridLength(0), Auto, Auto, Auto, new GridLength(12), Star);
            root.Children.Add(At(PlanRow(width), 2));
            // Package 2: more narrower weekly windows, extra usage and a gateway's spend limit, as chips. They step
            // aside while a session needs you, so the ask strip and the ledger keep their room.
            if (!asking && ClaudeText.PlanChips(view.Limits, DateTime.UtcNow, use24).Count > 0) root.Children.Add(At(PlanChips(), 3));
            if (asking) { askStrip = AttentionStrip(attention); askStrip.Margin = new Thickness(0, 12, 0, 0); root.Children.Add(At(askStrip, 4)); }
            bool side = CardSizing.ClaudeSide(width);
            var ledger = side ? Columns(Star, new GridLength(16), new GridLength(CardSizing.ClaudeSideWidth)) : Columns(Star);
            ledger.Children.Add(Ledger(side ? width - 16 - CardSizing.ClaudeSideWidth : width));
            if (side) ledger.Children.Add(At(SidePanel(), 0, 2));
            root.Children.Add(At(names.Add(ledger, "ClaudeLedgerArea"), 6));
            return root;
        }
        // The chips under the plan row: "Opus week 18% · resets Thu", "Extra usage 25% · 12.40 of 50", …
        private FrameworkElement PlanChips()
        {
            var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            var blocks = new List<TextBlock>();
            for (int i = 0; i < ClaudeText.MostPlanChips; i++)
            {
                var text = Trimmed(Text("", Scaled(12), "Muted"));
                var chip = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 4), Child = text, BorderThickness = new Thickness(1) };
                chip.SetResourceReference(Border.BorderBrushProperty, "Track");
                blocks.Add(text); row.Children.Add(names.Add(chip, "ClaudePlanChip" + i));
            }
            fills.Add(now => {
                var chips = ClaudeText.PlanChips(view.Limits, now, use24);
                for (int i = 0; i < blocks.Count; i++)
                {
                    var chip = (Border)row.Children[i];
                    chip.Visibility = i < chips.Count ? Visibility.Visible : Visibility.Collapsed;
                    if (i >= chips.Count) continue;
                    blocks[i].Text = chips[i].Key; blocks[i].SetResourceReference(TextBlock.ForegroundProperty, chips[i].Value ? "Warning" : "Muted");
                }
            });
            return names.Add(row, "ClaudePlanChips");
        }

        // ----- Plain list (package 4.6) -----

        // The plan in one line, then each project with its threads under it: state, name, since when, tokens. A row
        // with a window to show is a button. Numbers follow on every render; a change of threads rebuilds the list.
        private FrameworkElement BuildPlain(double width)
        {
            var list = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            AutomationProperties.SetName(list, "Claude Code threads, plain list");
            var plan = names.Add(Trimmed(Text("", Scaled(15), "Text")), "ClaudePlainPlan"); plan.TextWrapping = TextWrapping.Wrap; plan.Margin = new Thickness(0, 0, 0, 12);
            list.Children.Add(plan);
            fills.Add(now => { plan.Text = ClaudeText.PlainPlan(view, now, use24); AutomationProperties.SetName(plan, plan.Text); });
            var groups = view.Groups(period);
            if (groups.Count == 0) list.Children.Add(Caption(ClaudeText.NoThreads(period)));
            int index = 0;
            foreach (var group in groups)
            {
                var heading = Text(group.Name, Scaled(15), "Text"); heading.FontWeight = FontWeights.SemiBold; heading.Margin = new Thickness(0, index == 0 ? 0 : 14, 0, 4);
                var totals = Text("", Scaled(13), "Muted"); totals.Margin = new Thickness(10, 0, 0, 0);
                var head = new WrapPanel(); head.Children.Add(heading); head.Children.Add(totals); totals.VerticalAlignment = VerticalAlignment.Bottom; totals.Margin = new Thickness(10, 0, 0, 5);
                list.Children.Add(names.Add(head, "ClaudePlainProject" + index));
                string folder = group.Folder;
                fills.Add(now => {
                    var g = view.Groups(period).FirstOrDefault(x => String.Equals(x.Folder, folder, StringComparison.OrdinalIgnoreCase));
                    totals.Text = g == null ? "" : ClaudeText.PlainProject(g, Counted);
                    AutomationProperties.SetName(head, heading.Text + ". " + totals.Text);
                });
                int row = 0;
                foreach (var thread in group.Threads)
                {
                    list.Children.Add(PlainRow(thread, width, "ClaudePlainThread" + index + "_" + row));
                    row++;
                }
                if (group.Hidden > 0) { var hidden = Caption(group.Hidden + (group.Hidden == 1 ? " thread hidden by Settings" : " threads hidden by Settings")); hidden.Margin = new Thickness(0, 2, 0, 0); list.Children.Add(hidden); }
                index++;
            }
            // The rows are buttons that take focus, and a focused row scrolls itself into view, so the list itself
            // needn't be a tab stop. Like the ledger, it shows whole rows only and scrolls to the rest.
            var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, VerticalAlignment = VerticalAlignment.Top };
            var area = new Grid(); area.Children.Add(scroll);
            area.SizeChanged += (sender, e) => {
                var heights = list.Children.OfType<FrameworkElement>().Select(r => r.ActualHeight + r.Margin.Top + r.Margin.Bottom).ToList();
                double fit = CardSizing.ClaudeWholeRows(heights, area.ActualHeight);
                if (Double.IsNaN(scroll.Height) || Math.Abs(scroll.Height - fit) > 0.5) scroll.Height = fit;
            };
            return area;
        }
        private FrameworkElement PlainRow(ClaudeSession thread, double width, string name)
        {
            // The chip's column grows with the text size, so "NEEDS YOU" stays whole at 140%.
            var grid = Columns(new GridLength(Math.Min(Scaled(130), Math.Max(Scaled(96), width * 0.16))), Star, Auto);
            grid.Margin = new Thickness(0, 2, 0, 2);
            var chip = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var chipText = Text("", Scaled(12), "Text"); chip.Child = chipText; grid.Children.Add(chip);
            var words = new StackPanel { Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            var title = Trimmed(Text("", Scaled(14), "Text")); var detail = Trimmed(Text("", Scaled(12), "Muted"));
            words.Children.Add(title); words.Children.Add(detail); grid.Children.Add(At(words, 0, 1));
            var numbers = Right(Text("", Scaled(13), "Text")); grid.Children.Add(At(numbers, 0, 2));
            var area = Tappable(grid, thread);
            string id = thread.Id;
            fills.Add(now => {
                var t = view.Threads.FirstOrDefault(x => x.Id == id);
                if (t == null) return;
                chipText.Text = ClaudeText.Chip(t).ToUpperInvariant(); SetChip(chip, chipText, t.Pit);
                title.Text = ClaudeText.ThreadTitle(t, now, use24);
                detail.Text = ClaudeText.PlainDetail(t, view, now, use24);
                numbers.Text = ClaudeText.PlainNumbers(t, period, Counted);
                string said = ClaudeText.Chip(t) + ". " + title.Text + ". " + detail.Text + ". " + numbers.Text + (t.Running && t.CanFocus ? ". Click to show its window." : ". No window to show.");
                AutomationProperties.SetName(area, said);
            });
            return names.Add(area, name);
        }

        // The plan row: the 5-hour tank, the week's laps and the rev counter (from 900 wide).
        private FrameworkElement PlanRow(double width)
        {
            bool revs = CardSizing.ClaudeRevs(width), known = view.Limits != null && view.Limits.Known;
            // v0.53.0: the rev dial is gone; its sparkline and 7-day bars keep a narrow tile, so the tank and the week
            // share the rest and their time words fit.
            var row = revs ? Columns(Star, new GridLength(12), Star, new GridLength(12), new GridLength(CardSizing.ClaudeBurn(textScale))) : Columns(Star, new GridLength(12), Star);
            row.Height = CardSizing.ClaudePlan(textScale);
            double tile = revs ? (width - 24 - CardSizing.ClaudeBurn(textScale)) / 2 : (width - 12) / 2;
            if (known) { row.Children.Add(names.Add(TankTile(tile), "ClaudeTank")); row.Children.Add(At(names.Add(WeekTile(), "ClaudeWeekTile"), 0, 2)); }
            else
            {
                var message = names.Add(Wrapped(ClaudeText.LimitsMissing(view), Scaled(15), CardSizing.ClaudePlan(textScale) - 24), "ClaudeLimits");
                message.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(At(Tile(message, 12, new Thickness(14, 12, 14, 12)), 0, 0, 3));
            }
            if (revs) row.Children.Add(At(names.Add(BurnTile(), "ClaudeRevs"), 0, 4));
            return row;
        }
        private TextBlock Wrapped(string text, double size, double maxHeight)
        {
            var block = Text(text, size, "Muted"); block.TextWrapping = TextWrapping.Wrap; block.TextTrimming = TextTrimming.WordEllipsis; block.MaxHeight = Math.Max(18, maxHeight);
            return block;
        }
        private static TextBlock Big(string text) { var block = Reading(text, 40); block.SetResourceReference(TextBlock.ForegroundProperty, "Text"); return block; }
        private static void SetBig(TextBlock block, double percent, string brush)
        {
            block.Inlines.Clear(); block.Inlines.Add(Colored(Math.Round(percent).ToString(CultureInfo.InvariantCulture), brush));
            var unit = Colored("%", "Muted"); unit.FontSize = block.FontSize / 2; block.Inlines.Add(unit);
        }
        private Border TankTile(double width)
        {
            double size = width >= 380 ? 140 : width >= 290 ? 112 : 96;
            double room = Math.Max(60, width - size - 12 - 30);
            List<string> paceForms = null, resetForms = null;
            var gauge = names.Add(new PitGauge { Width = size, Height = size * 0.6, VerticalAlignment = VerticalAlignment.Center, TextScale = textScale }, "ClaudeTankGauge");
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            text.Children.Add(Upper("5-hour tank", Scaled(13)));
            var value = Big("—"); text.Children.Add(value);
            var pace = Trimmed(Text("", Scaled(13), "Muted")); text.Children.Add(pace);
            var reset = Trimmed(Text("", Scaled(13), "Muted")); text.Children.Add(reset);
            names.Add(value, "ClaudeFiveHourPercent"); names.Add(pace, "ClaudeFiveHourPace"); names.Add(reset, "ClaudeFiveHourReset");
            var row = Columns(Auto, Star); row.Children.Add(gauge); row.Children.Add(At(text, 0, 1));
            // The words are fitted again when the tile's width settles, so the longest form that fits shows.
            pace.SizeChanged += (sender, e) => { if (paceForms != null && Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5) Fit(pace, e.NewSize.Width, paceForms); };
            reset.SizeChanged += (sender, e) => { if (resetForms != null && Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5) Fit(reset, e.NewSize.Width, resetForms); };
            fills.Add(now => {
                var w = view.Limits.FiveHour;
                double pct = ClaudeText.Percent(w, now), elapsed = ClaudePace.Elapsed(w, ClaudePace.FiveHours, now), projection = ClaudePace.Projection(pct, elapsed);
                bool warn = ClaudePace.Warns(projection) || ClaudeText.Warns(pct);
                gauge.Value = pct / 100; gauge.Projection = Double.IsNaN(projection) ? Double.NaN : projection / 100; gauge.Pace = elapsed; gauge.Warn = pct >= 100; gauge.InvalidateVisual();
                if (w == null) value.Text = "—"; else SetBig(value, pct, ClaudeText.Warns(pct) ? "Warning" : "Text");
                // Package 2: when the tank fills at the last half hour's pace, if that's before it resets.
                // v0.53.0: pace in time, "≈ empty 3:21 am, 37 min before reset", in the longest form that fits.
                double width2 = pace.ActualWidth > 0 ? pace.ActualWidth : room;
                paceForms = ClaudeText.PaceTimes(projection, view.LimitEtaMinutes, w, now, use24);
                Fit(pace, width2, paceForms);
                pace.SetResourceReference(TextBlock.ForegroundProperty, warn || !Double.IsNaN(view.LimitEtaMinutes) ? "Warning" : "Muted");
                pace.ToolTip = Double.IsNaN(view.LimitEtaMinutes) ? (Double.IsNaN(projection) ? null : "Where the 5-hour window ends at this window's pace so far.") : "At the last half hour's pace, across every project, the 5-hour limit runs out before it resets.";
                resetForms = w == null ? new List<string> { ClaudeText.NoFiveHour } : ClaudeText.FiveHourResets(w, now, use24);
                Fit(reset, width2, resetForms);
                reset.SetResourceReference(TextBlock.ForegroundProperty, pct >= 100 ? "Warning" : "Muted");
            });
            return Tile(row, 12, new Thickness(12, 10, 14, 10));
        }
        private Border WeekTile()
        {
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var top = new DockPanel();
            var resets = Text("", Scaled(13), "Muted"); DockPanel.SetDock(resets, Dock.Right); top.Children.Add(resets);
            var lap = Upper("Week", Scaled(13)); top.Children.Add(lap);
            stack.Children.Add(top);
            var middle = Columns(Auto, Star); middle.Margin = new Thickness(0, 2, 0, 4);
            var value = Big("—"); middle.Children.Add(value);
            var notes = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var pace = Right(Trimmed(Text("", Scaled(13), "Muted"))); var extra = Right(Trimmed(Text("", Scaled(13), "Muted")));
            notes.Children.Add(pace); notes.Children.Add(extra); middle.Children.Add(At(notes, 0, 1));
            stack.Children.Add(middle);
            var laps = names.Add(new WeekLaps { Height = 16 + Math.Ceiling(Scaled(11) * 1.33), TextScale = textScale }, "ClaudeLaps"); stack.Children.Add(laps);
            names.Add(value, "ClaudeWeekPercent"); names.Add(pace, "ClaudeWeekPace"); names.Add(resets, "ClaudeWeekReset");
            fills.Add(now => {
                var w = view.Limits.Week;
                double pct = ClaudeText.Percent(w, now), elapsed = ClaudePace.Elapsed(w, ClaudePace.Week, now), projection = ClaudePace.Projection(pct, elapsed);
                lap.Text = ClaudeText.Lap(ClaudePace.LapNow(elapsed));
                resets.Text = w == null ? "" : w.Current(now) ? ClaudeText.WeekReset(w, use24) : "reset";
                if (w == null) value.Text = "—"; else SetBig(value, pct, ClaudeText.Warns(pct) ? "Warning" : "Text");
                Fit(pace, notes.ActualWidth > 0 ? notes.ActualWidth : 160, ClaudeText.PaceTimes(projection, Double.NaN, w, ClaudePace.Week, now, use24)); pace.SetResourceReference(TextBlock.ForegroundProperty, ClaudePace.Warns(projection) ? "Warning" : "Muted");
                extra.Text = ClaudeText.Extras(view.Limits, now);
                laps.Percent = pct; laps.Projection = projection; laps.Now = ClaudePace.LapNow(elapsed); laps.Days = ClaudeText.LapDays(w); laps.InvalidateVisual();
            });
            return Tile(stack, 12, new Thickness(14, 10, 14, 10));
        }
        // The burn tile (v0.53.0, the rev dial dropped): tokens a minute now, the last hour's sparkline and the 7-day bars.
        private Border BurnTile()
        {
            var text = new Grid { VerticalAlignment = VerticalAlignment.Center };
            foreach (var h in new[] { Auto, Auto, Auto, Auto }) text.RowDefinitions.Add(new RowDefinition { Height = h });
            text.Children.Add(Upper("Burn · tok/min", Scaled(13)));
            var value = Big("—"); text.Children.Add(At(value, 1));
            var burn = names.Add(new BurnLine { Height = 24, Margin = new Thickness(0, 2, 0, 2) }, "ClaudeBurn"); text.Children.Add(At(burn, 2));
            var foot = new DockPanel();
            DaySpark days = null;
            if (view.Days != null)
            {
                // Package 4: the last 7 days beside the hour, with last week's tick.
                days = names.Add(new DaySpark { Width = 62, Height = 16, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }, "ClaudeDays");
                DockPanel.SetDock(days, Dock.Right); foot.Children.Add(days);
            }
            foot.Children.Add(names.Add(Trimmed(Text("last 60 min", Scaled(13), "Muted")), "ClaudeBurnFoot"));
            text.Children.Add(At(foot, 3));
            fills.Add(now => {
                string rate = ClaudeUsage.Format((long)view.Rate);
                value.Inlines.Clear();
                string digits = rate.TrimEnd('k', 'M', 'B'), unit = rate.Substring(digits.Length);
                value.Inlines.Add(Colored(digits, view.Rate >= view.Scale * ClaudePace.Redline ? "Warning" : "Text"));
                if (unit.Length > 0) { var small = Colored(unit, "Muted"); small.FontSize = 20; value.Inlines.Add(small); }
                value.ToolTip = "Tokens a minute across every session now; the scale tops out at " + ClaudeUsage.Format((long)view.Scale) + ".";
                burn.Values = view.Burn; burn.Maximum = view.Scale; burn.InvalidateVisual();
                if (days != null && view.Days != null)
                {
                    days.Values = view.Days; days.Tick = view.LastWeekByNow; days.InvalidateVisual();
                    days.ToolTip = ClaudeText.DaysTip(view.Days, view.LastWeekByNow, view.Sample);
                }
            });
            return Tile(text, 12, new Thickness(14, 10, 14, 10));
        }
        // Puts the first of the forms that fits the room into the block (the last one, trimmed, when none does).
        private static void Fit(TextBlock block, double room, IList<string> forms)
        {
            if (forms == null || forms.Count == 0) { block.Text = ""; return; }
            foreach (var form in forms) if (Wide(block, form) <= room + 0.5) { block.Text = form; return; }
            block.Text = forms[forms.Count - 1];
        }
        // A text's width in the block's font, measured as the window draws it (the header's way, AppController.Measure:
        // a probe in the block's text formatting mode, rounded up, plus a unit of slack).
        public static double Wide(TextBlock block, string text)
        {
            if (String.IsNullOrEmpty(text)) return 0;
            var probe = new TextBlock { Text = text, FontFamily = block.FontFamily, FontSize = block.FontSize, FontWeight = block.FontWeight, FontStyle = block.FontStyle, FontStretch = block.FontStretch };
            TextOptions.SetTextFormattingMode(probe, TextOptions.GetTextFormattingMode(block));
            probe.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            return Math.Ceiling(probe.DesiredSize.Width) + 1;
        }

        // The pit stop strip: the thread that waited on you longest, what it wants, and Go to it.
        // The attention queue (issue 1): the strip shows one thread at a time, "1 of 3" with Next when more wait. Needs
        // you and Stuck are Amber; Your turn (10+ min) and silent working threads are calm. Empty means no strip.
        private string attentionId;
        private Border AttentionStrip(List<ClaudeAttention> queue)
        {
            int at = Math.Max(0, queue.FindIndex(a => a.Thread.Id == attentionId));
            var item = queue[at]; var thread = item.Thread; string kind = item.Kind;
            bool loud = kind == ClaudeAttention.Need || kind == ClaudeAttention.Stuck;
            var strip = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 6, 8, 6), BorderThickness = new Thickness(1), Height = Math.Max(60, Scaled(16) * 1.35 + Scaled(13) * 1.35 + 14) };
            string tint = loud ? "Warning" : kind == ClaudeAttention.Turn && host.TryFindResource("Turn") is SolidColorBrush ? "Turn" : "Muted";
            strip.Background = Tint(tint, loud ? 0.12 : 0.08); strip.BorderBrush = Tint(tint, loud ? 0.45 : 0.35);
            var row = Columns(Auto, Star, Auto, Auto);
            var car = Car(thread, thread.Project, 62, "ClaudeAskCar", false);
            car.View.Margin = new Thickness(0, 0, 12, 0);
            row.Children.Add(car.View);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var title = names.Add(Trimmed(Text("", Scaled(16), "Text")), "ClaudeAskTitle"); title.FontWeight = FontWeights.SemiBold; words.Children.Add(title);
            var reason = names.Add(Trimmed(Text("", Scaled(13), loud ? "Warning" : "Muted")), "ClaudeAskReason"); words.Children.Add(reason);
            row.Children.Add(At(words, 0, 1));
            if (queue.Count > 1)
            {
                var count = names.Add(Text((at + 1) + " of " + queue.Count, Scaled(13), "Muted"), "ClaudeAskCount"); count.VerticalAlignment = VerticalAlignment.Center; count.Margin = new Thickness(12, 0, 0, 0);
                var next = new Button { Content = "Next", MinHeight = 32, MinWidth = 64, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                next.SetResourceReference(Control.FontSizeProperty, "SmallSize");
                string following = queue[(at + 1) % queue.Count].Thread.Id;
                next.Click += (sender, e) => { attentionId = following; built = ""; Rebuild(); };
                AutomationProperties.SetName(next, "Next of " + queue.Count + " threads waiting for attention");
                var counter = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                counter.Children.Add(count); counter.Children.Add(names.Add(next, "ClaudeAskNext"));
                row.Children.Add(At(counter, 0, 2));
            }
            if (thread.CanFocus)
            {
                var go = new Button { Content = "Go to it", MinHeight = 48, MinWidth = 96, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, BorderThickness = new Thickness(0) };
                if (loud) { go.SetResourceReference(Control.BackgroundProperty, "Warning"); go.Foreground = OnWarning(); }
                go.SetResourceReference(Control.FontSizeProperty, "SmallSize");
                string id = thread.Id; go.Click += (sender, e) => focus(id);
                AutomationProperties.SetName(go, kind == ClaudeAttention.Need ? "Go to the thread that needs you" : "Go to " + ClaudeText.AttentionWord(kind).ToLowerInvariant() + " thread");
                row.Children.Add(At(names.Add(go, "ClaudeAskGo"), 0, 3));
            }
            strip.Child = row;
            fills.Add(now => {
                var t = view.Sessions.FirstOrDefault(x => x.Id == thread.Id) ?? thread;
                title.Text = ClaudeText.AttentionTitle(kind, ClaudeText.ThreadTitle(t, now, use24), t.ProjectName);
                reason.Text = ClaudeText.AttentionReason(kind, t, view, now, use24);
                AutomationProperties.SetName(strip, title.Text + ". " + reason.Text + (queue.Count > 1 ? ". " + (at + 1) + " of " + queue.Count + "." : "."));
            });
            return names.Add(strip, "ClaudeAsk");
        }
        // The words on an Amber fill: the palette's (white on paper), else the dark ink the dark palettes use.
        private Brush OnWarning() { return host.TryFindResource("OnWarning") as Brush ?? DarkInk; }
        private static readonly Brush DarkInk = Frozen(Color.FromRgb(0x11, 0x17, 0x0E));
        private static Brush Frozen(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }
        private Brush Tint(string key, double opacity)
        {
            var brush = host.TryFindResource(key) as SolidColorBrush;
            var color = brush == null ? Color.FromRgb(0xF5, 0xC4, 0x7D) : brush.Color;
            var tinted = new SolidColorBrush(Color.FromArgb((byte)Math.Round(opacity * 255), color.R, color.G, color.B)); tinted.Freeze();
            return tinted;
        }

        // The ledger's columns: car, project or thread, tokens, ≈ $, models and lines (the last two drop on narrow ones).
        private Grid LedgerRow(double width, double height)
        {
            var row = new Grid { Height = height };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = Star });
            if (Live)
            {
                // v0.53.0: For, Last sign, Now, Tokens, Lines.
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Scaled(60) + 10) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Scaled(88) + 10) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CardSizing.ClaudeLiveNow(width, textScale) ? CardSizing.ClaudeNowWidth(textScale) : 0) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Scaled(72) + 10) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CardSizing.ClaudeLiveLines(width, textScale) ? Scaled(84) + 10 : 0) });
                return row;
            }
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Scaled(72) + 10) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CardSizing.ClaudeCache(width, textScale) ? Scaled(80) + 10 : 0) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Scaled(64) + 10) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CardSizing.ClaudeModels(width, textScale) ? Scaled(150) + 10 : 0) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CardSizing.ClaudeLines(width, textScale) ? Scaled(84) + 10 : 0) });
            return row;
        }
        private FrameworkElement Ledger(double width)
        {
            var groups = view.Groups(period);
            var table = Rows(new GridLength(24), Star, Auto);
            var head = LedgerRow(width, 24);
            bool live = Live;
            var labels = live ? new[] { "", "Project · thread", "For", "Last sign", "Now", "Tokens", "Lines" } : new[] { "", "Project · thread", "Tokens", "Cache reads", showApi ? "API value" : "Replies", "Models", "Lines" };
            var tips = live ? new[] { "", "", ClaudeText.ForTip, ClaudeText.SignTip, ClaudeText.NowHeadTip, ClaudeText.TokensTip, "Lines added and removed, from Claude Code's logs" }
                : new[] { "", "", ClaudeText.TokensTip, ClaudeText.CacheTip, showApi ? ClaudeText.ApiValueTip : ClaudeText.RepliesTip, "The model each thread used; a mix shows its top two with their share of the tokens", "Lines added and removed, from Claude Code's logs" };
            int leftColumn = live ? 4 : 5;
            for (int i = 1; i < labels.Length; i++)
            {
                if (head.ColumnDefinitions[i].Width.Value == 0 && !head.ColumnDefinitions[i].Width.IsStar) continue;
                var label = Upper(labels[i], Scaled(12)); label.VerticalAlignment = VerticalAlignment.Center; if (i >= 2) { if (i != leftColumn) Right(label); label.Margin = new Thickness(10, 0, 0, 0); }
                names.Add(label, "ClaudeLedgerHead" + i);
                if (tips[i].Length > 0) label.ToolTip = tips[i];
                head.Children.Add(At(label, 0, i));
            }
            var line = new Border { BorderThickness = new Thickness(0, 0, 0, 1) }; line.SetResourceReference(Border.BorderBrushProperty, "ControlBorder"); Grid.SetColumnSpan(line, 7); head.Children.Add(line);
            table.Children.Add(head);
            var list = new StackPanel();
            if (groups.Count == 0) list.Children.Add(Empty(width));
            for (int i = 0; i < groups.Count; i++) AddGarageRows(list, groups[i], i, width);
            var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly, VerticalAlignment = VerticalAlignment.Top };
            ledgerScroll = scroll;
            table.Children.Add(At(names.Add(scroll, "ClaudeLedger"), 1));
            // Issue 1: when rows are clipped, a footer that never hides says what's out of view and takes you there.
            var moreText = Trimmed(Text("", Scaled(13), "Muted"));
            var more = new Button { Content = moreText, Height = MoreHeight, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 0, 8, 0), BorderThickness = new Thickness(0, 1, 0, 0), Background = Brushes.Transparent, Visibility = Visibility.Collapsed, Cursor = Cursors.Hand };
            more.SetResourceReference(Control.BorderBrushProperty, "ControlBorder");
            table.Children.Add(At(names.Add(more, "ClaudeLedgerMore"), 2));
            Action follow = () => UpdateMore(list, scroll, more, moreText);
            more.Click += (sender, e) => { double to = MoreTarget(list, scroll); if (!Double.IsNaN(to)) scroll.ScrollToVerticalOffset(to); };
            // Whole rows only: no row is cut through its text at the bottom (review fix 7). The footer's room is kept
            // only while the rows don't all fit, so it never covers one. It's worked out again when the card or the rows
            // change size and after a scroll, so the rows always end whole.
            Action fit = () => {
                if (table.ActualHeight <= 0) return;
                double room = table.ActualHeight - 24;
                bool over = list.Children.OfType<FrameworkElement>().Sum(r => Tall(r)) > room + 0.5;
                more.Visibility = over ? Visibility.Visible : Visibility.Collapsed;
                FitWhole(list, scroll, over ? room - MoreHeight : room);
                follow();
            };
            scroll.ScrollChanged += (sender, e) => { if (Math.Abs(e.VerticalChange) > 0.1) fit(); else follow(); };
            fills.Add(now => follow());
            table.SizeChanged += (sender, e) => fit();
            list.SizeChanged += (sender, e) => fit();
            return table;
        }
        private double MoreHeight { get { return Math.Max(28, Math.Ceiling(Scaled(13) * 1.4 + 10)); } }
        // The ledger's rows out of view, below (or, at the bottom, above): thread rows by their thread, and a folded
        // garage by the threads in it.
        private sealed class OutOfView { public int Below, Above, Needs, Stuck, Turn, UpNeeds, UpStuck, UpTurn; public double FirstUrgent = Double.NaN; }
        private OutOfView Hidden(StackPanel list, ScrollViewer scroll)
        {
            var result = new OutOfView();
            if (view == null) return result;
            double top = 0, offset = scroll.VerticalOffset, bottom = offset + (Double.IsNaN(scroll.Height) ? scroll.ViewportHeight : scroll.Height);
            var threads = view.Threads.ToDictionary(t => t.Id);
            var groups = view.Groups(period);
            foreach (var row in list.Children.OfType<FrameworkElement>())
            {
                double h = Tall(row), at = top; top += h;
                bool below = at + h > bottom + 0.5, above = !below && at < offset - 0.5;
                if (!below && !above) continue;
                var tag = row.Tag as string; if (tag == null) continue;
                var these = new List<ClaudeSession>();
                ClaudeSession one;
                if (tag.StartsWith("g:")) { var g = groups.FirstOrDefault(x => String.Equals(x.Folder, tag.Substring(2), StringComparison.OrdinalIgnoreCase)); if (g != null && collapsed.Contains(g.Folder)) these.AddRange(g.Threads); }
                else if (threads.TryGetValue(tag, out one)) these.Add(one);
                if (these.Count == 0) continue;
                int needs = these.Count(t => t.Running && t.State == ClaudeState.NeedsYou), stuck = these.Count(t => t.Running && t.State == ClaudeState.Stuck), turn = these.Count(t => t.YourTurn);
                if (below) { result.Below += these.Count; result.Needs += needs; result.Stuck += stuck; result.Turn += turn; }
                else { result.Above += these.Count; result.UpNeeds += needs; result.UpStuck += stuck; result.UpTurn += turn; }
                if (needs + stuck + turn > 0 && Double.IsNaN(result.FirstUrgent)) result.FirstUrgent = at;
            }
            return result;
        }
        private static double Tall(FrameworkElement row) { return row.ActualHeight + row.Margin.Top + row.Margin.Bottom; }
        // Whole rows only, from wherever the ledger is scrolled to: the viewport ends at the last row that fits whole, so
        // neither the card's edge nor the footer cuts one (review fix 7, and the Lead on #13).
        private static void FitWhole(StackPanel list, ScrollViewer scroll, double room)
        {
            var rows = list.Children.OfType<FrameworkElement>().Select(Tall).ToList();
            double offset = scroll.VerticalOffset, top = 0;
            var from = new List<double>();
            foreach (double h in rows) { if (top + h > offset + 0.5) from.Add(from.Count == 0 ? top + h - offset : h); top += h; }
            if (from.Count == 0) from = rows;
            double fit = CardSizing.ClaudeWholeRows(from, room);
            if (Double.IsNaN(scroll.Height) || Math.Abs(scroll.Height - fit) > 0.5) scroll.Height = fit;
        }
        private void UpdateMore(StackPanel list, ScrollViewer scroll, Button more, TextBlock text)
        {
            if (more.Visibility != Visibility.Visible) return;
            var hidden = Hidden(list, scroll);
            text.Text = hidden.Below + hidden.Above == 0 ? "All rows shown" : ClaudeText.MoreRows(hidden.Below, hidden.Needs, hidden.Stuck, hidden.Turn, hidden.Above, hidden.UpNeeds, hidden.UpStuck, hidden.UpTurn);
            text.SetResourceReference(TextBlock.ForegroundProperty, hidden.Needs + hidden.Stuck + hidden.UpNeeds + hidden.UpStuck > 0 ? "Warning" : "Muted");
            AutomationProperties.SetName(more, text.Text + ". Press to scroll to them.");
        }
        // Where the footer takes you: the first row out of view with something waiting on you, else the first one below
        // (or the top, when everything hidden is above).
        private double MoreTarget(StackPanel list, ScrollViewer scroll)
        {
            var hidden = Hidden(list, scroll);
            if (!Double.IsNaN(hidden.FirstUrgent)) return hidden.FirstUrgent;
            if (hidden.Below > 0)
            {
                double top = 0, bottom = scroll.VerticalOffset + scroll.ViewportHeight;
                foreach (var row in list.Children.OfType<FrameworkElement>()) { double h = Tall(row); if (top + h > bottom + 0.5) return top; top += h; }
            }
            return 0;
        }
        private FrameworkElement Empty(double width)
        {
            var row = Columns(Auto, Star); row.Margin = new Thickness(0, 12, 0, 0);
            var car = Car(null, "", 62, "ClaudeIdleCar", true);
            row.Children.Add(car.View);
            string words = !view.Connected && view.Threads.Count == 0 ? ClaudeText.NotConnected : ClaudeText.NoThreads(period);
            var text = names.Add(Wrapped(words, Scaled(15), 60), "ClaudeMessage"); text.VerticalAlignment = VerticalAlignment.Center; text.Margin = new Thickness(16, 0, 0, 0);
            row.Children.Add(At(text, 0, 1));
            return row;
        }
        private void AddGarageRows(StackPanel list, ClaudeProjectGroup group, int index, double width)
        {
            bool folded = collapsed.Contains(group.Folder);
            double projectHeight = Math.Max(52, Math.Ceiling(Scaled(17) * 1.33 + Scaled(12) * 1.33 + 10)), threadHeight = Math.Max(36, Math.Ceiling(Scaled(14) * 1.33 + 10));
            var row = LedgerRow(width, projectHeight);
            var top = new Border { BorderThickness = new Thickness(0, index == 0 ? 0 : 1, 0, 0) }; top.SetResourceReference(Border.BorderBrushProperty, "ControlBorder"); Grid.SetColumnSpan(top, 7); row.Children.Add(top);
            var garage = Garage(group, 40, "ClaudeGarage" + index);
            row.Children.Add(garage.Tap);
            var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            var title = names.Add(Trimmed(Text(group.Name, Scaled(17), "Text")), "ClaudeProject" + index); title.FontWeight = FontWeights.SemiBold; name.Children.Add(title);
            TextBlock info;
            if (folded)
            {
                info = Trimmed(Text("", Scaled(12), "Muted"));
                info.Inlines.Add(new InlineUIContainer(Bubble(group.Threads.Count)) { BaselineAlignment = BaselineAlignment.Center });
                info.Inlines.Add(new Run(" " + ClaudeText.Cars(group.Threads.Count) + " in the garage · click to roll them out"));
            }
            else info = Trimmed(Text(ClaudeText.GroupDetail(group), Scaled(12), "Muted"));
            name.Children.Add(names.Add(info, "ClaudeProjectDetail" + index));
            var share = new MeterBar { Height = 4, Margin = new Thickness(0, 4, 0, 0), Fill = Solid(PitArt.Accent(group.Name)) };
            share.SetResourceReference(MeterBar.TrackProperty, "Track");
            name.Children.Add(share);
            row.Children.Add(At(name, 0, 1));
            bool live = Live;
            var tokens = Right(Text("", Scaled(17), "Text")); tokens.Margin = new Thickness(10, 0, 0, 0); row.Children.Add(At(tokens, 0, live ? 5 : 2));
            var cached = Right(Text("", Scaled(15), "Muted")); cached.Margin = new Thickness(10, 0, 0, 0); if (!live && CardSizing.ClaudeCache(width, textScale)) row.Children.Add(At(cached, 0, 3));
            var dollars = Right(Text("", Scaled(15), "Muted")); dollars.Margin = new Thickness(10, 0, 0, 0); if (!live) row.Children.Add(At(dollars, 0, 4));
            var mix = Trimmed(Text("", Scaled(12), "Muted")); mix.VerticalAlignment = VerticalAlignment.Center; mix.Margin = new Thickness(10, 0, 0, 0); if (!live && CardSizing.ClaudeModels(width, textScale)) row.Children.Add(At(mix, 0, 5));
            var lines = Right(Text("", Scaled(12), "Muted")); lines.Margin = new Thickness(10, 0, 0, 0); if (live ? CardSizing.ClaudeLiveLines(width, textScale) : CardSizing.ClaudeLines(width, textScale)) row.Children.Add(At(lines, 0, 6));
            row.Tag = "g:" + group.Folder;
            list.Children.Add(names.Add(row, "ClaudeProjectRow" + index));
            string folder = group.Folder;
            fills.Add(now => {
                var g = view.Groups(period).FirstOrDefault(x => String.Equals(x.Folder, folder, StringComparison.OrdinalIgnoreCase));
                if (g == null) return;
                title.SetResourceReference(TextBlock.ForegroundProperty, g.NeedsYou ? "Warning" : "Text");
                if (!folded) info.Text = ClaudeText.GroupDetail(g);
                long total = view.Groups(period).Sum(x => x.Tokens);
                share.Value = total <= 0 ? 0 : 100.0 * g.Tokens / total;
                tokens.Text = TokenCell(g.Tokens); tokens.ToolTip = Unreadable; cached.Text = Marked(ClaudeUsage.Format(g.CacheReads)); cached.ToolTip = Unreadable; dollars.Text = Marked(showApi ? "$" + ClaudeText.Dollars(g.Cost) : g.Replies.ToString(CultureInfo.InvariantCulture)); dollars.ToolTip = Unreadable;
                SetModels(mix, g.Models);
                SetLines(lines, g.Added, g.Removed);
                RenderGarage(garage, g);
            });
            if (folded) return;
            for (int j = 0; j < group.Threads.Count; j++)
            {
                var thread = group.Threads[j];
                var trow = LedgerRow(width, threadHeight);
                var car = Car(thread, group.Folder, 48, "ClaudeCar" + index + "_" + j, false);
                trow.Children.Add(car.View);
                garage.Cars.Add(car);
                var what = new DockPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0), LastChildFill = true };
                var chip = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                var chipText = Text("", Scaled(11), "Text"); chip.Child = chipText; DockPanel.SetDock(chip, Dock.Left); what.Children.Add(chip);
                var where = Text("", Scaled(12), "Muted"); where.Margin = new Thickness(8, 0, 0, 0); where.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(where, Dock.Right); if (live ? CardSizing.ClaudeLiveWhere(width, textScale) : CardSizing.ClaudeWhere(width, textScale)) what.Children.Add(where);
                var name2 = names.Add(Trimmed(Text("", Scaled(14), "Text")), "ClaudeThreadTitle" + index + "_" + j); name2.VerticalAlignment = VerticalAlignment.Center; what.Children.Add(name2);
                trow.Children.Add(At(what, 0, 1));
                // v0.53.0: in 5 h and Today a thin context bar under every thread's title, always there (empty with a
                // tip when the context isn't known yet).
                MeterBar context = null;
                if (live)
                {
                    context = names.Add(new MeterBar { Height = 3, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(4, 0, 0, 3) }, "ClaudeThreadContext" + index + "_" + j);
                    context.SetResourceReference(MeterBar.TrackProperty, "Track");
                    trow.Children.Add(At(context, 0, 1));
                }
                var tfor = names.Add(Right(Text("", Scaled(14), "Text")), "ClaudeThreadFor" + index + "_" + j); tfor.Margin = new Thickness(10, 0, 0, 0);
                var tsign = names.Add(Right(Trimmed(Text("", Scaled(13), "Muted"))), "ClaudeThreadSign" + index + "_" + j); tsign.Margin = new Thickness(10, 0, 0, 0);
                var tnow = names.Add(Trimmed(Text("", Scaled(13), "Text")), "ClaudeThreadNow" + index + "_" + j); tnow.Margin = new Thickness(10, 0, 0, 0); tnow.VerticalAlignment = VerticalAlignment.Center;
                if (live) { trow.Children.Add(At(tfor, 0, 2)); trow.Children.Add(At(tsign, 0, 3)); if (CardSizing.ClaudeLiveNow(width, textScale)) trow.Children.Add(At(tnow, 0, 4)); }
                var ttokens = Right(Text("", Scaled(14), "Text")); ttokens.Margin = new Thickness(10, 0, 0, 0); trow.Children.Add(At(ttokens, 0, live ? 5 : 2));
                var tcached = Right(Text("", Scaled(14), "Muted")); tcached.Margin = new Thickness(10, 0, 0, 0); if (!live && CardSizing.ClaudeCache(width, textScale)) trow.Children.Add(At(tcached, 0, 3));
                var tdollars = Right(Text("", Scaled(14), "Muted")); tdollars.Margin = new Thickness(10, 0, 0, 0); if (!live) trow.Children.Add(At(tdollars, 0, 4));
                var tmix = Trimmed(Text("", Scaled(12), "Muted")); tmix.VerticalAlignment = VerticalAlignment.Center; tmix.Margin = new Thickness(10, 0, 0, 0); if (!live && CardSizing.ClaudeModels(width, textScale)) trow.Children.Add(At(tmix, 0, 5));
                var tlines = Right(Text("", Scaled(12), "Muted")); tlines.Margin = new Thickness(10, 0, 0, 0); if (live ? CardSizing.ClaudeLiveLines(width, textScale) : CardSizing.ClaudeLines(width, textScale)) trow.Children.Add(At(tlines, 0, 6));
                var tap = Tappable(trow, thread);
                LiveTip(tap, thread.Id);
                car.Row = tap; tap.Tag = thread.Id;
                list.Children.Add(names.Add(tap, "ClaudeThreadRow" + index + "_" + j));
                string id = thread.Id;
                fills.Add(now => {
                    var t = view.Threads.FirstOrDefault(x => x.Id == id);
                    if (t == null) return;
                    // Ended and parked rows step back by colour, not opacity, so their words keep the palette's contrast
                    // (critique issue 4); the car still fades.
                    bool dim = !t.Running || ClaudePace.Parked(t.Pit);
                    car.View.Opacity = ClaudePace.Parked(t.Pit) ? 0.45 : t.Running ? 1 : 0.62;
                    chipText.Text = ClaudeText.Chip(t).ToUpperInvariant();
                    SetChip(chip, chipText, t.Pit);
                    name2.Text = ClaudeText.ThreadTitle(t, now, use24);
                    name2.SetResourceReference(TextBlock.ForegroundProperty, t.Pit == PitState.Need ? "Warning" : dim ? "Muted" : "Text");
                    ttokens.SetResourceReference(TextBlock.ForegroundProperty, dim ? "Muted" : "Text");
                    // A stuck thread says why where it would say where it runs (decision 023).
                    bool stuck = t.Running && t.State == ClaudeState.Stuck;
                    // Live rows give the time its own labelled columns, so beside the title is only where it runs.
                    where.Text = stuck ? ClaudeText.StuckReason(t, view, now, use24) : live ? ClaudeText.Place(t) : ClaudeText.Where(t, now);
                    where.SetResourceReference(TextBlock.ForegroundProperty, stuck ? "Warning" : "Muted");
                    if (live)
                    {
                        tfor.Text = ClaudeText.StateFor(t, now);
                        tsign.Text = ClaudeText.Sign(t, now);
                        tsign.SetResourceReference(TextBlock.ForegroundProperty, ClaudeText.Silent(t, now) ? "Text" : "Muted");
                        tnow.Text = ClaudeText.NowWords(t, now); tnow.ToolTip = t.Running ? ClaudeText.NowTip(t, view.Facts != null && view.Facts.Telemetry) : null;
                        context.Value = Double.IsNaN(t.Context) ? 0 : t.Context;
                        context.SetResourceReference(MeterBar.FillProperty, !Double.IsNaN(t.Context) && t.Context >= 80 ? "Warning" : "Muted");
                        context.ToolTip = ClaudeText.ContextTip(t);
                        context.Visibility = t.Running ? Visibility.Visible : Visibility.Hidden;
                    }
                    ttokens.Text = TokenCell(t.TokensFor(period)); ttokens.ToolTip = Unreadable; tcached.Text = Marked(ClaudeUsage.Format(t.CacheReadsFor(period))); tcached.ToolTip = Unreadable; tdollars.Text = Marked(showApi ? "$" + ClaudeText.Dollars(t.CostFor(period)) : t.RepliesFor(period).ToString(CultureInfo.InvariantCulture)); tdollars.ToolTip = Unreadable;
                    SetModels(tmix, t.ModelsFor(period));
                    var l = t.LinesFor(period); SetLines(tlines, l[0], l[1]);
                });
            }
        }
        private bool Counted { get { return view.LogsRead && !view.LogsFailed; } }
        // 5 h and Today are the live status board; Week is the ledger (observability critique, issue 2).
        private bool Live { get { return period != ClaudePeriod.Week; } }
        // A token cell: "≈1.2M" while the logs are still being read, "—" with why in its tip when they can't be
        // (observability critique, issue 3).
        private string TokenCell(long tokens) { return Marked(ClaudeUsage.Format(tokens)); }
        // Cache reads, replies and the API value take the same mark as tokens (Lead's review of v0.50.0).
        private string Marked(string value) { return view.LogsFailed ? "—" : (Counted ? "" : "≈") + value; }
        private string Unreadable { get { return view.LogsFailed ? ClaudeText.NoLogs : null; } }
        private static Brush Solid(uint color) { var b = new SolidColorBrush(Color.FromArgb((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color)); b.Freeze(); return b; }
        private FrameworkElement Bubble(int count)
        {
            double size = Math.Max(20, Scaled(12) + 8);
            var bubble = new Border { MinWidth = size, Height = size, CornerRadius = new CornerRadius(size / 2), Padding = new Thickness(5, 0, 5, 0) };
            bubble.SetResourceReference(Border.BackgroundProperty, "Text");
            var number = Text(count.ToString(CultureInfo.InvariantCulture), Scaled(12), "Page"); number.FontWeight = FontWeights.Bold; number.HorizontalAlignment = HorizontalAlignment.Center; number.VerticalAlignment = VerticalAlignment.Center;
            bubble.Child = number;
            return bubble;
        }
        private void SetChip(Border chip, TextBlock text, PitState state)
        {
            chip.BorderThickness = new Thickness(state == PitState.End || state == PitState.Idle ? 1 : 0);
            chip.SetResourceReference(Border.BorderBrushProperty, "ControlBorder");
            if (state == PitState.Need) { chip.SetResourceReference(Border.BackgroundProperty, "Warning"); text.Foreground = OnWarning(); text.FontWeight = FontWeights.Bold; return; }
            text.FontWeight = FontWeights.Normal;
            if (state == PitState.Done)
            {
                // Claude Dashboard's palettes name the Glacier they read in (darker on paper); the widget app has none.
                var turn = host.TryFindResource("Turn") as SolidColorBrush;
                if (turn != null) { chip.Background = Tint("Turn", 0.14); text.Foreground = turn; }
                else { chip.Background = Solid(PitArt.Alpha(PitArt.Accents[1], 0.14)); text.Foreground = Solid(PitArt.Accents[1]); }
                return;
            }
            if (state == PitState.Stuck) { chip.Background = Tint("Warning", 0.16); text.SetResourceReference(TextBlock.ForegroundProperty, "Warning"); return; }
            if (state == PitState.End || state == PitState.Idle || ClaudePace.Parked(state)) { chip.Background = Brushes.Transparent; text.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); return; }
            chip.Background = Tint("Accent", 0.16); text.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        }
        // The Models column: the model by name, or a mix's top two with shares, each in its family's colour (Opus Lime,
        // Sonnet Glacier, Haiku Lavender); the tooltip lists every model.
        private static void SetModels(TextBlock block, List<KeyValuePair<string, double>> models)
        {
            block.Inlines.Clear();
            var parts = ClaudePace.ModelLabel(models);
            if (parts.Count == 0) { block.Inlines.Add(Colored("—", "Muted")); block.ToolTip = "No replies in this period"; return; }
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0) block.Inlines.Add(Colored(" · ", "Muted"));
                int family = ClaudePace.Family(parts[i].Value);
                block.Inlines.Add(new Run(parts[i].Key) { Foreground = Solid(family < 0 ? PitArt.Muted : MixBar.Colors[family]) });
            }
            block.ToolTip = String.Join(" · ", models.Select(m => m.Key + " " + Math.Round(m.Value * 100) + "%"));
        }
        private static void SetLines(TextBlock block, long added, long removed)
        {
            block.Inlines.Clear();
            if (added == 0 && removed == 0) { block.Inlines.Add(Colored("—", "Muted")); return; }
            block.Inlines.Add(new Run(ClaudeText.Added(added)) { Foreground = Solid(PitArt.Lime) });
            block.Inlines.Add(new Run(" " + ClaudeText.Removed(removed)) { Foreground = Solid(0xFFF09A9A) });
        }

        // The side panel: the 5-hour lanes and four facts for the period (Settings → Claude picks them; package 2).
        private FrameworkElement SidePanel()
        {
            var side = new StackPanel();
            var lanes = new StackPanel();
            lanes.Children.Add(Upper("Last 5 hours · laps of 10 min", Scaled(11)));
            var bins = new List<LaneBins>();
            foreach (var lane in view.Lanes)
            {
                bool thread = lane.Session.Length > 0;
                var row = Columns(new GridLength(Scaled(104)), Star); row.Margin = new Thickness(0, thread ? 3 : 6, 0, 0);
                var name = Trimmed(Text(thread ? "· " + lane.Name : lane.Name, Scaled(thread ? 11 : 12), thread ? "Muted" : "Text"));
                if (thread) name.Margin = new Thickness(8, 0, 0, 0);
                name.ToolTip = lane.Name;
                row.Children.Add(name);
                var b = new LaneBins { Height = thread ? 10 : 24, Margin = new Thickness(8, 0, 0, 0), Color = PitArt.Accent(ClaudeBridge.ProjectName(lane.Folder)), Bins = lane.Bins };
                bins.Add(b); row.Children.Add(At(b, 0, 1));
                lanes.Children.Add(row);
            }
            if (view.Lanes.Count == 0) { var none = Text("Nothing in the last 5 hours.", Scaled(12), "Muted"); none.Margin = new Thickness(0, 6, 0, 0); lanes.Children.Add(none); }
            var axis = Columns(new GridLength(Scaled(104)), Star); axis.Margin = new Thickness(0, 4, 0, 0);
            var times = new Grid { Margin = new Thickness(8, 0, 0, 0) };
            var start = Text("", Scaled(11), "Muted"); var middle = Text("", Scaled(11), "Muted"); middle.HorizontalAlignment = HorizontalAlignment.Center; var end = Text("now", Scaled(11), "Muted"); end.HorizontalAlignment = HorizontalAlignment.Right;
            times.Children.Add(start); times.Children.Add(middle); times.Children.Add(end);
            axis.Children.Add(At(times, 0, 1));
            lanes.Children.Add(axis);
            side.Children.Add(names.Add(Tile(lanes, 12, new Thickness(12, 10, 12, 10)), "ClaudeLanes"));
            var facts = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(4, 10, 4, 0) };
            var values = new TextBlock[4]; var keys = new TextBlock[4];
            for (int i = 0; i < 4; i++)
            {
                var fact = new WrapPanel { Margin = new Thickness(0, 0, 8, 4) };
                values[i] = Reading("", Scaled(20)); values[i].SetResourceReference(TextBlock.ForegroundProperty, "Text"); values[i].Margin = new Thickness(0, 0, 6, 0);
                keys[i] = Upper("", Scaled(11)); keys[i].VerticalAlignment = VerticalAlignment.Bottom; keys[i].Margin = new Thickness(0, 0, 0, 3);
                fact.Children.Add(values[i]); fact.Children.Add(keys[i]);
                facts.Children.Add(names.Add(fact, "ClaudeFact" + i));
            }
            side.Children.Add(facts);
            // "Telemetry off: port 43832 in use" (decision 023) is on the window's source-health line, at every size.
            fills.Add(now => {
                double max = Math.Max(1, view.Lanes.SelectMany(l => l.Bins).DefaultIfEmpty(0).Max());
                foreach (var b in bins) { b.Maximum = max; b.InvalidateVisual(); }
                start.Text = ClaudeText.Clock(now - ClaudePace.FiveHours, use24); middle.Text = ClaudeText.Clock(now - TimeSpan.FromMinutes(150), use24);
                // The four facts Settings → Claude picked, for the chosen period (package 2).
                for (int i = 0; i < 4; i++)
                {
                    string kind = factKinds[i];
                    var fact = ClaudeText.Fact(kind, view, period, Counted, showApi);
                    values[i].Inlines.Clear();
                    if (kind == ClaudeFactKinds.Lines && fact[0] != "—") values[i].Inlines.Add(new Run(fact[0]) { Foreground = Solid(PitArt.Lime) });
                    else values[i].Inlines.Add(new Run(fact[0]));
                    keys[i].Text = fact[1];
                    values[i].ToolTip = fact[2]; keys[i].ToolTip = fact[2];
                    bool warn = (kind == ClaudeFactKinds.Errors && view.FactsFor(period).Errors > 0) || (kind == ClaudeFactKinds.Failures && view.FactsFor(period).FailureRate >= 0.2);
                    values[i].SetResourceReference(TextBlock.ForegroundProperty, warn ? "Warning" : "Text");
                }
            });
            return names.Add(side, "ClaudeSide");
        }

        // ----- Medium -----

        // The two plan meters with pace, then the top garages with their cars inline (a waiting thread's first).
        private FrameworkElement BuildMedium(double width, double height)
        {
            // Rows 0 and 1 were the heading, which the window's header took over (phase 4).
            var root = Rows(new GridLength(0), new GridLength(0), Auto, Star);
            var tiles = Columns(Star, new GridLength(10), Star); tiles.Height = CardSizing.ClaudeMini(textScale);
            if (view.Limits != null && view.Limits.Known)
            {
                tiles.Children.Add(MiniTile(true, (width - 10) / 2));
                tiles.Children.Add(At(MiniTile(false, (width - 10) / 2), 0, 2));
            }
            else
            {
                var message = names.Add(Wrapped(ClaudeText.LimitsMissing(view), Scaled(13), CardSizing.ClaudeMini(textScale) - 16), "ClaudeLimits"); message.VerticalAlignment = VerticalAlignment.Center;
                tiles.Children.Add(At(Tile(message, 12, new Thickness(10, 8, 10, 8)), 0, 0, 3));
            }
            root.Children.Add(At(tiles, 2));
            var groups = view.Groups(period);
            double rowHeight = CardSizing.ClaudeMediumRow(textScale), room = height - CardSizing.ClaudeMini(textScale) - 6;
            int fit = Math.Max(1, (int)Math.Floor(room / rowHeight));
            var list = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            if (groups.Count == 0) list.Children.Add(Empty(width));
            bool money = width >= 400;
            // Issue 1: projects that don't fit are counted on a last row, with what in them is waiting on you.
            int rows = groups.Count > fit ? Math.Max(0, fit - 1) : groups.Count;
            for (int i = 0; i < rows; i++)
            {
                var group = groups[i];
                bool folded = collapsed.Contains(group.Folder);
                var row = new Grid { Height = rowHeight };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = Star });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Scaled(64) + 10) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(money ? Scaled(56) + 10 : 0) });
                var line = new Border { BorderThickness = new Thickness(0, 1, 0, 0) }; line.SetResourceReference(Border.BorderBrushProperty, "ControlBorder"); Grid.SetColumnSpan(line, 4); row.Children.Add(line);
                var garage = Garage(group, 36, "ClaudeGarage" + i);
                row.Children.Add(garage.Tap);
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
                var first = new DockPanel { LastChildFill = true };
                var inline = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                DockPanel.SetDock(inline, Dock.Right);
                double nameRoom = width - 48 - 6 - (Scaled(64) + 10) - (money ? Scaled(56) + 10 : 0);
                int shown = folded ? 0 : CardSizing.ClaudeInlineCars(nameRoom, group.Threads.Count);
                if (folded) inline.Children.Add(Bubble(group.Threads.Count));
                for (int j = 0; j < shown; j++)
                {
                    var car = Car(group.Threads[j], group.Folder, 30, "ClaudeCar" + i + "_" + j, true);
                    car.View.Margin = new Thickness(0, 0, 2, 0); car.OnlyWorst = true;
                    var tap = Tappable(car.View, group.Threads[j]); LiveTip(tap, group.Threads[j].Id);
                    car.Row = tap; inline.Children.Add(tap); garage.Cars.Add(car);
                }
                if (!folded && shown < group.Threads.Count) { var more = Caption("+" + (group.Threads.Count - shown)); more.VerticalAlignment = VerticalAlignment.Center; more.Margin = new Thickness(2, 0, 0, 0); inline.Children.Add(more); }
                first.Children.Add(inline);
                var name = names.Add(Trimmed(Text(group.Name, Scaled(15), "Text")), "ClaudeProject" + i); name.FontWeight = FontWeights.SemiBold; name.VerticalAlignment = VerticalAlignment.Center;
                first.Children.Add(name);
                text.Children.Add(first);
                var sub = names.Add(Trimmed(Text("", Scaled(12), "Muted")), "ClaudeProjectDetail" + i); text.Children.Add(sub);
                row.Children.Add(At(text, 0, 1));
                var tokens = Right(Text("", Scaled(15), "Text")); tokens.Margin = new Thickness(10, 0, 0, 0); row.Children.Add(At(tokens, 0, 2));
                var dollars = Right(Text("", Scaled(14), "Muted")); dollars.Margin = new Thickness(10, 0, 0, 0); if (money) row.Children.Add(At(dollars, 0, 3));
                list.Children.Add(names.Add(row, "ClaudeProjectRow" + i));
                string folder = group.Folder;
                fills.Add(now => {
                    var g = view.Groups(period).FirstOrDefault(x => String.Equals(x.Folder, folder, StringComparison.OrdinalIgnoreCase));
                    if (g == null) return;
                    var asking = g.Threads.FirstOrDefault(t => t.Running && t.State == ClaudeState.NeedsYou);
                    var stuck = asking == null ? g.Threads.FirstOrDefault(t => t.Running && t.State == ClaudeState.Stuck) : null;
                    name.SetResourceReference(TextBlock.ForegroundProperty, asking != null ? "Warning" : "Text");
                    sub.Text = asking != null ? "Needs you " + ClaudeText.Duration(now - asking.Since) + " · " + ClaudeText.ThreadTitle(asking, now, use24) : stuck != null ? "Stuck · " + ClaudeText.StuckReason(stuck, view, now, use24) : folded ? ClaudeText.Cars(g.Threads.Count) + " in the garage" : ClaudeText.GroupDetail(g, true);
                    sub.SetResourceReference(TextBlock.ForegroundProperty, asking != null || stuck != null ? "Warning" : "Muted");
                    tokens.Text = TokenCell(g.Tokens); tokens.ToolTip = Unreadable; dollars.Text = Marked(showApi ? "$" + ClaudeText.Dollars(g.Cost) : g.Replies.ToString(CultureInfo.InvariantCulture));
                    dollars.ToolTip = Unreadable ?? (showApi ? ClaudeText.ApiValueTip : ClaudeText.Replies(g.Replies));
                    RenderGarage(garage, g);
                });
            }
            if (rows < groups.Count)
            {
                var moreLine = new Border { Height = rowHeight, BorderThickness = new Thickness(0, 1, 0, 0) }; moreLine.SetResourceReference(Border.BorderBrushProperty, "ControlBorder");
                var moreText = names.Add(Trimmed(Text("", Scaled(13), "Muted")), "ClaudeMoreProjects"); moreText.VerticalAlignment = VerticalAlignment.Center; moreText.Margin = new Thickness(6, 0, 0, 0);
                moreLine.Child = moreText; list.Children.Add(moreLine);
                int from = rows;
                fills.Add(now => {
                    var rest = view.Groups(period).Skip(from).ToList();
                    var threads = rest.SelectMany(g => g.Threads).Where(t => t.Running).ToList();
                    int needs = threads.Count(t => t.State == ClaudeState.NeedsYou), stuck = threads.Count(t => t.State == ClaudeState.Stuck), turn = threads.Count(t => t.YourTurn);
                    moreText.Text = ClaudeText.MoreProjects(rest.Count, needs, stuck, turn);
                    moreText.SetResourceReference(TextBlock.ForegroundProperty, needs + stuck > 0 ? "Warning" : "Muted");
                    AutomationProperties.SetName(moreText, moreText.Text + ". Make the window bigger to see them.");
                });
            }
            root.Children.Add(At(names.Add(list, "ClaudeGarages"), 3));
            return root;
        }
        // A mini plan tile: the 5-hour or the week's percentage, its reset and pace, over a pace meter.
        private Border MiniTile(bool fiveHour, double tileWidth)
        {
            var stack = new StackPanel();
            var top = new DockPanel();
            var reset = Text("", Scaled(12), "Muted"); DockPanel.SetDock(reset, Dock.Right); top.Children.Add(reset);
            top.Children.Add(Upper(fiveHour ? "5-hour" : "Week", Scaled(12)));
            stack.Children.Add(top);
            var middle = new DockPanel { Margin = new Thickness(0, 2, 0, 4) };
            var pace = Text("", Scaled(12), "Muted"); pace.VerticalAlignment = VerticalAlignment.Bottom; pace.Margin = new Thickness(0, 0, 0, 4); DockPanel.SetDock(pace, Dock.Right); middle.Children.Add(pace);
            var value = Reading("—", 28); value.SetResourceReference(TextBlock.ForegroundProperty, "Text"); middle.Children.Add(value);
            stack.Children.Add(middle);
            var bar = new PaceBar { Height = 8 }; stack.Children.Add(bar);
            string name = fiveHour ? "ClaudeFiveHour" : "ClaudeWeek";
            names.Add(value, name + "Percent"); names.Add(reset, name + "Reset"); names.Add(pace, name + "Pace"); names.Add(bar, name + "Bar");
            fills.Add(now => {
                var w = fiveHour ? view.Limits.FiveHour : view.Limits.Week;
                double pct = ClaudeText.Percent(w, now), elapsed = ClaudePace.Elapsed(w, fiveHour ? ClaudePace.FiveHours : ClaudePace.Week, now), projection = ClaudePace.Projection(pct, elapsed);
                if (w == null) value.Text = "—"; else SetBig(value, pct, ClaudeText.Warns(pct) ? "Warning" : "Text");
                reset.Text = w == null ? (fiveHour ? "none now" : "") : !w.ResetsAt.HasValue ? "" : !w.Current(now) ? "reset" : pct >= 100 ? "back " + ClaudeText.When(w.ResetsAt.Value, now, use24) : ClaudeText.When(w.ResetsAt.Value, now, use24);
                reset.SetResourceReference(TextBlock.ForegroundProperty, pct >= 100 ? "Warning" : "Muted");
                // v0.53.0: in time, like the tank ("≈ empty 9:34 am", "≈ 76% by reset"), in what room the reading leaves.
                double tileRoom = (stack.ActualWidth > 0 ? stack.ActualWidth : tileWidth - 20) - ClaudeCard.Wide(value, value.Text.Length > 0 ? value.Text : "100%") - 8;
                Fit(pace, tileRoom, ClaudeText.PaceTimes(projection, fiveHour ? view.LimitEtaMinutes : Double.NaN, w, fiveHour ? ClaudePace.FiveHours : ClaudePace.Week, now, use24).Where(f => !f.Contains(", ")).ToList());
                pace.SetResourceReference(TextBlock.ForegroundProperty, ClaudePace.Warns(projection) ? "Warning" : "Muted");
                bar.Value = pct / 100; bar.Projection = Double.IsNaN(projection) ? Double.NaN : projection / 100; bar.Pace = elapsed; bar.Warn = ClaudePace.Warns(projection) || ClaudeText.Warns(pct); bar.InvalidateVisual();
            });
            return Tile(stack, 12, new Thickness(10, 8, 10, 8));
        }

        // ----- Small -----

        // The car that needs you (or the busiest running one), its project and state, then the 5-hour meter with pace.
        private FrameworkElement BuildSmall(double width, double height)
        {
            bool meter = view.Limits != null && view.Limits.Known, caption = CardSizing.ClaudeSmallMeter(height, textScale);
            var root = Rows(Star, Auto);
            var first = view.Sessions.FirstOrDefault();
            var top = Columns(Auto, Star); top.VerticalAlignment = VerticalAlignment.Center;
            double size = CardSizing.ClaudeSmallCar(width, height - (caption ? CardSizing.ClaudeSmallMeterHeight : 0));
            var car = Car(first, first == null ? "" : first.Project, size, "ClaudeSmallCar", true);
            var tap = Tappable(car.View, first); car.Row = tap;
            top.Children.Add(tap);
            var side = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            if (first == null)
            {
                var message = names.Add(Wrapped(view.Connected ? ClaudeText.NoSessions : ClaudeText.NotConnected, width < 300 ? Scaled(13) : Scaled(15), height - (caption ? CardSizing.ClaudeSmallMeterHeight : 0)), "ClaudeMessage");
                side.Children.Add(message);
            }
            else
            {
                var project = names.Add(Trimmed(Text("", Scaled(17), "Text")), "ClaudeTitle0"); project.FontWeight = FontWeights.SemiBold; side.Children.Add(project);
                var state = names.Add(Trimmed(Text("", Scaled(13), "Muted")), "ClaudeState0"); side.Children.Add(state);
                var more = names.Add(Trimmed(Text("", Scaled(12), "Muted")), "ClaudeMore"); side.Children.Add(more);
                double room = height - (caption ? CardSizing.ClaudeSmallMeterHeight : 0);
                if (room < CardSizing.ClaudeSmallSide(textScale, true, view.Sample)) more.Visibility = Visibility.Collapsed;
                string id = first.Id;
                fills.Add(now => {
                    var s = view.Sessions.FirstOrDefault(x => x.Id == id); if (s == null) return;
                    project.Text = s.ProjectName;
                    state.Text = s.State == ClaudeState.Stuck ? "Stuck · " + ClaudeText.StuckReason(s, view, now, use24) : ClaudeText.StateWords(s.State, s.Since, now, true);
                    state.SetResourceReference(TextBlock.ForegroundProperty, s.State == ClaudeState.NeedsYou || s.State == ClaudeState.Stuck ? "Warning" : "Muted");
                    int working = view.Sessions.Count(x => x.Id != id && x.State == ClaudeState.Working), others = view.Groups(period).Sum(g => g.Threads.Count) - 1 - working;
                    var parts = new List<string>();
                    if (working > 0) parts.Add("+" + working + " working");
                    if (others > 0) parts.Add(others + " more " + (others == 1 ? "thread" : "threads"));
                    if (view.Sessions.Any(x => x.Id != id && x.State == ClaudeState.NeedsYou)) parts.Insert(0, "another needs you");
                    // The longest form that fits beside the car: every part, then the first, then nothing (never "5 more th…").
                    double beside = Math.Max(0, width - size - 12);
                    string text = String.Join(" · ", parts);
                    if (!Fits(more, text, beside)) text = parts.Count > 0 && Fits(more, parts[0], beside) ? parts[0] : "";
                    more.Text = text;
                });
            }
            top.Children.Add(At(side, 0, 1));
            root.Children.Add(top);
            if (meter && caption)
            {
                var bottom = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
                var line = new DockPanel();
                var right = Text("", Scaled(12), "Muted"); right.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(right, Dock.Right); line.Children.Add(right);
                var label = Upper(view.Limits.FiveHour == null ? "Week" : "5-hour", Scaled(12)); label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new Thickness(0, 0, 8, 0); DockPanel.SetDock(label, Dock.Left); line.Children.Add(label);
                var value = Reading("—", 28); value.SetResourceReference(TextBlock.ForegroundProperty, "Text"); line.Children.Add(value);
                bottom.Children.Add(line);
                var bar = names.Add(new PaceBar { Height = 8, Margin = new Thickness(0, 4, 0, 0) }, "ClaudeSmallMeter"); bottom.Children.Add(bar);
                names.Add(value, "ClaudeSmallPercent"); names.Add(right, "ClaudeSmallPace");
                fills.Add(now => {
                    bool weekly = view.Limits.FiveHour == null;
                    var w = weekly ? view.Limits.Week : view.Limits.FiveHour;
                    double pct = ClaudeText.Percent(w, now), elapsed = ClaudePace.Elapsed(w, weekly ? ClaudePace.Week : ClaudePace.FiveHours, now), projection = ClaudePace.Projection(pct, elapsed);
                    value.Text = w == null ? "—" : Math.Round(pct) + "%"; value.SetResourceReference(TextBlock.ForegroundProperty, ClaudeText.Warns(pct) ? "Warning" : "Text");
                    // v0.53.0: the pace in time, then the reset and how old the reading is, as much as fits.
                    var paces = ClaudeText.PaceTimes(projection, weekly ? Double.NaN : view.LimitEtaMinutes, w, weekly ? ClaudePace.Week : ClaudePace.FiveHours, now, use24).Where(f => !f.Contains(", ")).ToList();
                    string when = w != null && w.ResetsAt.HasValue && w.Current(now) ? "resets " + ClaudeText.When(w.ResetsAt.Value, now, use24) : "", asOf = ClaudeText.Old(view, now) ? "as of " + ClaudeText.Clock(view.Limits.UpdatedAt, use24) : "";
                    var forms = new List<string>();
                    foreach (var p in paces) { forms.Add(String.Join(" · ", new[] { p, when, asOf }.Where(x => x.Length > 0))); forms.Add(String.Join(" · ", new[] { p, when }.Where(x => x.Length > 0))); }
                    foreach (var p in paces) forms.Add(p);
                    double lineRoom = (line.ActualWidth > 0 ? line.ActualWidth : width - 24) - ClaudeCard.Wide(label, label.Text) - 8 - ClaudeCard.Wide(value, value.Text) - 8;
                    Fit(right, lineRoom, forms.Distinct().ToList()); right.SetResourceReference(TextBlock.ForegroundProperty, ClaudePace.Warns(projection) ? "Warning" : "Muted");
                    bar.Value = pct / 100; bar.Projection = Double.IsNaN(projection) ? Double.NaN : projection / 100; bar.Pace = elapsed; bar.Warn = ClaudePace.Warns(projection) || ClaudeText.Warns(pct); bar.InvalidateVisual();
                });
                root.Children.Add(At(bottom, 1));
            }
            else if (caption)
            {
                var message = names.Add(Trimmed(Caption(view.Plan == ClaudePlanState.Expired ? "Claude sign-in ended" : view.Plan == ClaudePlanState.SignedOut ? "Sign in for plan limits" : ClaudeText.LimitsMissing(view))), "ClaudeLimits");
                message.Margin = new Thickness(0, 6, 0, 0);
                root.Children.Add(At(message, 1));
            }
            return root;
        }

        // ----- Filling in -----

        private void Render()
        {
            if (view == null || rolling != null) return;
            DateTime now = DateTime.UtcNow;
            // The one state allowed to shout: the window's frame turns Amber while a session needs you, and Glacier
            // while one waits for your reply. The frame keeps its thickness, so nothing moves.
            Edge = view.NeedsYou > 0 ? "Warning" : view.Sessions.Any(s => s.Pit == PitState.Done) ? "Turn" : null;
            if (Edge != null) card.SetResourceReference(Border.BorderBrushProperty, Edge); else card.BorderBrush = Brushes.Transparent;
            foreach (var fill in fills) fill(now);
            var threads = view.Threads.ToDictionary(t => t.Id);
            // The fuel lamp follows the plan's tightest window: the 5-hour one, or the week when the plan has none.
            double fiveHour = view.Limits == null ? 0 : ClaudeText.Percent(view.Limits.FiveHour ?? view.Limits.Week, now);
            // Medium shows one context ring per garage: its fullest running thread's.
            var worst = new Dictionary<string, ClaudeSession>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in cars.Where(c => c.OnlyWorst && c.Id != null))
            {
                ClaudeSession t, best;
                if (!threads.TryGetValue(part.Id, out t) || !t.Running || ClaudePace.ContextLevel(t.Context) == 0) continue;
                if (!worst.TryGetValue(part.Folder ?? "", out best) || t.Context > best.Context) worst[part.Folder ?? ""] = t;
            }
            foreach (var part in cars)
            {
                ClaudeSession t = null; if (part.Id != null) threads.TryGetValue(part.Id, out t);
                RenderCar(part, t, fiveHour);
                ClaudeSession fullest;
                bool ringed = t != null && t.Running && (!part.OnlyWorst || (worst.TryGetValue(part.Folder ?? "", out fullest) && fullest.Id == t.Id));
                double context = ringed ? t.Context : Double.NaN;
                if (!part.Ring.Percent.Equals(context)) { part.Ring.Percent = context; part.Ring.InvalidateVisual(); }
                // Medium's small inline cars show only the crew; the ledger's and Large's show tasks too.
                int workers = t != null && t.Running ? t.Workers : 0, done = t != null && t.Running ? t.TasksDone : 0, total = t != null && t.Running ? t.TasksTotal : 0;
                if (part.Crew.Workers != workers || part.Crew.TasksDone != done || part.Crew.TasksTotal != total) { part.Crew.Workers = workers; part.Crew.TasksDone = done; part.Crew.TasksTotal = total; part.Crew.InvalidateVisual(); }
            }
            clock.Update();
            if (Rendered != null) Rendered();
        }
        private void RenderCar(CarPart part, ClaudeSession t, double fiveHour)
        {
            PitState shown = t == null ? (view.Connected ? PitState.Idle : PitState.End) : t.Pit;
            // An archived or resolved car is an ended one under its cover, dimmed the most (decision 020).
            PitState state = ClaudePace.Parked(shown) ? PitState.End : shown;
            uint color = t == null ? PitArt.Accents[0] : PitArt.Accent(t.ProjectName);
            double share = t == null ? 0 : ClaudePace.Share(t.Rate, view.Scale);
            bool fuel = t != null && t.Running && ClaudePace.Fuel(fiveHour);
            var look = new PitArt.CarLook { Car = t == null ? PitCar.Sports : t.Car, Fuel = fuel, Hot = state == PitState.Work && ClaudePace.Hot(share), Fast = ClaudePace.Heavy(share) };
            string key; var frames = PitSprite.CarFrames(color, state, look, part.Width, out key);
            PitState before = state; bool known = part.Id != null && seen.TryGetValue(part.Id, out before);
            bool restart = known && before != state && PitSprite.PlaysOnce(state) && !quiet && live;
            if (part.Id != null) seen[part.Id] = state;
            part.Sprite.Fps = quiet ? 0 : PitSprite.CarFps(state, share, fuel);
            part.Sprite.Once = PitSprite.PlaysOnce(state);
            part.Sprite.Show(key, frames, PitSprite.CarOverflow, PitSprite.StillFrame(state), restart);
            if (quiet || !live) part.Sprite.Hold();
            // Idle has its engine off, dimmed; an ended or archived car is dimmed where its row isn't.
            part.Sprite.Opacity = state == PitState.Idle ? 0.7 : part.Dim && ClaudePace.Parked(shown) ? 0.45 : part.Dim && state == PitState.End ? 0.6 : 1;
        }
        private void RenderGarage(GaragePart part, ClaudeProjectGroup group)
        {
            bool closed = collapsed.Contains(group.Folder);
            var state = group.Garage;
            string key; var frames = PitSprite.GarageFrames(PitArt.Accent(group.Name), state, closed, group.Threads.Count, part.Sprite.Width, out key);
            part.Sprite.Fps = quiet || !(state == PitState.Need || (state == PitState.Work && !closed)) ? 0 : 3;
            part.Sprite.Once = false;
            part.Sprite.Show(key, frames, 0, 0, false);
            if (quiet || !live) part.Sprite.Hold();
            part.Sprite.Opacity = state == PitState.End ? 0.6 : 1;
        }

        // ----- Rolling in and out (Ali, 1 Nov 2026): about 1.2 s, instant in Quiet mode -----

        private sealed class RollState
        {
            public GaragePart Garage; public bool Out; public Action Done; public double T;
            public PitSprite Back, Front; public uint Color; public int Count;
            public Point Bay; public double GarageSize; public Point GarageAt;
            public readonly List<RollCar> Cars = new List<RollCar>();
        }
        // Ended (and archived or resolved) cars keep their cover and stay dimmed while they roll (review fix 8).
        private sealed class RollCar { public CarPart Part; public PitSprite Moving; public Point Target, Apron; public double Width; public PitCar Car; public bool Ended; }

        // Where a piece sits on the overlay: its top left in the card body's coordinates.
        private Point On(FrameworkElement element) { return element.TranslatePoint(new Point(0, 0), host); }

        private void StartRoll(GaragePart garage, bool outward, Action done)
        {
            if (!garage.Sprite.IsLoaded || garage.Sprite.ActualWidth <= 0) { if (done != null) done(); else ShowGarage(garage); return; }
            var group = view.Groups(period).FirstOrDefault(g => String.Equals(g.Folder, garage.Folder, StringComparison.OrdinalIgnoreCase));
            string name = group == null ? garage.Folder : group.Name;
            var roll = new RollState { Garage = garage, Out = outward, Done = done, Color = PitArt.Accent(name), Count = group == null ? garage.Cars.Count : group.Threads.Count };
            roll.GarageSize = garage.Sprite.ActualWidth; roll.GarageAt = On(garage.Sprite);
            // Only what shows moves: the ledger's visible part on Large, the whole card body otherwise.
            var viewport = ledgerScroll != null && ledgerScroll.IsLoaded ? new Rect(On(ledgerScroll), new Size(ledgerScroll.ActualWidth, ledgerScroll.ActualHeight)) : new Rect(0, 0, host.ActualWidth, host.ActualHeight);
            Func<FrameworkElement, bool> shows = e => { var box = new Rect(On(e), new Size(e.ActualWidth, e.ActualHeight)); return box.Top >= viewport.Top - 1 && box.Bottom <= viewport.Bottom + 1; };
            if (!shows(garage.Sprite)) { if (done != null) done(); else ShowGarage(garage); return; }
            layer.Clip = new RectangleGeometry(viewport);
            roll.Bay = new Point(roll.GarageAt.X + roll.GarageSize * 0.5, roll.GarageAt.Y + roll.GarageSize * 0.93);
            layer.Children.Clear();
            roll.Back = Piece(roll.GarageAt, roll.GarageSize, roll.GarageSize, 0);
            roll.Back.Show("back|" + roll.Color, new[] { Overlay("back|" + roll.Color, PitArt.GarageBack(roll.Color), roll.GarageSize, 100, 100) }, 0, 0, false);
            roll.Front = Piece(roll.GarageAt, roll.GarageSize, roll.GarageSize, 2);
            foreach (var c in garage.Cars.Where(c => c.Sprite.IsLoaded && c.Sprite.ActualWidth > 0 && shows(c.Sprite)))
            {
                var at = On(c.Sprite);
                var target = new Point(at.X + c.Sprite.ActualWidth / 2, at.Y + c.Sprite.ActualHeight);
                ClaudeSession t = view.Threads.FirstOrDefault(x => x.Id == c.Id);
                var car = new RollCar { Part = c, Target = target, Apron = new Point(target.X + c.Sprite.ActualWidth * 1.4, target.Y), Width = c.Sprite.ActualWidth, Car = t == null ? PitCar.Sports : t.Car, Ended = t == null || !t.Running };
                car.Moving = Piece(new Point(0, 0), car.Width, car.Width * 60 / 140, 3);
                car.Moving.Visibility = Visibility.Hidden;
                roll.Cars.Add(car);
                if (outward) c.Sprite.Visibility = Visibility.Hidden;
            }
            garage.Sprite.Visibility = Visibility.Hidden;
            rolling = roll;
            clock.Extra = StepRoll;
            clock.Update();
            DrawRoll();
        }
        private PitSprite Piece(Point at, double width, double height, int z)
        {
            var sprite = new PitSprite { Width = width, Height = height };
            Canvas.SetLeft(sprite, at.X); Canvas.SetTop(sprite, at.Y); Panel.SetZIndex(sprite, z);
            layer.Children.Add(sprite);
            return sprite;
        }
        private static System.Windows.Media.Imaging.BitmapSource Overlay(string key, List<PitOp> ops, double width, double boxWidth, double boxHeight)
        {
            int px = PitRaster.PixelSize(width), w = Math.Max(4, (int)Math.Round(width / px)), h = Math.Max(4, (int)Math.Round(width * boxHeight / boxWidth / px));
            return PitRaster.Frame("roll|" + key + "|" + w, () => ops, new Rect(0, 0, boxWidth, boxHeight), w, h);
        }
        private void StepRoll(double seconds)
        {
            if (rolling == null) { clock.Extra = null; return; }
            rolling.T += seconds;
            if (rolling.T >= Roll.TotalSeconds) { EndRoll(true); return; }
            DrawRoll();
        }
        private static double Ease(double t) { return t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2; }
        private static double Span(double t, double a, double b) { return Math.Max(0, Math.Min(1, (t - a) / (b - a))); }
        private static double Lerp(double a, double b, double t) { return a + (b - a) * t; }

        // One frame of the roll: the shutter, then each car's path, one after another.
        private void DrawRoll()
        {
            var r = rolling; double t = r.T;
            int n = r.Cars.Count, k;
            double drive = 0.6, stagger = n > 1 ? Math.Min(0.15, 0.3 / (n - 1)) : 0;
            bool sign = false;
            if (r.Out) k = t < 0.15 ? (int)Math.Round(8 * (1 - t / 0.15)) : t > 1.05 ? (int)Math.Round(4 * Span(t, 1.05, 1.2)) : 0;
            else { k = t < 0.15 ? (int)Math.Round(4 * (1 - t / 0.15)) : t > 1.05 ? (int)Math.Round(8 * Span(t, 1.05, 1.2)) : 0; sign = t > 1.15; }
            r.Front.Show("front|" + k + sign, new[] { Overlay("front|" + r.Color + "|" + k + "|" + (sign ? r.Count : 0), PitArt.GarageFront(r.Color, k, sign ? r.Count : 0, !sign), r.GarageSize, 100, 100) }, 0, 0, false);
            int wheel = (int)Math.Floor(t * 16) % 4 * 2;
            for (int i = 0; i < n; i++)
            {
                var car = r.Cars[i];
                int order = r.Out ? i : n - 1 - i;
                double start = 0.15 + order * stagger, u = Span(t, start, start + drive);
                if (t < start) { car.Moving.Visibility = Visibility.Hidden; if (!r.Out) car.Part.Sprite.Visibility = Visibility.Visible; continue; }
                if (!r.Out) car.Part.Sprite.Visibility = Visibility.Hidden;
                car.Moving.Visibility = u >= 1 && r.Out ? Visibility.Hidden : Visibility.Visible;
                if (u >= 1 && r.Out) { car.Part.Sprite.Visibility = Visibility.Visible; continue; }
                if (u >= 1) { car.Moving.Visibility = Visibility.Hidden; continue; }
                double bayScale = 0.4, frontWidth = car.Width * 0.55;
                if (r.Out)
                {
                    if (u < 0.45) { double v = Ease(u / 0.45); Place(car, "front", r.Color, Lerp(r.Bay.X, car.Apron.X, v), Lerp(r.Bay.Y, car.Apron.Y, v), frontWidth * Lerp(bayScale, 1, v), 1, v < 0.3 ? 1 : 3, 0); }
                    else if (u < 0.6) Place(car, "turn", r.Color, car.Apron.X, car.Apron.Y, car.Width, 0.45, 3, 0);
                    else { double v = Ease(Span(u, 0.6, 1)); Place(car, "side", r.Color, Lerp(car.Apron.X, car.Target.X, v), car.Target.Y, car.Width, 1, 3, wheel); }
                }
                else
                {
                    if (u < 0.4) { double v = Ease(u / 0.4); Place(car, "reverse", r.Color, Lerp(car.Target.X, car.Apron.X, v), car.Target.Y, car.Width, 1, 3, wheel); }
                    else if (u < 0.52) Place(car, "turnback", r.Color, car.Apron.X, car.Apron.Y, car.Width, 0.45, 3, 0);
                    else { double v = Ease(Span(u, 0.52, 1)); Place(car, v < 0.15 ? "brake" : "rear", r.Color, Lerp(car.Apron.X, r.Bay.X, v), Lerp(car.Apron.Y, r.Bay.Y, v), frontWidth * Lerp(1, bayScale, v), 1, v > 0.6 ? 1 : 3, 0); }
                }
            }
        }
        // Puts a moving car at a point (the middle of its bottom edge), in one of its views; z 1 is inside the bay,
        // behind the garage's walls.
        private void Place(RollCar car, string pose, uint color, double x, double bottom, double width, double squash, int z, int wheel)
        {
            System.Windows.Media.Imaging.BitmapSource bitmap; double height, over = 0;
            string look = ClaudePace.CarName(car.Car);
            if (pose == "front" || pose == "rear" || pose == "brake")
            {
                double full = car.Width * 0.55;
                bitmap = Overlay(pose + "|" + color + "|" + car.Ended, pose == "front" ? PitArt.Front(color, !car.Ended) : PitArt.Rear(color, pose == "brake" && !car.Ended), full, 80, 56);
                height = width * 56 / 80;
            }
            else if (car.Ended)
            {
                // Seen from the side, an ended car is its covered still frame.
                string coverKey; var covered = PitSprite.CarFrames(color, PitState.End, new PitArt.CarLook { Car = car.Car }, car.Width, out coverKey);
                bitmap = covered[PitSprite.StillFrame(PitState.End)];
                height = width * 60 / 140; over = PitSprite.CarOverflow; wheel = 0;
            }
            else
            {
                bool reverse = pose == "reverse" || pose == "turnback";
                var a = new PitArt.CarLook { Car = car.Car, Lights = !reverse, Reverse = reverse };
                string key; var frames = PitSprite.CarFrames(color, pose == "side" || pose == "reverse" ? PitState.Work : PitState.Idle, a, car.Width, out key);
                bitmap = frames[pose == "side" || pose == "reverse" ? wheel : 0];
                height = width * 60 / 140; over = PitSprite.CarOverflow;
            }
            double shown = width * squash;
            car.Moving.Width = shown; car.Moving.Height = height;
            Canvas.SetLeft(car.Moving, x - shown / 2); Canvas.SetTop(car.Moving, bottom - height);
            Panel.SetZIndex(car.Moving, z);
            car.Moving.Opacity = car.Ended ? 0.6 : 1;
            car.Moving.Show(pose + "|" + look + "|" + wheel + "|" + Math.Round(width) + "|" + car.Ended, new[] { bitmap }, over, 0, false);
        }
        private void EndRoll(bool finished)
        {
            var r = rolling;
            if (r == null) return;
            rolling = null; clock.Extra = null;
            if (layer != null) layer.Children.Clear();
            ShowGarage(r.Garage);
            foreach (var c in r.Cars) c.Part.Sprite.Visibility = Visibility.Visible;
            clock.Update();
            if (finished && r.Done != null) r.Done();
            else if (finished) Render();
        }
        private static void ShowGarage(GaragePart garage) { garage.Sprite.Visibility = Visibility.Visible; foreach (var c in garage.Cars) c.Sprite.Visibility = Visibility.Visible; }
    }

    // How the card follows its size: Small, Medium or Large, and which parts each one shows. Folded in from the widget
    // app's CardSizing (phase 4); the class keeps its name so the checks read the same.
    public static class CardSizing
    {
        public enum Design { Small, Medium, Large }
        // Claude Code (docs/design/claude-watcher-v2.html; decision 017): the car that needs you over the 5-hour meter
        // under 360 wide or 280 tall, the two plan meters over the top garages between, and the pit wall (plan row, pit
        // stop strip, ledger, and from 1000 units of body the 5-hour lanes and facts) from 640 × 420. Heights that hold
        // text follow the text size, so nothing leaves the card at 140%.
        public static Design Claude(double width, double height) { return width < 360 || height < 280 ? Design.Small : width >= 640 && height >= 420 ? Design.Large : Design.Medium; }
        // Large: the plan row's tiles, the rev counter from 860 units of body, the side panel from 1000, and the
        // ledger's models and lines columns when the table is wide enough for them.
        public const double ClaudePlanHeight = 124, ClaudeSideWidth = 300, ClaudeBurnWidth = 200;
        // The plan row grows with the text: its tallest tile holds three 13-unit lines beside the big number.
        public static double ClaudePlan(double textScale) { return ClaudePlanHeight + Math.Max(0, Math.Ceiling(3 * 1.33 * (Math.Round(13 * textScale) - 13))); }
        // The burn tile (v0.53.0) widens with the text so "last 60 min" and the 7-day bars keep their line.
        public static double ClaudeBurn(double textScale) { return Math.Round(ClaudeBurnWidth + 60 * Math.Max(0, textScale - 1)); }
        public static bool ClaudeRevs(double bodyWidth) { return bodyWidth >= 860; }
        public static bool ClaudeSide(double bodyWidth) { return bodyWidth >= 1000; }
        // The optional columns step aside as the text grows, so a thread's title keeps at least 240 units: the fixed
        // columns are the car (52), tokens, cache reads and replies, each its scaled width plus a 10-unit gap.
        public static double ClaudeFixedColumns(double textScale) { return 52 + Math.Round(72 * textScale) + 10 + Math.Round(80 * textScale) + 10 + Math.Round(64 * textScale) + 10; }
        public static bool ClaudeModels(double tableWidth, double textScale) { return tableWidth >= ClaudeFixedColumns(textScale) + Math.Round(150 * textScale) + 10 + 240; }
        public static bool ClaudeLines(double tableWidth, double textScale) { return ClaudeModels(tableWidth, textScale) && tableWidth >= ClaudeFixedColumns(textScale) + Math.Round(150 * textScale) + 10 + Math.Round(84 * textScale) + 10 + 240; }
        // A thread row's title keeps room for about 14 characters (review fix 4): first the "Desktop · 2 min" beside it
        // steps aside (it stays in the tooltip), then the Cache reads column. ClaudeChip and ClaudeWhere are the chip's
        // and that text's usual widths with their gaps.
        public const double ClaudeChip = 70, ClaudeWhereWidth = 103;
        // The ledger shows whole rows only (review fix 7): the tallest run of rows from the top that fits, and always the
        // first one; the rest scroll.
        public static double ClaudeWholeRows(IEnumerable<double> heights, double room)
        {
            double used = 0; bool first = true;
            foreach (double h in heights)
            {
                if (!first && used + h > room + 0.5) break;
                used += h; first = false;
            }
            return Math.Min(used, Math.Max(room, 0)) == used ? used : Math.Max(0, room);
        }
        public static double ClaudeTitleMin(double textScale) { return Math.Round(110 * textScale); }
        public static double ClaudeTitleRoom(double tableWidth, double textScale)
        {
            return tableWidth - ClaudeFixedColumns(textScale) - (ClaudeModels(tableWidth, textScale) ? Math.Round(150 * textScale) + 10 : 0) - (ClaudeLines(tableWidth, textScale) ? Math.Round(84 * textScale) + 10 : 0);
        }
        public static bool ClaudeWhere(double tableWidth, double textScale) { return ClaudeTitleRoom(tableWidth, textScale) - Math.Round(ClaudeChip * textScale) - Math.Round(ClaudeWhereWidth * textScale) >= ClaudeTitleMin(textScale); }
        public static bool ClaudeCache(double tableWidth, double textScale) { return ClaudeTitleRoom(tableWidth, textScale) - Math.Round(ClaudeChip * textScale) >= ClaudeTitleMin(textScale); }
        // v0.53.0, the live ledger (5 h and Today): the car, For, Last sign and Tokens always; Now while the title keeps
        // 200 units, then Lines while it keeps 240. Cache reads, Models and Replies are Week's.
        public static double ClaudeLiveFixed(double textScale) { return 52 + Math.Round(60 * textScale) + 10 + Math.Round(88 * textScale) + 10 + Math.Round(72 * textScale) + 10; }
        public static double ClaudeNowWidth(double textScale) { return Math.Round(104 * textScale) + 10; }
        public static bool ClaudeLiveNow(double tableWidth, double textScale) { return tableWidth >= ClaudeLiveFixed(textScale) + ClaudeNowWidth(textScale) + 200; }
        public static bool ClaudeLiveLines(double tableWidth, double textScale) { return ClaudeLiveNow(tableWidth, textScale) && tableWidth >= ClaudeLiveFixed(textScale) + ClaudeNowWidth(textScale) + Math.Round(84 * textScale) + 10 + 240; }
        public static bool ClaudeLiveWhere(double tableWidth, double textScale)
        {
            double room = tableWidth - ClaudeLiveFixed(textScale) - (ClaudeLiveNow(tableWidth, textScale) ? ClaudeNowWidth(textScale) : 0) - (ClaudeLiveLines(tableWidth, textScale) ? Math.Round(84 * textScale) + 10 : 0);
            return room - Math.Round(ClaudeChip * textScale) - Math.Round(ClaudeWhereWidth * textScale) >= ClaudeTitleMin(textScale);
        }
        // Medium: two mini plan tiles, then a 44-unit row per garage (taller with bigger text) with up to three cars
        // beside the project's name when they leave it 110 units.
        public const double ClaudeMiniHeight = 84;
        public static double ClaudeMini(double textScale) { return ClaudeMiniHeight + Math.Max(0, Math.Ceiling(1.33 * (Math.Round(12 * textScale) - 12))); }
        public static double ClaudeMediumRow(double textScale) { return Math.Max(44, Math.Ceiling(Math.Round(15 * textScale) * 1.33 + Math.Round(12 * textScale) * 1.33 + 8)); }
        public static int ClaudeInlineCars(double nameRoom, int threads)
        {
            int fit = (int)Math.Max(0, Math.Floor((nameRoom - 110) / 32));
            if (threads <= Math.Min(3, fit)) return threads;
            return (int)Clamp(Math.Min(3, fit - 1), 0, 3);
        }
        // Small: the 5-hour meter (its line and bar) only when the project, state, count and Sample flag still fit
        // beside the car above it.
        public const double ClaudeSmallMeterHeight = 52;
        public static bool ClaudeSmallMeter(double bodyHeight, double textScale) { return bodyHeight >= ClaudeSmallMeterHeight + Math.Ceiling(Math.Round(17 * textScale) * 1.33) + 34 + 17; }
        // The words beside Small's car: the project, its state and the Sample flag, and the "+2 working" line when
        // there's room for it too (it steps aside first as the text grows, so the meter keeps its place).
        public static double ClaudeSmallSide(double textScale, bool more, bool sample)
        {
            return Math.Ceiling(1.33 * (Math.Round(17 * textScale) + Math.Round(13 * textScale) + (more ? Math.Round(12 * textScale) : 0) + (sample ? Math.Round(13 * textScale) : 0)));
        }
        // The Small car: a third of the card's width, as long as the room above the meter allows.
        public static double ClaudeSmallCar(double bodyWidth, double room) { return Clamp(Math.Floor(Math.Min(bodyWidth * 0.34, room * 140 / 60)), 48, 120); }
        private static double Clamp(double value, double min, double max) { return Double.IsNaN(value) ? min : Math.Max(min, Math.Min(max, value)); }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using ClaudeDashboard;

// The lap chart's engine (package A, v0.54.0): the state timeline with its watch-start edge, Retrying, last sign of
// life, the plan reading ring and "≈ limit", tool times, the feed, and the snapshot's Reliability and Sources fields.
// All in memory; the checks use made-up sessions and the samples.
internal static class ClaudeTimelineTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static string Fresh(string directory, string name)
    {
        string path = Path.Combine(directory, "claude-timeline", name);
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
        return path;
    }
    private static string Nanos(DateTime time) { return (ClaudeBridge.Epoch(time) * 1000000L).ToString(CultureInfo.InvariantCulture); }
    private static string S(string key, string value) { return "{\"key\":\"" + key + "\",\"value\":{\"stringValue\":\"" + value + "\"}}"; }
    private static string I(string key, long value) { return "{\"key\":\"" + key + "\",\"value\":{\"intValue\":\"" + value.ToString(CultureInfo.InvariantCulture) + "\"}}"; }
    private static string D(string key, double value) { return "{\"key\":\"" + key + "\",\"value\":{\"doubleValue\":" + value.ToString("R", CultureInfo.InvariantCulture) + "}}"; }
    private static string Record(string name, string session, DateTime at, params string[] attributes)
    {
        var all = new List<string> { S("event.name", name), S("session.id", session) }; all.AddRange(attributes);
        return "{\"timeUnixNano\":\"" + Nanos(at) + "\",\"body\":{\"stringValue\":\"claude_code." + name + "\"},\"attributes\":[" + String.Join(",", all) + "]}";
    }
    private static List<ClaudeTelemetryEvent> Logs(params string[] records)
    {
        return ClaudeOtlp.ParseLogs("{\"resourceLogs\":[{\"resource\":{\"attributes\":[" + S("service.name", "claude-code") + "]},\"scopeLogs\":[{\"scope\":{\"name\":\"com.anthropic.claude_code.events\"},\"logRecords\":[" + String.Join(",", records) + "]}]}]}");
    }
    private static ClaudeService Service(string directory, string name, string id, DateTime at)
    {
        string bridge = Fresh(directory, name + "-bridge");
        // Seen now, so the test's own process (which keeps it alive) isn't younger than the session.
        var file = new ClaudeSessionFile { Id = id, State = ClaudeState.Working, Project = "C:/Code/api", Title = "Fix the login timeout", Since = ClaudeBridge.Epoch(at), LastEvent = ClaudeBridge.Epoch(at), Pid = Process.GetCurrentProcess().Id };
        ClaudeBridge.WriteAtomic(ClaudeBridge.SessionPath(bridge, id), ClaudeJson.Compact(file.ToJson()));
        var service = new ClaudeService(bridge, Fresh(directory, name + "-config"), appThreads: new ClaudeAppThreads(() => new string[0]));
        service.ReadSessions(at, true);
        return service;
    }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Claude timeline: trails start at the watch start, keep five hours and put changes on the feed (v0.54.0)", () => {
            DateTime start = Now.AddHours(-6);
            var timeline = new ClaudeTimeline(start);
            Assert(!timeline.Note("never", Now, ClaudeLaneState.Ended, Now), "An ended session the timeline never saw got a lane.");
            Assert(timeline.Note("a", Now.AddHours(-8), ClaudeLaneState.Working, Now.AddHours(-6)), "The first state wasn't noted.");
            Assert(!timeline.Note("a", Now.AddHours(-5.5), ClaudeLaneState.Working, Now.AddHours(-5.5)), "The same state was noted twice.");
            timeline.Note("a", Now.AddHours(-5.5), ClaudeLaneState.NeedsYou, Now.AddHours(-5.5));
            timeline.Note("a", Now.AddHours(-4), ClaudeLaneState.Working, Now.AddHours(-4));
            // A change dated before the last mark lands on it, not before it.
            timeline.Note("a", Now.AddHours(-4.5), ClaudeLaneState.YourTurn, Now.AddHours(-3));
            timeline.Note("a", Now.AddMinutes(-10), ClaudeLaneState.Silent, Now);
            var trail = timeline.Trails(Now)["a"];
            Assert(trail[0].At == Now.AddHours(-5) && trail[0].State == ClaudeLaneState.NeedsYou, "The trail doesn't start at the five hours in the state it was in then: " + trail[0].At + " " + trail[0].State);
            Assert(trail.Select(m => m.State).SequenceEqual(new[] { ClaudeLaneState.NeedsYou, ClaudeLaneState.YourTurn, ClaudeLaneState.Silent }), "Trail states wrong: " + String.Join(",", trail.Select(m => m.State)));
            Assert(trail[1].At == Now.AddHours(-4), "A late change moved before the mark it follows.");
            for (int i = 1; i < trail.Count; i++) Assert(trail[i].At > trail[i - 1].At, "The trail isn't in order.");
            // A timeline started an hour ago: a session already running joins at the edge, which is no news.
            var fresh = new ClaudeTimeline(Now.AddHours(-1));
            fresh.Note("b", Now.AddHours(-3), ClaudeLaneState.Working, Now.AddMinutes(-50));
            fresh.Note("b", Now.AddMinutes(-20), ClaudeLaneState.NeedsYou, Now.AddMinutes(-20));
            fresh.Note("c", Now.AddMinutes(-5), ClaudeLaneState.Working, Now.AddMinutes(-5));
            var b = fresh.Trails(Now)["b"];
            Assert(b[0].At == fresh.WatchStart, "A lane starts before the app began watching.");
            var feed = fresh.Feed;
            Assert(feed.Count == 2 && feed[0].Session == "c" && feed[0].Kind == ClaudeFeedItem.Working && feed[1].Kind == ClaudeFeedItem.Needs, "The feed has the wrong lines: " + String.Join(",", feed.Select(f => f.Session + ":" + f.Kind)));
            fresh.Note("c", Now, ClaudeLaneState.Ended, Now);
            Assert(fresh.Trails(Now)["c"].Last().State == ClaudeLaneState.Ended && fresh.Feed[0].Kind == ClaudeFeedItem.Ended, "An ended session's lane or feed line is missing.");
            Assert(!fresh.Trails(Now.AddHours(6)).ContainsKey("c"), "An ended lane stayed past five hours.");
        });

        check("Claude timeline: the feed keeps the last 50 lines, newest first, names and times only (v0.54.0)", () => {
            var timeline = new ClaudeTimeline(Now.AddHours(-1));
            for (int i = 0; i < 60; i++) timeline.Event(Now.AddSeconds(-i * 7), "s" + (i % 3), i % 2 == 0 ? ClaudeFeedItem.ToolFailed : ClaudeFeedItem.ApiError, i % 2 == 0 ? "Bash" : ClaudeTimeline.ErrorDetail(529, i));
            var feed = timeline.Feed;
            Assert(feed.Count == ClaudeTimeline.FeedKept && feed[0].At == Now && feed.Last().At == Now.AddSeconds(-49 * 7), "The feed didn't keep the newest 50: " + feed.Count);
            for (int i = 1; i < feed.Count; i++) Assert(feed[i].At <= feed[i - 1].At, "The feed isn't newest first.");
            Assert(ClaudeTimeline.ErrorDetail(529, 2) == "HTTP 529 · attempt 2" && ClaudeTimeline.ErrorDetail(0, 0) == "", "The API error's detail reads wrongly.");
        });

        check("Claude timeline: ≈ limit needs 3 plan readings, then goes by the last 30 minutes' pace (v0.54.0)", () => {
            var window = new ClaudeLimitWindow { Percent = 60, ResetsAt = Now.AddHours(2) };
            var timeline = new ClaudeTimeline(Now.AddHours(-3));
            Assert(timeline.Reading(Now.AddMinutes(-50), 40, window.ResetsAt, Now), "A reading wasn't kept.");
            Assert(!timeline.Reading(Now.AddMinutes(-50), 40, window.ResetsAt, Now) && !timeline.Reading(Now.AddMinutes(-60), 30, window.ResetsAt, Now), "A repeated or older reading was kept.");
            timeline.Reading(Now.AddMinutes(-40), 45, window.ResetsAt, Now);
            var two = ClaudeTimeline.AtPace(timeline.Readings(Now), window, Now);
            Assert(!two.Known && !two.BeforeReset && two.Why.Contains("needs 3") && two.Readings == 2, "Two readings made a projection, or didn't say why: " + two.Why);
            // A reading from the window before (it reset) doesn't count toward this one.
            timeline.Reading(Now.AddMinutes(-35), 90, Now.AddHours(-1), Now);
            Assert(!ClaudeTimeline.AtPace(timeline.Readings(Now), window, Now).Known, "Another window's reading counted.");
            timeline.Reading(Now.AddMinutes(-30), 50, window.ResetsAt, Now);
            timeline.Reading(Now, 60, window.ResetsAt, Now);
            // From the reading 30 minutes ago (50%) to now (60%): 20% an hour, so 100% in two hours, just at the reset.
            var pace = ClaudeTimeline.AtPace(timeline.Readings(Now), window, Now);
            Assert(pace.Known && Math.Abs(pace.PerHour - 20) < 1e-6 && !pace.BeforeReset && Math.Abs(pace.AtReset - 100) < 1e-6, "The pace projection is wrong: " + pace.PerHour + " " + pace.LimitAt + " " + pace.AtReset);
            timeline.Reading(Now.AddMinutes(1), 65, window.ResetsAt, Now.AddMinutes(1));
            pace = ClaudeTimeline.AtPace(timeline.Readings(Now.AddMinutes(1)), window, Now.AddMinutes(1));
            Assert(pace.BeforeReset && pace.LimitAt.Value > Now.AddMinutes(60) && pace.LimitAt.Value < window.ResetsAt.Value, "A faster pace doesn't hit the limit before the reset: " + pace.LimitAt);
            var average = ClaudeTimeline.AtAverage(timeline.Readings(Now.AddMinutes(1)), window, Now.AddMinutes(1), 10);
            Assert(average.Known && average.Basis == ClaudeLimitProjection.Average && !average.BeforeReset && Math.Abs(average.AtReset - (65 + 10 * (119 / 60.0))) < 1e-6, "The average projection is wrong: " + average.AtReset);
            Assert(!ClaudeTimeline.AtAverage(timeline.Readings(Now), window, Now, Double.NaN).Known, "No average still projected.");
            Assert(!ClaudeTimeline.AtPace(timeline.Readings(Now), new ClaudeLimitWindow { Percent = 5, ResetsAt = Now.AddMinutes(-1) }, Now).Known, "A window that reset projected.");
            Assert(timeline.Readings(Now.AddHours(6)).Count == 0, "Readings older than five hours were kept.");
        });

        check("Claude timeline: typical tool times are medians per tool (v0.54.0)", () => {
            var runs = new List<ClaudeToolRun> {
                new ClaudeToolRun { Tool = "Bash", Ms = 100, Session = "a" }, new ClaudeToolRun { Tool = "Bash", Ms = 300, Session = "a", Failed = true }, new ClaudeToolRun { Tool = "Bash", Ms = 200, Session = "b" },
                new ClaudeToolRun { Tool = "Read", Ms = 40, Session = "b" }, new ClaudeToolRun { Tool = "Read", Ms = 60, Session = "b" }, new ClaudeToolRun { Tool = "Read", Session = "b" }, new ClaudeToolRun { Tool = "", Ms = 1 } };
            var times = ClaudeTimeline.ToolTimes(runs);
            Assert(times.Count == 2 && times[0].Tool == "Bash" && times[0].Runs == 3 && times[0].Failed == 1 && times[0].MedianMs == 200 && times[0].LongestMs == 300, "Bash's times are wrong.");
            Assert(times[1].Tool == "Read" && times[1].Runs == 3 && times[1].MedianMs == 50 && times[1].LongestMs == 60, "Read's median should skip a run with no time: " + times[1].MedianMs);
            Assert(Double.IsNaN(ClaudeTimeline.Median(new double[0])), "A median of nothing isn't NaN.");
        });

        check("Claude timeline: the service records lanes, Retrying, the feed, plan readings and the snapshot's fields (v0.54.0)", () => {
            DateTime real = DateTime.UtcNow;
            using (var service = Service(directory, "service", "lp", real))
            {
                service.WatchFrom(real.AddHours(-1));
                var view = service.View(real, null);
                var t = view.Threads.Single(x => x.Id == "lp");
                Assert(t.Lane == ClaudeLaneState.Working && view.Trails.ContainsKey("lp") && Math.Abs((view.Trails["lp"][0].At - real).TotalSeconds) < 1 && view.WatchStart == real.AddHours(-1), "A working session has no trail from its start.");
                Assert(view.AppThreadsReadAt > DateTime.MinValue && view.PlanNextCheck == null && view.TelemetryPort == 0, "The sources' freshness is wrong.");
                // Two overloaded answers and no request through since: Retrying, with the errors on the feed.
                service.Receive(Logs(Record("tool_decision", "lp", real.AddSeconds(-60), S("tool_name", "Bash"), S("tool_use_id", "t1"), S("decision", "accept"), S("source", "user_temporary")),
                    Record("tool_result", "lp", real.AddSeconds(-50), S("tool_name", "Bash"), S("tool_use_id", "t1"), S("success", "false"), D("duration_ms", 1200), S("error_type", "exit_code")),
                    Record("api_request", "lp", real.AddSeconds(-45), S("model", "claude-opus-5-5"), I("input_tokens", 1), S("request_id", "lp-1")),
                    Record("api_error", "lp", real.AddSeconds(-30), I("status_code", 529), I("attempt", 1)),
                    Record("api_error", "lp", real.AddSeconds(-20), I("status_code", 529), I("attempt", 2))));
                Assert(service.ToolEntries.Any(e => e.Tool == "Bash" && e.Asked), "A tool decision lost its tool's name.");
                view = service.View(real, null);
                t = view.Threads.Single(x => x.Id == "lp");
                Assert(t.Retrying && t.Lane == ClaudeLaneState.Retrying && view.Trails["lp"].Last().State == ClaudeLaneState.Retrying, "An API error with no request since isn't Retrying.");
                Assert(t.LastSign >= real.AddSeconds(-20), "Telemetry didn't count as a sign of life: " + t.LastSign);
                Assert(view.Feed.Count >= 3 && view.Feed[0].Kind == ClaudeFeedItem.Retrying && view.Feed.Any(f => f.Kind == ClaudeFeedItem.ApiError && f.Detail == "HTTP 529 · attempt 2" && f.Title == "Fix the login timeout")
                    && view.Feed.Any(f => f.Kind == ClaudeFeedItem.ToolFailed && f.Detail == "Bash"), "The feed is missing lines: " + String.Join(", ", view.Feed.Select(f => f.Kind + " " + f.Detail)));
                Assert(view.ToolTimesKnown && view.ToolTimes.Single().Tool == "Bash" && view.ToolTimes[0].Failed == 1 && t.ToolTimes.Single().MedianMs == 1200, "Tool times are missing.");
                Assert(view.ToolRunsToday.Count == (real.AddSeconds(-50) >= view.Starts[ClaudePeriod.Today] ? 1 : 0) && view.ApiErrors.Count == 2, "Today's runs or the day's API errors are wrong.");
                // A request goes through: no longer retrying.
                service.Receive(Logs(Record("api_request", "lp", real.AddSeconds(-5), S("model", "claude-opus-5-5"), I("input_tokens", 1), S("request_id", "lp-2"))));
                view = service.View(real, null);
                Assert(!view.Threads.Single(x => x.Id == "lp").Retrying && view.Trails["lp"].Last().State == ClaudeLaneState.Working, "A request through didn't end Retrying.");
                // Plan readings from the plan check: two say why there's no projection, three project.
                Func<double, int, ClaudeLimits> limits = (percent, ago) => new ClaudeLimits { FiveHour = new ClaudeLimitWindow { Percent = percent, ResetsAt = real.AddHours(3) }, UpdatedAt = real.AddMinutes(-ago), Source = "online" };
                service.UseOnlineLimits(limits(40, 20)); service.UseOnlineLimits(limits(45, 10));
                view = service.View(real, null);
                Assert(view.PlanReadings.Count == 2 && !view.LimitAtPace.Known && view.LimitAtPace.Why.Length > 0, "Two readings projected, or didn't say why.");
                service.UseOnlineLimits(limits(50, 1));
                view = service.View(real, null);
                Assert(view.PlanReadings.Count == 3 && view.LimitAtPace.BeforeReset && view.LimitAtPace.LimitAt.Value > real.AddMinutes(85) && view.LimitAtPace.LimitAt.Value < real.AddMinutes(100), "Three readings didn't project ≈ limit: " + view.LimitAtPace.LimitAt);
                Assert(!view.LimitAtAverage.Known && view.LimitAtAverage.Why.Length > 0, "No logs still gave today's average.");
            }
        });

        check("Claude timeline: the samples carry trails, readings, ≈ limit, the feed and tool times (v0.54.0)", () => {
            foreach (bool needs in new[] { false, true })
                foreach (bool crowded in new[] { false, true })
                {
                    var view = ClaudeView.Fixture(Now, needs, crowded);
                    string name = (needs ? "needs" : "plain") + (crowded ? "+crowded" : "");
                    Assert(view.Sample && view.WatchStart == Now.AddHours(-3), "The sample doesn't watch from three hours ago: " + name);
                    foreach (var t in view.Threads.Where(x => x.Running))
                    {
                        List<ClaudeStateMark> trail;
                        Assert(view.Trails.TryGetValue(t.Id, out trail) && trail.Count > 1, "A running sample thread has no trail: " + t.Id + " in " + name);
                        Assert(trail[0].At >= view.WatchStart && trail.Last().State == t.Lane, "A sample trail starts before the watch or doesn't end in its lane: " + t.Id);
                    }
                    Assert(view.PlanReadings.Count >= 3 && view.LimitAtPace.Known && view.LimitAtAverage.Known, "The sample has no ≈ limit: " + name + " " + view.LimitAtPace.Why);
                    Assert(view.Feed.Count > 0 && view.Feed.Count <= ClaudeTimeline.FeedKept && view.Feed.All(f => f.Title.Length > 0 && f.Detail.Length <= 80), "The sample's feed is wrong: " + name);
                    Assert(view.ToolTimesKnown && view.ToolTimes.Count > 0 && view.ToolTimes.All(x => !Double.IsNaN(x.MedianMs)) && view.ToolRunsToday.Count > 0 && view.ApiErrors.Count > 0, "The sample's tool times or errors are missing: " + name);
                    Assert(view.LogsReadAt > DateTime.MinValue && view.TelemetryPort == ClaudeTelemetry.DefaultPort && view.PlanNextCheck.HasValue, "The sample's sources' freshness is missing: " + name);
                    Assert(view.Compactions.Any(c => c.Session == "sample-3"), "The compacting sample isn't counted: " + name);
                    if (crowded)
                    {
                        Assert(view.LimitAtPace.BeforeReset, "The crowd's ≈ limit isn't before the reset.");
                        Assert(view.Threads.Single(x => x.Id == "crowd-10").Lane == ClaudeLaneState.Retrying && view.Threads.Single(x => x.Id == "crowd-3").Lane == ClaudeLaneState.Silent, "The crowd has no Retrying or Silent lane.");
                    }
                    else if (!needs) Assert(!view.LimitAtPace.BeforeReset && view.LimitAtPace.AtReset < 100, "The calm sample hits the limit.");
                    if (needs) Assert(view.Threads.Single(x => x.Id == "sample-1").Lane == ClaudeLaneState.NeedsYou, "The needs sample's lane isn't Needs you.");
                }
        });
    }
}

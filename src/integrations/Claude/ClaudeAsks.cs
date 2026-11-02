using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace WidgetScreen
{
    // One permission dialog a Claude Code session is waiting on, as ClaudeHook.exe's ask mode left it in the bridge's
    // asks folder (package 4, decision 027): which session, the tool, what it is about, and until when the hook waits.
    public sealed class ClaudeAsk
    {
        public string Id = "", Session = "", Tool = "", Detail = "", Cwd = "";
        public int HookPid;
        public DateTime At, Until;

        public string ProjectName { get { return ClaudeBridge.ProjectName(Cwd); } }
        // Whole seconds left before Claude Code shows its own dialog.
        public int SecondsLeft(DateTime now) { return Math.Max(0, (int)Math.Ceiling((Until - now).TotalSeconds)); }

        public static ClaudeAsk FromJson(JsonObject o)
        {
            if (o == null || String.IsNullOrEmpty(ClaudeJson.Str(o, "id")) || String.IsNullOrEmpty(ClaudeJson.Str(o, "session"))) return null;
            double at = ClaudeJson.Num(o, "at"), until = ClaudeJson.Num(o, "until"), pid = ClaudeJson.Num(o, "hookPid");
            if (Double.IsNaN(at) || Double.IsNaN(until)) return null;
            return new ClaudeAsk { Id = ClaudeJson.Str(o, "id"), Session = ClaudeJson.Str(o, "session"), Tool = ClaudeJson.Str(o, "tool") ?? "",
                Detail = ClaudeJson.Str(o, "detail") ?? "", Cwd = ClaudeJson.Str(o, "cwd") ?? "", HookPid = Double.IsNaN(pid) ? 0 : (int)pid,
                At = ClaudeBridge.FromEpoch(at), Until = ClaudeBridge.FromEpoch(until) };
        }
    }

    // The dashboard's side of answering (package 4, decision 027). Listening writes the listener file the ask hook looks
    // for; Pending reads the open asks; Answer leaves Allow once, Deny or Open for one of them. Only an ask that is still
    // open, from a hook that still runs, can be answered, and every leftover file is cleared away.
    public static class ClaudeAsks
    {
        // How long after its wait an ask file may stay before it counts as left behind (a hook Claude Code cancelled).
        public static readonly TimeSpan Slack = TimeSpan.FromSeconds(5);

        public static void Listen(string bridge, int waitSeconds)
        {
            int pid; using (var self = System.Diagnostics.Process.GetCurrentProcess()) pid = self.Id;
            ClaudeBridge.Listen(bridge, pid, ClaudeProcesses.Started(pid), waitSeconds);
        }
        public static void StopListening(string bridge)
        {
            int pid; using (var self = System.Diagnostics.Process.GetCurrentProcess()) pid = self.Id;
            try { ClaudeBridge.StopListening(bridge, pid); } catch (Exception) { }
        }
        // Whether this dashboard is the one listening, with this wait.
        public static bool Listening(string bridge, int waitSeconds)
        {
            int pid; using (var self = System.Diagnostics.Process.GetCurrentProcess()) pid = self.Id;
            var listener = ClaudeBridge.ReadListener(bridge);
            return listener != null && listener.Item1 == pid && listener.Item3 == ClaudeBridge.ClampWait(waitSeconds);
        }

        // The open asks, oldest first. alive says whether a hook's process still runs (the checks pass their own).
        // Asks whose hook has gone or whose wait is long over are deleted with their answers.
        public static List<ClaudeAsk> Pending(string bridge, DateTime now, Func<int, bool> alive)
        {
            var list = new List<ClaudeAsk>();
            string folder = ClaudeBridge.AsksFolder(bridge);
            string[] files;
            try { if (!Directory.Exists(folder)) return list; files = Directory.GetFiles(folder, "*.ask.json"); }
            catch (Exception) { return list; }
            foreach (var path in files)
            {
                ClaudeAsk ask = null;
                try { ask = ClaudeAsk.FromJson(ClaudeJson.Obj(ClaudeJson.Parse(File.ReadAllText(path, Encoding.UTF8)))); }
                catch (Exception) { ask = null; }
                if (ask == null)
                {
                    // Being written (the hook moves it into place whole) or broken: broken ones go once they're old.
                    try { if (now - File.GetLastWriteTimeUtc(path) > Slack) File.Delete(path); } catch (Exception) { }
                    continue;
                }
                bool gone = now > ask.Until + Slack || (ask.HookPid > 0 && !alive(ask.HookPid));
                if (gone) { Remove(bridge, ask.Id); continue; }
                if (now >= ask.Until) continue;
                if (ClaudeBridge.ReadAnswer(bridge, ask.Id).Length > 0) continue;
                list.Add(ask);
            }
            // Answer files whose ask has gone (the hook ended first).
            try
            {
                foreach (var path in Directory.GetFiles(folder, "*.answer.json"))
                {
                    string id = Path.GetFileName(path); id = id.Substring(0, id.Length - ".answer.json".Length);
                    if (!File.Exists(ClaudeBridge.AskPath(bridge, id)) && now - File.GetLastWriteTimeUtc(path) > Slack) File.Delete(path);
                }
            }
            catch (Exception) { }
            return list.OrderBy(a => a.At).ToList();
        }
        public static List<ClaudeAsk> Pending(string bridge, DateTime now) { return Pending(bridge, now, pid => ClaudeProcesses.Alive(pid)); }

        // Leaves the answer for one open ask: ClaudeBridge.Allow, Deny or OpenIt. False when the ask isn't open any more
        // (answered in Claude Code, timed out, or never there), so nothing is written for a dialog that has moved on.
        public static bool Answer(string bridge, string id, string answer, DateTime now)
        {
            if (answer != ClaudeBridge.Allow && answer != ClaudeBridge.Deny && answer != ClaudeBridge.OpenIt) return false;
            if (String.IsNullOrEmpty(id) || ClaudeBridge.SafeName(id) != id) return false;
            ClaudeAsk ask;
            try
            {
                string path = ClaudeBridge.AskPath(bridge, id);
                if (!File.Exists(path)) return false;
                ask = ClaudeAsk.FromJson(ClaudeJson.Obj(ClaudeJson.Parse(File.ReadAllText(path, Encoding.UTF8))));
            }
            catch (Exception) { return false; }
            if (ask == null || ask.Id != id || now >= ask.Until || ClaudeBridge.ReadAnswer(bridge, id).Length > 0) return false;
            var o = new JsonObject(); o["id"] = id; o["answer"] = answer; o["at"] = new JsonNumber(ClaudeBridge.Epoch(now));
            try { ClaudeBridge.WriteAtomic(ClaudeBridge.AnswerPath(bridge, id), ClaudeJson.Compact(o)); return true; }
            catch (Exception) { return false; }
        }

        private static void Remove(string bridge, string id)
        {
            foreach (var path in new[] { ClaudeBridge.AskPath(bridge, id), ClaudeBridge.AnswerPath(bridge, id) })
                try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
        }
    }
}

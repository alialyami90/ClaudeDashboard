using System;
using System.Collections.Generic;
using System.Linq;

namespace WidgetScreen
{
    // Answering Claude Code's permission dialogs from the dashboard (package 4, decision 027), kept out of the window's
    // controller so it moves with the Claude wiring: the controller makes one, calls Tick on the heartbeat and Sync when
    // the bridge folder changes, and disposes it on close. While Settings → Claude → Answer permission prompts is on,
    // the dashboard is connected and Quiet mode is off, it writes the listener file the ask hook looks for and shows each
    // open ask as a ClaudeAskToast; otherwise there is no listener, and Claude Code shows its own dialog at once.
    public sealed class ClaudePermissions : IDisposable
    {
        // At most this many toasts at once; a further ask is passed straight to Claude Code's own dialog.
        public const int MostShown = 4;
        private readonly Func<string> bridge;
        private readonly Action<string> focus;
        private readonly Func<string, string> threadOf;
        private readonly Func<string, object> resource;
        private readonly Dictionary<string, ClaudeAskToast> shown = new Dictionary<string, ClaudeAskToast>();
        private readonly HashSet<string> passed = new HashSet<string>();
        private string listeningIn;
        private int listeningWait, ticks;

        // bridge gives the bridge folder; focus brings a session's window forward; threadOf names a session's thread for
        // the toast (or ""); resource finds the palette's brushes.
        public ClaudePermissions(Func<string> bridge, Action<string> focus, Func<string, string> threadOf, Func<string, object> resource)
        {
            this.bridge = bridge; this.focus = focus; this.threadOf = threadOf; this.resource = resource;
        }

        public bool Listening { get { return listeningIn != null; } }
        // Whether a toast for this session is up, so the plain Needs-you notification can stay quiet.
        public bool Asking(string session) { return shown.Values.Any(t => t.Ask.Session == session); }

        // Once a second: listens or stops as the settings say, then follows the asks.
        public void Tick(bool enabled, int waitSeconds, bool connected, bool quiet, DateTime now)
        {
            ticks++;
            string folder = bridge();
            bool wanted = enabled && connected && !quiet && !Program.Rendering && !String.IsNullOrEmpty(folder);
            if (wanted)
            {
                int wait = ClaudeBridge.ClampWait(waitSeconds);
                // Written again when the choice changes and every ten seconds if another copy of the dashboard took over.
                if (listeningIn != folder || listeningWait != wait || (ticks % 10 == 0 && !ClaudeAsks.Listening(folder, wait)))
                {
                    if (listeningIn != null && listeningIn != folder) ClaudeAsks.StopListening(listeningIn);
                    try { ClaudeAsks.Listen(folder, wait); listeningIn = folder; listeningWait = wait; } catch (Exception) { listeningIn = null; }
                }
            }
            else if (listeningIn != null) Stop();
            Sync(now);
        }

        // Shows the open asks, closes the ones that ended, and moves each countdown on.
        public void Sync(DateTime now)
        {
            List<ClaudeAsk> open = listeningIn == null ? new List<ClaudeAsk>() : ClaudeAsks.Pending(listeningIn, now);
            var ids = new HashSet<string>(open.Select(a => a.Id));
            foreach (var id in shown.Keys.Where(k => !ids.Contains(k)).ToList()) Close(id);
            passed.RemoveWhere(id => !ids.Contains(id));
            foreach (var ask in open)
            {
                if (shown.ContainsKey(ask.Id) || passed.Contains(ask.Id)) continue;
                if (shown.Count >= MostShown || !ClaudeToast.Enabled) { if (ClaudeToast.Enabled) { ClaudeAsks.Answer(listeningIn, ask.Id, ClaudeBridge.OpenIt, now); passed.Add(ask.Id); } continue; }
                Show(ask);
            }
            foreach (var pair in shown.ToList()) if (!pair.Value.Tick(now)) Close(pair.Key);
            Arrange();
        }

        private void Show(ClaudeAsk ask)
        {
            string thread = "";
            try { thread = threadOf == null ? "" : threadOf(ask.Session) ?? ""; } catch (Exception) { }
            ClaudeAskToast toast = null;
            toast = new ClaudeAskToast(ask, thread, resource, answer => Answered(ask, answer));
            shown[ask.Id] = toast;
            try { toast.Show(); toast.SizeChanged += (s, e) => Arrange(); }
            catch (Exception) { shown.Remove(ask.Id); }
        }

        private void Answered(ClaudeAsk ask, string answer)
        {
            string folder = listeningIn;
            if (folder != null) ClaudeAsks.Answer(folder, ask.Id, answer, DateTime.UtcNow);
            Close(ask.Id);
            passed.Add(ask.Id);
            if (answer == ClaudeBridge.OpenIt && focus != null) { try { focus(ask.Session); } catch (Exception) { } }
            Arrange();
        }

        private void Arrange()
        {
            int slot = 0;
            foreach (var toast in shown.Values.OrderBy(t => t.Ask.At)) { try { toast.Place(slot++); } catch (Exception) { } }
        }

        private void Close(string id)
        {
            ClaudeAskToast toast;
            if (!shown.TryGetValue(id, out toast)) return;
            shown.Remove(id);
            try { toast.Close(); } catch (Exception) { }
        }

        // No listener: every waiting hook sees it within a second and lets Claude Code ask in its own window.
        private void Stop()
        {
            if (listeningIn != null) ClaudeAsks.StopListening(listeningIn);
            listeningIn = null;
            foreach (var id in shown.Keys.ToList()) Close(id);
            passed.Clear();
        }

        public void Dispose() { Stop(); }
    }
}

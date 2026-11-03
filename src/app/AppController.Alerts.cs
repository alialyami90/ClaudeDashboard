using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace ClaudeDashboard
{
    // Alerts that respect attention (observability critique, issue 5): the taskbar button carries an Amber dot with how
    // many sessions need you, flashes once when a new one does (never in Quiet mode, never while the window is in
    // front), and Ctrl+G (Ctrl+1 until v0.55.0) opens the session that has waited longest, in the attention queue's order. The window's frame
    // (ClaudeCard.Render) goes Glacier only once a reply you owe has waited 10 minutes.
    public sealed partial class AppController
    {
        private string taskbarShown = "";
        // The checks: how many times the taskbar would have flashed (the window has no handle until it's shown).
        public int TaskbarFlashes { get; private set; }

        // The dot on the taskbar button: drawn in code, Amber with the count in OnWarning, gone when nothing needs you.
        // Redrawn only when the count or the palette changes.
        private void UpdateTaskbar()
        {
            int count = claudeView == null ? 0 : claudeView.NeedsYou;
            var fill = window.Resources["Warning"] as SolidColorBrush;
            var ink = window.Resources["OnWarning"] as SolidColorBrush;
            string key = count == 0 || fill == null || ink == null ? "" : count + "|" + fill.Color + "|" + ink.Color;
            if (key == taskbarShown) return;
            taskbarShown = key;
            if (window.TaskbarItemInfo == null) window.TaskbarItemInfo = new TaskbarItemInfo();
            window.TaskbarItemInfo.Overlay = key.Length == 0 ? null : Badge(count, fill.Color, ink.Color);
            window.TaskbarItemInfo.Description = key.Length == 0 ? "" : count == 1 ? "1 session needs you" : count + " sessions need you";
        }

        // A 16-unit circle with the count (9+ past nine), the digits as outlines so no font is needed when Windows draws it.
        public static ImageSource Badge(int count, Color fill, Color ink)
        {
            var group = new DrawingGroup();
            var fillBrush = new SolidColorBrush(fill); fillBrush.Freeze();
            var inkBrush = new SolidColorBrush(ink); inkBrush.Freeze();
            group.Children.Add(new GeometryDrawing(fillBrush, null, new EllipseGeometry(new Point(8, 8), 8, 8)));
            string digits = count > 9 ? "9+" : count.ToString();
            var text = new FormattedText(digits, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), digits.Length > 1 ? 9 : 11, inkBrush, 1.0);
            var shape = text.BuildGeometry(new Point(0, 0));
            var bounds = shape.Bounds;
            if (!bounds.IsEmpty) shape.Transform = new TranslateTransform(8 - (bounds.Left + bounds.Width / 2), 8 - (bounds.Top + bounds.Height / 2));
            group.Children.Add(new GeometryDrawing(inkBrush, null, shape));
            var image = new DrawingImage(group);
            image.Freeze();
            return image;
        }

        // One flash of the taskbar button for each view that brings a new Needs you, when the window isn't in front and
        // Quiet mode is off. Returns whether it flashed.
        public bool FlashForNeeds(List<ClaudeAlert> fresh)
        {
            if (fresh == null || !fresh.Any(a => a.Kind == "need") || settings.QuietMode || window.IsActive || Program.Rendering) return false;
            TaskbarFlashes++;
            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
            {
                var info = new FLASHWINFO { cbSize = (uint)Marshal.SizeOf(typeof(FLASHWINFO)), hwnd = handle, dwFlags = FlashTray, uCount = 1, dwTimeout = 0 };
                try { FlashWindowEx(ref info); } catch (Exception) { }
            }
            return true;
        }

        // Ctrl+G: the first session in the attention queue (Needs you first, the longest waiting first). Returns its id,
        // or null when nothing is waiting on you.
        public string OpenWaitingLongest()
        {
            var first = WaitingLongest(DateTime.UtcNow);
            if (first == null) { Notify("Nothing is waiting on you."); return null; }
            FocusClaudeSession(first.Thread.Id);
            return first.Thread.Id;
        }
        public ClaudeAttention WaitingLongest(DateTime now)
        {
            return claudeView == null ? null : claudeView.Attention(now).FirstOrDefault();
        }

        // A view's action (ClaudeViewContext.Act, decision 030): each name takes exactly the path of its Settings →
        // Claude button, confirmation sheet and settings.json backups included, and is refused when that button
        // wouldn't show. Returns null when it started or worked, or why not.
        public static readonly string[] Actions = { "Connect", "Disconnect", "Reconnect", "OpenLogs", "UpdateConnection", "CheckPlan", "SignIn", "ShowArchived", "HideArchived", "KeepLogs", "UndoKeepLogs" };
        public string Act(string name)
        {
            if (disposed) return "Claude Dashboard is closing.";
            if (claudeSample) return Array.IndexOf(Actions, name) >= 0 ? "These are sample numbers, so there's nothing to change." : "Unknown action: " + (name ?? "(none)") + ".";
            switch (name)
            {
                case "Connect": case "Reconnect": case "UpdateConnection":
                {
                    claude.CheckConnected();
                    string port = claude.TelemetryWarning();
                    bool update = claude.Connector.NeedsUpdate() || claude.HooksElsewhere != null || (port.Length > 0 && !claude.WidgetScreenOpen);
                    if (name == "Connect" && claude.Connected && !update) return "Claude Code is already connected.";
                    if (name == "Reconnect" && claude.HooksElsewhere == null) return claude.Connected ? "Claude Code's hooks already run Claude Dashboard's bridge." : "Claude Code isn't connected. Connect it first.";
                    if (name == "UpdateConnection" && (!claude.Connected || !update)) return claude.Connected ? "The connection is up to date." : "Claude Code isn't connected. Connect it first.";
                    ConnectClaude();
                    return null;
                }
                case "Disconnect":
                    claude.CheckConnected();
                    if (!claude.Connected) return "Claude Code isn't connected.";
                    DisconnectClaude();
                    return null;
                case "OpenLogs":
                {
                    string folder = System.IO.Path.Combine(claude.ConfigFolder, "projects");
                    if (!System.IO.Directory.Exists(folder)) folder = claude.ConfigFolder;
                    if (!System.IO.Directory.Exists(folder)) return "Claude Code's folder isn't on this PC yet: " + claude.ConfigFolder;
                    try { System.Diagnostics.Process.Start("explorer.exe", "\"" + folder + "\""); return null; }
                    catch (Exception e) { return "Couldn't open " + folder + ": " + e.Message; }
                }
                case "CheckPlan":
                    if (!claude.Account.SignedIn) return "Sign in to Claude first (Settings → Claude), so Claude Dashboard can check the plan.";
                    claude.CheckSoon(); TickClaudeOnline();
                    return null;
                case "SignIn":
                    if (claude.Account.SignedIn) return "Claude Dashboard is already signed in to Claude.";
                    if (claude.Account.SigningIn) return "The sign-in is already open in your browser.";
                    SignInClaude();
                    return null;
                case "ShowArchived": case "HideArchived":
                    settings.ClaudeShowArchived = name == "ShowArchived";
                    Persist();
                    claudeDirty = true;
                    if (claudeView != null) { claudeView.ShowArchived = settings.ClaudeShowArchived; claudeCard.Update(claudeView); }
                    return null;
                // v0.59.0: Sources' "Keep logs a year" and its Undo (Claude Code's cleanupPeriodDays, with Connect's backups).
                case "KeepLogs": case "UndoKeepLogs":
                {
                    string said;
                    try { said = name == "KeepLogs" ? claude.Connector.KeepLogsAYear() : claude.Connector.UndoKeepLogs(); }
                    catch (Exception e) { said = e.Message; }
                    claude.LogsKeptChanged(); claudeDirty = true;
                    return said;
                }
                default:
                    return "Unknown action: " + (name ?? "(none)") + ".";
            }
        }

        private const uint FlashTray = 2;
        [StructLayout(LayoutKind.Sequential)]
        private struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }
        [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FLASHWINFO info);
    }
}

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
    // front), and Ctrl+1 opens the session that has waited longest, in the attention queue's order. The window's frame
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

        // Ctrl+1: the first session in the attention queue (Needs you first, the longest waiting first). Returns its id,
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

        private const uint FlashTray = 2;
        [StructLayout(LayoutKind.Sequential)]
        private struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }
        [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FLASHWINFO info);
    }
}

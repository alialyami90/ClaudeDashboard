using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace WidgetScreen
{
    // Phase 5 (decision 026): the first run's question, on the window's own confirm sheet before the dashboard starts,
    // whether to bring Widget Screen's Claude setup over from %LOCALAPPDATA%\WidgetScreen (Migration does the work).
    // Asking before the controller exists means the dashboard then starts once, on the settings, sign-in and bridge it
    // will keep, with nothing to reload.
    public sealed partial class AppController
    {
        // What happened, for the status line once the dashboard is up.
        private static string moveMessage;

        // Shows the window and asks, when there's something to bring over and nothing in the new folder yet. Only the
        // two buttons answer: closing the window instead ends the app with nothing written, so the question comes back
        // next time. Returns false when the window was closed.
        public static bool OfferMove(Window window, string settingsPath)
        {
            string oldFolder = Migration.OldFolder, config = ClaudeConnector.DefaultConfigFolder;
            Migration.Found found;
            try
            {
                if (!Migration.ShouldOffer(oldFolder, settingsPath)) return true;
                found = Migration.Look(oldFolder, config);
            }
            catch (Exception e) { Program.Log(e); return true; }

            Palettes.Apply(window.Resources, "Midnight", "Lime", 1);
            var overlay = (Grid)window.FindName("ConfirmOverlay");
            var action = (Button)window.FindName("ConfirmAction"); var cancel = (Button)window.FindName("ConfirmCancel");
            ((TextBlock)window.FindName("ConfirmTitle")).Text = "WIDGET SCREEN";
            ((TextBlock)window.FindName("ConfirmSubject")).Text = "Bring your Claude setup over from Widget Screen?";
            var detail = (TextBlock)window.FindName("ConfirmDetail");
            detail.Text = MoveDetail(found, oldFolder); detail.Visibility = Visibility.Visible;
            object actionWords = action.Content, cancelWords = cancel.Content;
            action.Content = "Bring it over"; AutomationProperties.SetName(action, "Bring it over");
            cancel.Content = "Start fresh"; AutomationProperties.SetName(cancel, "Start fresh");

            bool? answer = null;
            var frame = new DispatcherFrame();
            RoutedEventHandler yes = (sender, e) => { answer = true; frame.Continue = false; };
            RoutedEventHandler no = (sender, e) => { answer = false; frame.Continue = false; };
            EventHandler closed = (sender, e) => frame.Continue = false;
            action.Click += yes; cancel.Click += no; window.Closed += closed;
            overlay.Visibility = Visibility.Visible;
            window.Show();
            action.Focus();
            Dispatcher.PushFrame(frame);
            action.Click -= yes; cancel.Click -= no; window.Closed -= closed;
            if (answer == null) return false;

            overlay.Visibility = Visibility.Collapsed;
            action.Content = actionWords; AutomationProperties.SetName(action, Convert.ToString(actionWords));
            cancel.Content = cancelWords; AutomationProperties.SetName(cancel, "Cancel");
            if (answer == false)
            {
                try { Migration.Decline(settingsPath); } catch (Exception e) { Program.Log(e); }
                if (found.Connected) moveMessage = "Starting fresh. Claude Code still sends its sessions to Widget Screen until you connect it in Settings → Claude.";
                return true;
            }
            try
            {
                Migration.Run(oldFolder, settingsPath, config, BundledHook);
                moveMessage = "Your Claude setup came over from Widget Screen" + (found.Connected ? ", and Claude Code now runs Claude Dashboard's hooks" : "") + ". You can uninstall Widget Screen now.";
            }
            catch (InvalidOperationException e)
            {
                moveMessage = "Your Claude settings came over from Widget Screen, but Claude Code couldn't be connected again: " + e.Message + " Try Settings → Claude → Connect.";
            }
            catch (Exception e)
            {
                Program.Log(e);
                moveMessage = "Widget Screen's Claude setup couldn't be brought over: " + e.GetBaseException().Message + " Details are in error.log next to settings.xml.";
            }
            return true;
        }

        // The sheet's words: what comes over, what doesn't change, and what Start fresh means.
        public static string MoveDetail(Migration.Found found, string oldFolder)
        {
            var parts = new List<string>();
            if (found.Settings) parts.Add("your Claude settings and theme");
            if (found.SignIn) parts.Add("your Claude sign-in");
            if (found.Bridge) parts.Add("Claude Code's sessions");
            string list = parts.Count == 0 ? "nothing" : parts.Count == 1 ? parts[0] : String.Join(", ", parts.GetRange(0, parts.Count - 1)) + " and " + parts[parts.Count - 1];
            string text = "Copies " + list + " from " + oldFolder + ".";
            if (found.Connected) text += " Claude Code's hooks and status line then run Claude Dashboard's own ClaudeHook.exe; its settings.json is backed up first.";
            text += " Widget Screen's folder isn't changed, and you can uninstall Widget Screen afterwards. Start fresh copies nothing";
            text += found.Connected ? ", and Claude Code stays connected to Widget Screen until you connect it here." : ".";
            return text;
        }
    }
}

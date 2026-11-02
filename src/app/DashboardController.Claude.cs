using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace WidgetScreen
{
    // The Claude Code watcher (docs/design/claude-watcher.md, decisions 014–016): Settings → Claude, the card, the
    // header pill and the phone's claude section. While the card is on any page the heartbeat looks at the bridge's
    // session files, Claude Code's own session list and the Claude app's thread records (directory listings; only
    // changed files are read) every Settings → Claude "Update the card every" seconds (2 by default, Quiet mode or
    // not), so the pill can light up from another page; the logs are read on the same interval only while the card is
    // on the page shown. The telemetry receiver listens on loopback while Claude Code is connected, and the plan check
    // runs on its own interval (300 s by default, longer while Anthropic turns checks away) while signed in.
    // Package 3 (decision 025): while the card is on a page, Windows tells the dashboard when one of those folders
    // changes (ClaudeWatch), so the card follows at once and the interval above is only a 30-second fallback; a shown card
    // with sessions running is still worked out at the interval, since turns and revving go by the clock. New alerts
    // become a Windows notification and ride to the phone in every /state.
    public sealed partial class DashboardController
    {
        private ClaudeService claude;
        private ClaudeCard claudeCard;
        private ClaudeView claudeView;
        private Task claudeScan;
        private DateTime lastClaudeScan = DateTime.MinValue, lastClaudeRead = DateTime.MinValue, lastClaudeAlive = DateTime.MinValue;
        private bool claudeSample, claudeOnlineBusy, claudeDirty = true, claudeLogsWaiting, claudeWatchTried;
        // Package 3: what newly happened to the running sessions (Windows notifications and the phone's alerts), and when
        // the card was last worked out.
        private readonly ClaudeAlerts claudeAlerts = new ClaudeAlerts();
        private ClaudeToast claudeToast;
        // Package 4: Allow once and Deny for permission dialogs (ClaudePermissions keeps it out of this file).
        private ClaudePermissions claudePermissions;
        // Package 4: the daily summary files (Settings → Claude → History).
        private ClaudeHistory claudeHistory;
        private bool claudeHistoryCleared;
        private DateTime lastClaudeRefresh = DateTime.MinValue;
        private readonly HashSet<string> claudeNeeding = new HashSet<string>();
        // The project ticks shown in Settings → Claude, by folder, until Save.
        private readonly Dictionary<string, CheckBox> claudeProjectBoxes = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        // The card and service, for the checks (tests/Claude/ClaudeTests.cs).
        public ClaudeCard ClaudeCardView { get { return claudeCard; } }
        public ClaudeService Claude { get { return claude; } }

        // The ClaudeHook.exe that came with this copy of Widget Screen; Connect copies it beside settings.xml.
        private static string BundledHook { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ClaudeBridge.HookExe); } }

        private void InitializeClaude()
        {
            claude = new ClaudeService(SettingsStore.ClaudePath(settingsPath), ClaudeConnector.DefaultConfigFolder, SettingsStore.ClaudeSignInPath(settingsPath));
            claudeCard = new ClaudeCard(window, Control<Grid>("ClaudeBody"), Control<Border>("ClaudeCard"), id => FocusClaudeSession(id), SetClaudeCollapsed, ChooseClaudePeriod);
            claude.PlanEvery = TimeSpan.FromSeconds(settings.ClaudePlanSeconds);
            // Minimising doesn't hide the card's elements, and the heartbeat skips the card while minimised, so the frame
            // clock is told here (review fix 5).
            window.StateChanged += (sender, e) => UpdateClaudeLive();
            Button("ClaudeConnectButton", ConnectClaude);
            Button("ClaudeDisconnectButton", DisconnectClaude);
            Button("ClaudeSignInButton", SignInClaude);
            Button("ClaudeSignOutButton", SignOutClaude);
            Button("ClaudeCardSecondsLess", () => StepClaudeSeconds("ClaudeCardSecondsBox", -1, ClaudeIntervals.Card, settings.ClaudeCardSeconds));
            Button("ClaudeCardSecondsMore", () => StepClaudeSeconds("ClaudeCardSecondsBox", 1, ClaudeIntervals.Card, settings.ClaudeCardSeconds));
            Button("ClaudePlanSecondsLess", () => StepClaudeSeconds("ClaudePlanSecondsBox", -10, ClaudeIntervals.Plan, settings.ClaudePlanSeconds));
            Button("ClaudePlanSecondsMore", () => StepClaudeSeconds("ClaudePlanSecondsBox", 10, ClaudeIntervals.Plan, settings.ClaudePlanSeconds));
            Control<TextBox>("ClaudePlanSecondsBox").TextChanged += (sender, e) => UpdateClaudePlanNote();
            claudeToast = new ClaudeToast(id => { if (!disposed) FocusClaudeSession(id); });
            claudePermissions = new ClaudePermissions(() => claude.BridgeFolder, id => { if (!disposed) FocusClaudeSession(id); }, ClaudeThreadTitle, key => window.TryFindResource(key));
            // Closing the dashboard takes the listener away, so a waiting hook lets Claude Code ask in its own window.
            window.Closed += (sender, e) => claudePermissions.Dispose();
            claudeHistory = new ClaudeHistory(claude.BridgeFolder);
            if (Program.Rendering) return;
            // A watched folder changed or telemetry arrived (package 3): the card looks at once, on its own thread.
            claude.Changed = () => { try { window.Dispatcher.BeginInvoke(new Action(ClaudeWatchFired)); } catch (Exception) { } };
            claude.CheckConnected();
            // After an update, Claude Code runs the bridge that came with it.
            if (claude.Connected) { claude.Connector.RefreshHook(BundledHook); claude.StartTelemetry(); }
            if (claude.Account.StartupWarning != null && pendingMessage == null) pendingMessage = claude.Account.StartupWarning;
        }

        // A labelled fixture instead of this PC's sessions, for renders (--render-claude-sample) and checks.
        public void UseClaudeSample(bool needsYou)
        {
            claudeSample = true;
            claudeView = ClaudeView.Fixture(DateTime.UtcNow, needsYou);
            ClaudeChoices(claudeView);
            claudeCard.Update(claudeView);
        }

        // Which projects the card shows (Settings → Claude → Projects).
        private Func<string, bool> ClaudeFilter { get { return ClaudeService.Filter(settings.ClaudeProjects, settings.ClaudeShowNewProjects); } }
        private ClaudePeriod ClaudePeriodChosen { get { ClaudePeriod p; return Enum.TryParse(settings.ClaudePeriod, out p) ? p : ClaudePeriod.Today; } }
        // The card's own refresh, in seconds (Settings → Claude): the user's number wins over Quiet mode.
        private TimeSpan ClaudeEvery { get { return TimeSpan.FromSeconds(ClaudeIntervals.Card(settings.ClaudeCardSeconds)); } }

        // A project folded into its critter or let out (the card, or the phone's claude.collapse): kept in settings.xml
        // at once, outside the Settings panel, so the phone and the next start see it.
        public void SetClaudeCollapsed(string folder, bool fold)
        {
            string key = ClaudeUsage.Key(folder);
            if (key.Length == 0) return;
            var set = new List<string>((settings.ClaudeCollapsed ?? new string[0]).Where(f => !String.Equals(f, key, StringComparison.OrdinalIgnoreCase)));
            if (fold) set.Add(key);
            settings.ClaudeCollapsed = set.ToArray();
            Persist();
            if (claudeView != null) { if (fold) claudeView.Collapsed.Add(key); else claudeView.Collapsed.Remove(key); }
        }
        private void ChooseClaudePeriod(ClaudePeriod period) { settings.ClaudePeriod = period.ToString(); Persist(); }

        private void TickClaude()
        {
            claudeCard.Tick(ClaudeStill, true);
            UpdateClaudeLive();
            if (claudeSample) return;
            // Renders never read this PC's sessions or logs, or ask for the plan's usage; they use the labelled sample.
            if (Program.Rendering) return;
            // Permission dialogs are answered from the heartbeat too (package 4).
            claudePermissions.Tick(settings.ClaudeAnswer, settings.ClaudeAnswerWait, claude.Connected, settings.QuietMode, DateTime.UtcNow);
            DateTime now = DateTime.UtcNow;
            bool changed = false;
            // The folders are watched while the card is shown (package 3); folders that appear later join within 10 s.
            if (ticks % 10 == 0 || !claudeWatchTried) { claudeWatchTried = true; claude.Watch.Watch(claude.WatchFolders()); }
            // With the folders watched, reading every source is only a fallback every 30 s (or the chosen interval, if
            // longer); without them, the chosen interval as before. Within a second of it, so 2 s doesn't drift to 3.
            var every = claude.Watch.Running && ClaudeWatch.Fallback > ClaudeEvery ? ClaudeWatch.Fallback : ClaudeEvery;
            if (now - lastClaudeRead >= every - TimeSpan.FromMilliseconds(500))
            {
                lastClaudeRead = now;
                bool alive = now - lastClaudeAlive >= TimeSpan.FromSeconds(5);
                if (alive) lastClaudeAlive = now;
                changed |= claude.ReadSessions(now, alive);
                changed |= claude.ReadLimits();
                if (claude.TelemetryChanged) { claude.TelemetryChanged = false; changed = true; }
                if ((claudeScan == null || claudeScan.IsCompleted)) ScanClaudeLogs();
            }
            // A log changed while the card was hidden, a scan was running or one ran moments ago: it's read now.
            if (claudeLogsWaiting && (claudeScan == null || claudeScan.IsCompleted) && now - lastClaudeScan >= ClaudeEvery - TimeSpan.FromMilliseconds(500)) ScanClaudeLogs();
            // Turns, revving and idling go by the clock, so a shown card with something running is worked out again at the
            // chosen interval even when no file changed.
            if (claude.Watch.Running && claudeView != null && claudeView.Sessions.Count > 0 && now - lastClaudeRefresh >= ClaudeEvery - TimeSpan.FromMilliseconds(500)) changed = true;
            if (ticks % 10 == 0)
            {
                bool was = claude.Connected; claude.CheckConnected(); changed |= was != claude.Connected;
                if (claude.Connected) claude.StartTelemetry(); else claude.Telemetry.Stop();
                changed |= claudeView != null && claudeView.TelemetryWarning != claude.TelemetryWarning();
            }
            if (changed || claudeDirty) RefreshClaudeView();
        }
        // The card's frame clock runs only while the window isn't minimised.
        private void UpdateClaudeLive() { claudeCard.SetLive(window.WindowState != WindowState.Minimized); }
        // Quiet mode keeps the cars still only when Settings → Claude says so (decision 019).
        private bool ClaudeStill { get { return settings.QuietMode && settings.ClaudeStillInQuiet; } }

        // The plan check runs on its interval while the widget is signed in to Claude, minimised too (Ali, 1 Nov 2026).
        private void TickClaudeOnline()
        {
            if (Program.Rendering || claudeSample || claudeOnlineBusy || !claude.OnlineDue(DateTime.UtcNow)) return;
            CheckClaudeOnline();
        }

        private async void ScanClaudeLogs()
        {
            lastClaudeScan = DateTime.UtcNow; claudeLogsWaiting = false;
            var scan = Task.Run(() => claude.Usage.Scan(DateTime.UtcNow));
            claudeScan = scan;
            try { await scan; } catch (Exception) { }
            if (disposed) return;
            claudeDirty = true;
            // Watched, the new replies show as soon as they're read (package 3), not on the next heartbeat.
            if (claude.Watch.Running) RefreshClaudeView();
        }

        // A watched folder changed or telemetry arrived (package 3): only what changed is read, and the card follows.
        // Log changes come in bursts while Claude writes, so they're read at most once per chosen interval (2 s by
        // default), as before; the heartbeat picks up one that has to wait.
        private void ClaudeWatchFired()
        {
            if (disposed || claudeSample || Program.Rendering) return;
            var kinds = claude.Watch.Take();
            // A new ask shows at once rather than on the next heartbeat (package 4).
            if ((kinds & ClaudeWatch.Kinds.Sessions) != 0 && claudePermissions.Listening) claudePermissions.Sync(DateTime.UtcNow);
            if (kinds == ClaudeWatch.Kinds.None) return;
            DateTime now = DateTime.UtcNow;
            bool changed = false;
            if ((kinds & ClaudeWatch.Kinds.Sessions) != 0)
            {
                bool alive = now - lastClaudeAlive >= TimeSpan.FromSeconds(5);
                if (alive) lastClaudeAlive = now;
                changed |= claude.ReadSessions(now, alive);
                changed |= claude.ReadLimits();
            }
            if (claude.TelemetryChanged) { claude.TelemetryChanged = false; changed = true; }
            if ((kinds & ClaudeWatch.Kinds.Logs) != 0) claudeLogsWaiting = true;
            if (claudeLogsWaiting && (claudeScan == null || claudeScan.IsCompleted) && now - lastClaudeScan >= ClaudeEvery - TimeSpan.FromMilliseconds(500)) ScanClaudeLogs();
            if (changed) RefreshClaudeView();
        }

        private async void CheckClaudeOnline()
        {
            claudeOnlineBusy = true;
            try { if (await claude.CheckOnlineAsync(DateTime.UtcNow) && !disposed) { claudeDirty = true; if (SettingsOpen) UpdateClaudePlanStatus(); } }
            catch (Exception) { }
            finally { claudeOnlineBusy = false; }
        }

        // The folded projects and which threads Settings → Claude hides.
        private void ClaudeChoices(ClaudeView view)
        {
            view.Collapsed.UnionWith(settings.ClaudeCollapsed ?? new string[0]);
            view.ShowArchived = settings.ClaudeShowArchived; view.HideIdle = settings.ClaudeHideIdle;
        }
        private void RefreshClaudeView()
        {
            claudeDirty = false;
            if (claudeSample) return;
            lastClaudeRefresh = DateTime.UtcNow;
            claudeView = claude.View(DateTime.UtcNow, claude.Usage.Scanned ? claude.Usage.Entries : null, ClaudeFilter);
            ClaudeChoices(claudeView);
            claudeCard.Update(claudeView);
            NotifyClaude(claudeAlerts.Update(claudeView.Sessions, DateTime.UtcNow));
            KeepClaudeHistory(DateTime.UtcNow);
            // A chime for each session that newly needs you, if Settings → Claude asks for one.
            var needing = claudeView.Sessions.Where(s => s.State == ClaudeState.NeedsYou).Select(s => s.Id).ToList();
            bool fresh = needing.Any(id => !claudeNeeding.Contains(id));
            claudeNeeding.Clear(); foreach (var id in needing) claudeNeeding.Add(id);
            if (fresh && settings.ClaudeChime && !settings.QuietMode) Chime();
        }

        // Package 3: a Windows notification for a session that newly needs you, got stuck or finished a long turn, unless
        // Settings → Claude says not to or Quiet mode is on. The newest one shows (each replaces the last); the icon goes
        // once nothing needs you.
        private void NotifyClaude(List<ClaudeAlert> fresh)
        {
            if (claudeView == null || claudeToast == null) return;
            if (claudeView.NeedsYou == 0 && claudeView.Stuck == 0) claudeToast.Clear();
            if (fresh.Count == 0 || !settings.ClaudeToast || settings.QuietMode || Program.Rendering) return;
            // A permission dialog with its own Allow/Deny toast needs no second notification (package 4).
            if (claudePermissions.Listening) { claudePermissions.Sync(DateTime.UtcNow); fresh = fresh.Where(x => x.Kind != "need" || !claudePermissions.Asking(x.Session)).ToList(); if (fresh.Count == 0) return; }
            var a = fresh.OrderBy(x => x.Kind == "need" ? 0 : x.Kind == "stuck" ? 1 : 2).First();
            string title = (a.Kind == "need" ? "Claude needs you" : a.Kind == "stuck" ? "Claude is stuck" : "Claude finished") + " · " + a.Project;
            string what = a.Kind == "need" ? ClaudeText.Ask(a.Text) : a.Text;
            if (what.Length == 0) what = a.Kind == "need" ? "Waiting for you" : a.Kind == "stuck" ? "Stopped on an error" : "Done";
            if (fresh.Count > 1) what += " (and " + (fresh.Count - 1) + " more)";
            claudeToast.Show(title, a.Title + ": " + what, a.Session, a.Kind != "done");
        }

        // Package 4: today's needs and waits are followed and the day files written once a minute, from the replies the
        // card already read; turning History off deletes them.
        private void KeepClaudeHistory(DateTime now)
        {
            if (claudeHistory == null || claudeView == null || claudeView.Sample) return;
            if (!settings.ClaudeHistory)
            {
                if (!claudeHistoryCleared) { claudeHistory.Clear(); claudeHistoryCleared = true; }
                return;
            }
            claudeHistoryCleared = false;
            claudeHistory.Note(claudeView.Sessions, now);
            if (claude.Usage.Scanned && claudeHistory.Due(now)) claudeHistory.Save(claude.Usage.Entries, ClaudeProjectName, now);
        }
        private string ClaudeProjectName(string folder)
        {
            KeyValuePair<string, DateTime> known;
            return claudeView != null && claudeView.Known.TryGetValue(folder, out known) ? known.Key : "";
        }

        // A session's thread name for the permission toast, or "" when the card doesn't know it.
        private string ClaudeThreadTitle(string id)
        {
            var session = claudeView == null ? null : claudeView.Sessions.FirstOrDefault(x => x.Id == id);
            return session == null ? "" : session.Title ?? "";
        }

        // Brings a session's terminal or editor forward (the one place the dashboard activates another window, because
        // the user tapped for it). Returns null when it worked, or what went wrong.
        private string FocusClaudeSession(string id)
        {
            var session = claudeView == null ? null : claudeView.Sessions.FirstOrDefault(s => s.Id == id);
            if (session == null || claudeSample) return null;
            var handle = ClaudeProcesses.Window(session.Window, session.WindowPid);
            string problem = handle == IntPtr.Zero ? "Couldn't find the window for " + session.Shown + "." : !ClaudeProcesses.Focus(handle) ? "Windows didn't let Claude Dashboard bring " + session.Shown + " forward. It's flashing on the taskbar." : null;
            if (problem != null) Notify(problem);
            return problem;
        }

        // ----- Settings → Claude -----

        private bool SettingsOpen { get { var overlay = window.FindName("SettingsOverlay") as UIElement; return overlay != null && overlay.Visibility == Visibility.Visible; } }

        private void UpdateClaudeSettings()
        {
            claude.CheckConnected();
            bool update = claude.Connector.NeedsUpdate();
            string portWarning = claude.TelemetryWarning();
            Text("ClaudeConnectionStatus", !claude.Connected ? "Not connected" : update ? "Connected by an earlier version. Update the connection to add the latest hooks (permission answers, API errors, subagents, tasks, model switches, usage-limit waits) and telemetry."
                : portWarning.Length > 0 ? portWarning + ". Update the connection to send telemetry to a free port." : "Connected. Every Claude Code session on this PC shows on the card.");
            update |= claude.Connected && portWarning.Length > 0;
            Text("ClaudeFolderText", "Claude Code folder: " + claude.ConfigFolder);
            var connect = Control<Button>("ClaudeConnectButton");
            connect.Content = update ? "Update connection" : "Connect Claude Code";
            connect.Visibility = !claude.Connected || update ? Visibility.Visible : Visibility.Collapsed;
            Control<Button>("ClaudeDisconnectButton").Visibility = claude.Connected ? Visibility.Visible : Visibility.Collapsed;
            var conflicts = claude.Connector.TelemetryConflicts();
            Text("ClaudeTelemetryText", conflicts.Count > 0 ? "Telemetry is left out: your settings.json already sets " + String.Join(", ", conflicts) + ". Remove those to let Claude Dashboard show live usage per session; until then usage comes from Claude Code's logs."
                : !claude.Connected ? "" : claude.Telemetry.Problem ?? (claude.Telemetry.LastReceived > DateTime.MinValue ? "Telemetry last arrived at " + ClaudeText.Clock(claude.Telemetry.LastReceived, settings.Use24HourClock) + "." : "Telemetry arrives from Claude Code sessions started after connecting."));
            Control<CheckBox>("ClaudeChimeChoice").IsChecked = settings.ClaudeChime;
            Control<CheckBox>("ClaudeToastChoice").IsChecked = settings.ClaudeToast;
            // Package 3: what the organisation's managed settings keep Widget Screen's connection from doing.
            var managed = claude.Connector.ManagedOverrides();
            Text("ClaudeManagedText", managed.Count == 0 ? "" : "Your organisation's managed Claude Code settings (" + claude.Connector.ManagedPath + ") take precedence over yours, so Claude Dashboard's " + String.Join(", ", managed) + (managed.Count == 1 ? " doesn't" : " don't") + " take effect. Ask whoever manages Claude Code on this PC to allow them.");
            Control<TextBlock>("ClaudeManagedText").Visibility = managed.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            Control<CheckBox>("ClaudeStillChoice").IsChecked = settings.ClaudeStillInQuiet;
            Control<CheckBox>("ClaudeApiValueChoice").IsChecked = settings.ClaudeShowApiValue;
            Control<CheckBox>("ClaudeHideArchivedChoice").IsChecked = !settings.ClaudeShowArchived;
            Control<CheckBox>("ClaudeHideIdleChoice").IsChecked = settings.ClaudeHideIdle;
            // The side panel's four facts (package 2).
            var facts = ClaudeFactKinds.Clean(settings.ClaudeFacts);
            for (int i = 0; i < 4; i++)
            {
                var box = Control<ComboBox>("ClaudeFact" + i + "Choice");
                box.ItemsSource = ClaudeFactKinds.All.Select(k => ClaudeFactKinds.Name(k)).ToList();
                box.SelectedIndex = Array.IndexOf(ClaudeFactKinds.All, facts[i]);
            }
            Control<CheckBox>("ClaudeShowNewChoice").IsChecked = settings.ClaudeShowNewProjects;
            Control<TextBox>("ClaudeCardSecondsBox").Text = settings.ClaudeCardSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Control<TextBox>("ClaudePlanSecondsBox").Text = settings.ClaudePlanSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            UpdateClaudePlanNote();
            UpdateClaudePlanStatus();
            FillClaudeProjects();
        }

        private void UpdateClaudePlanStatus()
        {
            var plan = claude.Plan; var limits = claude.Limits;
            string asOf = limits != null && limits.Known && limits.Source == "online" ? " Checked at " + ClaudeText.Clock(limits.UpdatedAt, settings.Use24HourClock) + "." : "";
            Text("ClaudePlanStatus", claude.Account.SigningIn ? "Waiting for you to sign in in your browser…"
                : plan == ClaudePlanState.Expired ? "Your Claude sign-in has ended. Sign in again to keep the plan meters current."
                : plan == ClaudePlanState.SignedOut ? "Not signed in. The card's plan meters need this."
                : plan == ClaudePlanState.NoMeters ? "Signed in. Anthropic says your plan has no usage meters, so the card shows none."
                : plan == ClaudePlanState.Failed ? (claude.BackingOff ? "Signed in. Anthropic asked Claude Dashboard to slow down; it tries again in " + ClaudeText.Wait(claude.NextWait) + "." : "Signed in. The last check didn't go through; it tries again in " + ClaudeText.Wait(claude.NextWait) + ".") + asOf
                : plan == ClaudePlanState.Reading ? "Signed in. Reading your plan's limits…" : "Signed in." + asOf);
            var signIn = Control<Button>("ClaudeSignInButton");
            signIn.Content = claude.Account.SigningIn ? "Cancel sign-in" : plan == ClaudePlanState.Expired ? "Sign in again" : "Sign in with Claude";
            signIn.Visibility = plan == ClaudePlanState.SignedOut || plan == ClaudePlanState.Expired || claude.Account.SigningIn ? Visibility.Visible : Visibility.Collapsed;
            Control<Button>("ClaudeSignOutButton").Visibility = plan != ClaudePlanState.SignedOut && !claude.Account.SigningIn ? Visibility.Visible : Visibility.Collapsed;
        }

        // One tick per project the sources know, newest first: its name, when it was last used and this week's usage.
        private void FillClaudeProjects()
        {
            var host = Control<StackPanel>("ClaudeProjectsList");
            host.Children.Clear(); claudeProjectBoxes.Clear();
            if (!claude.Usage.Scanned && !Program.Rendering) { try { claude.Usage.Scan(DateTime.UtcNow); } catch (Exception) { } }
            if (!Program.Rendering) claude.ReadSessions(DateTime.UtcNow, false);
            var projects = Program.Rendering || claudeSample ? new List<ClaudeProjectInfo>() : claude.Projects(DateTime.UtcNow, ClaudeFilter);
            Control<TextBlock>("ClaudeProjectsEmpty").Visibility = projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var project in projects.Take(40))
            {
                var name = new TextBlock { Text = project.Name, TextTrimming = TextTrimming.CharacterEllipsis };
                var detail = new TextBlock { Text = "Last used " + project.LastUsed.ToLocalTime().ToString("MMM d", culture) + " · " + ClaudeText.Threads(project.Threads) + " this week · " + ClaudeUsage.Format(project.WeekTokens) + " tokens · " + ClaudeUsage.Money(project.WeekCost) + " at API prices", TextTrimming = TextTrimming.CharacterEllipsis };
                detail.SetResourceReference(FrameworkElement.StyleProperty, "Small");
                var text = new StackPanel(); text.Children.Add(name); text.Children.Add(detail);
                var box = new CheckBox { Content = text, IsChecked = project.Shown, ToolTip = project.Folder, Margin = new Thickness(0, 0, 0, 6) };
                System.Windows.Automation.AutomationProperties.SetName(box, "Show " + project.Name + " on the Claude card");
                claudeProjectBoxes[project.Folder] = box;
                host.Children.Add(box);
            }
        }

        // What Save keeps: a choice for every project listed (so a project ticked off stays off), plus earlier choices
        // for projects that aren't listed now.
        private void SaveClaudeChoices(AppSettings candidate)
        {
            candidate.ClaudeChime = Control<CheckBox>("ClaudeChimeChoice").IsChecked == true;
            candidate.ClaudeToast = Control<CheckBox>("ClaudeToastChoice").IsChecked == true;
            candidate.ClaudeStillInQuiet = Control<CheckBox>("ClaudeStillChoice").IsChecked == true;
            candidate.ClaudeShowApiValue = Control<CheckBox>("ClaudeApiValueChoice").IsChecked == true;
            candidate.ClaudeShowArchived = Control<CheckBox>("ClaudeHideArchivedChoice").IsChecked != true;
            candidate.ClaudeHideIdle = Control<CheckBox>("ClaudeHideIdleChoice").IsChecked == true;
            candidate.ClaudeFacts = ClaudeFactKinds.Clean(Enumerable.Range(0, 4).Select(i => { int at = Control<ComboBox>("ClaudeFact" + i + "Choice").SelectedIndex; return at >= 0 && at < ClaudeFactKinds.All.Length ? ClaudeFactKinds.All[at] : ""; }).ToArray());
            candidate.ClaudeShowNewProjects = Control<CheckBox>("ClaudeShowNewChoice").IsChecked == true;
            candidate.ClaudeCardSeconds = ClaudeIntervals.Parse(Control<TextBox>("ClaudeCardSecondsBox").Text, settings.ClaudeCardSeconds, ClaudeIntervals.Card);
            candidate.ClaudePlanSeconds = ClaudeIntervals.Parse(Control<TextBox>("ClaudePlanSecondsBox").Text, settings.ClaudePlanSeconds, ClaudeIntervals.Plan);
            if (candidate.ClaudePlanSeconds != settings.ClaudePlanSeconds) { claude.PlanEvery = TimeSpan.FromSeconds(candidate.ClaudePlanSeconds); claude.CheckSoon(); }
            var choices = (settings.ClaudeProjects ?? new ClaudeProjectChoice[0]).Where(c => c != null && c.Folder != null && !claudeProjectBoxes.ContainsKey(ClaudeUsage.Key(c.Folder))).ToList();
            foreach (var pair in claudeProjectBoxes) choices.Add(new ClaudeProjectChoice { Folder = pair.Key, Shown = pair.Value.IsChecked == true });
            candidate.ClaudeProjects = choices.ToArray();
            claudeDirty = true;
        }

        // The −/+ buttons beside the two interval boxes, and the note under the plan's when it's under a minute.
        private void StepClaudeSeconds(string box, int step, Func<int, int> clamp, int fallback)
        {
            var text = Control<TextBox>(box);
            int value = ClaudeIntervals.Parse(text.Text, fallback, clamp);
            text.Text = clamp(value + step).ToString(System.Globalization.CultureInfo.InvariantCulture);
            UpdateClaudePlanNote();
        }
        private void UpdateClaudePlanNote()
        {
            int plan = ClaudeIntervals.Parse(Control<TextBox>("ClaudePlanSecondsBox").Text, settings.ClaudePlanSeconds, ClaudeIntervals.Plan);
            Control<TextBlock>("ClaudePlanSecondsNote").Visibility = plan < ClaudeIntervals.PlanWarnBelow ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void ConnectClaude()
        {
            bool update = claude.Connected;
            // Package 3: say up front what the organisation's managed settings will override.
            var managed = claude.Connector.ManagedOverrides();
            bool yes = await confirm.AskAsync("CLAUDE CODE", update ? "Update the connection?" : "Connect Claude Code?",
                "This adds Claude Dashboard's hooks, status line and telemetry settings to your Claude Code settings (settings.json). Telemetry goes only to Claude Dashboard on this PC and never includes your prompts. A backup is saved next to it. Your current status line keeps showing."
                + (managed.Count == 0 ? "" : " Your organisation's managed settings override Claude Dashboard's " + String.Join(", ", managed) + ", so " + (managed.Count == 1 ? "that part" : "those parts") + " won't work until they're allowed."), update ? "Update" : "Connect");
            if (!yes || disposed) return;
            try
            {
                // Binds the telemetry port first and writes the one it got (decision 023), then reads every source at
                // once, so sessions already running show without waiting for their next hook.
                claude.Connect(BundledHook);
                claude.CheckConnected();
                claude.ReadSessions(DateTime.UtcNow, true);
                if (claudeScan == null || claudeScan.IsCompleted) ScanClaudeLogs();
                Text("SettingsStatus", "Claude Code is connected. Sessions show on the card. Sessions already open pick up telemetry when they restart.");
            }
            catch (Exception e) { Text("SettingsStatus", e is InvalidOperationException ? e.Message : "Couldn't connect Claude Code: " + e.Message); }
            UpdateClaudeSettings(); claudeDirty = true;
        }

        private async void DisconnectClaude()
        {
            bool yes = await confirm.AskAsync("CLAUDE CODE", "Disconnect Claude Code?", "This takes Claude Dashboard's hooks, status line and telemetry settings out of your Claude Code settings (settings.json) and puts your earlier status line back. A backup is saved next to it.", "Disconnect");
            if (!yes || disposed) return;
            try { claude.Connector.Disconnect(); claude.Telemetry.Stop(); Text("SettingsStatus", "Claude Code is disconnected."); }
            catch (Exception e) { Text("SettingsStatus", e is InvalidOperationException ? e.Message : "Couldn't disconnect Claude Code: " + e.Message); }
            UpdateClaudeSettings(); claudeDirty = true;
        }

        private async void SignInClaude()
        {
            if (claude.Account.SigningIn) { claude.Account.CancelSignIn(); return; }
            Text("SettingsStatus", "Sign in to Claude in your browser, then come back here.");
            var signing = claude.Account.SignInAsync(message => window.Dispatcher.BeginInvoke(new Action(() => { if (!disposed) Text("SettingsStatus", message); })));
            UpdateClaudePlanStatus();
            try
            {
                await signing;
                if (disposed) return;
                Text("SettingsStatus", "Claude Dashboard is signed in to Claude. The plan meters fill in a moment.");
                claude.CheckSoon(); TickClaudeOnline();
            }
            catch (Exception e)
            {
                if (disposed) return;
                Text("SettingsStatus", e is OperationCanceledException ? "The Claude sign-in was cancelled." : e is UnauthorizedAccessException ? "Claude didn't accept the sign-in. Try again." : "Couldn't sign in to Claude: " + e.Message);
            }
            UpdateClaudePlanStatus(); claudeDirty = true;
        }

        private async void SignOutClaude()
        {
            bool yes = await confirm.AskAsync("CLAUDE", "Sign out of Claude?", "Claude Dashboard forgets its Claude sign-in on this PC. The plan meters stop updating until you sign in again. Claude Code's own sign-in isn't affected.", "Sign out");
            if (!yes || disposed) return;
            claude.Account.SignOut();
            Text("SettingsStatus", "Signed out of Claude.");
            UpdateClaudePlanStatus(); claudeDirty = true;
        }
    }
}

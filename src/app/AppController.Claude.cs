using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ClaudeDashboard
{
    // The Claude Code watcher in Claude Dashboard's window (decisions 014–025, phase 4 of the transformation plan): the
    // card, the heartbeat, the Windows notification and Settings → Claude. The window is the card, so there are no
    // pages, no "needs you" pill and no phone link. While the window is open (minimised too) the dashboard watches the
    // bridge's session files, Claude Code's own session list and the Claude app's thread records (ClaudeWatch; only
    // changed files are read, with a full read every 30 s as the fallback or at the chosen interval when a watcher
    // can't run), so a session that needs you still chimes and notifies from the taskbar. The logs are scanned and
    // the cars move only while the window isn't minimised. The plan check runs on its own interval while signed in.
    // Package 4 (Watcher) adds its own calls here at the swap.
    public sealed partial class AppController
    {
        private ClaudeService claude;
        private ClaudeCard claudeCard;
        private ClaudeView claudeView;
        private Task claudeScan;
        private DateTime lastClaudeScan = DateTime.MinValue, lastClaudeRead = DateTime.MinValue, lastClaudeAlive = DateTime.MinValue;
        private bool claudeSample, claudeOnlineBusy, claudeDirty = true, claudeLogsWaiting, claudeWatchTried;
        private readonly ClaudeAlerts claudeAlerts = new ClaudeAlerts();
        private ClaudeToast claudeToast;
        // Package 4: the daily summary files (Settings → Claude → History).
        private ClaudeHistory claudeHistory;
        private bool claudeHistoryCleared;
        private long[] claudeDays; private long? claudeLastWeek; private DateTime claudeDaysAt = DateTime.MinValue;
        private DateTime lastClaudeRefresh = DateTime.MinValue;
        private readonly HashSet<string> claudeNeeding = new HashSet<string>();
        // The project ticks shown in Settings → Claude, by folder, until Save.
        private readonly Dictionary<string, CheckBox> claudeProjectBoxes = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        // The card and service, for the checks.
        public ClaudeCard ClaudeCardView { get { return claudeCard; } }
        public ClaudeService Claude { get { return claude; } }

        // The ClaudeHook.exe that came with this copy of the app; Connect copies it beside settings.xml.
        private static string BundledHook { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ClaudeBridge.HookExe); } }

        private void InitializeClaude()
        {
            claude = new ClaudeService(SettingsStore.ClaudePath(settingsPath), ClaudeConnector.DefaultConfigFolder, SettingsStore.ClaudeSignInPath(settingsPath));
            claudeCard = new ClaudeCard(window, Control<Grid>("ClaudeBody"), Control<Border>("WindowEdge"), id => FocusClaudeSession(id), SetClaudeCollapsed, ChooseClaudePeriod);
            claudeCard.Rendered += UpdateHeader;
            claude.PlanEvery = TimeSpan.FromSeconds(settings.ClaudePlanSeconds);
            // Minimising doesn't hide the card's elements, so the frame clock is told here.
            window.StateChanged += (sender, e) => { UpdateClaudeLive(); if (CardShown) claudeDirty = true; };
            claudeToast = new ClaudeToast(id => { if (!disposed) { ShowWindow(); FocusClaudeSession(id); } });
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
        public void UseClaudeSample(bool needsYou) { UseClaudeSample(needsYou, false); }
        // crowded: 25 threads over eight projects (renders and the layout checks).
        public void UseClaudeSample(bool needsYou, bool crowded)
        {
            claudeSample = true;
            claudeView = ClaudeView.Fixture(DateTime.UtcNow, needsYou, crowded);
            ClaudeChoices(claudeView);
            claudeCard.Update(claudeView);
            UpdateHeader();
        }

        // The pit wall shows unless the window is minimised.
        private bool CardShown { get { return window.WindowState != WindowState.Minimized; } }
        private Func<string, bool> ClaudeFilter { get { return ClaudeService.Filter(settings.ClaudeProjects, settings.ClaudeShowNewProjects); } }
        private ClaudePeriod ClaudePeriodChosen { get { ClaudePeriod p; return Enum.TryParse(settings.ClaudePeriod, out p) ? p : ClaudePeriod.Today; } }
        private TimeSpan ClaudeEvery { get { return TimeSpan.FromSeconds(ClaudeIntervals.Card(settings.ClaudeCardSeconds)); } }

        // A garage rolled in or out: kept in settings.xml at once, outside Settings, so the next start sees it.
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
            bool shown = CardShown;
            claudeCard.Tick(ClaudeStill, shown);
            UpdateClaudeLive();
            if (claudeSample || Program.Rendering) return;
            DateTime now = DateTime.UtcNow;
            bool changed = false;
            // The folders are watched while the app runs (package 3); folders that appear later join within 10 s.
            if (ticks % 10 == 0 || !claudeWatchTried) { claudeWatchTried = true; claude.Watch.Watch(claude.WatchFolders()); }
            // Watched, reading every source is only a fallback every 30 s (or the chosen interval, if longer); without
            // the watchers, the chosen interval. Within a second of it, so 2 s doesn't drift to 3.
            var every = claude.Watch.Running && ClaudeWatch.Fallback > ClaudeEvery ? ClaudeWatch.Fallback : ClaudeEvery;
            if (now - lastClaudeRead >= every - TimeSpan.FromMilliseconds(500))
            {
                lastClaudeRead = now;
                bool alive = now - lastClaudeAlive >= TimeSpan.FromSeconds(5);
                if (alive) lastClaudeAlive = now;
                changed |= claude.ReadSessions(now, alive);
                changed |= claude.ReadLimits();
                if (claude.TelemetryChanged) { claude.TelemetryChanged = false; changed = true; }
                if (shown && (claudeScan == null || claudeScan.IsCompleted)) ScanClaudeLogs();
            }
            // A log changed while minimised, a scan was running or one ran moments ago: it's read now.
            if (claudeLogsWaiting && shown && (claudeScan == null || claudeScan.IsCompleted) && now - lastClaudeScan >= ClaudeEvery - TimeSpan.FromMilliseconds(500)) ScanClaudeLogs();
            // Turns, revving and idling go by the clock, so a shown pit wall with something running is worked out again
            // at the chosen interval even when no file changed.
            if (claude.Watch.Running && shown && claudeView != null && claudeView.Sessions.Count > 0 && now - lastClaudeRefresh >= ClaudeEvery - TimeSpan.FromMilliseconds(500)) changed = true;
            if (ticks % 10 == 0)
            {
                bool was = claude.Connected; claude.CheckConnected(); changed |= was != claude.Connected;
                if (claude.Connected) claude.StartTelemetry(); else claude.Telemetry.Stop();
                changed |= claudeView != null && claudeView.TelemetryWarning != claude.TelemetryWarning();
            }
            if (changed || claudeDirty) RefreshClaudeView();
        }
        // The cars move only while the window isn't minimised.
        private void UpdateClaudeLive() { claudeCard.SetLive(CardShown); }
        private bool ClaudeStill { get { return settings.QuietMode && settings.ClaudeStillInQuiet; } }

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
            if (claude.Watch.Running && CardShown) RefreshClaudeView();
        }

        // A watched folder changed or telemetry arrived (package 3): only what changed is read, and the pit wall follows.
        private void ClaudeWatchFired()
        {
            if (disposed || claudeSample || Program.Rendering) return;
            var kinds = claude.Watch.Take();
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
            if (claudeLogsWaiting && CardShown && (claudeScan == null || claudeScan.IsCompleted) && now - lastClaudeScan >= ClaudeEvery - TimeSpan.FromMilliseconds(500)) ScanClaudeLogs();
            if (changed) RefreshClaudeView();
        }

        private async void CheckClaudeOnline()
        {
            claudeOnlineBusy = true;
            try { if (await claude.CheckOnlineAsync(DateTime.UtcNow) && !disposed) { claudeDirty = true; if (SettingsOpen) UpdateClaudePlanStatus(); } }
            catch (Exception) { }
            finally { claudeOnlineBusy = false; }
        }

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
            ClaudeDays(claudeView, DateTime.UtcNow);
            claudeCard.Update(claudeView);
            NotifyClaude(claudeAlerts.Update(claudeView.Sessions, DateTime.UtcNow));
            KeepClaudeHistory(DateTime.UtcNow);
            var needing = claudeView.Sessions.Where(s => s.State == ClaudeState.NeedsYou).Select(s => s.Id).ToList();
            bool fresh = needing.Any(id => !claudeNeeding.Contains(id));
            claudeNeeding.Clear(); foreach (var id in needing) claudeNeeding.Add(id);
            if (fresh && settings.ClaudeChime && !settings.QuietMode) Chime();
            UpdateHeader();
        }

        // Package 3: a Windows notification for a session that newly needs you, got stuck or finished a long turn, unless
        // Settings → Claude says not to or Quiet mode is on. The newest one shows; the icon goes once nothing needs you.
        private void NotifyClaude(List<ClaudeAlert> fresh)
        {
            if (claudeView == null || claudeToast == null) return;
            if (claudeView.NeedsYou == 0 && claudeView.Stuck == 0) claudeToast.Clear();
            if (fresh.Count == 0 || !settings.ClaudeToast || settings.QuietMode || Program.Rendering) return;
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
        // The plan row's 7-day bars and last week's tick, worked out once a minute (the files change no more often).
        private void ClaudeDays(ClaudeView view, DateTime now)
        {
            if (claudeHistory == null || !settings.ClaudeHistory || !claude.Usage.Scanned) { claudeDays = null; claudeLastWeek = null; claudeDaysAt = DateTime.MinValue; return; }
            if (claudeDays == null || now - claudeDaysAt >= ClaudeHistory.WriteEvery || now < claudeDaysAt)
            {
                var today = claudeHistory.Recent(claude.Usage.Entries, null, now, 1);
                claudeDays = claudeHistory.Spark(now, today.Count > 0 ? today[0] : null);
                claudeLastWeek = claudeHistory.SameTimeLastWeek(now);
                claudeDaysAt = now;
            }
            view.Days = claudeDays; view.LastWeekByNow = claudeLastWeek;
        }
        private string ClaudeProjectName(string folder)
        {
            KeyValuePair<string, DateTime> known;
            return claudeView != null && claudeView.Known.TryGetValue(folder, out known) ? known.Key : "";
        }

        // A click on the notification brings the dashboard back from the taskbar before the session's own window.
        private void ShowWindow()
        {
            if (Program.Rendering) return;
            if (window.WindowState == WindowState.Minimized) window.WindowState = settings.WindowMaximised ? WindowState.Maximized : WindowState.Normal;
        }

        // Brings a session's terminal or editor forward. Returns null when it worked, or what went wrong.
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

        // The tab's buttons, each time the Settings window opens.
        private void BindClaudeSettings()
        {
            Button("ClaudeConnectButton", ConnectClaude);
            Button("ClaudeDisconnectButton", DisconnectClaude);
            Button("ClaudeSignInButton", SignInClaude);
            Button("ClaudeSignOutButton", SignOutClaude);
            Button("ClaudeCardSecondsLess", () => StepClaudeSeconds("ClaudeCardSecondsBox", -1, ClaudeIntervals.Card, settings.ClaudeCardSeconds));
            Button("ClaudeCardSecondsMore", () => StepClaudeSeconds("ClaudeCardSecondsBox", 1, ClaudeIntervals.Card, settings.ClaudeCardSeconds));
            Button("ClaudePlanSecondsLess", () => StepClaudeSeconds("ClaudePlanSecondsBox", -10, ClaudeIntervals.Plan, settings.ClaudePlanSeconds));
            Button("ClaudePlanSecondsMore", () => StepClaudeSeconds("ClaudePlanSecondsBox", 10, ClaudeIntervals.Plan, settings.ClaudePlanSeconds));
            Control<TextBox>("ClaudePlanSecondsBox").TextChanged += (sender, e) => UpdateClaudePlanNote();
            Button("ClaudeCopySummaryButton", CopyClaudeSummary);
            Button("ClaudeSaveCsvButton", SaveClaudeCsv);
        }

        private void UpdateClaudeSettings()
        {
            if (!SettingsOpen) return;
            claude.CheckConnected();
            bool update = claude.Connector.NeedsUpdate();
            string portWarning = claude.TelemetryWarning();
            Text("ClaudeConnectionStatus", !claude.Connected ? "Not connected" : update ? "Connected by an earlier version. Update the connection to add the latest hooks (API errors, subagents, tasks, model switches, usage-limit waits) and telemetry."
                : portWarning.Length > 0 ? portWarning + ". Update the connection to send telemetry to a free port." : "Connected. Every Claude Code session on this PC shows on the pit wall.");
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
            Control<CheckBox>("ClaudeHistoryChoice").IsChecked = settings.ClaudeHistory;
            Control<CheckBox>("ClaudePlainChoice").IsChecked = settings.ClaudePlain;
            var managed = claude.Connector.ManagedOverrides();
            Text("ClaudeManagedText", managed.Count == 0 ? "" : "Your organisation's managed Claude Code settings (" + claude.Connector.ManagedPath + ") take precedence over yours, so Claude Dashboard's " + String.Join(", ", managed) + (managed.Count == 1 ? " doesn't" : " don't") + " take effect. Ask whoever manages Claude Code on this PC to allow them.");
            Control<TextBlock>("ClaudeManagedText").Visibility = managed.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            Control<CheckBox>("ClaudeStillChoice").IsChecked = settings.ClaudeStillInQuiet;
            Control<CheckBox>("ClaudeApiValueChoice").IsChecked = settings.ClaudeShowApiValue;
            Control<CheckBox>("ClaudeHideArchivedChoice").IsChecked = !settings.ClaudeShowArchived;
            Control<CheckBox>("ClaudeHideIdleChoice").IsChecked = settings.ClaudeHideIdle;
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
            if (!SettingsOpen) return;
            var plan = claude.Plan; var limits = claude.Limits;
            string asOf = limits != null && limits.Known && limits.Source == "online" ? " Checked at " + ClaudeText.Clock(limits.UpdatedAt, settings.Use24HourClock) + "." : "";
            Text("ClaudePlanStatus", claude.Account.SigningIn ? "Waiting for you to sign in in your browser…"
                : plan == ClaudePlanState.Expired ? "Your Claude sign-in has ended. Sign in again to keep the plan meters current."
                : plan == ClaudePlanState.SignedOut ? "Not signed in. The plan meters need this."
                : plan == ClaudePlanState.NoMeters ? "Signed in. Anthropic says your plan has no usage meters, so the pit wall shows none."
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
            if (!claude.Usage.Scanned && !Program.Rendering && !claudeSample) { try { claude.Usage.Scan(DateTime.UtcNow); } catch (Exception) { } }
            if (!Program.Rendering && !claudeSample) claude.ReadSessions(DateTime.UtcNow, false);
            var projects = Program.Rendering || claudeSample ? new List<ClaudeProjectInfo>() : claude.Projects(DateTime.UtcNow, ClaudeFilter);
            Control<TextBlock>("ClaudeProjectsEmpty").Visibility = projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var project in projects.Take(40))
            {
                var name = new TextBlock { Text = project.Name, TextTrimming = TextTrimming.CharacterEllipsis };
                var detail = new TextBlock { Text = "Last used " + project.LastUsed.ToLocalTime().ToString("MMM d", culture) + " · " + ClaudeText.Threads(project.Threads) + " this week · " + ClaudeUsage.Format(project.WeekTokens) + " tokens · " + ClaudeUsage.Money(project.WeekCost) + " at API prices", TextTrimming = TextTrimming.CharacterEllipsis };
                detail.SetResourceReference(FrameworkElement.StyleProperty, "Small");
                var text = new StackPanel(); text.Children.Add(name); text.Children.Add(detail);
                var box = new CheckBox { Content = text, IsChecked = project.Shown, ToolTip = project.Folder, Margin = new Thickness(0, 0, 0, 4) };
                System.Windows.Automation.AutomationProperties.SetName(box, "Show " + project.Name + " on the pit wall");
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
            candidate.ClaudeHistory = Control<CheckBox>("ClaudeHistoryChoice").IsChecked == true;
            candidate.ClaudePlain = Control<CheckBox>("ClaudePlainChoice").IsChecked == true;
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

        // Package 4: Settings → Claude → History's Copy summary and Save CSV, both for the last 7 days and both from what
        // the pit wall already read (names, counts and costs only).
        private bool ClaudeExportReady()
        {
            if (claudeSample || claudeHistory == null) { Text("SettingsStatus", "The pit wall is showing a sample, so there's nothing to export."); return false; }
            if (!claude.Usage.Scanned) { Text("SettingsStatus", "Claude Code's logs are still being read. Try again in a moment."); return false; }
            return true;
        }
        private void CopyClaudeSummary()
        {
            if (!ClaudeExportReady()) return;
            DateTime now = DateTime.UtcNow;
            string text = ClaudeExport.Summary(claudeHistory.Recent(claude.Usage.Entries, ClaudeProjectName, now, ClaudeExport.Days), now);
            try { Clipboard.SetText(text); Text("SettingsStatus", "Copied a summary of the last 7 days. Paste it anywhere."); }
            catch (Exception e) { Text("SettingsStatus", "Couldn't copy the summary: " + e.Message); }
        }
        private void SaveClaudeCsv()
        {
            if (!ClaudeExportReady()) return;
            DateTime now = DateTime.UtcNow;
            var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Save the last 7 days as CSV", Filter = "CSV files (*.csv)|*.csv", DefaultExt = ".csv", AddExtension = true, OverwritePrompt = true,
                FileName = "claude-code-" + DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ".csv" };
            if (dialog.ShowDialog(settingsWindow) != true) return;
            try
            {
                // With a byte order mark, so Excel reads names in any language.
                File.WriteAllText(dialog.FileName, ClaudeExport.Csv(claude.Usage.Entries, ClaudeThreadTitle, ClaudeProjectName, now), new System.Text.UTF8Encoding(true));
                Text("SettingsStatus", "Saved " + Path.GetFileName(dialog.FileName) + ".");
            }
            catch (Exception e) { Text("SettingsStatus", "Couldn't save the CSV: " + e.Message); }
        }
        // A thread's typed, generated or Claude app name for the CSV, or "" (its id label is used then). A name taken from
        // the first request is never written to disk (decision 018).
        private string ClaudeThreadTitle(string id)
        {
            var thread = claudeView == null ? null : claudeView.Threads.FirstOrDefault(x => x.Id == id);
            return thread == null || thread.TitleAsked ? "" : thread.Title ?? "";
        }

        private void StepClaudeSeconds(string box, int step, Func<int, int> clamp, int fallback)
        {
            var text = Control<TextBox>(box);
            int value = ClaudeIntervals.Parse(text.Text, fallback, clamp);
            text.Text = clamp(value + step).ToString(System.Globalization.CultureInfo.InvariantCulture);
            UpdateClaudePlanNote();
        }
        private void UpdateClaudePlanNote()
        {
            if (!SettingsOpen) return;
            int plan = ClaudeIntervals.Parse(Control<TextBox>("ClaudePlanSecondsBox").Text, settings.ClaudePlanSeconds, ClaudeIntervals.Plan);
            Control<TextBlock>("ClaudePlanSecondsNote").Visibility = plan < ClaudeIntervals.PlanWarnBelow ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void ConnectClaude()
        {
            bool update = claude.Connected;
            var managed = claude.Connector.ManagedOverrides();
            bool yes = await confirm.AskAsync("CLAUDE CODE", update ? "Update the connection?" : "Connect Claude Code?",
                "This adds Claude Dashboard's hooks, status line and telemetry settings to your Claude Code settings (settings.json). Telemetry goes only to Claude Dashboard on this PC and never includes your prompts. A backup is saved next to it. Your current status line keeps showing."
                + (managed.Count == 0 ? "" : " Your organisation's managed settings override Claude Dashboard's " + String.Join(", ", managed) + ", so " + (managed.Count == 1 ? "that part" : "those parts") + " won't work until they're allowed."), update ? "Update" : "Connect");
            if (!yes || disposed) return;
            try
            {
                claude.Connect(BundledHook);
                claude.CheckConnected();
                claude.ReadSessions(DateTime.UtcNow, true);
                if (claudeScan == null || claudeScan.IsCompleted) ScanClaudeLogs();
                Text("SettingsStatus", "Claude Code is connected. Sessions already open pick up telemetry when they restart.");
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace WidgetScreen
{
    public sealed partial class DashboardController : IDisposable
    {
        private readonly Window window;
        private readonly string settingsPath;
        private readonly bool preview;
        private AppSettings settings;
        // Counts settings saves, so the phone's layout version only has to be recomputed when something was saved.
        private int settingsVersion;
        private readonly ConfirmSheet confirm;
        private readonly DispatcherTimer clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private readonly FocusKeeper focusKeeper;
        public LayoutEditor Editor { get; private set; }
        private bool syncingEditor;
        private bool openingSettings;
        private bool disposed, fullscreen;
        private int ticks, settingsGeneration, statusClearsAt;
        private string pendingMessage;

        private sealed class MonitorOption
        {
            public string Id, Label;
            public override string ToString() { return Label; }
        }

        public DashboardController(Window window, string settingsPath, bool preview)
        {
            this.window = window; this.settingsPath = settingsPath; this.preview = preview;
            string warning; settings = SettingsStore.Load(settingsPath, out warning); pendingMessage = warning;
            Editor = new LayoutEditor(window);
            confirm = new ConfirmSheet(window);
            InitializeStickers();
            InitializeClaude();
            Editor.Changed += UpdateEditor;
            focusKeeper = new FocusKeeper(window);
            BindActions();
            InitializeRemote();
            InitializePages();
            FitScreen(); ApplySettings();
            clockTimer.Tick += Tick;
            window.Loaded += Loaded;
            window.PreviewKeyDown += KeyDown;
            window.SizeChanged += (sender, e) => FitScreen();
            SystemEvents.DisplaySettingsChanged += DisplaysChanged;
        }

        private T Control<T>(string name) where T : class
        {
            var control = window.FindName(name) as T;
            if (control == null) throw new InvalidOperationException("Missing UI element: " + name);
            return control;
        }
        private void Text(string name, string value) { Control<TextBlock>(name).Text = value; }
        private void Button(string name, Action action) { Control<Button>(name).Click += (sender, e) => action(); }
        // Status messages are transient: they clear after about ten seconds on the existing heartbeat. A sticky message stays until replaced.
        private void Notify(string text, bool sticky = false) { Text("StatusMessage", text); statusClearsAt = sticky ? int.MaxValue : ticks + 10; }

        private void BindActions()
        {
            Button("CloseButton", () => window.Close());
            Button("SettingsButton", OpenSettings);
            Button("HintDismiss", () => { settings.HintDismissed = true; Persist(); UpdateHint(); });
            Button("EditLayoutButton", BeginLayoutEdit);
            Button("OpenLayoutEditor", () => { CloseSettings(); BeginLayoutEdit(); });
            Button("UndoLayoutButton", () => Editor.Undo());
            Button("RedoLayoutButton", () => Editor.Redo());
            Button("ResetLayoutButton", () => Editor.Reset());
            Button("ArrangeLayoutButton", () => { if (!Editor.Arrange()) Notify("The widgets are already arranged."); });
            Button("CancelLayoutButton", CancelLayoutEdit);
            Button("SaveLayoutButton", SaveLayoutEdit);
            InitializeEditPanel();
            Control<CheckBox>("SnapLayoutChoice").Click += (sender, e) => Editor.Snap = Control<CheckBox>("SnapLayoutChoice").IsChecked == true;
            Button("CancelSettings", CloseSettings);
            Button("CancelSettingsFooter", CloseSettings);
            Button("SaveSettings", SaveSettings);
            Button("FullscreenButton", ToggleFullscreen);
            Button("QuietButton", () => {
                settings.QuietMode = !settings.QuietMode; ApplySettings(); Persist();
                Notify(settings.QuietMode ? "Quiet mode on." : "Quiet mode off.");
            });
            foreach (string name in new[] { "ScaleChoice", "CornersChoice" }) TouchSlider.Attach(Control<Slider>(name));
            Control<Slider>("ScaleChoice").ValueChanged += (sender, e) => Text("ScaleLabel", Math.Round(e.NewValue) + "%");
            Control<ComboBox>("ThemeChoice").SelectionChanged += (sender, e) => PreviewAppearance();
            Control<ComboBox>("AccentChoice").SelectionChanged += (sender, e) => PreviewAppearance();
            Control<ComboBox>("InkModeChoice").SelectionChanged += (sender, e) => PreviewAppearance();
            Control<Slider>("CornersChoice").ValueChanged += (sender, e) => PreviewAppearance();
            Control<Slider>("ScaleChoice").ValueChanged += (sender, e) => PreviewAppearance();
        }

        private void Loaded(object sender, RoutedEventArgs e)
        {
            if (!preview && settings.StartFullscreen && DisplayService.Fullscreen(window, settings.Monitor)) fullscreen = true;
            else DisplayService.Windowed(window);
            FitScreen(); UpdateDisplayStatus();
            UpdateClock();
            clockTimer.Start();
            if (!Program.Rendering) ApplyRemote();
            if (!String.IsNullOrEmpty(pendingMessage)) Notify(pendingMessage, true);
        }

        private void Tick(object sender, EventArgs e)
        {
            ticks++; UpdateClock();
            if (pairing.Code != null && Control<Grid>("SettingsOverlay").Visibility == Visibility.Visible) UpdateRemoteSettings();
            if (ticks >= statusClearsAt && !Editor.Editing) { Text("StatusMessage", ""); statusClearsAt = int.MaxValue; }
            UpdateTray();
            TickClaudeOnline();
            // Minimized or in the tray: the dashboard isn't visible, so it doesn't poll (a phone still gets answers).
            if (!Showing) return;
            TickClaude();
        }

        private void UpdateClock()
        {
            DateTime now = DateTime.Now;
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            Text("ClockTime", now.ToString(settings.Use24HourClock ? "HH:mm" : "h:mm", culture));
            Text("ClockPeriod", now.ToString("tt", culture));
            Control<TextBlock>("ClockPeriod").Visibility = settings.Use24HourClock ? Visibility.Collapsed : Visibility.Visible;
            Text("ClockDay", now.ToString("dddd", culture).ToUpperInvariant());
            Text("ClockDate", now.ToString("MMMM d", culture));
        }
        private static void Chime() { try { System.Media.SystemSounds.Asterisk.Play(); } catch (Exception) { } }

        private void ApplySettings()
        {
            ApplyAppearance(settings);
            ApplyLayout(settings);
            focusKeeper.Enabled = settings.KeepGameFocus && !Program.Rendering;
            // Quiet mode is a toggle: when on it takes the selected look.
            var quiet = Control<Button>("QuietButton");
            quiet.SetResourceReference(System.Windows.Controls.Button.BorderBrushProperty, settings.QuietMode ? "Accent" : "ControlBorder");
            quiet.SetResourceReference(System.Windows.Controls.Button.BackgroundProperty, settings.QuietMode ? "Selected" : "Raised");
            quiet.BorderThickness = new Thickness(settings.QuietMode ? 2 : 1);
            AutomationProperties.SetName(quiet, settings.QuietMode ? "Quiet mode, on" : "Quiet mode, off");
            UpdateClock(); claudeCard.Configure(settings.Use24HourClock, ClaudePeriodChosen, settings.ClaudeShowApiValue, settings.ClaudeFacts); claude.PlanEvery = TimeSpan.FromSeconds(settings.ClaudePlanSeconds);
        }

        private void ApplyAppearance(AppSettings appearance)
        {
            DashboardAppearance.Apply(window, appearance);
            string selectedAccent = appearance.Accent;
            string color = selectedAccent == "Glacier" ? "#8ED8F8" : selectedAccent == "Lavender" ? "#C5AEFF" : selectedAccent == "Amber" ? "#F5C47D" : "#BAF28B";
            var accent = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); accent.Freeze(); window.Resources["Accent"] = accent;
            window.Resources["SmallSize"] = 15.0 * appearance.TextScale;
            window.Resources["BodySize"] = 18.0 * appearance.TextScale;
            window.Resources["TitleSize"] = 24.0 * appearance.TextScale;
            window.Resources["HeaderClockSize"] = 52.0 * Math.Min(1.06, appearance.TextScale);
            stripScale = appearance.TextScale;
            ApplyInks(editInks ?? appearance.WidgetInks, appearance.WidgetColours, selectedAccent);
        }

        private void PreviewAppearance()
        {
            if (openingSettings || Control<Grid>("SettingsOverlay").Visibility != Visibility.Visible) return;
            var appearance = new AppSettings { Theme = (string)Control<ComboBox>("ThemeChoice").SelectedItem, Accent = (string)Control<ComboBox>("AccentChoice").SelectedItem,
                CornerRadius = Control<Slider>("CornersChoice").Value, TextScale = Control<Slider>("ScaleChoice").Value / 100,
                WidgetInks = settings.WidgetInks, WidgetColours = ChosenInkMode() };
            ApplyAppearance(appearance);
            Editor.Refresh();
            Text("CornersLabel", appearance.CornerRadius == 0 ? "Square" : Math.Round(appearance.CornerRadius) + " px");
        }

        private void ApplyLayout(AppSettings layout)
        {
            Editor.Apply(layout);
        }

        private void BeginLayoutEdit()
        {
            Editor.Begin();
            editInks = settings.WidgetInks;
            Control<Button>("SaveLayoutButton").Focus();
        }
        private void CancelLayoutEdit()
        {
            CloseEditPanel(); Editor.Cancel(); UpdateDisplayStatus();
            editInks = null; ApplyInks(settings.WidgetInks, settings.WidgetColours, settings.Accent);
            Notify("Layout changes discarded."); Control<Button>("EditLayoutButton").Focus();
        }
        private void SaveLayoutEdit()
        {
            // The layout is saved for this canvas size of the page shown; other screen shapes keep their own.
            var previous = WidgetLayout.Pages(settings).Select(p => p.Copy()).ToList(); bool previousSnap = settings.SnapWidgets; string previousInks = settings.WidgetInks;
            WidgetLayout.Store(settings, Editor.CanvasSize, Editor.Export()); settings.SnapWidgets = Editor.Snap;
            // Inks belong to the widget, not to a page or screen shape (decision 021).
            if (editInks != null) settings.WidgetInks = Inks.Clean(editInks);
            try { SaveSettingsFile(settings); }
            catch (Exception e)
            {
                WidgetLayout.SetPages(settings, previous); settings.SnapWidgets = previousSnap; settings.WidgetInks = previousInks;
                Notify("Could not save layout: " + e.Message + ". Your changes are still in the editor."); return;
            }
            editInks = null;
            CloseEditPanel(); Editor.Accept(); ApplySettings(); UpdateDisplayStatus();
            Notify("Layout saved."); Control<Button>("EditLayoutButton").Focus();
        }
        private void UpdateEditor()
        {
            if (syncingEditor) return;
            syncingEditor = true;
            try
            {
                bool editing = Editor.Editing;
                Control<Grid>("EditToolbar").Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
                Control<StackPanel>("MainActions").Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
                if (editing)
                {
                    Control<Button>("UndoLayoutButton").IsEnabled = Editor.CanUndo && !Editor.GestureActive;
                    Control<Button>("RedoLayoutButton").IsEnabled = Editor.CanRedo && !Editor.GestureActive;
                    Control<Button>("ArrangeLayoutButton").IsEnabled = !Editor.GestureActive;
                    Control<CheckBox>("SnapLayoutChoice").IsChecked = Editor.Snap;
                    UpdateEditPanel();
                    var bounds = Editor.Bounds(Editor.SelectedId);
                    string info = WidgetLayout.Title(Editor.SelectedId) + " · " + Math.Round(bounds.Width) + " × " + Math.Round(bounds.Height) + " · X " + Math.Round(bounds.X) + ", Y " + Math.Round(bounds.Y);
                    if (Editor.SelectedId == "System" && !Editor.IsVisible("System") && Editor.IsEnabled("System")) info += " · Enable metrics in Settings to show system stats";
                    else if (!Editor.IsEnabled(Editor.SelectedId)) info += " · Hidden · Turn on Show widget to bring it back";
                    if (Editor.HasOverlaps()) info += " · Widgets overlap";
                    Notify(info, true);
                    Text("DisplayStatus", "ARROWS MOVE · SHIFT+ARROWS RESIZE · ESC CANCELS");
                }
                // Card contents follow their card's size (CardSizing, DashboardController.Cards.cs).
                FitCards();
                UpdatePages(); UpdateClaudePill();
            }
            finally { syncingEditor = false; }
        }

        // Opens Settings on a tab by its header, so tabs can be added without renumbering.
        private void OpenSettingsTab(string header) { OpenSettings(); SelectSettingsTab(window, header); }
        public static void SelectSettingsTab(Window window, string header)
        {
            var tabs = (TabControl)window.FindName("SettingsTabs");
            var tab = tabs.Items.OfType<TabItem>().FirstOrDefault(t => (string)t.Header == header);
            if (tab != null) tabs.SelectedItem = tab;
        }
        // An unhandled error on the UI thread (Program.Main): the dashboard stays up and says where the details are.
        public void ReportError(Exception error)
        {
            if (disposed) return;
            try { Notify("Something went wrong: " + error.GetBaseException().Message + " Details are in error.log next to settings.xml.", true); } catch (Exception) { }
        }

        private void OpenSettings()
        {
            openingSettings = true;
            settingsGeneration++;
            var monitors = DisplayService.Screens.Where(s => !s.Primary).Select(s => new MonitorOption { Id = s.DeviceName, Label = s.DeviceName + "  ·  " + s.Bounds.Width + " × " + s.Bounds.Height }).ToList();
            if (monitors.Count == 0) monitors.Add(new MonitorOption { Id = "", Label = "No second display connected" });
            var monitorChoice = Control<ComboBox>("MonitorChoice"); monitorChoice.ItemsSource = monitors; monitorChoice.SelectedItem = monitors.FirstOrDefault(m => m.Id == settings.Monitor) ?? monitors[0];
            var accent = Control<ComboBox>("AccentChoice"); accent.ItemsSource = new[] { "Lime", "Glacier", "Lavender", "Amber" }; accent.SelectedItem = settings.Accent;
            var theme = Control<ComboBox>("ThemeChoice"); theme.ItemsSource = new[] { "Midnight", "Slate", "Warm" }; theme.SelectedItem = settings.Theme;
            var inkMode = Control<ComboBox>("InkModeChoice"); inkMode.ItemsSource = InkModeNames; inkMode.SelectedIndex = settings.WidgetColours == Inks.AccentOnly ? 1 : 0;
            Control<Slider>("CornersChoice").Value = settings.CornerRadius;
            Control<CheckBox>("StartFullscreenChoice").IsChecked = settings.StartFullscreen;
            Control<CheckBox>("KeepFocusChoice").IsChecked = settings.KeepGameFocus;
            Control<Slider>("ScaleChoice").Value = settings.TextScale * 100;
            Control<CheckBox>("ClockChoice").IsChecked = settings.Use24HourClock;
            Text("SettingsStatus", ""); UpdateRemoteSettings(); UpdateClaudeSettings();
            Control<Grid>("DashboardContent").IsEnabled = false; Control<Grid>("SettingsOverlay").Visibility = Visibility.Visible;
            Control<TabControl>("SettingsTabs").SelectedIndex = 0;
            openingSettings = false; PreviewAppearance();
            ((TabItem)Control<TabControl>("SettingsTabs").Items[0]).Focus();
        }
        private void CloseSettings() { settingsGeneration++; Control<Grid>("SettingsOverlay").Visibility = Visibility.Collapsed; ApplySettings(); Control<Grid>("DashboardContent").IsEnabled = true; Control<Button>("SettingsButton").Focus(); }

        private void SaveSettings()
        {
            var monitor = Control<ComboBox>("MonitorChoice").SelectedItem as MonitorOption;
            // Only the fields the panel shows change; pages, the phone link and everything else come along unchanged.
            var candidate = settings.Copy();
            candidate.Theme = (string)Control<ComboBox>("ThemeChoice").SelectedItem; candidate.CornerRadius = Control<Slider>("CornersChoice").Value;
            candidate.StartFullscreen = Control<CheckBox>("StartFullscreenChoice").IsChecked == true;
            candidate.KeepGameFocus = Control<CheckBox>("KeepFocusChoice").IsChecked == true;
            candidate.Accent = (string)Control<ComboBox>("AccentChoice").SelectedItem; candidate.TextScale = Control<Slider>("ScaleChoice").Value / 100;
            candidate.WidgetColours = ChosenInkMode();
            candidate.Monitor = monitor == null ? "" : monitor.Id;
            candidate.Use24HourClock = Control<CheckBox>("ClockChoice").IsChecked == true;
            SaveClaudeChoices(candidate);
            try { SaveSettingsFile(candidate); }
            catch (Exception e) { Text("SettingsStatus", "Could not save settings: " + e.Message); return; }
            settings = candidate; CloseSettings();
            if (fullscreen) { if (!DisplayService.Fullscreen(window, settings.Monitor)) { DisplayService.Windowed(window); fullscreen = false; } UpdateDisplayStatus(); }
            FitScreen();
            Notify("Settings saved.");
        }

        private void Persist() { try { SaveSettingsFile(settings); } catch (Exception e) { Notify("Settings could not be saved: " + e.Message); } }
        // Every settings save goes through here, so the phone's layout version can tell that something changed.
        private void SaveSettingsFile(AppSettings which) { SettingsStore.Save(settingsPath, which); settingsVersion++; }
        private void ToggleFullscreen()
        {
            if (fullscreen) { DisplayService.Windowed(window); fullscreen = false; }
            else if (DisplayService.Fullscreen(window, settings.Monitor)) fullscreen = true;
            else Notify("Fullscreen needs a second display. Connect one, then try again.");
            FitScreen(); UpdateDisplayStatus();
        }
        private void UpdateDisplayStatus()
        {
            if (!Editor.Editing) Text("DisplayStatus", "");
            Control<Button>("FullscreenButton").Content = fullscreen ? "Window preview" : "Second screen";
        }
        private void KeyDown(object sender, KeyEventArgs e)
        {
            if (Editor.Editing)
            {
                if (e.Key == Key.Escape) { if (editPanelOpen) CloseEditPanel(); else CancelLayoutEdit(); e.Handled = true; return; }
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && (e.Key == Key.Z || e.Key == Key.Y))
                {
                    if (e.Key == Key.Y || (Keyboard.Modifiers & ModifierKeys.Shift) != 0) Editor.Redo(); else Editor.Undo();
                    e.Handled = true; return;
                }
                if (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down)
                {
                    // Keep dropdown navigation intact; arrows on a widget move it, Shift+arrows resize it.
                    if (Keyboard.FocusedElement is ComboBox || Keyboard.FocusedElement is TextBox) return;
                    double step = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? 1 : 8;
                    Editor.TransformSelected((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? "SE" : "", e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0, e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0, false);
                    e.Handled = true; return;
                }
            }
            if (e.Key == Key.Escape)
            {
                if (confirm.IsOpen) confirm.Cancel();
                else if (Control<Grid>("SettingsOverlay").Visibility == Visibility.Visible) CloseSettings(); else if (fullscreen) ToggleFullscreen(); e.Handled = true;
            }
            else if (e.Key == Key.F11) { ToggleFullscreen(); e.Handled = true; }
        }
        private void DisplaysChanged(object sender, EventArgs e)
        {
            if (disposed) return;
            window.Dispatcher.BeginInvoke(new Action(() => {
                if (disposed) return;
                if (fullscreen && !DisplayService.Fullscreen(window, settings.Monitor)) { DisplayService.Windowed(window); fullscreen = false; Notify("Your second display was disconnected, so the dashboard moved to a window.", true); }
                FitScreen(); UpdateDisplayStatus();
            }));
        }
        public void Dispose()
        {
            disposed = true; DisposeRemote(); tray.Dispose(); clockTimer.Stop(); SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
            claude.Dispose(); if (claudeToast != null) claudeToast.Dispose();
        }
    }
}

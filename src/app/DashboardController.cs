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

namespace WidgetScreen
{
    public sealed partial class DashboardController : IDisposable
    {
        private readonly Window window;
        private readonly string settingsPath;
        private AppSettings settings;
        private readonly ConfirmSheet confirm;
        private readonly DispatcherTimer clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private bool openingSettings;
        private bool disposed;
        private int ticks, statusClearsAt;
        private string pendingMessage;
        // The card size the Claude card was last fitted to.
        private Size fittedCard;

        // One ordinary window (decision 026): the Claude card fills it under the header, and it resizes and closes
        // like any other app.
        public DashboardController(Window window, string settingsPath)
        {
            this.window = window; this.settingsPath = settingsPath;
            string warning; settings = SettingsStore.Load(settingsPath, out warning); pendingMessage = warning;
            confirm = new ConfirmSheet(window);
            InitializeClaude();
            BindActions();
            ApplySettings();
            Control<Border>("ClaudeCard").SizeChanged += (sender, e) => FitCard();
            clockTimer.Tick += Tick;
            window.Loaded += Loaded;
            window.PreviewKeyDown += KeyDown;
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
            Button("CancelSettings", CloseSettings);
            Button("CancelSettingsFooter", CloseSettings);
            Button("SaveSettings", SaveSettings);
            Button("QuietButton", () => {
                settings.QuietMode = !settings.QuietMode; ApplySettings(); Persist();
                Notify(settings.QuietMode ? "Quiet mode on." : "Quiet mode off.");
            });
            Control<Slider>("ScaleChoice").ValueChanged += (sender, e) => Text("ScaleLabel", Math.Round(e.NewValue) + "%");
            Control<ComboBox>("ThemeChoice").SelectionChanged += (sender, e) => PreviewAppearance();
            Control<ComboBox>("AccentChoice").SelectionChanged += (sender, e) => PreviewAppearance();
            Control<Slider>("CornersChoice").ValueChanged += (sender, e) => PreviewAppearance();
            Control<Slider>("ScaleChoice").ValueChanged += (sender, e) => PreviewAppearance();
        }

        private void Loaded(object sender, RoutedEventArgs e)
        {
            UpdateClock();
            clockTimer.Start();
            if (!String.IsNullOrEmpty(pendingMessage)) Notify(pendingMessage, true);
        }

        private void Tick(object sender, EventArgs e)
        {
            ticks++; UpdateClock();
            if (ticks >= statusClearsAt) { Text("StatusMessage", ""); statusClearsAt = int.MaxValue; }
            TickClaudeOnline();
            // Minimised, the card isn't visible, so it doesn't poll.
            if (window.WindowState == WindowState.Minimized) return;
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
            // Quiet mode is a toggle: when on it takes the selected look.
            var quiet = Control<Button>("QuietButton");
            quiet.SetResourceReference(System.Windows.Controls.Button.BorderBrushProperty, settings.QuietMode ? "Accent" : "ControlBorder");
            quiet.SetResourceReference(System.Windows.Controls.Button.BackgroundProperty, settings.QuietMode ? "Selected" : "Raised");
            quiet.BorderThickness = new Thickness(settings.QuietMode ? 2 : 1);
            AutomationProperties.SetName(quiet, settings.QuietMode ? "Quiet mode, on" : "Quiet mode, off");
            UpdateClock(); FitCard(true); claudeCard.Configure(settings.Use24HourClock, ClaudePeriodChosen, settings.ClaudeShowApiValue, settings.ClaudeFacts); claude.PlanEvery = TimeSpan.FromSeconds(settings.ClaudePlanSeconds);
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
            // The card's label strip wears Coral (review fix 9), with dark text on it.
            window.Resources["InkOfClaude"] = Frozen("#FF9E85"); window.Resources["InkText"] = Frozen("#11170E");
        }

        private void PreviewAppearance()
        {
            if (openingSettings || Control<Grid>("SettingsOverlay").Visibility != Visibility.Visible) return;
            var appearance = new AppSettings { Theme = (string)Control<ComboBox>("ThemeChoice").SelectedItem, Accent = (string)Control<ComboBox>("AccentChoice").SelectedItem,
                CornerRadius = Control<Slider>("CornersChoice").Value, TextScale = Control<Slider>("ScaleChoice").Value / 100 };
            ApplyAppearance(appearance);
            Text("CornersLabel", appearance.CornerRadius == 0 ? "Square" : Math.Round(appearance.CornerRadius) + " px");
        }

        private static SolidColorBrush Frozen(string hex) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); brush.Freeze(); return brush; }

        // The card's contents follow its size (CardSizing): worked out again when the window resizes or the text size
        // changes, never twice for the same size.
        private void FitCard(bool always = false)
        {
            var card = Control<Border>("ClaudeCard");
            var size = new Size(card.ActualWidth, card.ActualHeight);
            if (size.Width <= 0 || size.Height <= 0 || (!always && size == fittedCard)) return;
            fittedCard = size;
            claudeCard.Fit(new Rect(size), settings.TextScale);
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
            var accent = Control<ComboBox>("AccentChoice"); accent.ItemsSource = new[] { "Lime", "Glacier", "Lavender", "Amber" }; accent.SelectedItem = settings.Accent;
            var theme = Control<ComboBox>("ThemeChoice"); theme.ItemsSource = new[] { "Midnight", "Slate", "Warm" }; theme.SelectedItem = settings.Theme;
            Control<Slider>("CornersChoice").Value = settings.CornerRadius;
            Control<Slider>("ScaleChoice").Value = settings.TextScale * 100;
            Control<CheckBox>("ClockChoice").IsChecked = settings.Use24HourClock;
            Text("SettingsStatus", ""); UpdateClaudeSettings();
            Control<Grid>("DashboardContent").IsEnabled = false; Control<Grid>("SettingsOverlay").Visibility = Visibility.Visible;
            Control<TabControl>("SettingsTabs").SelectedIndex = 0;
            openingSettings = false; PreviewAppearance();
            ((TabItem)Control<TabControl>("SettingsTabs").Items[0]).Focus();
        }
        private void CloseSettings() { Control<Grid>("SettingsOverlay").Visibility = Visibility.Collapsed; ApplySettings(); Control<Grid>("DashboardContent").IsEnabled = true; Control<Button>("SettingsButton").Focus(); }

        private void SaveSettings()
        {
            // Only the fields the panel shows change; Quiet mode and the card's folds come along unchanged.
            var candidate = settings.Copy();
            candidate.Theme = (string)Control<ComboBox>("ThemeChoice").SelectedItem; candidate.CornerRadius = Control<Slider>("CornersChoice").Value;
            candidate.Accent = (string)Control<ComboBox>("AccentChoice").SelectedItem; candidate.TextScale = Control<Slider>("ScaleChoice").Value / 100;
            candidate.Use24HourClock = Control<CheckBox>("ClockChoice").IsChecked == true;
            SaveClaudeChoices(candidate);
            try { SaveSettingsFile(candidate); }
            catch (Exception e) { Text("SettingsStatus", "Could not save settings: " + e.Message); return; }
            settings = candidate; CloseSettings();
            Notify("Settings saved.");
        }

        private void Persist() { try { SaveSettingsFile(settings); } catch (Exception e) { Notify("Settings could not be saved: " + e.Message); } }
        private void SaveSettingsFile(AppSettings which) { SettingsStore.Save(settingsPath, which); }
        private void KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            if (confirm.IsOpen) confirm.Cancel();
            else if (Control<Grid>("SettingsOverlay").Visibility == Visibility.Visible) CloseSettings();
            else return;
            e.Handled = true;
        }
        public void Dispose()
        {
            disposed = true; clockTimer.Stop();
            claude.Dispose(); if (claudeToast != null) claudeToast.Dispose();
        }
    }
}

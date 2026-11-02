using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WidgetScreen
{
    // Claude Dashboard's controller (phase 4 of the transformation plan): one ordinary window that is the pit wall, a
    // Settings window beside it, and one 1-second heartbeat. It owns the settings transaction (Settings previews the
    // palette and text size live; Save keeps them, Cancel puts the saved ones back), the palette, the header, the
    // window's place and size, and Start with Windows. The Claude wiring lives in AppController.Claude.cs.
    public sealed partial class AppController : IDisposable
    {
        private readonly Window window;
        private readonly string settingsPath;
        private AppSettings settings;
        private readonly DispatcherTimer heartbeat = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private readonly ConfirmSheet windowConfirm;
        private Window settingsWindow;
        private ConfirmSheet settingsConfirm;
        private bool openingSettings, savingSettings, closing, disposed;
        private int ticks, statusClearsAt = int.MaxValue;
        private string pendingMessage, shownTitle;
        // The text size the card is laid out for: the saved one, or the one Settings previews.
        private double appearanceScale = 1;
        private static readonly ClaudePeriod[] HeaderPeriods = { ClaudePeriod.FiveHour, ClaudePeriod.Today, ClaudePeriod.Week };

        public AppSettings CurrentSettings { get { return settings; } }
        public Window MainWindow { get { return window; } }
        public Window SettingsWindow { get { return settingsWindow; } }
        private bool SettingsOpen { get { return settingsWindow != null; } }
        // The sheet of the window in front: Settings' while it's open, so Connect asks there.
        private ConfirmSheet confirm { get { return settingsConfirm ?? windowConfirm; } }

        // Loads one of the app's windows from its XAML with the shared styles (ShellStyles.xaml) as its resources, so
        // implicit styles apply as the window is built. The palette then goes over them (Palettes.Apply).
        public static Window LoadWindow(string name)
        {
            string xaml = Resource(name + ".xaml"), styles = Resource("ShellStyles.xaml");
            int start = styles.IndexOf("<ResourceDictionary", StringComparison.Ordinal);
            if (start < 0) throw new InvalidOperationException("ShellStyles.xaml has no ResourceDictionary.");
            xaml = xaml.Replace("<!--SHELL STYLES-->", "<Window.Resources>" + styles.Substring(start) + "</Window.Resources>");
            using (var reader = new StringReader(xaml))
            using (var xml = System.Xml.XmlReader.Create(reader))
                return (Window)XamlReader.Load(xml);
        }
        private static string Resource(string name)
        {
            using (var stream = typeof(AppController).Assembly.GetManifestResourceStream("WidgetScreen." + name))
            {
                if (stream == null) throw new InvalidOperationException("Missing resource " + name + ".");
                using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
            }
        }

        public AppController(Window window, string settingsPath)
        {
            this.window = window; this.settingsPath = settingsPath;
            string warning; settings = SettingsStore.Load(settingsPath, out warning); pendingMessage = warning;
            windowConfirm = new ConfirmSheet(window);
            InitializeClaude();
            Button("SettingsButton", OpenSettings);
            Button("QuietButton", ToggleQuiet);
            for (int i = 0; i < HeaderPeriods.Length; i++) { var period = HeaderPeriods[i]; Button("ShellPeriod" + i, () => claudeCard.ShowPeriod(period)); }
            // The card follows its own size, and the header folds its words away as the window narrows.
            Control<Border>("ClaudeCard").SizeChanged += (sender, e) => FitCard();
            Control<Grid>("Dashboard").SizeChanged += (sender, e) => FitHeader(e.NewSize.Width);
            window.PreviewKeyDown += KeyDown;
            window.Loaded += Loaded;
            window.Closing += (sender, e) => { closing = true; CloseSettingsWindow(); SavePlacement(); };
            heartbeat.Tick += Tick;
            ApplySettings();
            PlaceWindow();
        }

        // ----- Small helpers, the same as the widget app's -----

        private T Find<T>(string name) where T : class
        {
            var control = settingsWindow == null ? null : settingsWindow.FindName(name) as T;
            return control ?? window.FindName(name) as T;
        }
        private T Control<T>(string name) where T : class
        {
            var control = Find<T>(name);
            if (control == null) throw new InvalidOperationException("Missing UI element: " + name);
            return control;
        }
        // Settings' words may arrive after the window closed (a sign-in finishing): then there's nothing to fill.
        private void Text(string name, string value) { var block = Find<TextBlock>(name); if (block != null) block.Text = value; }
        private void Button(string name, Action action) { Control<Button>(name).Click += (sender, e) => action(); }
        private static void Chime() { try { System.Media.SystemSounds.Asterisk.Play(); } catch (Exception) { } }

        // The status line under the pit wall: transient words that clear after about ten seconds, or sticky ones.
        private void Notify(string text, bool sticky = false)
        {
            var line = Control<TextBlock>("StatusMessage");
            line.Text = text ?? ""; line.Visibility = String.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            statusClearsAt = sticky ? int.MaxValue : ticks + 10;
        }
        public string Status { get { return Control<TextBlock>("StatusMessage").Text; } }
        public void ReportError(Exception error)
        {
            if (disposed) return;
            try { Notify("Something went wrong: " + error.GetBaseException().Message + " Details are in error.log next to settings.xml.", true); } catch (Exception) { }
        }

        private void Loaded(object sender, RoutedEventArgs e)
        {
            FitCard();
            heartbeat.Start();
            if (!String.IsNullOrEmpty(pendingMessage)) Notify(pendingMessage, true);
        }

        private void Tick(object sender, EventArgs e)
        {
            ticks++;
            if (ticks >= statusClearsAt) { Notify(""); statusClearsAt = int.MaxValue; }
            TickClaudeOnline();
            TickClaude();
        }

        private void KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && windowConfirm.IsOpen) { windowConfirm.Cancel(); e.Handled = true; }
        }

        // ----- Appearance -----

        private void ApplySettings()
        {
            ApplyAppearance(settings.Theme, settings.Accent, settings.TextScale);
            var quiet = Control<Button>("QuietButton");
            quiet.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, settings.QuietMode ? "Accent" : "ControlBorder");
            quiet.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, settings.QuietMode ? "Selected" : "Surface");
            quiet.BorderThickness = new Thickness(settings.QuietMode ? 2 : 1);
            AutomationProperties.SetName(quiet, settings.QuietMode ? "Quiet mode, on" : "Quiet mode, off");
            claudeCard.Configure(settings.Use24HourClock, ClaudePeriodChosen, settings.ClaudeShowApiValue, settings.ClaudeFacts);
            claudeCard.Plain = settings.ClaudePlain;
            claude.PlanEvery = TimeSpan.FromSeconds(settings.ClaudePlanSeconds);
            UpdateClaudeLive();
            UpdateHeader();
        }

        // A palette, accent and text size on both windows; the card works out its tints again.
        private void ApplyAppearance(string theme, string accent, double textScale)
        {
            Palettes.Apply(window.Resources, theme, accent, textScale);
            if (settingsWindow != null) Palettes.Apply(settingsWindow.Resources, theme, accent, textScale);
            appearanceScale = textScale;
            claudeCard.Restyle();
            FitCard();
        }

        private void FitCard()
        {
            var host = Find<Border>("ClaudeCard");
            if (host == null || host.ActualWidth <= 0 || host.ActualHeight <= 0) return;
            claudeCard.Fit(new Rect(0, 0, host.ActualWidth, host.ActualHeight), appearanceScale);
        }

        // The header keeps the summary as long as it can: the app name goes first (the title bar names it), then the
        // flag, then the buttons' words (their glyphs and tooltips stay), then the period switch.
        private void FitHeader(double width)
        {
            Control<TextBlock>("AppName").Visibility = width >= 900 ? Visibility.Visible : Visibility.Collapsed;
            Control<TextBlock>("ClaudeFlag").Visibility = width >= 720 ? Visibility.Visible : Visibility.Collapsed;
            var words = width >= 620 ? Visibility.Visible : Visibility.Collapsed;
            Control<TextBlock>("QuietLabel").Visibility = words; Control<TextBlock>("SettingsLabel").Visibility = words;
            Control<StackPanel>("PeriodSwitch").Visibility = width >= 480 ? Visibility.Visible : Visibility.Collapsed;
        }

        // The header follows every fill of the card: the live summary, its dot, the flag, the chosen period, and the
        // window's title (the taskbar shows it while a session needs you).
        private void UpdateHeader()
        {
            var view = claudeCard.View;
            var summary = Control<TextBlock>("ClaudeSummary"); var dot = Control<Ellipse>("ClaudeSummaryDot"); var flag = Control<TextBlock>("ClaudeFlag");
            bool needs = view != null && view.NeedsYou > 0;
            summary.Text = view == null ? "Reading Claude Code…" : ClaudeText.Summary(view, claudeCard.Period, true);
            summary.SetResourceReference(TextBlock.ForegroundProperty, needs ? "Warning" : "Text");
            dot.SetResourceReference(Shape.FillProperty, needs ? "Warning" : view != null && view.Working > 0 ? "Accent" : "Muted");
            flag.Text = view == null ? "" : ClaudeText.Flag(view, DateTime.UtcNow, settings.Use24HourClock, true);
            for (int i = 0; i < HeaderPeriods.Length; i++)
            {
                var button = Control<Button>("ShellPeriod" + i);
                bool selected = HeaderPeriods[i] == claudeCard.Period;
                button.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, selected ? "Accent" : "ControlBorder");
                button.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, selected ? "Selected" : "Surface");
                button.BorderThickness = new Thickness(selected ? 2 : 1);
                AutomationProperties.SetName(button, "Threads and usage for " + (HeaderPeriods[i] == ClaudePeriod.FiveHour ? "the 5-hour window" : HeaderPeriods[i] == ClaudePeriod.Today ? "today" : "the week") + (selected ? ", selected" : ""));
            }
            string title = needs ? (view.NeedsYou == 1 ? "Claude needs you" : view.NeedsYou + " sessions need you") + " · Claude Dashboard" : "Claude Dashboard";
            if (title != shownTitle) { shownTitle = title; window.Title = title; }
        }

        private void ToggleQuiet()
        {
            settings.QuietMode = !settings.QuietMode;
            Persist();
            ApplySettings();
            if (SettingsOpen) { var choice = Find<CheckBox>("QuietChoice"); if (choice != null) choice.IsChecked = settings.QuietMode; }
        }

        private void Persist() { try { SaveSettingsFile(settings); } catch (Exception e) { Notify("Settings could not be saved: " + e.Message); } }
        private void SaveSettingsFile(AppSettings which) { SettingsStore.Save(settingsPath, which); }

        // ----- Settings -----

        public void OpenSettings()
        {
            if (settingsWindow != null) { settingsWindow.Activate(); return; }
            openingSettings = true;
            settingsWindow = LoadWindow("SettingsWindow");
            Palettes.Apply(settingsWindow.Resources, settings.Theme, settings.Accent, settings.TextScale);
            settingsConfirm = new ConfirmSheet(settingsWindow);
            var theme = Control<ComboBox>("ThemeChoice"); theme.ItemsSource = Palettes.Themes; theme.SelectedItem = Palettes.CleanTheme(settings.Theme);
            var accent = Control<ComboBox>("AccentChoice"); accent.ItemsSource = Palettes.Accents; accent.SelectedItem = Palettes.CleanAccent(settings.Accent);
            string previousTheme = (string)theme.SelectedItem;
            theme.SelectionChanged += (sender, e) => {
                // A palette brings its own accent along when the accent was the old palette's default (Lime on the dark
                // ones, terracotta on paper).
                string next = theme.SelectedItem as string;
                if (!openingSettings && next != null && accent.SelectedItem as string == Palettes.DefaultAccent(previousTheme)) accent.SelectedItem = Palettes.DefaultAccent(next);
                previousTheme = next ?? previousTheme;
                PreviewAppearance();
            };
            accent.SelectionChanged += (sender, e) => PreviewAppearance();
            var scale = Control<Slider>("ScaleChoice"); scale.Value = settings.TextScale * 100;
            scale.ValueChanged += (sender, e) => PreviewAppearance();
            Control<CheckBox>("ClockChoice").IsChecked = settings.Use24HourClock;
            Control<CheckBox>("QuietChoice").IsChecked = settings.QuietMode;
            Control<CheckBox>("StartWithWindowsChoice").IsChecked = StartupShortcut.Exists;
            Control<CheckBox>("StartMinimisedChoice").IsChecked = settings.StartMinimised;
            Control<CheckBox>("RememberWindowChoice").IsChecked = !settings.WindowCentred;
            Button("SaveSettings", SaveSettings);
            Button("CancelSettings", CancelSettings);
            settingsWindow.PreviewKeyDown += (sender, e) => { if (e.Key == Key.Escape && settingsConfirm != null && settingsConfirm.IsOpen) { settingsConfirm.Cancel(); e.Handled = true; } };
            var opened = settingsWindow;
            settingsWindow.Closing += (sender, e) => { if (opened == settingsWindow) CloseSettings(); };
            Text("SettingsStatus", "");
            BindClaudeSettings();
            UpdateClaudeSettings();
            openingSettings = false;
            PreviewAppearance();
            if (window.IsVisible) { settingsWindow.Owner = window; settingsWindow.Show(); }
        }

        // Opens a tab by its header ("Claude"), never by its position.
        public void SelectSettingsTab(string header)
        {
            if (settingsWindow == null || header == null) return;
            var tabs = Control<TabControl>("SettingsTabs");
            int index;
            if (Int32.TryParse(header, out index) && index >= 0 && index < tabs.Items.Count) { tabs.SelectedIndex = index; return; }
            var tab = tabs.Items.OfType<TabItem>().FirstOrDefault(t => String.Equals(t.Header as string, header, StringComparison.OrdinalIgnoreCase));
            if (tab != null) tabs.SelectedItem = tab;
        }

        private void PreviewAppearance()
        {
            if (openingSettings || settingsWindow == null) return;
            var scale = Control<Slider>("ScaleChoice").Value;
            Text("ScaleLabel", Math.Round(scale) + "%");
            ApplyAppearance(Control<ComboBox>("ThemeChoice").SelectedItem as string, Control<ComboBox>("AccentChoice").SelectedItem as string, scale / 100);
        }

        // Only the fields Settings shows change; everything else (the folded garages, the period, the window's place)
        // comes along unchanged.
        public void SaveSettings()
        {
            if (settingsWindow == null) return;
            var candidate = settings.Copy();
            candidate.Theme = Palettes.CleanTheme(Control<ComboBox>("ThemeChoice").SelectedItem as string);
            candidate.Accent = Palettes.CleanAccent(Control<ComboBox>("AccentChoice").SelectedItem as string);
            candidate.TextScale = Control<Slider>("ScaleChoice").Value / 100;
            candidate.Use24HourClock = Control<CheckBox>("ClockChoice").IsChecked == true;
            candidate.QuietMode = Control<CheckBox>("QuietChoice").IsChecked == true;
            candidate.StartMinimised = Control<CheckBox>("StartMinimisedChoice").IsChecked == true;
            candidate.WindowCentred = Control<CheckBox>("RememberWindowChoice").IsChecked != true;
            bool startup = Control<CheckBox>("StartWithWindowsChoice").IsChecked == true;
            SaveClaudeChoices(candidate);
            try { SaveSettingsFile(candidate); }
            catch (Exception e) { Text("SettingsStatus", "Could not save settings: " + e.Message); return; }
            settings = candidate;
            string startupProblem = startup == StartupShortcut.Exists ? null : StartupShortcut.Set(startup);
            savingSettings = true;
            try { CloseSettingsWindow(); } finally { savingSettings = false; }
            Notify(startupProblem ?? "Settings saved.");
        }

        // The Settings window closed (Save, Cancel, the close button or the main window closing): the saved settings
        // apply again, so a preview that wasn't saved goes away.
        private void CloseSettings()
        {
            if (settingsWindow == null) return;
            if (claude.Account.SigningIn && !savingSettings) claude.Account.CancelSignIn();
            settingsWindow = null; settingsConfirm = null; claudeProjectBoxes.Clear();
            ApplySettings();
            if (window.IsVisible && !closing) window.Activate();
        }
        // Cancel: the saved settings come back. The window is let go before it closes, so the same path works for a
        // window that was never shown (renders and checks), whose Close raises no Closing.
        public void CancelSettings() { CloseSettingsWindow(); }
        private void CloseSettingsWindow()
        {
            var open = settingsWindow;
            if (open == null) return;
            CloseSettings();
            open.Close();
        }

        // ----- The window's place -----

        // Where the window opens: where it was closed (if that's still on a screen), else the middle of the main screen
        // at its last size; maximised if it was. Start minimised still watches the sessions and notifies.
        private void PlaceWindow()
        {
            if (Program.Rendering) return;
            if (!settings.WindowCentred && settings.WindowWidth > 0 && settings.WindowHeight > 0)
            {
                var saved = new Rect(settings.WindowLeft, settings.WindowTop, Math.Max(window.MinWidth, settings.WindowWidth), Math.Max(window.MinHeight, settings.WindowHeight));
                window.Width = saved.Width; window.Height = saved.Height;
                if (WindowPlacement.Visible(saved, WindowPlacement.WorkAreas()))
                {
                    window.WindowStartupLocation = WindowStartupLocation.Manual;
                    window.Left = saved.Left; window.Top = saved.Top;
                }
                if (settings.WindowMaximised) window.WindowState = WindowState.Maximized;
            }
            if (settings.StartMinimised) window.WindowState = WindowState.Minimized;
        }

        private void SavePlacement()
        {
            if (Program.Rendering || disposed || !window.IsLoaded) return;
            var bounds = window.WindowState == WindowState.Normal ? new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight) : window.RestoreBounds;
            if (bounds.IsEmpty || Double.IsNaN(bounds.Left) || bounds.Width < 1) return;
            settings.WindowLeft = Math.Round(bounds.Left); settings.WindowTop = Math.Round(bounds.Top);
            settings.WindowWidth = Math.Round(bounds.Width); settings.WindowHeight = Math.Round(bounds.Height);
            if (window.WindowState != WindowState.Minimized) settings.WindowMaximised = window.WindowState == WindowState.Maximized;
            Persist();
        }

        // ----- Renders and checks -----

        // Lays a window's content out at a size in device-independent pixels, as a shown window would (a few passes,
        // because the card builds its design when it learns its size).
        public static void Lay(Window target, Size size)
        {
            var root = (FrameworkElement)target.Content;
            for (int i = 0; i < 4; i++) { root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout(); }
        }
        // Draws a window's content to a PNG at a size in pixels and a display scaling (1.5 for 150%).
        public static void Render(Window target, Size pixels, double scale, string path)
        {
            scale = Math.Max(1, Math.Min(3, scale));
            Lay(target, new Size(Math.Round(pixels.Width / scale), Math.Round(pixels.Height / scale)));
            var bitmap = new RenderTargetBitmap((int)pixels.Width, (int)pixels.Height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render((Visual)target.Content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
            using (var file = File.Create(path)) encoder.Save(file);
        }

        public void Dispose()
        {
            if (disposed) return;
            CloseSettingsWindow();
            disposed = true;
            heartbeat.Stop();
            claude.Dispose();
            if (claudeToast != null) claudeToast.Dispose();
        }
    }

    // Whether a saved window place is still on a screen: its title bar (the top 40 units, inset 40 at the sides) has
    // to meet some monitor's work area, so the window can be grabbed and moved.
    public static class WindowPlacement
    {
        public static bool Visible(Rect window, IEnumerable<Rect> workAreas)
        {
            if (window.IsEmpty || window.Width < 1 || window.Height < 1) return false;
            var grip = new Rect(window.Left + 40, window.Top, Math.Max(1, window.Width - 80), 40);
            return workAreas.Any(area => { var meet = Rect.Intersect(area, grip); return !meet.IsEmpty && meet.Width >= 40 && meet.Height >= 20; });
        }
        // Every monitor's work area in device-independent pixels at the system scaling.
        public static List<Rect> WorkAreas()
        {
            var areas = new List<Rect>();
            try
            {
                double scale;
                using (var graphics = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) scale = graphics.DpiX / 96.0;
                foreach (var screen in System.Windows.Forms.Screen.AllScreens)
                {
                    var a = screen.WorkingArea;
                    areas.Add(new Rect(a.Left / scale, a.Top / scale, a.Width / scale, a.Height / scale));
                }
            }
            catch (Exception) { areas.Add(SystemParameters.WorkArea); }
            if (areas.Count == 0) areas.Add(SystemParameters.WorkArea);
            return areas;
        }
    }

    // Settings → Window → Start Claude Dashboard when I sign in: the shortcut in the Startup folder, the same one Setup
    // makes, pointing at this copy of the app. The checks point Folder at a throwaway one.
    public static class StartupShortcut
    {
        public const string Name = "Claude Dashboard.lnk";
        private static string folder;
        public static string Folder
        {
            get { return folder ?? Environment.GetFolderPath(Environment.SpecialFolder.Startup); }
            set { folder = value; }
        }
        public static string Path { get { return System.IO.Path.Combine(Folder, Name); } }
        public static bool Exists { get { try { return File.Exists(Path); } catch (Exception) { return false; } } }

        // Adds or removes the shortcut. Returns null when it worked, or what went wrong, in words.
        public static string Set(bool on)
        {
            try
            {
                if (!on) { if (File.Exists(Path)) File.Delete(Path); return null; }
                Directory.CreateDirectory(Folder);
                string exe = System.Reflection.Assembly.GetEntryAssembly() == null ? System.Reflection.Assembly.GetExecutingAssembly().Location : System.Reflection.Assembly.GetEntryAssembly().Location;
                var type = Type.GetTypeFromProgID("WScript.Shell");
                if (type == null) return "Windows couldn't make the Startup shortcut.";
                object shell = Activator.CreateInstance(type);
                try
                {
                    object link = type.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { Path });
                    var linkType = link.GetType();
                    linkType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { exe });
                    linkType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { System.IO.Path.GetDirectoryName(exe) });
                    linkType.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { "Starts Claude Dashboard when you sign in" });
                    linkType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, link, null);
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
                }
                finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
                return null;
            }
            catch (Exception e) { return "Couldn't change the Startup shortcut: " + e.Message; }
        }
    }
}

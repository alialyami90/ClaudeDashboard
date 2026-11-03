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

namespace ClaudeDashboard
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
        private string pendingMessage, shownTitle, fullSummary = "", shortSummary = "";
        // How many sessions needed you at the last header update, so a new one is announced once (critique issue 4).
        private int announcedNeeds;
        // Shorter forms still, for the narrowest headers: the most urgent part with the count, then without it.
        private string[] shorterSummaries = new string[0];
        private double headerWidth, headerHeight;
        // The text size the card is laid out for: the saved one, or the one Settings previews.
        private double appearanceScale = 1;
        // The source-health line's height (it follows the text size) and what it shows now. It overlaps the lower 16
        // units of the card's bottom padding, so its own row is that much shorter.
        private double sourceHeight = 24;
        private const double SourceOverlap = 16;
        private List<ClaudeHealthItem> sources = new List<ClaudeHealthItem>();
        private static readonly ClaudePeriod[] HeaderPeriods = { ClaudePeriod.FiveHour, ClaudePeriod.Today, ClaudePeriod.Week };
        // The lap chart's views (decision 030): the tab chosen, the views made so far and what each was built for,
        // and whether the window is wide enough for tabs (below ClaudeViews.TabsFrom it is the Garage card alone).
        private string viewChosen = ClaudeViews.Garage;
        private readonly Dictionary<string, IClaudeView> views = new Dictionary<string, IClaudeView>();
        private readonly Dictionary<string, string> viewBuilt = new Dictionary<string, string>();
        private bool tabsShown, choosingTab;
        // Where the source-health line is from 640 wide: beside the tabs, on its own line under them, or (when neither has
        // room for it, at 640 and 140% text) at the card's foot as below 640. It is always the same control.
        private enum SourcePlace { Beside, Under, Foot }
        private SourcePlace sourcePlace = SourcePlace.Foot;

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
            using (var stream = typeof(AppController).Assembly.GetManifestResourceStream("ClaudeDashboard." + name))
            {
                if (stream == null) throw new InvalidOperationException("Missing resource " + name + ".");
                using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
            }
        }

        public AppController(Window window, string settingsPath)
        {
            this.window = window; this.settingsPath = settingsPath;
            string warning; settings = SettingsStore.Load(settingsPath, out warning); pendingMessage = warning ?? moveMessage; moveMessage = null;
            windowConfirm = new ConfirmSheet(window);
            InitializeClaude();
            Button("SettingsButton", OpenSettings);
            Button("QuietButton", ToggleQuiet);
            for (int i = 0; i < HeaderPeriods.Length; i++) { var period = HeaderPeriods[i]; Button("ShellPeriod" + i, () => claudeCard.ShowPeriod(period)); }
            // Live opens first once its package is in this build; until then Garage, the pit wall as before.
            viewChosen = View(ClaudeViews.Live) is PlaceholderView ? ClaudeViews.Garage : ClaudeViews.Live;
            var tabs = Control<TabControl>("ViewTabs");
            for (int i = 0; i < ClaudeViews.Names.Length; i++) AutomationProperties.SetAcceleratorKey(Control<TabItem>("ViewTab" + i), "Ctrl+" + (i + 1));
            tabs.SelectionChanged += (sender, e) => { if (choosingTab || e.OriginalSource != tabs || tabs.SelectedIndex < 0) return; ShowView(ClaudeViews.Names[tabs.SelectedIndex]); };
            Control<Border>("ViewHost").SizeChanged += (sender, e) => RefreshView();
            window.StateChanged += (sender, e) => ApplyView();
            // The card follows its own size, and the header folds its words away as the window narrows.
            Control<Border>("ClaudeCard").SizeChanged += (sender, e) => FitCard();
            Control<Grid>("Dashboard").SizeChanged += (sender, e) => { headerHeight = e.NewSize.Height; FitHeader(e.NewSize.Width); };
            window.PreviewKeyDown += KeyDown;
            window.Loaded += Loaded;
            window.Closing += (sender, e) => { closing = true; CloseSettingsWindow(); SavePlacement(); };
            heartbeat.Tick += Tick;
            ApplySettings();
            PlaceWindow();
            // The first run's question (OfferMove) showed the window already, so Loaded has been and gone.
            if (window.IsLoaded) Loaded(window, new RoutedEventArgs());
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
            // Ctrl+G opens the session that has waited longest (observability critique, issue 5; Ctrl+1 until v0.55.0).
            else if (e.Key == Key.G && Keyboard.Modifiers == ModifierKeys.Control) { OpenWaitingLongest(); e.Handled = true; }
            // Ctrl+1 to Ctrl+6 choose the views in tab order (decision 030).
            else if (Keyboard.Modifiers == ModifierKeys.Control && ViewKey(e.Key) >= 0) { ChooseViewByKey(ViewKey(e.Key)); e.Handled = true; }
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
            window.Topmost = settings.KeepOnTop;
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
            // The source-health line's words follow the text size, and its row grows with them.
            sourceHeight = Math.Ceiling(16 * textScale) + 8;
            SetSourceRow();
            foreach (var name in new[] { "SourceSample", "SourceText0", "SourceText1", "SourceText2", "SourceText3", "SourceGlyph0", "SourceGlyph1", "SourceGlyph2", "SourceGlyph3" })
                Control<TextBlock>(name).FontSize = Math.Round(12 * textScale, 1);
            claudeCard.Restyle();
            UpdateTaskbar();
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
            if (width > 0) headerWidth = width;
            width = headerWidth;
            if (width <= 0) return;
            Control<TextBlock>("AppName").Visibility = width >= 900 ? Visibility.Visible : Visibility.Collapsed;
            var words = width >= 620 ? Visibility.Visible : Visibility.Collapsed;
            Control<TextBlock>("QuietLabel").Visibility = words; Control<TextBlock>("SettingsLabel").Visibility = words;
            FitTabs(width);
            // The period switch is Garage's (decision 030): it shows only while the Garage card does.
            Control<StackPanel>("PeriodSwitch").Visibility = width >= 480 && GarageShown ? Visibility.Visible : Visibility.Collapsed;
            // The summary is never cut mid-word (Lead's review of package 4): in full beside the name, the flag and the
            // buttons when it fits there, else on a line of its own under them, in full or in its short form.
            var summary = Control<TextBlock>("ClaudeSummary"); var flag = Control<TextBlock>("ClaudeFlag");
            var line = Control<DockPanel>("SummaryLine"); var header = Control<Grid>("Header");
            double room = width - 6 - 24 - 20;
            double used = Desired(Control<TextBlock>("AppName")) + Desired(Control<StackPanel>("HeaderActions"));
            double flagWidth = flag.Text.Length == 0 ? 0 : Measure(flag, flag.Text) + 24;
            double full = 16 + Measure(summary, fullSummary), brief = 16 + Measure(summary, shortSummary);
            // In order: the whole summary on the header's line; under 1000 wide its short form there; then a line of
            // its own, but only where the card keeps its room (Small's 5-hour meter, Scribe's review of v0.47.2);
            // otherwise the shortest form that fits on the header's line (the most urgent part with the count, then
            // alone; Lead's review of v0.50.0 at 140% text), stopping at a whole word only if even that won't fit.
            bool oneLine; string shown;
            if (used + full + flagWidth <= room) { oneLine = true; shown = fullSummary; }
            else if (width < 1000 && used + brief + flagWidth <= room) { oneLine = true; shown = shortSummary; }
            else if (SecondLineFits(width)) { oneLine = false; shown = full <= room ? fullSummary : shortSummary; if (shown == shortSummary && brief > room) shown = Shortest(summary, room); }
            else
            {
                oneLine = true; shown = Shortest(summary, room - used - flagWidth);
                // Still too wide: the plan stamp steps aside before a word is cut (its age is on the source-health line).
                if (flagWidth > 0 && 16 + Measure(summary, shown) > room - used - flagWidth) { flagWidth = 0; shown = Shortest(summary, room - used); }
            }
            flag.Visibility = flagWidth > 0 || flag.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            summary.Text = shown;
            // On one line the summary leaves the flag its room at the right; on two, the flag keeps the first line.
            line.Margin = new Thickness(0, 0, oneLine ? flagWidth : 0, 0);
            Grid.SetRow(line, oneLine ? 0 : 1); Grid.SetColumn(line, oneLine ? 1 : 0); Grid.SetColumnSpan(line, oneLine ? 1 : 3);
            header.RowDefinitions[1].Height = new GridLength(oneLine ? 0 : 28);
            Control<Grid>("DashboardContent").RowDefinitions[0].Height = new GridLength(oneLine ? 56 : 84);
            HeaderTwoLines = !oneLine;
            FitSources(width);
        }
        // The card's room under a header of one or two lines, as the card counts it.
        private double CardRoom(double headerLines) { return headerHeight - 6 - (headerLines == 1 ? 56 : 84) - SourceRow; }
        // The first of the short form and the shorter ones that fits in the room, else the shortest.
        private string Shortest(TextBlock summary, double room)
        {
            var forms = new List<string> { shortSummary }; forms.AddRange(shorterSummaries);
            foreach (var form in forms) if (16 + Measure(summary, form) <= room) return form;
            return forms[forms.Count - 1];
        }
        // Whether a second header line (28 more units) leaves the card its room: Small keeps its 5-hour meter if it had
        // it, and nothing drops from Medium or Large to Small.
        private bool SecondLineFits(double width)
        {
            if (headerHeight <= 0) return true;
            double cardWidth = width - 6, one = CardRoom(1), two = CardRoom(2);
            var before = CardSizing.Claude(cardWidth, one); var after = CardSizing.Claude(cardWidth, two);
            if (before != CardSizing.Design.Small) return after != CardSizing.Design.Small;
            return !CardSizing.ClaudeSmallMeter(one - 48, appearanceScale) || CardSizing.ClaudeSmallMeter(two - 48, appearanceScale);
        }
        // Whether the summary is on its own line under the name and the buttons (for the checks).
        public bool HeaderTwoLines { get; private set; }
        // For the checks: the source-health line's height and the four sources it shows.
        public double SourceLineHeight { get { return sourceHeight; } }
        // The units under the header that the card doesn't get: the tab row from 640 wide (the source-health line is in
        // it), otherwise the source-health line's row less the padding it overlaps.
        public double SourceRow { get { return (tabsShown ? Control<Grid>("TabRow").ActualHeight : 0) + (SourcesAtFoot ? sourceHeight - SourceOverlap : 0); } }
        private bool SourcesAtFoot { get { return !tabsShown || sourcePlace == SourcePlace.Foot; } }
        private void SetSourceRow() { Control<Grid>("DashboardContent").RowDefinitions[3].Height = new GridLength(SourcesAtFoot ? sourceHeight - SourceOverlap : 0); }
        // Whether the source-health line is in the tab row, and on a line of its own under the tabs (for the checks).
        public bool SourcesInTabRow { get { return !SourcesAtFoot; } }
        public bool SourcesUnderTabs { get { return tabsShown && sourcePlace == SourcePlace.Under; } }
        public List<ClaudeHealthItem> Sources { get { return sources; } }

        // The source-health line (observability critique, issue 3): each source's glyph and colour by its level, its
        // reason in the tooltip and for screen readers. Sample is pinned at its start whenever the numbers are a fixture.
        private void UpdateSources(ClaudeView view)
        {
            sources = ClaudeHealth.Of(view, DateTime.UtcNow, settings.Use24HourClock, claudeSample ? null : claudeLinks);
            Control<TextBlock>("SourceSample").Visibility = view != null && view.Sample ? Visibility.Visible : Visibility.Collapsed;
            for (int i = 0; i < 4; i++)
            {
                var item = sources[i];
                var glyph = Control<TextBlock>("SourceGlyph" + i); var text = Control<TextBlock>("SourceText" + i); var part = Control<StackPanel>("Source" + i);
                glyph.Text = item.Glyph;
                string brush = item.Warns ? "Warning" : "Muted";
                glyph.SetResourceReference(TextBlock.ForegroundProperty, brush); text.SetResourceReference(TextBlock.ForegroundProperty, item.Warns ? "Warning" : "Muted");
                part.ToolTip = item.Long + "\n" + item.Tip;
                AutomationProperties.SetName(part, item.Spoken);
            }
        }
        // The words that fit: every reason in full; then the short reasons; then the sources that are fine keep only
        // their glyph (still named in the tooltip); then the sources in trouble only their name; then only the Amber ones
        // (old or failed) keep their name and the waiting and off ones their glyph (Scribe's review of v0.50.0: not
        // connected, counting and signed out at 360 wide and 140%). Never cut mid-word.
        private void FitSources(double width)
        {
            if (sources.Count < 4 || width <= 0) return;
            double room = width - 6 - 48;
            var sample = Control<TextBlock>("SourceSample");
            double fixedWidth = sample.Visibility == Visibility.Visible ? Measure(sample, sample.Text) + 16 : 0;
            int tiers = 5;
            Func<int, double> used = tier => { double sum = fixedWidth; for (int i = 0; i < 4; i++) sum += SourceWidth(i, SourceWords(sources[i], tier)) + (i < 3 ? 16 : 0); return sum; };
            // From 640 wide the line sits at the tab row's right end, beside the tabs. From 1000 wide it keeps every reason
            // in full (as it did under the card), on a line of its own under the tabs when they leave it too little room.
            // Narrower, it shortens beside the tabs; when even its shortest form has no room there (640 at 140% text) it
            // stays at the card's foot, which costs the card 8 units where a line under the tabs would cost a row (Medium
            // keeps its two project rows at 640 × 420 and 140% text, v0.53.1). The card keeps its 24-unit padding, which
            // its own layout counts on.
            if (tabsShown)
            {
                double beside = room - Desired(Control<TabControl>("ViewTabs")) - 16;
                var place = used(0) <= beside ? SourcePlace.Beside : width >= 1000 ? SourcePlace.Under : used(tiers - 1) <= beside ? SourcePlace.Beside : SourcePlace.Foot;
                if (place == SourcePlace.Beside) room = beside;
                if (place != sourcePlace) { sourcePlace = place; SetSourceRow(); }
                var holder = Control<Border>("TabSources");
                bool under = place == SourcePlace.Under;
                Grid.SetRow(holder, under ? 1 : 0); Grid.SetColumn(holder, under ? 0 : 1); Grid.SetColumnSpan(holder, under ? 2 : 1);
                holder.Margin = new Thickness(under ? 0 : 16, under ? 4 : 0, 0, 0);
            }
            PlaceSources();
            int chosen = tiers - 1;
            for (int tier = 0; tier < tiers; tier++) if (used(tier) <= room) { chosen = tier; break; }
            for (int i = 0; i < 4; i++)
            {
                string words = SourceWords(sources[i], chosen);
                var text = Control<TextBlock>("SourceText" + i);
                text.Text = words; text.Visibility = words.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                Control<TextBlock>("SourceGlyph" + i).Margin = new Thickness(0, 1, words.Length > 0 ? 5 : 0, 0);
            }
        }
        private static string SourceWords(ClaudeHealthItem item, int tier)
        {
            bool ok = item.Level == ClaudeHealthLevel.Ok;
            if (tier == 0) return item.Long;
            if (tier == 1) return item.Short;
            if (tier == 2) return ok ? "" : item.Short;
            if (tier == 3) return ok ? "" : item.Name;
            return item.Warns ? item.Name : "";
        }
        private double SourceWidth(int index, string words)
        {
            var glyph = Control<TextBlock>("SourceGlyph" + index);
            return Measure(glyph, glyph.Text) + (words.Length > 0 ? 5 + Measure(Control<TextBlock>("SourceText" + index), words) : 0);
        }
        private static double Desired(FrameworkElement element)
        {
            if (element.Visibility != Visibility.Visible) return 0;
            element.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            return element.DesiredSize.Width;
        }
        // A text's width in a TextBlock's font, without changing it.
        private static double Measure(TextBlock block, string text)
        {
            if (String.IsNullOrEmpty(text)) return 0;
            var probe = new TextBlock { Text = text, FontFamily = block.FontFamily, FontSize = block.FontSize, FontWeight = block.FontWeight, FontStyle = block.FontStyle, FontStretch = block.FontStretch };
            // The window draws text in Display mode, whose widths differ from a lone TextBlock's Ideal ones.
            TextOptions.SetTextFormattingMode(probe, TextOptions.GetTextFormattingMode(block));
            probe.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            return Math.Ceiling(probe.DesiredSize.Width) + 1;
        }

        // The header follows every fill of the card: the live summary, its dot, the flag, the chosen period, and the
        // window's title (the taskbar shows it while a session needs you).
        private void UpdateHeader()
        {
            var view = claudeCard.View;
            var summary = Control<TextBlock>("ClaudeSummary"); var dot = Control<Ellipse>("ClaudeSummaryDot"); var flag = Control<TextBlock>("ClaudeFlag");
            bool needs = view != null && view.NeedsYou > 0;
            // The states now, and the threads of Garage's chosen period while Garage is on screen (below 640 wide too,
            // where it's the only view; as before v0.55.0), or of the last 5 hours on every other view (decision 030).
            // Either way, when the 5-hour limit is reached at this pace ("≈ limit 4:12 pm") when it is before the reset.
            string limit = view == null ? "" : LimitText(view, DateTime.UtcNow, settings.Use24HourClock);
            var period = SummaryPeriod;
            fullSummary = view == null ? "Reading Claude Code…" : ClaudeText.Summary(view, period, true) + limit;
            shortSummary = view == null ? fullSummary : ClaudeText.ShortSummary(view, period) + limit;
            shorterSummaries = view == null ? new string[0] : new[] { ClaudeText.ShortSummary(view, period, 1, true) + limit, ClaudeText.ShortSummary(view, period, 1, true), ClaudeText.ShortSummary(view, period, 1, false) };
            summary.SetResourceReference(TextBlock.ForegroundProperty, needs ? "Warning" : "Text");
            dot.SetResourceReference(Shape.FillProperty, needs ? "Warning" : view != null && view.Working > 0 ? "Accent" : "Muted");
            flag.Text = view == null ? "" : ClaudeText.Flag(view, DateTime.UtcNow, settings.Use24HourClock, true);
            UpdateSources(view);
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
            FitHeader(0);
            Announce(summary, needs ? view.NeedsYou : 0);
            RefreshView();
        }
        // " · ≈ limit 4:12 pm": when the 5-hour window reaches 100% at the plan readings' pace (v0.54.0's LimitAtPace),
        // only when that is known (3 readings or more) and before the reset.
        public static string LimitText(ClaudeView view, DateTime now, bool use24)
        {
            var at = view == null ? null : view.LimitAtPace;
            return at == null || !at.BeforeReset ? "" : " · ≈ limit " + ClaudeText.Clock(at.LimitAt.Value, use24);
        }
        // The period the header counts threads over: Garage's while it's on screen, else the last 5 hours.
        public ClaudePeriod SummaryPeriod { get { return GarageShown ? claudeCard.Period : ClaudePeriod.FiveHour; } }
        // For the checks: the header summary's forms, longest first.
        public string[] SummaryForms { get { var forms = new List<string> { fullSummary, shortSummary }; forms.AddRange(shorterSummaries); return forms.ToArray(); } }

        // ----- The views (decision 030) -----

        // The view on screen: the chosen tab from 640 wide, else Garage.
        public string CurrentView { get { return tabsShown ? viewChosen : ClaudeViews.Garage; } }
        public string ChosenView { get { return viewChosen; } }
        public bool TabsShown { get { return tabsShown; } }
        private bool GarageShown { get { return CurrentView == ClaudeViews.Garage; } }
        // A tab's view, made the first time it's asked for (null for Garage, which is the card).
        public IClaudeView View(string name)
        {
            name = ClaudeViews.Clean(name);
            if (name == null || name == ClaudeViews.Garage) return null;
            IClaudeView view;
            if (!views.TryGetValue(name, out view)) { view = ClaudeViews.Create(name); views[name] = view; }
            return view;
        }
        // Chooses a view by name (the tabs, Ctrl+1 to Ctrl+6, --render-view, a view's own link). Unknown names do nothing.
        public void ShowView(string name)
        {
            name = ClaudeViews.Clean(name);
            if (name == null) return;
            viewChosen = name;
            ApplyView();
        }
        private static int ViewKey(Key key)
        {
            if (key >= Key.D1 && key <= Key.D6) return key - Key.D1;
            if (key >= Key.NumPad1 && key <= Key.NumPad6) return key - Key.NumPad1;
            return -1;
        }
        private void ChooseViewByKey(int index)
        {
            ShowView(ClaudeViews.Names[index]);
            if (!tabsShown && viewChosen != ClaudeViews.Garage) Notify(viewChosen + " shows when the window is at least " + ClaudeViews.TabsFrom + " wide.");
        }
        // Tabs from 640 wide; below, the window is the Garage card alone, as before v0.55.0.
        private void FitTabs(double width)
        {
            bool shown = width >= ClaudeViews.TabsFrom;
            if (shown == tabsShown) return;
            tabsShown = shown;
            Control<Grid>("TabRow").Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            SetSourceRow();
            ApplyView();
        }
        // The source-health line is one control: at the tab row's right end from 640 wide, under the card below that.
        private void PlaceSources()
        {
            var line = Control<StackPanel>("SourceLine"); var holder = Control<Border>("TabSources"); var content = Control<Grid>("DashboardContent");
            bool inRow = !SourcesAtFoot;
            if (inRow && line.Parent != holder) { content.Children.Remove(line); holder.Child = line; }
            else if (!inRow && line.Parent == holder) { holder.Child = null; content.Children.Add(line); Grid.SetRow(line, 3); }
            line.Margin = inRow ? new Thickness(0) : new Thickness(24, -16, 24, 4);
        }
        // Shows the current view: the card for Garage (the other views' card is hidden but keeps its size), or the view in
        // ViewHost. Only the view on screen is Visible, and none while the window is minimised.
        private void ApplyView()
        {
            var tabs = Find<TabControl>("ViewTabs");
            if (tabs == null || claudeCard == null) return;
            choosingTab = true;
            try { tabs.SelectedIndex = Array.IndexOf(ClaudeViews.Names, viewChosen); } finally { choosingTab = false; }
            bool garage = GarageShown, minimised = window.WindowState == WindowState.Minimized;
            Control<Border>("ClaudeCard").Visibility = garage ? Visibility.Visible : Visibility.Hidden;
            var host = Control<Border>("ViewHost");
            host.Visibility = garage ? Visibility.Collapsed : Visibility.Visible;
            if (!garage) View(CurrentView);
            foreach (var pair in views) pair.Value.Visible = !garage && !minimised && pair.Key == CurrentView;
            UpdateClaudeLive();
            UpdateHeader();
        }
        // Builds the view on screen when it's new or its room or text size changed, then fills it in from the snapshot the
        // card reads. Runs with every fill of the card (the heartbeat and the watchers), never while hidden.
        private void RefreshView()
        {
            if (GarageShown || claudeCard == null) return;
            var view = View(CurrentView);
            if (view == null || !view.Visible) return;
            var host = Control<Border>("ViewHost");
            double width = host.ActualWidth - host.Padding.Left - host.Padding.Right, height = host.ActualHeight - host.Padding.Top - host.Padding.Bottom;
            if (width <= 0 || height <= 0) return;
            string key = view.Name + "|" + Math.Round(width) + "x" + Math.Round(height) + "|" + appearanceScale + "|" + settings.Use24HourClock + "|" + claudeSample;
            string built;
            if (!viewBuilt.TryGetValue(view.Name, out built) || built != key || host.Tag as string != view.Name)
            {
                viewBuilt[view.Name] = key;
                host.Child = view.Build(ViewContext(), new Size(width, height));
                host.Tag = view.Name;
            }
            if (claudeCard.View != null) view.Refresh(claudeCard.View, DateTime.UtcNow);
        }
        private ClaudeViewContext ViewContext()
        {
            return new ClaudeViewContext { TextScale = appearanceScale, Use24Hour = settings.Use24HourClock, Sample = claudeSample, Focus = id => FocusClaudeSession(id), Show = ShowView, Act = Act,
                History = (now, count) => settings.ClaudeHistory && !claudeSample && claudeHistory != null ? claudeHistory.Days(now, count) : null };
        }

        // The header summary is an assertive live region: when a session starts needing you, a screen reader reads it
        // out at once. Fewer or the same number say nothing.
        private void Announce(TextBlock summary, int needs)
        {
            AutomationProperties.SetLiveSetting(summary, AutomationLiveSetting.Assertive);
            bool more = needs > announcedNeeds; announcedNeeds = needs;
            if (!more) return;
            var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(summary);
            if (peer != null) peer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }
        // For the checks: whether the header would announce now.
        public bool HeaderAnnounces { get { var summary = window.FindName("ClaudeSummary") as TextBlock; return summary != null && AutomationProperties.GetLiveSetting(summary) == AutomationLiveSetting.Assertive; } }
        public int AnnouncedNeeds { get { return announcedNeeds; } }

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
            Control<CheckBox>("KeepOnTopChoice").IsChecked = settings.KeepOnTop;
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
            candidate.KeepOnTop = Control<CheckBox>("KeepOnTopChoice").IsChecked == true;
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

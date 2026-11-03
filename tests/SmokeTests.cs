using System;
using System.IO;
using System.Linq;
using System.Threading;
using ClaudeDashboard;

internal static class SmokeTests
{
    private static int failures;
    private static void Check(string name, Action action)
    {
        try { action(); Console.WriteLine("PASS " + name); }
        catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e); }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    // Whether an element and every parent up to root are visible (IsVisible needs a shown window).
    private static bool Shown(System.Windows.DependencyObject element, System.Windows.DependencyObject root)
    {
        for (var e = element; e != null && e != root; e = System.Windows.Media.VisualTreeHelper.GetParent(e) ?? System.Windows.LogicalTreeHelper.GetParent(e))
            if (e is System.Windows.UIElement && ((System.Windows.UIElement)e).Visibility != System.Windows.Visibility.Visible) return false;
        return true;
    }
    private static System.Collections.Generic.IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject parent)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [STAThread]
    private static int Main(string[] args)
    {
        string directory = args.Length > 0 ? args[0] : AppDomain.CurrentDomain.BaseDirectory;
        // Claude Code's folder is a throwaway one for every check: the real settings.json, sign-in and logs are never touched.
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", Path.Combine(directory, "claude-config"));
        // So are the Claude app's thread records: an empty throwaway folder.
        Directory.CreateDirectory(Path.Combine(directory, "claude-app-data"));
        Environment.SetEnvironmentVariable(ClaudeAppThreads.RootsVariable, Path.Combine(directory, "claude-app-data"));
        // And the organisation's managed settings (package 3): a file that doesn't exist. No Claude notification pops up.
        Environment.SetEnvironmentVariable(ClaudeConnector.ManagedVariable, Path.Combine(directory, "claude-managed-settings.json"));
        // And Widget Screen's bridge folder, which ClaudeHook.exe also writes to (decision 028): a throwaway one, missing
        // until a check makes it, so the real %LOCALAPPDATA%\WidgetScreen is never written.
        Environment.SetEnvironmentVariable(ClaudeBridge.PartnerVariable, Path.Combine(directory, "widgetscreen-bridge"));
        ClaudeToast.Enabled = false;
        ClaudeTests.Run(directory, Check);
        ClaudeSourcesTests.Run(directory, Check);
        ClaudeTreeTests.Run(directory, Check);
        ShellTests.Run(directory, Check);
        ClaudeReviewTests.Run(directory, Check);
        ClaudeTrustTests.Run(directory, Check);
        ClaudeNumbersTests.Run(directory, Check);
        ClaudeInstantTests.Run(directory, Check);
        MigrationTests.Run(directory, Check);
        Check("Native XAML and control templates load", () => {
            var window = AppController.LoadWindow("ShellWindow"); var settings = AppController.LoadWindow("SettingsWindow");
            Assert(window.FindName("ClaudeCard") != null && window.FindName("WindowEdge") != null && window.FindName("SettingsButton") != null, "Dashboard controls missing.");
            Assert(settings.FindName("SettingsTabs") != null && settings.FindName("SaveSettings") != null && settings.FindName("CancelSettings") != null, "Settings controls missing.");
            window.Close(); settings.Close();
        });
        Check("Settings round trip and atomic replacement", () => {
            string path = Path.Combine(directory, "round-trip.xml");
            var settings = new AppSettings { Accent = "Lavender", TextScale = 1.15, QuietMode = true, ClaudePeriod = "Week" };
            SettingsStore.Save(path, settings); settings.Use24HourClock = true; SettingsStore.Save(path, settings);
            string warning; var loaded = SettingsStore.Load(path, out warning);
            Assert(warning == null && loaded.Accent == "Lavender" && loaded.TextScale == 1.15 && loaded.Use24HourClock && loaded.QuietMode && loaded.ClaudePeriod == "Week", "Saved settings were not restored.");
        });
        Check("Malformed settings fall back without overwriting", () => {
            string path = Path.Combine(directory, "malformed.xml"); File.WriteAllText(path, "<broken");
            string warning; var loaded = SettingsStore.Load(path, out warning);
            Assert(warning != null && loaded.Theme == "Midnight" && File.ReadAllText(path) == "<broken", "Corrupt settings recovery changed the original file.");
        });
        Check("Settings written by the widget app still load, keeping the Claude choices and dropping the rest", () => {
            // The spec's default settings.xml, when this checkout has it, and a settings.xml as v0.41.0 wrote it with the
            // other widgets set up: their fields are no longer known, so they are left out without a warning.
            string reference = null;
            for (var folder = new DirectoryInfo(Path.GetFullPath(directory)); folder != null && reference == null; folder = folder.Parent)
            {
                foreach (string spec in new[] { Path.Combine("docs", "archive", "widget-screen", "design specfication"), "design specfication" })
                {
                    string candidate = Path.Combine(folder.FullName, spec, "as-built-reference", "default-settings.xml");
                    if (reference == null && File.Exists(candidate)) reference = candidate;
                }
            }
            string warning;
            if (reference != null)
            {
                string copy = Path.Combine(directory, "as-built-default-settings.xml"); File.Copy(reference, copy, true);
                var baseline = SettingsStore.Load(copy, out warning);
                Assert(warning == null && baseline.Theme == "Midnight" && baseline.Accent == "Lime", "The as-built default settings.xml no longer loads: " + warning);
            }
            else Console.WriteLine("  The as-built default settings.xml isn't in this checkout; only the v0.41.0 sample was loaded.");
            string path = Path.Combine(directory, "v0.41.0-settings.xml");
            File.WriteAllText(path, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<AppSettings xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">"
                + "<Theme>Slate</Theme><Accent>Glacier</Accent><TextScale>1.1</TextScale><QuietMode>true</QuietMode>"
                + "<ShowCpu>false</ShowCpu><ShowGpu>true</ShowGpu><SystemStyles>Cpu=Ring</SystemStyles><WeatherLocation>Riyadh</WeatherLocation><Latitude>24.7</Latitude><Longitude>46.7</Longitude>"
                + "<StartFullscreen>true</StartFullscreen><KeepGameFocus>true</KeepGameFocus><Monitor>\\\\.\\DISPLAY2</Monitor><RemoteEnabled>true</RemoteEnabled><HideToTrayWithPhone>true</HideToTrayWithPhone><WidgetInks>Focus=Rose</WidgetInks><WidgetColours>Each</WidgetColours><PageName>Main</PageName><CurrentPage>0</CurrentPage><HintDismissed>true</HintDismissed>"
                + "<SpotifyClientId>0123456789abcdef0123456789abcdef</SpotifyClientId><FocusChime>true</FocusChime><ShowLevels>false</ShowLevels><SwapSideWidgets>true</SwapSideWidgets>"
                + "<Shortcuts><Shortcut><Label>Music</Label><Action>Website</Action><Target>https://example.com</Target></Shortcut></Shortcuts>"
                + "<Macros><Macro><Name>Stream</Name></Macro></Macros><WorldClocks>Tokyo|Tokyo Standard Time</WorldClocks><HomeEntities>light.desk</HomeEntities>"
                + "<WidgetLayout><WidgetPlacement><Id>Focus</Id><X>0</X><Y>0</Y><Width>320</Width><Height>576</Height><Visible>true</Visible><Layer>0</Layer></WidgetPlacement>"
                + "<WidgetPlacement><Id>Claude</Id><X>336</X><Y>0</Y><Width>800</Width><Height>576</Height><Visible>false</Visible><Layer>2</Layer></WidgetPlacement></WidgetLayout>"
                + "<ClaudePeriod>Week</ClaudePeriod><ClaudeCardSeconds>3</ClaudeCardSeconds><ClaudeShowArchived>true</ClaudeShowArchived>"
                + "</AppSettings>");
            var loaded = SettingsStore.Load(path, out warning);
            Assert(warning == null, "The v0.41.0 settings.xml raised a warning: " + warning);
            Assert(loaded.Theme == "Slate" && loaded.Accent == "Glacier" && loaded.TextScale == 1.1 && loaded.QuietMode, "Appearance or Quiet mode was lost.");
            Assert(loaded.ClaudePeriod == "Week" && loaded.ClaudeCardSeconds == 3 && loaded.ClaudeShowArchived, "The Claude choices were lost.");
            SettingsStore.Save(path, loaded);
            string saved = File.ReadAllText(path);
            foreach (string gone in new[] { "ShowCpu", "SystemStyles", "WeatherLocation", "SpotifyClientId", "FocusChime", "ShowLevels", "SwapSideWidgets", "<Shortcuts", "<Macros", "WorldClocks", "HomeEntities", "StartFullscreen", "KeepGameFocus", "Monitor", "RemoteEnabled", "HideToTray", "WidgetInks", "WidgetColours", "<WidgetLayout", "PageName", "CurrentPage", "HintDismissed" })
                Assert(!saved.Contains(gone), gone + " is still written.");
            // The card was hidden in that layout; there is no layout any more, so it shows.
            {
                var window = AppController.LoadWindow("ShellWindow");
                using (var controller = new AppController(window, path))
                    Assert(((System.Windows.UIElement)window.FindName("ClaudeCard")).Visibility == System.Windows.Visibility.Visible, "A card hidden in an old layout stays hidden.");
                window.Close();
            }
        });
        Check("Appearance survives save, and older settings inherit defaults", () => {
            string path = Path.Combine(directory, "customization.xml");
            var settings = new AppSettings { Theme = "Warm", CornerRadius = 0 };
            SettingsStore.Save(path, settings); string warning; var loaded = SettingsStore.Load(path, out warning);
            Assert(warning == null && loaded.Theme == "Warm" && loaded.CornerRadius == 0, "Appearance lost.");
            File.WriteAllText(path, "<AppSettings><Accent>Glacier</Accent></AppSettings>"); loaded = SettingsStore.Load(path, out warning);
            Assert(loaded.CornerRadius == 20 && loaded.Theme == "Midnight" && loaded.Accent == "Glacier" && loaded.ClaudeToast, "Older settings did not inherit defaults.");
            loaded.CornerRadius = 100; loaded.Theme = "invalid"; loaded.Normalize();
            Assert(loaded.CornerRadius == 24 && loaded.Theme == "Midnight", "Invalid appearance not normalized.");
        });
        Check("Settings tabs preview, cancel and save", () => {
            string path = Path.Combine(directory, "ui-settings.xml"); SettingsStore.Save(path, new AppSettings());
            {
                var window = AppController.LoadWindow("ShellWindow");
                using (var controller = new AppController(window, path))
                {
                    Action<string> click = name => ((System.Windows.Controls.Button)(controller.SettingsWindow != null && controller.SettingsWindow.FindName(name) != null ? controller.SettingsWindow.FindName(name) : window.FindName(name))).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Func<string, object> find = name => controller.SettingsWindow == null ? null : controller.SettingsWindow.FindName(name);
                    click("SettingsButton");
                    var tabs = (System.Windows.Controls.TabControl)find("SettingsTabs"); Assert(tabs.Items.Count == 3 && (string)((System.Windows.Controls.TabItem)tabs.Items[1]).Header == "Claude", "Settings tabs missing.");
                    ((System.Windows.Controls.ComboBox)find("ThemeChoice")).SelectedItem = "Warm";
                    var preview = ((System.Windows.Media.SolidColorBrush)window.Resources["Page"]).Color;
                    click("CancelSettings");
                    Assert(controller.SettingsWindow == null && ((System.Windows.Media.SolidColorBrush)window.Resources["Page"]).Color != preview, "Cancel retained preview theme.");
                    click("SettingsButton"); ((System.Windows.Controls.ComboBox)find("ThemeChoice")).SelectedItem = "Slate";
                    ((System.Windows.Controls.CheckBox)find("ClockChoice")).IsChecked = true;
                    ((System.Windows.Controls.TabControl)find("SettingsTabs")).SelectedIndex = 1; click("SaveSettings");
                    string warning; var saved = SettingsStore.Load(path, out warning);
                    Assert(saved.Theme == "Slate" && saved.Use24HourClock, "Cross-tab save lost values.");
                }
                window.Close();
            }
        });
        Check("Cancel discards the settings draft, and Settings opens again on the saved choices", () => {
            string path = Path.Combine(directory, "footer-cancel.xml"); SettingsStore.Save(path, new AppSettings());
            {
                var window = AppController.LoadWindow("ShellWindow");
                using (var controller = new AppController(window, path))
                {
                    Action<string> click = name => ((System.Windows.Controls.Button)(controller.SettingsWindow != null && controller.SettingsWindow.FindName(name) != null ? controller.SettingsWindow.FindName(name) : window.FindName(name))).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Func<string, object> find = name => controller.SettingsWindow == null ? null : controller.SettingsWindow.FindName(name);
                    var page = ((System.Windows.Media.SolidColorBrush)window.Resources["Page"]).Color; double small = (double)window.Resources["SmallSize"];
                    click("SettingsButton"); ((System.Windows.Controls.ComboBox)find("ThemeChoice")).SelectedItem = "Warm";
                    ((System.Windows.Controls.Slider)find("ScaleChoice")).Value = 115;
                    Assert((double)window.Resources["SmallSize"] > small, "The text size didn't preview.");
                    click("CancelSettings");
                    Assert(controller.SettingsWindow == null, "Cancel left Settings open.");
                    Assert(((System.Windows.Media.SolidColorBrush)window.Resources["Page"]).Color == page && (double)window.Resources["SmallSize"] == small, "Cancel kept the previewed appearance.");
                    string warning; var saved = SettingsStore.Load(path, out warning);
                    Assert(saved.Theme == "Midnight" && saved.TextScale == 1, "Cancel saved the draft.");
                    click("SettingsButton");
                    Assert((string)((System.Windows.Controls.ComboBox)find("ThemeChoice")).SelectedItem == "Midnight", "Reopened Settings showed the discarded draft.");
                    Assert(controller.SettingsWindow.MinWidth == 640 && controller.SettingsWindow.MinHeight == 480 && !controller.SettingsWindow.ShowInTaskbar, "The Settings window isn't the 640 × 480 one beside the dashboard.");
                    click("CancelSettings");
                }
                window.Close();
            }
        });
        Check("Settings copy is deep and complete", () => {
            var original = new AppSettings { Accent = "Amber", QuietMode = true, ClaudeCollapsed = new[] { "C:\\work" }, ClaudeFacts = new[] { "tokens" },
                ClaudeProjects = new[] { new ClaudeProjectChoice { Folder = "C:\\work", Shown = false } } };
            var copy = original.Copy();
            Func<AppSettings, string> xml = s => { using (var writer = new StringWriter()) { new System.Xml.Serialization.XmlSerializer(typeof(AppSettings)).Serialize(writer, s); return writer.ToString(); } };
            Assert(xml(copy) == xml(original), "The copy differs from the original.");
            Assert(copy.ClaudeCollapsed != original.ClaudeCollapsed && copy.ClaudeProjects != original.ClaudeProjects && copy.ClaudeFacts != original.ClaudeFacts, "The copy shares collections with the original.");
            copy.ClaudeProjects[0].Shown = true; copy.ClaudeCollapsed[0] = "C:\\other";
            Assert(!original.ClaudeProjects[0].Shown && original.ClaudeCollapsed[0] == "C:\\work", "Changing the copy changed the original.");
        });
        Check("Settings → Save keeps Quiet mode and the card's folds", () => {
            string path = Path.Combine(directory, "save-keeps.xml");
            SettingsStore.Save(path, new AppSettings { QuietMode = true, ClaudeCollapsed = new[] { "C:\\work" } });
            {
                var window = AppController.LoadWindow("ShellWindow");
                using (var controller = new AppController(window, path))
                {
                    Action<string> click = name => ((System.Windows.Controls.Button)(controller.SettingsWindow != null && controller.SettingsWindow.FindName(name) != null ? controller.SettingsWindow.FindName(name) : window.FindName(name))).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Func<string, object> find = name => controller.SettingsWindow == null ? null : controller.SettingsWindow.FindName(name);
                    click("SettingsButton"); ((System.Windows.Controls.ComboBox)find("ThemeChoice")).SelectedItem = "Warm"; click("SaveSettings");
                    string warning; var saved = SettingsStore.Load(path, out warning);
                    Assert(saved.Theme == "Warm", "The theme change was not saved.");
                    Assert(saved.QuietMode && saved.ClaudeCollapsed.SequenceEqual(new[] { "C:\\work" }), "Save changes reset Quiet mode or the card's folds.");
                }
                window.Close();
            }
        });
        Check("Settings has its three tabs in order, and the window is an ordinary one", () => {
            string path = Path.Combine(directory, "tabs.xml");
            SettingsStore.Save(path, new AppSettings());
            {
                var window = AppController.LoadWindow("ShellWindow");
                using (var controller = new AppController(window, path))
                {
                    controller.OpenSettings();
                    var tabs = (System.Windows.Controls.TabControl)controller.SettingsWindow.FindName("SettingsTabs");
                    var headers = tabs.Items.OfType<System.Windows.Controls.TabItem>().Select(t => (string)t.Header).ToList();
                    Assert(headers.SequenceEqual(new[] { "Theme", "Claude", "Window" }), "The Settings tabs aren't in order: " + String.Join(", ", headers));
                    controller.CancelSettings();
                    // No fullscreen, tray or focus tricks: a resizable window with its own title bar that takes the focus when clicked.
                    Assert(window.WindowStyle == System.Windows.WindowStyle.SingleBorderWindow && window.ResizeMode == System.Windows.ResizeMode.CanResize && window.ShowInTaskbar && window.ShowActivated, "The window isn't an ordinary one.");
                    Assert(window.MinWidth == 360 && window.MinHeight == 300, "The window's smallest size isn't 360 × 300.");
                    IntPtr handle = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
                    Assert((GetWindowLong(handle, -20) & 0x08000000) == 0, "The window still refuses the focus.");
                    foreach (string gone in new[] { "EditLayoutButton", "FullscreenButton", "TrayButton", "PageSwitcher", "ClaudePill", "HintRow", "EditPanelLayer", "RemoteEnabledChoice", "MonitorChoice", "KeepFocusChoice", "SettingsOverlay", "ClaudeHeading" })
                        Assert(window.FindName(gone) == null, gone + " is still in the window.");
                    Assert(System.Windows.Input.Stylus.GetIsPressAndHoldEnabled(window) && System.Windows.Input.Stylus.GetIsFlicksEnabled(window), "Press-and-hold or flicks are still turned off.");
                }
                window.Close();
            }
        });
        Check("Buttons and choices keep a 32-unit target in both windows", () => {
            string path = Path.Combine(directory, "touch-targets.xml"); SettingsStore.Save(path, new AppSettings());
            {
                var window = AppController.LoadWindow("ShellWindow");
                using (var controller = new AppController(window, path))
                {
                    controller.OpenSettings();
                    var settings = controller.SettingsWindow;
                    AppController.Lay(settings, new System.Windows.Size(980, 720)); AppController.Lay(window, new System.Windows.Size(1280, 760));
                    foreach (string name in new[] { "SaveSettings", "CancelSettings", "ThemeChoice", "AccentChoice", "SettingsButton", "QuietButton" })
                    {
                        var element = (System.Windows.FrameworkElement)(settings.FindName(name) ?? window.FindName(name));
                        Assert(element.ActualHeight >= 32, name + " is shorter than 32.");
                    }
                    controller.CancelSettings();
                }
                window.Close();
            }
        });
        Check("Web requests skip IPv6 only when the host also has IPv4", () => {
            var v6 = System.Net.IPAddress.Parse("2a04:4e42:54::810"); var v4 = System.Net.IPAddress.Parse("199.232.83.42");
            Assert(PreferIPv4Handler.SkipAddress(v6, new[] { v6, v4 }), "Dual-stack host still tries IPv6 first.");
            Assert(!PreferIPv4Handler.SkipAddress(v4, new[] { v6, v4 }), "IPv4 address was skipped.");
            Assert(!PreferIPv4Handler.SkipAddress(v6, new[] { v6 }) && !PreferIPv4Handler.SkipAddress(v6, new System.Net.IPAddress[0]), "IPv6-only host became unreachable.");
        });
        Check("Settings normalization", () => {
            var settings = new AppSettings { TextScale = 9, Accent = "invalid" }; settings.Normalize();
            Assert(settings.TextScale == 1.4 && settings.Accent == "Lime", "Invalid settings were not normalized.");
        });
        Check("The Claude card fills the window and follows its size", () => {
            string path = Path.Combine(directory, "fills.xml"); SettingsStore.Save(path, new AppSettings());
            {
                var window = AppController.LoadWindow("ShellWindow");
                using (var controller = new AppController(window, path))
                {
                    controller.UseClaudeSample(false);
                    var card = (System.Windows.FrameworkElement)window.FindName("ClaudeCard");
                    foreach (var size in new[] { new System.Windows.Size(1920, 720), new System.Windows.Size(1000, 480), new System.Windows.Size(2560, 1400), new System.Windows.Size(360, 300) })
                    {
                        AppController.Lay(window, size);
                        // The 3-unit frame on each side, the header (56 units on one line, 84 with the summary under it) and
                        // the source-health line's row under the card (it also overlaps 16 units of the card's padding).
                        double head = controller.HeaderTwoLines ? 84 : 56;
                        Assert(Math.Abs(card.ActualWidth - (size.Width - 6)) < 1 && Math.Abs(card.ActualHeight - (size.Height - 6 - head - controller.SourceRow)) < 1, "The card doesn't fill a " + size + " window: " + card.ActualWidth + " × " + card.ActualHeight);
                        Assert(controller.ClaudeCardView.Current == CardSizing.Claude(card.ActualWidth, card.ActualHeight), "The card's design doesn't follow a " + size + " window.");
                    }
                }
                window.Close();
            }
        });
        Console.WriteLine(failures == 0 ? "All checks passed." : failures + " check(s) failed.");
        return failures == 0 ? 0 : 1;
    }
}

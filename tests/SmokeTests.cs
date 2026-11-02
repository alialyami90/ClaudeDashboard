using System;
using System.IO;
using System.Linq;
using System.Threading;
using WidgetScreen;

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
        // Dashboards in the checks are laid out for the 1920 × 720 reference screen, whatever monitors this PC has.
        Program.ScreenSize = new System.Windows.Size(1920, 720);
        // Claude Code's folder is a throwaway one for every check: the real settings.json, sign-in and logs are never touched.
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", Path.Combine(directory, "claude-config"));
        // So are the Claude app's thread records: an empty throwaway folder.
        Directory.CreateDirectory(Path.Combine(directory, "claude-app-data"));
        Environment.SetEnvironmentVariable(ClaudeAppThreads.RootsVariable, Path.Combine(directory, "claude-app-data"));
        // And the organisation's managed settings (package 3): a file that doesn't exist. No Claude notification pops up.
        Environment.SetEnvironmentVariable(ClaudeConnector.ManagedVariable, Path.Combine(directory, "claude-managed-settings.json"));
        ClaudeToast.Enabled = false;
        RemoteTests.Run(directory, Check);
        ClaudeTests.Run(directory, Check);
        ClaudeSourcesTests.Run(directory, Check);
        ClaudeTreeTests.Run(directory, Check);
        ClaudeReviewTests.Run(directory, Check);
        ClaudeTrustTests.Run(directory, Check);
        ClaudeNumbersTests.Run(directory, Check);
        ClaudeInstantTests.Run(directory, Check);
        Check("Native XAML and control templates load", () => {
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (System.Windows.Window)System.Windows.Markup.XamlReader.Load(stream);
                Assert(window.FindName("SettingsOverlay") != null && window.FindName("ClaudeCard") != null, "Dashboard controls missing.");
                window.Close();
            }
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
                string candidate = Path.Combine(folder.FullName, "design specfication", "as-built-reference", "default-settings.xml");
                if (File.Exists(candidate)) reference = candidate;
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
                + "<SpotifyClientId>0123456789abcdef0123456789abcdef</SpotifyClientId><FocusChime>true</FocusChime><ShowLevels>false</ShowLevels><SwapSideWidgets>true</SwapSideWidgets>"
                + "<Shortcuts><Shortcut><Label>Music</Label><Action>Website</Action><Target>https://example.com</Target></Shortcut></Shortcuts>"
                + "<Macros><Macro><Name>Stream</Name></Macro></Macros><WorldClocks>Tokyo|Tokyo Standard Time</WorldClocks><HomeEntities>light.desk</HomeEntities>"
                + "<WidgetLayout><WidgetPlacement><Id>Focus</Id><X>0</X><Y>0</Y><Width>320</Width><Height>576</Height><Visible>true</Visible><Layer>0</Layer></WidgetPlacement>"
                + "<WidgetPlacement><Id>Claude</Id><X>336</X><Y>0</Y><Width>800</Width><Height>576</Height><Visible>true</Visible><Layer>2</Layer></WidgetPlacement></WidgetLayout>"
                + "<ClaudePeriod>Week</ClaudePeriod><ClaudeCardSeconds>3</ClaudeCardSeconds><ClaudeShowArchived>true</ClaudeShowArchived>"
                + "</AppSettings>");
            var loaded = SettingsStore.Load(path, out warning);
            Assert(warning == null, "The v0.41.0 settings.xml raised a warning: " + warning);
            Assert(loaded.Theme == "Slate" && loaded.Accent == "Glacier" && loaded.TextScale == 1.1 && loaded.QuietMode, "Appearance or Quiet mode was lost.");
            Assert(loaded.ClaudePeriod == "Week" && loaded.ClaudeCardSeconds == 3 && loaded.ClaudeShowArchived, "The Claude choices were lost.");
            SettingsStore.Save(path, loaded);
            string saved = File.ReadAllText(path);
            foreach (string gone in new[] { "ShowCpu", "SystemStyles", "WeatherLocation", "SpotifyClientId", "FocusChime", "ShowLevels", "SwapSideWidgets", "<Shortcuts", "<Macros", "WorldClocks", "HomeEntities" })
                Assert(!saved.Contains(gone), gone + " is still written.");
        });
        Check("Appearance survives save, and older settings inherit defaults", () => {
            string path = Path.Combine(directory, "customization.xml");
            var settings = new AppSettings { Theme = "Warm", CornerRadius = 0, StartFullscreen = false };
            SettingsStore.Save(path, settings); string warning; var loaded = SettingsStore.Load(path, out warning);
            Assert(warning == null && loaded.Theme == "Warm" && loaded.CornerRadius == 0 && !loaded.StartFullscreen, "Appearance or launch preference lost.");
            File.WriteAllText(path, "<AppSettings><Accent>Glacier</Accent></AppSettings>"); loaded = SettingsStore.Load(path, out warning);
            Assert(loaded.StartFullscreen && loaded.Theme == "Midnight" && loaded.Accent == "Glacier", "Older settings did not inherit defaults.");
            loaded.CornerRadius = 100; loaded.Theme = "invalid"; loaded.Normalize();
            Assert(loaded.CornerRadius == 24 && loaded.Theme == "Midnight", "Invalid appearance not normalized.");
        });
        Check("Settings tabs preview, cancel and save", () => {
            string path = Path.Combine(directory, "ui-settings.xml"); SettingsStore.Save(path, new AppSettings());
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (System.Windows.Window)System.Windows.Markup.XamlReader.Load(stream);
                using (var controller = new DashboardController(window, path, true))
                {
                    Action<string> click = name => ((System.Windows.Controls.Button)window.FindName(name)).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    click("SettingsButton");
                    var tabs = (System.Windows.Controls.TabControl)window.FindName("SettingsTabs"); Assert(tabs.Items.Count == 5 && (string)((System.Windows.Controls.TabItem)tabs.Items[4]).Header == "Claude", "Settings tabs missing.");
                    ((System.Windows.Controls.ComboBox)window.FindName("ThemeChoice")).SelectedItem = "Warm";
                    var preview = ((System.Windows.Media.SolidColorBrush)window.Resources["Page"]).Color;
                    click("CancelSettings");
                    Assert(((System.Windows.Media.SolidColorBrush)window.Resources["Page"]).Color != preview, "Cancel retained preview theme.");
                    click("SettingsButton"); ((System.Windows.Controls.ComboBox)window.FindName("ThemeChoice")).SelectedItem = "Slate";
                    ((System.Windows.Controls.CheckBox)window.FindName("StartFullscreenChoice")).IsChecked = false;
                    tabs.SelectedIndex = 2; click("SaveSettings");
                    string warning; var saved = SettingsStore.Load(path, out warning);
                    Assert(saved.Theme == "Slate" && !saved.StartFullscreen, "Cross-tab save lost values.");
                }
                window.Close();
            }
        });
        Check("Footer Cancel discards the same settings draft as the header Cancel", () => {
            string path = Path.Combine(directory, "footer-cancel.xml"); SettingsStore.Save(path, new AppSettings());
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (System.Windows.Window)System.Windows.Markup.XamlReader.Load(stream);
                using (var controller = new DashboardController(window, path, true))
                {
                    Action<string> click = name => ((System.Windows.Controls.Button)window.FindName(name)).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    var page = ((System.Windows.Media.SolidColorBrush)window.Resources["Page"]).Color;
                    click("SettingsButton"); ((System.Windows.Controls.ComboBox)window.FindName("ThemeChoice")).SelectedItem = "Warm";
                    ((System.Windows.Controls.Slider)window.FindName("ScaleChoice")).Value = 115;
                    click("CancelSettingsFooter");
                    Assert(((System.Windows.Controls.Grid)window.FindName("SettingsOverlay")).Visibility == System.Windows.Visibility.Collapsed, "Footer Cancel left Settings open.");
                    Assert(((System.Windows.Media.SolidColorBrush)window.Resources["Page"]).Color == page && (double)window.Resources["SmallSize"] == 15, "Footer Cancel kept the previewed appearance.");
                    string warning; var saved = SettingsStore.Load(path, out warning);
                    Assert(saved.Theme == "Midnight" && saved.TextScale == 1, "Footer Cancel saved the draft.");
                    click("SettingsButton");
                    Assert((string)((System.Windows.Controls.ComboBox)window.FindName("ThemeChoice")).SelectedItem == "Midnight", "Reopened Settings showed the discarded draft.");
                    click("CancelSettings");
                    var panel = (System.Windows.Controls.Border)((System.Windows.Controls.Grid)window.FindName("SettingsOverlay")).Children[0];
                    Assert(panel.Width == 1440 && panel.Height == 672, "Settings panel is not 1440 × 672.");
                }
                window.Close();
            }
        });
        Check("Settings copy is deep and complete", () => {
            var original = new AppSettings { Accent = "Amber", RemoteEnabled = true, HideToTrayWithPhone = false, QuietMode = true, CurrentPage = 1, PageName = "Work", ClaudeCollapsed = new[] { "C:\\work" },
                MorePages = new[] { new LayoutPage { Name = "Games", WidgetLayout = WidgetLayout.DefaultFor(new AppSettings(), WidgetLayout.Reference) } },
                CanvasLayouts = new[] { new CanvasLayout { Width = 1000, Height = 500, Widgets = WidgetLayout.DefaultFor(new AppSettings(), new System.Windows.Size(1000, 500)) } } };
            original.WidgetLayout = WidgetLayout.DefaultFor(original, WidgetLayout.Reference);
            var copy = original.Copy();
            Func<AppSettings, string> xml = s => { using (var writer = new StringWriter()) { new System.Xml.Serialization.XmlSerializer(typeof(AppSettings)).Serialize(writer, s); return writer.ToString(); } };
            Assert(xml(copy) == xml(original), "The copy differs from the original.");
            Assert(copy.WidgetLayout != original.WidgetLayout && copy.CanvasLayouts != original.CanvasLayouts && copy.MorePages != original.MorePages, "The copy shares collections with the original.");
            copy.WidgetLayout[0].X += 8; copy.MorePages[0].Name = "Changed"; copy.CanvasLayouts[0].Widgets[0].Y += 8;
            Assert(original.WidgetLayout[0].X != copy.WidgetLayout[0].X && original.MorePages[0].Name == "Games" && original.CanvasLayouts[0].Widgets[0].Y != copy.CanvasLayouts[0].Widgets[0].Y, "Changing the copy changed the original.");
        });
        Check("Settings → Save keeps pages, the current page, Quiet mode and the phone link", () => {
            string path = Path.Combine(directory, "save-keeps.xml");
            SettingsStore.Save(path, new AppSettings { RemoteEnabled = true, HideToTrayWithPhone = false, QuietMode = true, PageName = "Work", MorePages = new[] { new LayoutPage { Name = "Games" }, new LayoutPage { Name = "Music" } }, CurrentPage = 1 });
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (System.Windows.Window)System.Windows.Markup.XamlReader.Load(stream);
                using (var controller = new DashboardController(window, path, true))
                {
                    Action<string> click = name => ((System.Windows.Controls.Button)window.FindName(name)).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    click("SettingsButton"); ((System.Windows.Controls.ComboBox)window.FindName("ThemeChoice")).SelectedItem = "Warm"; click("SaveSettings");
                    string warning; var saved = SettingsStore.Load(path, out warning);
                    Assert(saved.Theme == "Warm", "The theme change was not saved.");
                    Assert(WidgetLayout.PageCount(saved) == 3 && saved.PageName == "Work" && saved.MorePages[1].Name == "Music" && saved.CurrentPage == 1, "Save changes dropped the pages or the current page.");
                    Assert(saved.RemoteEnabled && !saved.HideToTrayWithPhone && saved.QuietMode, "Save changes reset the phone link or Quiet mode.");
                    click("SettingsButton");
                    Assert(((System.Windows.Controls.CheckBox)window.FindName("RemoteEnabledChoice")).IsChecked == true && ((System.Windows.Controls.TextBlock)window.FindName("PageNameText")).Text == "Games", "The dashboard shows a different state than it saved.");
                    click("CancelSettings");
                }
                window.Close();
            }
        });
        Check("The tips go away with Got it, and Settings has its five tabs in order", () => {
            string path = Path.Combine(directory, "tips.xml");
            SettingsStore.Save(path, new AppSettings());
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (System.Windows.Window)System.Windows.Markup.XamlReader.Load(stream);
                using (var controller = new DashboardController(window, path, true))
                {
                    Action<System.Windows.Controls.Button> click = button => button.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    string warning;
                    var hint = (System.Windows.Controls.StackPanel)window.FindName("HintRow");
                    Assert(hint.Visibility == System.Windows.Visibility.Visible, "The first-launch tips aren't shown.");
                    click((System.Windows.Controls.Button)window.FindName("HintDismiss"));
                    Assert(hint.Visibility == System.Windows.Visibility.Collapsed && SettingsStore.Load(path, out warning).HintDismissed, "Got it didn't put the tips away for good.");
                    var tabs = (System.Windows.Controls.TabControl)window.FindName("SettingsTabs");
                    var headers = tabs.Items.OfType<System.Windows.Controls.TabItem>().Select(t => (string)t.Header).ToList();
                    Assert(headers.SequenceEqual(new[] { "Theme", "Display", "Widgets", "Phone", "Claude" }), "The Settings tabs aren't in order: " + String.Join(", ", headers));
                    Assert(!new AppSettings().HideToTrayWithPhone, "Tray mode with the phone is on by default.");
                }
                window.Close();
            }
        });
        Check("Touch sliders map to the rendered track, clamp, snap and follow direction changes", () => {
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (System.Windows.Window)System.Windows.Markup.XamlReader.Load(stream);
                var style = (System.Windows.Style)window.Resources[typeof(System.Windows.Controls.Slider)];
                Func<double, double, double, System.Windows.Controls.Slider> make = (min, max, tick) => {
                    var s = new System.Windows.Controls.Slider { Style = style, Minimum = min, Maximum = max, TickFrequency = tick, IsSnapToTickEnabled = tick > 0 };
                    s.ApplyTemplate(); s.Measure(new System.Windows.Size(448, 48)); s.Arrange(new System.Windows.Rect(0, 0, 448, 48)); s.UpdateLayout(); return s;
                };
                var volume = make(0, 100, 0);
                var track = (System.Windows.Controls.Primitives.Track)volume.Template.FindName("PART_Track", volume);
                Assert(track.Thumb.ActualWidth == 48 && track.Thumb.ActualHeight == 48, "Slider thumb target is not 48 × 48.");
                double start = track.TranslatePoint(new System.Windows.Point(0, 0), volume).X, length = track.ActualWidth;
                Func<double, double> at = fraction => start + 24 + fraction * (length - 48);
                Assert(Math.Abs(TouchSlider.ValueAt(volume, at(.5)) - 50) < .01 && Math.Abs(TouchSlider.ValueAt(volume, at(.25)) - 25) < .01, "Touch does not map to the track centre line.");
                Assert(TouchSlider.ValueAt(volume, -500) == 0 && TouchSlider.ValueAt(volume, 5000) == 100 && TouchSlider.ValueAt(volume, at(0)) == 0 && TouchSlider.ValueAt(volume, at(1)) == 100, "Endpoints are not clamped.");
                // Rapid changes: the thumb has not been re-arranged, so mapping must not depend on the current value.
                foreach (double fraction in new[] { .9, .1, .7, .3, .95 }) { volume.Value = TouchSlider.ValueAt(volume, at(fraction)); Assert(Math.Abs(volume.Value - fraction * 100) < .01, "Rapid or reversed movement drifted before relayout."); }
                var corners = make(0, 24, 4);
                Assert(TouchSlider.ValueAt(corners, at(.30)) == 8 && TouchSlider.ValueAt(corners, at(.36)) == 8 && TouchSlider.ValueAt(corners, at(.42)) == 12 && TouchSlider.ValueAt(corners, at(1.2)) == 24, "Corner steps do not snap.");
                var scale = make(90, 115, 5);
                Assert(TouchSlider.ValueAt(scale, at(0)) == 90 && TouchSlider.ValueAt(scale, at(.62)) == 105 && TouchSlider.ValueAt(scale, at(.58)) == 105 && TouchSlider.ValueAt(scale, at(.5)) % 5 == 0, "Text size steps do not snap.");
                Assert(System.Windows.Input.Stylus.GetIsPressAndHoldEnabled(window) == false && System.Windows.Input.Stylus.GetIsFlicksEnabled(window) == false, "Press-and-hold or flicks are still enabled.");
                window.Close();
            }
        });
        Check("Settings panel touch targets", () => {
            string path = Path.Combine(directory, "touch-targets.xml"); SettingsStore.Save(path, new AppSettings());
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (System.Windows.Window)System.Windows.Markup.XamlReader.Load(stream);
                using (var controller = new DashboardController(window, path, true))
                {
                    Action<string> click = name => ((System.Windows.Controls.Button)window.FindName(name)).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    click("SettingsButton");
                    var dashboard = (System.Windows.FrameworkElement)window.FindName("Dashboard");
                    dashboard.Measure(new System.Windows.Size(1920, 720)); dashboard.Arrange(new System.Windows.Rect(0, 0, 1920, 720)); dashboard.UpdateLayout();
                    foreach (string name in new[] { "SaveSettings", "CancelSettingsFooter", "ThemeChoice", "AccentChoice" })
                        Assert(((System.Windows.FrameworkElement)window.FindName(name)).ActualHeight >= 52, name + " is shorter than 52.");
                    click("CancelSettings");
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
        Check("A fresh install shows the Claude card filling the canvas", () => {
            var layout = WidgetLayout.DefaultFor(new AppSettings(), WidgetLayout.Reference);
            Assert(layout.Length == 1 && layout[0].Id == "Claude" && layout[0].Visible && layout[0].Bounds == new System.Windows.Rect(0, 0, WidgetLayout.ReferenceWidth, WidgetLayout.ReferenceHeight), "The default layout isn't the Claude card alone.");
            var old = new[] { new WidgetPlacement { Id = "Focus", Width = 320, Height = 576 }, new WidgetPlacement { Id = "Claude", X = 336, Width = 800, Height = 576 } };
            var kept = WidgetLayout.Normalize(old, new AppSettings());
            Assert(kept.Length == 1 && kept[0].Id == "Claude" && kept[0].X == 336, "An older layout kept other widgets or lost the Claude card's place.");
        });
        Console.WriteLine(failures == 0 ? "All checks passed." : failures + " check(s) failed.");
        return failures == 0 ? 0 : 1;
    }
}

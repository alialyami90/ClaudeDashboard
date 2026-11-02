using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WidgetScreen
{
    public static class Program
    {
        // Render runs capture the window and exit; they never read this PC's sessions.
        public static bool Rendering { get; internal set; }
        // The window's size in pixels for renders (--render-size WxH, 1920 × 720 by default); null for an ordinary start.
        public static Size? ScreenSize { get; set; }

        [STAThread]
        public static int Main(string[] args)
        {
            // Claude Dashboard's new window (phase 4), beside the widget app's until the swap.
            if (args.Contains("--shell")) return ShellProgram.Run(args);
            try
            {
                var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                DashboardController running = null; int errors = 0;
                // One failing widget or service must never take the dashboard down: an unhandled error is logged and
                // shown on the status line, and the dashboard carries on. A storm of errors (something that throws on
                // every heartbeat) still closes the app rather than looping forever, and renders fail loudly.
                application.DispatcherUnhandledException += (sender, e) => {
                    Log(e.Exception);
                    if (Rendering || ++errors > 20) return;
                    e.Handled = true;
                    if (running != null) running.ReportError(e.Exception);
                };
                // Awaits started before Run (for example while the dashboard is constructed) must resume on the UI thread.
                System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                Window window;
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WidgetScreen.MainWindow.xaml")) window = (Window)XamlReader.Load(stream);
                string settingsPath = Argument(args, "--settings") ?? SettingsStore.DefaultPath;
                string renderPath = Argument(args, "--render");
                Rendering = renderPath != null;
                if (Rendering) { ScreenSize = RenderSize(Argument(args, "--render-size")); window.Width = ScreenSize.Value.Width; window.Height = ScreenSize.Value.Height; }
                using (var controller = new DashboardController(window, settingsPath))
                {
                    running = controller;
                    // A labelled fixture for the Claude card ("Sample" in its heading) instead of this PC's sessions.
                    if (renderPath != null && args.Contains("--render-claude-sample")) controller.UseClaudeSample(args.Contains("--render-claude-needs"));
                    if (renderPath != null)
                    {
                        // --render-wait N gives live readings longer to arrive (System stats history fills every 2 s).
                        int wait; if (!Int32.TryParse(Argument(args, "--render-wait"), out wait) || wait < 1 || wait > 120) wait = 5;
                        var renderTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(wait) };
                        renderTimer.Tick += (sender, e) => {
                            renderTimer.Stop();
                            if (args.Contains("--render-settings"))
                            {
                                ((System.Windows.Controls.Button)window.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                                // --render-tab takes the tab's position or its header ("Prayer").
                                int tabIndex; string tab = Argument(args, "--render-tab");
                                if (Int32.TryParse(tab, out tabIndex) && tabIndex >= 0 && tabIndex < ((System.Windows.Controls.TabControl)window.FindName("SettingsTabs")).Items.Count)
                                    ((System.Windows.Controls.TabControl)window.FindName("SettingsTabs")).SelectedIndex = tabIndex;
                                else if (tab != null) DashboardController.SelectSettingsTab(window, tab);
                            }
                            // The window's content is drawn at the render size, one unit to a pixel.
                            var dashboard = (FrameworkElement)window.FindName("Dashboard");
                            Size size = ScreenSize.Value;
                            dashboard.Measure(size); dashboard.Arrange(new Rect(size)); dashboard.UpdateLayout();
                            var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                            bitmap.Render(dashboard);
                            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(renderPath)));
                            using (var file = File.Create(renderPath)) encoder.Save(file);
                            window.Close();
                        };
                        window.Loaded += (sender, e) => renderTimer.Start();
                    }
                    application.Run(window);
                }
                return 0;
            }
            catch (Exception e)
            {
                Log(e);
                if (!args.Contains("--render")) MessageBox.Show("Claude Dashboard could not start.\n\n" + e.GetBaseException().Message, "Claude Dashboard", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
        private static Size RenderSize(string value)
        {
            var parts = (value ?? "").ToLowerInvariant().Split('x');
            int width, height;
            if (parts.Length == 2 && Int32.TryParse(parts[0], out width) && Int32.TryParse(parts[1], out height) && width >= 320 && height >= 240 && width <= 8192 && height <= 8192) return new Size(width, height);
            return new Size(1920, 720);
        }
        private static string Argument(string[] args, string name) { int index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
        internal static void Log(Exception error)
        {
            try
            {
                string directory = Path.GetDirectoryName(SettingsStore.DefaultPath); Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "error.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.WriteAllText(path, "");
                File.AppendAllText(path, DateTime.Now.ToString("s") + " " + error + Environment.NewLine);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}

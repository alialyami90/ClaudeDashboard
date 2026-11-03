using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace ClaudeDashboard
{
    // Claude Dashboard's start (phase 4): one ordinary window and its controller. Renders draw the window at a size in
    // pixels and a display scaling, and exit (--shell, from the weeks both windows were built, is still accepted):
    //   --render <png> [--render-size WxH] [--render-scale 1.5] [--render-claude-sample [--render-claude-needs] [--render-claude-crowded]]
    //   [--render-view Live|Garage|Limits|History|Reliability|Sources] [--render-settings [--render-tab Claude]]
    public static class ShellProgram
    {
        public static int Run(string[] args)
        {
            try
            {
                var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                AppController running = null; int errors = 0;
                // One failing service must never take the window down: an unhandled error is logged and shown on the
                // status line. A storm of errors still closes the app, and renders fail loudly.
                application.DispatcherUnhandledException += (sender, e) => {
                    Program.Log(e.Exception);
                    if (Program.Rendering || ++errors > 20) return;
                    e.Handled = true;
                    if (running != null) running.ReportError(e.Exception);
                };
                System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                string settingsPath = Argument(args, "--settings") ?? SettingsStore.DefaultPath;
                if (Argument(args, "--settings") != null) Program.LogBeside(settingsPath);
                string renderPath = Argument(args, "--render");
                Program.Rendering = renderPath != null;
                var window = AppController.LoadWindow("ShellWindow");
                // Phase 5: the first run offers to bring Widget Screen's Claude setup over (never for renders or a
                // --settings file, which keep away from the PC's real folders).
                if (renderPath == null && Argument(args, "--settings") == null && !AppController.OfferMove(window, settingsPath)) return 0;
                using (var controller = new AppController(window, settingsPath))
                {
                    running = controller;
                    if (renderPath != null)
                    {
                        // A labelled fixture ("SAMPLE" in the header) instead of this PC's sessions.
                        if (args.Contains("--render-claude-sample")) controller.UseClaudeSample(args.Contains("--render-claude-needs"), args.Contains("--render-claude-crowded"));
                        double scale = Scale(Argument(args, "--render-scale"));
                        // One of the lap chart's views (decision 030); from 640 wide, since narrower windows have no tabs.
                        string view = Argument(args, "--render-view");
                        if (view != null)
                        {
                            if (ClaudeViews.Clean(view) == null) throw new ArgumentException("--render-view takes one of " + String.Join(", ", ClaudeViews.Names) + ", not " + view + ".");
                            controller.ShowView(view);
                        }
                        if (args.Contains("--render-settings"))
                        {
                            controller.OpenSettings();
                            controller.SelectSettingsTab(Argument(args, "--render-tab"));
                            AppController.Render(controller.SettingsWindow, ParseSize(Argument(args, "--render-size"), new Size(980, 720)), scale, renderPath);
                        }
                        else AppController.Render(window, ParseSize(Argument(args, "--render-size"), new Size(1280, 760)), scale, renderPath);
                        return 0;
                    }
                    application.Run(window);
                }
                return 0;
            }
            catch (Exception e)
            {
                Program.Log(e);
                if (!args.Contains("--render")) MessageBox.Show("Claude Dashboard could not start.\n\n" + e.GetBaseException().Message, "Claude Dashboard", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }

        public static Size ParseSize(string value, Size fallback)
        {
            var parts = (value ?? "").ToLowerInvariant().Split('x');
            int width, height;
            if (parts.Length == 2 && Int32.TryParse(parts[0], out width) && Int32.TryParse(parts[1], out height) && width >= 240 && height >= 200 && width <= 8192 && height <= 8192) return new Size(width, height);
            return fallback;
        }
        private static double Scale(string value)
        {
            double scale;
            return Double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out scale) && scale >= 1 && scale <= 3 ? scale : 1;
        }
        private static string Argument(string[] args, string name) { int index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
    }
}

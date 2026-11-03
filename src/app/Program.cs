using System;
using System.IO;

namespace ClaudeDashboard
{
    // Claude Dashboard's entry point. ShellProgram opens the window (or draws a render and exits); this keeps what the
    // rest of the app asks of the program: whether this run is a render, and the error log.
    public static class Program
    {
        // Render runs capture the window and exit; they never read this PC's sessions.
        public static bool Rendering { get; internal set; }

        [STAThread]
        public static int Main(string[] args) { return ShellProgram.Run(args); }

        // The error log sits beside the settings: the real ones, or a --settings file's, so isolated runs and checks
        // never write to this PC's %LOCALAPPDATA% (v0.61.1).
        public static string LogFolder;
        public static string LogPath { get { return Path.Combine(LogFolder ?? Path.GetDirectoryName(SettingsStore.DefaultPath), "error.log"); } }
        public static void LogBeside(string settingsPath) { LogFolder = Path.GetDirectoryName(Path.GetFullPath(settingsPath)); }

        public static void Log(Exception error)
        {
            try
            {
                string path = LogPath; Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.WriteAllText(path, "");
                File.AppendAllText(path, DateTime.Now.ToString("s") + " " + error + Environment.NewLine);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}

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

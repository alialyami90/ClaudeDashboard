using System;
using System.IO;
using System.Linq;

namespace WidgetScreen
{
    // Phase 5 (decision 026): Claude Dashboard keeps its settings in %LOCALAPPDATA%\ClaudeDashboard, and Widget Screen
    // kept them in %LOCALAPPDATA%\WidgetScreen. On the first run with nothing in the new folder and something in the old
    // one, the app offers to bring over what still means something here: the Claude settings and the look, the Claude
    // sign-in (claude-session.dat) and the Claude Code bridge folder (claude\). If Widget Screen had connected Claude
    // Code, the connection is made again from the new folder, so settings.json's hooks and status line run the new
    // ClaudeHook.exe (Connect saves a dated backup first, as always). Widget Screen's own folder is only read, never
    // changed, so the old app keeps its files and the user can still go back. Declining saves fresh settings, so the
    // offer doesn't come back.
    public static class Migration
    {
        public const string OldFolderName = "WidgetScreen";
        public static string OldFolder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), OldFolderName); }
        }

        // The settings that carry over besides every Claude* one: they mean the same in both apps.
        public static readonly string[] SharedSettings = { "Accent", "Theme", "TextScale", "CornerRadius", "QuietMode", "Use24HourClock" };

        // What the old folder has, for the question and the result.
        public sealed class Found
        {
            public bool Settings, SignIn, Bridge, Connected;
            public bool Any { get { return Settings || SignIn || Bridge; } }
        }

        // Whether to ask: the new folder has no settings.xml yet, and the old folder has something to bring over. Never
        // for the old folder itself (a --settings path that points into it).
        public static bool ShouldOffer(string oldFolder, string newSettingsPath)
        {
            try
            {
                if (String.IsNullOrEmpty(oldFolder) || File.Exists(newSettingsPath)) return false;
                if (SameFolder(oldFolder, Path.GetDirectoryName(Path.GetFullPath(newSettingsPath)))) return false;
                return Look(oldFolder, null).Any;
            }
            catch (Exception) { return false; }
        }

        // What's in the old folder; Connected when Claude Code's settings.json (in configFolder) runs a ClaudeHook.exe.
        public static Found Look(string oldFolder, string configFolder)
        {
            var found = new Found();
            if (String.IsNullOrEmpty(oldFolder) || !Directory.Exists(oldFolder)) return found;
            found.Settings = File.Exists(Path.Combine(oldFolder, "settings.xml"));
            found.SignIn = File.Exists(Path.Combine(oldFolder, "claude-session.dat"));
            found.Bridge = Directory.Exists(Path.Combine(oldFolder, "claude"));
            if (configFolder != null) found.Connected = new ClaudeConnector(configFolder, Path.Combine(oldFolder, "claude")).IsConnected();
            return found;
        }

        // Copies the old folder's Claude files beside newSettingsPath and saves the merged settings; then, when Claude
        // Code was connected, connects it again from the new bridge folder with hookSource (the ClaudeHook.exe that
        // came with this app) and the telemetry port the old connection used (or port, or the default). Returns what it
        // found. Throws InvalidOperationException with a message for the user when the connection can't be made: the
        // files are copied by then, and Settings → Claude → Connect can try again.
        public static Found Run(string oldFolder, string newSettingsPath, string configFolder, string hookSource, int port = 0)
        {
            var found = Look(oldFolder, configFolder);
            string newFolder = Path.GetDirectoryName(Path.GetFullPath(newSettingsPath));
            Directory.CreateDirectory(newFolder);

            // The bridge folder first (sessions\, limits, the env record and the user's earlier status line), so the
            // connection below finds what Connect recorded and Disconnect can still take out exactly that.
            string oldBridge = Path.Combine(oldFolder, "claude"), newBridge = SettingsStore.ClaudePath(newSettingsPath);
            if (found.Bridge) CopyFolder(oldBridge, newBridge);
            // The sign-in is DPAPI-encrypted for this Windows user with the same entropy in both apps, so a copy works.
            if (found.SignIn) CopyFile(Path.Combine(oldFolder, "claude-session.dat"), SettingsStore.ClaudeSignInPath(newSettingsPath));

            var settings = new AppSettings();
            if (found.Settings)
            {
                string warning;
                var old = SettingsStore.Load(Path.Combine(oldFolder, "settings.xml"), out warning);
                if (warning == null) CopySettings(old, settings);
            }
            SettingsStore.Save(newSettingsPath, settings);

            if (found.Connected)
            {
                var connector = new ClaudeConnector(configFolder, newBridge);
                int oldPort = new ClaudeConnector(configFolder, oldBridge).ConfiguredPort();
                connector.Connect(hookSource, oldPort > 0 ? oldPort : port);
            }
            return found;
        }

        // Starts fresh: default settings in the new folder, so the offer isn't made again. The old folder stays as it was.
        public static void Decline(string newSettingsPath)
        {
            if (!File.Exists(newSettingsPath)) SettingsStore.Save(newSettingsPath, new AppSettings());
        }

        // Every Claude* setting and the shared look, from Widget Screen's settings (its other fields don't load here).
        public static void CopySettings(AppSettings from, AppSettings to)
        {
            foreach (var property in typeof(AppSettings).GetProperties())
            {
                if (!property.CanRead || !property.CanWrite) continue;
                if (!property.Name.StartsWith("Claude", StringComparison.Ordinal) && !SharedSettings.Contains(property.Name)) continue;
                property.SetValue(to, property.GetValue(from));
            }
            var copy = to.Copy();
            to.ClaudeProjects = copy.ClaudeProjects; to.ClaudeCollapsed = copy.ClaudeCollapsed; to.ClaudeFacts = copy.ClaudeFacts;
            to.Normalize();
        }

        // Copies a folder's files and subfolders; a file already in the new folder stays. A file Claude Code is
        // writing at that moment is skipped (the next hook writes it again).
        private static void CopyFolder(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from)) CopyFile(file, Path.Combine(to, Path.GetFileName(file)));
            foreach (var folder in Directory.GetDirectories(from)) CopyFolder(folder, Path.Combine(to, Path.GetFileName(folder)));
        }
        private static void CopyFile(string from, string to)
        {
            if (File.Exists(to)) return;
            try { File.Copy(from, to); } catch (IOException) { }
        }

        private static bool SameFolder(string a, string b)
        {
            return String.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }
    }
}

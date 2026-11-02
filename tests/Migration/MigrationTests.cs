using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WidgetScreen;

// Phase 5 (decision 026): the first run's offer to bring Widget Screen's Claude files over from %LOCALAPPDATA%\WidgetScreen
// to %LOCALAPPDATA%\ClaudeDashboard, and to point Claude Code's hooks at the new ClaudeHook.exe. Everything runs in
// throwaway folders; the PC's real settings and Claude Code's real settings.json are never touched.
internal static class MigrationTests
{
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static string Fresh(string directory, string name)
    {
        string path = Path.Combine(directory, "migration", name);
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
        return path;
    }
    // Every file under a folder with its bytes, to show a folder wasn't changed.
    private static Dictionary<string, string> Snapshot(string folder)
    {
        return Directory.GetFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(f => f.Substring(folder.Length), f => Convert.ToBase64String(File.ReadAllBytes(f)));
    }
    private static bool Same(Dictionary<string, string> a, Dictionary<string, string> b) { return a.Count == b.Count && a.All(p => b.ContainsKey(p.Key) && b[p.Key] == p.Value); }
    private static IEnumerable<string> HookCommands(JsonObject root)
    {
        var hooks = ClaudeJson.Obj(root, "hooks");
        if (hooks == null) yield break;
        foreach (var pair in hooks)
            foreach (var group in (pair.Value as List<object> ?? new List<object>()).OfType<JsonObject>())
                foreach (var hook in (ClaudeJson.List(group, "hooks") ?? new List<object>()).OfType<JsonObject>())
                    yield return ClaudeJson.Str(hook, "command");
    }

    // Widget Screen's settings.xml as v0.41 wrote it: Claude settings, the look, and fields this app no longer has.
    private const string OldSettings = "<?xml version=\"1.0\"?>\n<AppSettings xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">\n" +
        "  <Version>1</Version>\n  <Accent>Glacier</Accent>\n  <Theme>Warm</Theme>\n  <CornerRadius>12</CornerRadius>\n  <TextScale>1.2</TextScale>\n  <QuietMode>true</QuietMode>\n" +
        "  <PrayerCity>Riyadh</PrayerCity>\n  <TrayMode>true</TrayMode>\n  <Widgets><Widget Kind=\"Weather\" /></Widgets>\n" +
        "  <ClaudeChime>true</ClaudeChime>\n  <ClaudeToast>false</ClaudeToast>\n  <ClaudeHideIdle>true</ClaudeHideIdle>\n  <ClaudeShowNewProjects>false</ClaudeShowNewProjects>\n" +
        "  <ClaudeProjects>\n    <Project Folder=\"C:\\dev\\widgets\" Shown=\"true\" />\n    <Project Folder=\"C:\\dev\\old\" Shown=\"false\" />\n  </ClaudeProjects>\n" +
        "  <ClaudeCardSeconds>5</ClaudeCardSeconds>\n  <ClaudePlanSeconds>600</ClaudePlanSeconds>\n  <ClaudeCollapsed>\n    <Folder>C:\\dev\\widgets</Folder>\n  </ClaudeCollapsed>\n  <ClaudePeriod>Week</ClaudePeriod>\n" +
        "</AppSettings>\n";

    // An old folder with settings, a sign-in and a bridge folder holding a session file.
    private static string OldFolder(string directory, string name)
    {
        string old = Fresh(directory, name);
        File.WriteAllText(Path.Combine(old, "settings.xml"), OldSettings);
        File.WriteAllBytes(Path.Combine(old, "claude-session.dat"), new byte[] { 1, 2, 3, 4, 5 });
        string sessions = ClaudeBridge.SessionsFolder(Path.Combine(old, "claude"));
        Directory.CreateDirectory(sessions);
        File.WriteAllText(Path.Combine(sessions, "abc.json"), "{\"session_id\":\"abc\"}");
        File.WriteAllText(Path.Combine(old, "claude", "limits.json"), "{}");
        return old;
    }

    public static void Run(string directory, Action<string, Action> check)
    {
        string hook = Path.Combine(directory, ClaudeBridge.HookExe);

        check("The first run offers to bring Widget Screen's files over only when there's something to bring", () => {
            string empty = Fresh(directory, "offer-empty"), target = Fresh(directory, "offer-new");
            string newSettings = Path.Combine(target, "settings.xml");
            Assert(!Migration.ShouldOffer(empty, newSettings), "Offered with nothing in the old folder.");
            Assert(!Migration.ShouldOffer(Path.Combine(directory, "migration", "missing"), newSettings), "Offered with no old folder.");
            string old = OldFolder(directory, "offer-old");
            Assert(Migration.ShouldOffer(old, newSettings), "Not offered with Widget Screen's settings there.");
            Assert(!Migration.ShouldOffer(old, Path.Combine(old, "settings.xml")), "Offered to copy a folder onto itself.");
            File.WriteAllText(newSettings, "<AppSettings />");
            Assert(!Migration.ShouldOffer(old, newSettings), "Offered again once the new folder has settings.");
            string signInOnly = Fresh(directory, "offer-signin");
            File.WriteAllBytes(Path.Combine(signInOnly, "claude-session.dat"), new byte[] { 9 });
            Assert(Migration.ShouldOffer(signInOnly, Path.Combine(Fresh(directory, "offer-new2"), "settings.xml")), "Not offered for a sign-in alone.");
        });

        check("Bringing Widget Screen's files over keeps the Claude settings, the sign-in and the bridge, and leaves the old folder as it was", () => {
            string old = OldFolder(directory, "copy-old"), target = Fresh(directory, "copy-new");
            string config = Fresh(directory, "copy-config");
            string newSettings = Path.Combine(target, "settings.xml");
            var before = Snapshot(old);
            var found = Migration.Run(old, newSettings, config, hook);
            Assert(found.Settings && found.SignIn && found.Bridge && !found.Connected, "Didn't find what the old folder has.");
            string warning;
            var settings = SettingsStore.Load(newSettings, out warning);
            Assert(warning == null, "The new settings don't load: " + warning);
            Assert(settings.ClaudeChime && !settings.ClaudeToast && settings.ClaudeHideIdle && !settings.ClaudeShowNewProjects, "A Claude switch didn't carry over.");
            Assert(settings.ClaudeCardSeconds == 5 && settings.ClaudePlanSeconds == 600 && settings.ClaudePeriod == "Week", "A Claude interval or the period didn't carry over.");
            Assert(settings.ClaudeProjects.Length == 2 && settings.ClaudeProjects[0].Folder == @"C:\dev\widgets" && settings.ClaudeProjects[0].Shown && !settings.ClaudeProjects[1].Shown, "The project choices didn't carry over.");
            Assert(settings.ClaudeCollapsed.SequenceEqual(new[] { @"C:\dev\widgets" }), "The folded projects didn't carry over.");
            Assert(settings.Theme == "Warm" && settings.Accent == "Glacier" && settings.TextScale == 1.2 && settings.CornerRadius == 12 && settings.QuietMode, "The look didn't carry over.");
            Assert(File.ReadAllText(newSettings).IndexOf("PrayerCity", StringComparison.Ordinal) < 0, "A Widget Screen-only field came over.");
            Assert(File.ReadAllBytes(SettingsStore.ClaudeSignInPath(newSettings)).SequenceEqual(new byte[] { 1, 2, 3, 4, 5 }), "The sign-in wasn't copied as it was.");
            string bridge = SettingsStore.ClaudePath(newSettings);
            Assert(File.Exists(Path.Combine(ClaudeBridge.SessionsFolder(bridge), "abc.json")) && File.Exists(Path.Combine(bridge, "limits.json")), "The bridge folder wasn't copied.");
            Assert(Same(before, Snapshot(old)), "The old folder was changed.");
            Assert(!File.Exists(Path.Combine(config, "settings.json")), "Claude Code's settings were written without a connection.");
            Assert(!Migration.ShouldOffer(old, newSettings), "The offer would come back after bringing the files over.");
        });

        check("Bringing the files over points Claude Code's hooks at the new ClaudeHook.exe and changes nothing else", () => {
            string old = OldFolder(directory, "hooks-old"), target = Fresh(directory, "hooks-new");
            string config = Fresh(directory, "hooks-config");
            string oldBridge = Path.Combine(old, "claude"), newSettings = Path.Combine(target, "settings.xml"), newBridge = SettingsStore.ClaudePath(newSettings);
            var oldConnector = new ClaudeConnector(config, oldBridge);
            string original = "{\n  \"model\": \"opus\",\n  \"hooks\": {\n    \"PostToolUse\": [\n      {\n        \"matcher\": \"Edit\",\n        \"hooks\": [\n          {\n            \"type\": \"command\",\n            \"command\": \"npx prettier --write\"\n          }\n        ]\n      }\n    ]\n  },\n  \"statusLine\": {\n    \"type\": \"command\",\n    \"command\": \"~/.claude/my-line.sh\",\n    \"padding\": 2\n  },\n  \"enabledPlugins\": {\n    \"a@b\": true\n  }\n}\n";
            File.WriteAllText(oldConnector.SettingsPath, original);
            // Widget Screen connected on a port other than the default, and a hook it ran synchronously (package 4's
            // ask entry) was added beside its own.
            oldConnector.Connect(hook, 43834);
            var connected = (JsonObject)ClaudeJson.Parse(File.ReadAllText(oldConnector.SettingsPath));
            var ask = new JsonObject(); ask["type"] = "command"; ask["command"] = oldConnector.Command("ask"); ask["timeout"] = new JsonNumber(135);
            var group = new JsonObject(); group["matcher"] = "*"; group["hooks"] = new List<object> { ask };
            ((List<object>)ClaudeJson.Obj(connected, "hooks").Get("PermissionRequest")).Add(group);
            File.WriteAllText(oldConnector.SettingsPath, ClaudeJson.Write(connected) + "\n");
            string oldPath = oldBridge.Replace('\\', '/');
            Assert(HookCommands(connected).Count(c => c.Contains(oldPath)) > ClaudeConnector.Events.Length, "The fixture isn't connected to the old folder.");
            int backupsBefore = oldConnector.DatedBackups().Count;

            var found = Migration.Run(old, newSettings, config, hook);
            Assert(found.Connected, "The old connection wasn't seen.");
            var root = (JsonObject)ClaudeJson.Parse(File.ReadAllText(oldConnector.SettingsPath));
            var newConnector = new ClaudeConnector(config, newBridge);
            Assert(!File.ReadAllText(oldConnector.SettingsPath).Contains(oldPath), "settings.json still runs a hook in Widget Screen's folder.");
            Assert(File.Exists(newConnector.HookPath) && newConnector.IsConnected() && !newConnector.NeedsUpdate(), "The new bridge isn't connected, or asks for an update.");
            foreach (var e in ClaudeConnector.Events)
                Assert(((List<object>)ClaudeJson.Obj(root, "hooks").Get(e[0])).OfType<JsonObject>().Count(g => ClaudeJson.List(g, "hooks").Any(h => ClaudeJson.Str(h as JsonObject, "command") == newConnector.Command("hook"))) == 1, "No new hook for " + e[0] + ".");
            Assert(ClaudeJson.Str(ClaudeJson.Obj(root, "statusLine"), "command") == newConnector.Command("statusline") && ClaudeJson.Num(ClaudeJson.Obj(root, "statusLine"), "padding") == 2, "The status line doesn't run the new bridge.");
            Assert(ClaudeJson.Str(ClaudeJson.Obj(root, "env"), ClaudeConnector.Endpoint) == "http://127.0.0.1:43834", "The telemetry port changed.");
            Assert(root.Select(p => p.Key).SequenceEqual(new[] { "model", "hooks", "statusLine", "enabledPlugins", "env" }), "Keys moved.");
            Assert(HookCommands(root).Contains("npx prettier --write") && ClaudeJson.Str(root, "model") == "opus", "The user's own settings changed.");
            Assert(oldConnector.DatedBackups().Count == backupsBefore + 1, "No dated backup before the rewrite.");
            // Disconnect from the new folder puts the file back as it was before Widget Screen ever connected: the env
            // record and the user's earlier status line came over with the bridge.
            newConnector.Disconnect();
            Assert(File.ReadAllText(newConnector.SettingsPath) == original, "Disconnect after moving didn't put settings.json back as it was.");
        });

        check("Declining starts fresh and leaves Widget Screen's folder and Claude Code's settings alone", () => {
            string old = OldFolder(directory, "decline-old"), target = Fresh(directory, "decline-new");
            string config = Fresh(directory, "decline-config");
            new ClaudeConnector(config, Path.Combine(old, "claude")).Connect(hook);
            string settingsJson = File.ReadAllText(Path.Combine(config, "settings.json"));
            var before = Snapshot(old);
            string newSettings = Path.Combine(target, "settings.xml");
            Migration.Decline(newSettings);
            string warning;
            var settings = SettingsStore.Load(newSettings, out warning);
            Assert(warning == null && settings.Theme == "Midnight" && !settings.ClaudeChime && settings.ClaudeProjects == null, "Declining didn't save fresh settings.");
            Assert(!File.Exists(SettingsStore.ClaudeSignInPath(newSettings)) && !Directory.Exists(SettingsStore.ClaudePath(newSettings)), "Declining copied files.");
            Assert(Same(before, Snapshot(old)) && File.ReadAllText(Path.Combine(config, "settings.json")) == settingsJson, "Declining changed the old folder or Claude Code's settings.");
            Assert(!Migration.ShouldOffer(old, newSettings), "The offer would come back after declining.");
        });

        check("The first run's question says what comes over and what Start fresh leaves", () => {
            var all = new Migration.Found { Settings = true, SignIn = true, Bridge = true, Connected = true };
            string text = AppController.MoveDetail(all, @"C:\Users\ali\AppData\Local\WidgetScreen");
            Assert(text.StartsWith("Copies your Claude settings and theme, your Claude sign-in and Claude Code's sessions from C:\\Users\\ali\\AppData\\Local\\WidgetScreen.", StringComparison.Ordinal), "The list is wrong: " + text);
            Assert(text.Contains("backed up first") && text.Contains("quit Widget Screen, then uninstall it from Installed apps and keep its settings when asked") && text.EndsWith("until you connect it here.", StringComparison.Ordinal), "The connection or Start fresh isn't explained: " + text);
            string signIn = AppController.MoveDetail(new Migration.Found { SignIn = true }, "old");
            Assert(signIn.StartsWith("Copies your Claude sign-in from old.", StringComparison.Ordinal) && !signIn.Contains("backed up") && signIn.EndsWith("copies nothing.", StringComparison.Ordinal), "The sign-in-only words are wrong: " + signIn);
        });

        check("Settings live in %LOCALAPPDATA%\\ClaudeDashboard", () => {
            string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeDashboard", "settings.xml");
            Assert(String.Equals(SettingsStore.DefaultPath, expected, StringComparison.OrdinalIgnoreCase), "Settings path is " + SettingsStore.DefaultPath);
            Assert(Migration.OldFolder.EndsWith(@"\WidgetScreen", StringComparison.OrdinalIgnoreCase), "Widget Screen's folder is " + Migration.OldFolder);
        });
    }
}

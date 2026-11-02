using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace WidgetScreen
{
    // A project's tick in Settings → Claude → Projects, by its folder (docs/design/claude-watcher.md).
    public sealed class ClaudeProjectChoice
    {
        [XmlAttribute] public string Folder { get; set; }
        [XmlAttribute] public bool Shown { get; set; }
        public ClaudeProjectChoice Copy() { return (ClaudeProjectChoice)MemberwiseClone(); }
    }

    public sealed class AppSettings
    {
        public int Version { get; set; }
        public string Accent { get; set; }
        public string Theme { get; set; }
        public double CornerRadius { get; set; }
        public double TextScale { get; set; }
        public bool QuietMode { get; set; }
        public bool Use24HourClock { get; set; }
        // Claude Code (Settings → Claude): a chime when a session starts needing you, and which projects the card shows.
        // A project with no choice follows ClaudeShowNewProjects (on by default). The plan meters always show the plan.
        public bool ClaudeChime { get; set; }
        // A Windows notification when a session needs you, gets stuck or finishes a turn of 10 minutes or more (package 3;
        // on by default, silent in Quiet mode).
        public bool ClaudeToast { get; set; }
        // Package 4 (decision 027): Allow once and Deny for a permission dialog from the dashboard (off by default), and
        // how long Claude Code waits for that answer before showing its own dialog (10–120 s, default 30).
        public bool ClaudeAnswer { get; set; }
        public int ClaudeAnswerWait { get; set; }
        // Package 4: a small summary file per day, kept 8 weeks, for the plan row's sparkline and Settings → Claude →
        // History (on by default; turning it off deletes the files).
        public bool ClaudeHistory { get; set; }
        // Quiet mode stops other motion; the Claude cars keep moving unless this is set (Ali, 1 Nov 2026; decision 019).
        public bool ClaudeStillInQuiet { get; set; }
        // The ledger's "API value" (what the tokens would cost at API list prices): off by default, since a plan
        // doesn't pay those prices and Ali can't check them; replies show instead (decision 019).
        public bool ClaudeShowApiValue { get; set; }
        // Resolved and archived threads are hidden unless this is set ("Hide resolved and archived threads", on by default), and idle ones when
        // ClaudeHideIdle is (off by default). Hidden threads still count in their project's totals (Ali, 1 Nov 2026; decision 020).
        public bool ClaudeShowArchived { get; set; }
        public bool ClaudeHideIdle { get; set; }
        public bool ClaudeShowNewProjects { get; set; }
        [XmlArray("ClaudeProjects"), XmlArrayItem("Project")]
        public ClaudeProjectChoice[] ClaudeProjects { get; set; }
        // How often the card re-reads sessions, threads, telemetry totals and logs while it's on a page (default 2 s),
        // and how often the plan check asks Anthropic (default 300 s): the user's numbers, Quiet mode or not (Ali, 1 Nov 2026).
        public int ClaudeCardSeconds { get; set; }
        public int ClaudePlanSeconds { get; set; }
        // Project folders folded into their big critter on the card (the phone shares them), and the period the card
        // and the phone count threads and usage over: FiveHour, Today or Week.
        [XmlArray("ClaudeCollapsed"), XmlArrayItem("Folder")]
        public string[] ClaudeCollapsed { get; set; }
        public string ClaudePeriod { get; set; }
        // The four facts on the Claude card's side panel (package 2), from ClaudeFactKinds.All.
        [XmlArray("ClaudeFacts"), XmlArrayItem("Fact")]
        public string[] ClaudeFacts { get; set; }

        public AppSettings()
        {
            Version = 1;
            Accent = "Lime";
            Theme = "Midnight";
            CornerRadius = 20;
            TextScale = 1;
            Use24HourClock = false;
            ClaudeShowNewProjects = true;
            ClaudeToast = true;
            ClaudeAnswerWait = ClaudeBridge.DefaultWait; ClaudeHistory = true;
            ClaudeCardSeconds = ClaudeIntervals.CardDefault; ClaudePlanSeconds = ClaudeIntervals.PlanDefault;
            ClaudePeriod = "Today";
        }

        // A deep copy. Settings → Save starts from one and overwrites only the fields the panel owns, so everything
        // the panel doesn't show (Quiet mode, the Claude card's folds) survives a save.
        public AppSettings Copy()
        {
            var copy = (AppSettings)MemberwiseClone();
            copy.ClaudeProjects = ClaudeProjects == null ? null : ClaudeProjects.Where(c => c != null).Select(c => c.Copy()).ToArray();
            copy.ClaudeCollapsed = ClaudeCollapsed == null ? null : ClaudeCollapsed.ToArray();
            copy.ClaudeFacts = ClaudeFacts == null ? null : ClaudeFacts.ToArray();
            return copy;
        }

        public void Normalize()
        {
            // Up to 140% (audit, 22 Oct 2026): small or far-away screens need more than the 115% the baseline allowed.
            TextScale = Math.Max(0.9, Math.Min(1.4, Double.IsNaN(TextScale) ? 1 : TextScale));
            if (Accent != "Lime" && Accent != "Glacier" && Accent != "Lavender" && Accent != "Amber") Accent = "Lime";
            if (Theme != "Midnight" && Theme != "Slate" && Theme != "Warm") Theme = "Midnight";
            CornerRadius = Clamp(CornerRadius, 0, 24, 20);
            ClaudeAnswerWait = ClaudeAnswerWait == 0 ? ClaudeBridge.DefaultWait : ClaudeBridge.ClampWait(ClaudeAnswerWait);
            ClaudeCardSeconds = ClaudeCardSeconds == 0 ? ClaudeIntervals.CardDefault : ClaudeIntervals.Card(ClaudeCardSeconds);
            ClaudePlanSeconds = ClaudePlanSeconds == 0 ? ClaudeIntervals.PlanDefault : ClaudeIntervals.Plan(ClaudePlanSeconds);
            ClaudeCollapsed = (ClaudeCollapsed ?? new string[0]).Select(ClaudeUsage.Key).Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (ClaudePeriod != "FiveHour" && ClaudePeriod != "Today" && ClaudePeriod != "Week") ClaudePeriod = "Today";
            ClaudeFacts = ClaudeFactKinds.Clean(ClaudeFacts);
        }

        private static double Clamp(double value, double min, double max, double fallback)
        {
            return Double.IsNaN(value) || Double.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value));
        }
    }

    public static class SettingsStore
    {
        // The Claude Code bridge's folder (ClaudeHook.exe, sessions\, limits.json) beside settings.xml.
        public static string ClaudePath(string settingsPath) { return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath)), "claude"); }
        // The widget's own Claude sign-in (DPAPI-encrypted), beside settings.xml.
        public static string ClaudeSignInPath(string settingsPath) { return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath)), "claude-session.dat"); }
        public static string DefaultPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetScreen", "settings.xml"); }
        }

        public static AppSettings Load(string path, out string warning)
        {
            warning = null;
            if (!File.Exists(path)) return new AppSettings();
            try
            {
                using (var file = File.OpenRead(path))
                {
                    var settings = (AppSettings)new XmlSerializer(typeof(AppSettings)).Deserialize(file);
                    settings.Normalize();
                    return settings;
                }
            }
            catch (Exception e)
            {
                if (!(e is IOException) && !(e is InvalidOperationException) && !(e is UnauthorizedAccessException)) throw;
                warning = "Settings could not be read. Defaults are in use; the original file is preserved until you save.";
                return new AppSettings();
            }
        }

        public static void Save(string path, AppSettings settings)
        {
            settings.Normalize();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + ".tmp";
            using (var file = File.Create(temporary)) new XmlSerializer(typeof(AppSettings)).Serialize(file, settings);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }
}

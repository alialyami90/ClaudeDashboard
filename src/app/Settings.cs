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
        // Sticker Sheet inks (decision 021): "Focus=Coral;…" for widgets whose ink was changed in Edit layout, and
        // Settings → Theme → Widget colours: "Each" (every widget its own ink) or "Accent" (every ink is the accent).
        public string WidgetInks { get; set; }
        public string WidgetColours { get; set; }
        public string Theme { get; set; }
        public double CornerRadius { get; set; }
        public bool StartFullscreen { get; set; }
        // Taps on the dashboard leave the game or app in front focused (decision 013).
        public bool KeepGameFocus { get; set; }
        // The layout of the reference 1856 × 576 canvas (a 1920 × 720 screen), where it has always been.
        public WidgetPlacement[] WidgetLayout { get; set; }
        // Layouts saved on screens of other shapes, one per canvas size (docs/design/any-screen-size.md).
        public CanvasLayout[] CanvasLayouts { get; set; }
        // Pages: the fields above are the first page, named PageName; MorePages hold the rest. CurrentPage is the page
        // shown, on the PC and on a paired phone alike.
        public string PageName { get; set; }
        public LayoutPage[] MorePages { get; set; }
        public int CurrentPage { get; set; }
        public bool SnapWidgets { get; set; }
        public double TextScale { get; set; }
        public string Monitor { get; set; }
        public bool QuietMode { get; set; }
        public bool Use24HourClock { get; set; }
        // The one-time tips in the header were put away with Got it.
        public bool HintDismissed { get; set; }
        // Phone remote (Settings → Phone). Off until the user turns it on.
        public bool RemoteEnabled { get; set; }
        // Move the dashboard to the tray while a paired phone shows it (Settings → Phone).
        public bool HideToTrayWithPhone { get; set; }
        // Claude Code (Settings → Claude): a chime when a session starts needing you, and which projects the card shows.
        // A project with no choice follows ClaudeShowNewProjects (on by default). The plan meters always show the plan.
        public bool ClaudeChime { get; set; }
        // A Windows notification when a session needs you, gets stuck or finishes a turn of 10 minutes or more (package 3;
        // on by default, silent in Quiet mode).
        public bool ClaudeToast { get; set; }
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
            WidgetInks = ""; WidgetColours = Inks.EachOwn;
            Theme = "Midnight";
            CornerRadius = 20;
            StartFullscreen = true;
            KeepGameFocus = true;
            SnapWidgets = true;
            TextScale = 1;
            Use24HourClock = false;
            // Tray mode with the phone is opt-in (audit, 22 Oct 2026): the dashboard vanishing when the phone app opens
            // surprised more than it helped when both screens were in use.
            HideToTrayWithPhone = false;
            ClaudeShowNewProjects = true;
            ClaudeToast = true;
            ClaudeCardSeconds = ClaudeIntervals.CardDefault; ClaudePlanSeconds = ClaudeIntervals.PlanDefault;
            ClaudePeriod = "Today";
            Monitor = "";
        }

        // A deep copy. Settings → Save starts from one and overwrites only the fields the panel owns, so everything
        // the panel doesn't show (pages, the current page, the phone link, Quiet mode) survives a save.
        public AppSettings Copy()
        {
            var copy = (AppSettings)MemberwiseClone();
            copy.WidgetLayout = WidgetLayout == null ? null : WidgetScreen.WidgetLayout.Copy(WidgetLayout);
            copy.CanvasLayouts = CanvasLayouts == null ? null : CanvasLayouts.Where(c => c != null).Select(c => c.Copy()).ToArray();
            copy.ClaudeProjects = ClaudeProjects == null ? null : ClaudeProjects.Where(c => c != null).Select(c => c.Copy()).ToArray();
            copy.ClaudeCollapsed = ClaudeCollapsed == null ? null : ClaudeCollapsed.ToArray();
            copy.ClaudeFacts = ClaudeFacts == null ? null : ClaudeFacts.ToArray();
            copy.MorePages = MorePages == null ? null : MorePages.Where(p => p != null).Select(p => p.Copy()).ToArray();
            return copy;
        }

        public void Normalize()
        {
            // Up to 140% (audit, 22 Oct 2026): small or far-away screens need more than the 115% the baseline allowed.
            TextScale = Math.Max(0.9, Math.Min(1.4, Double.IsNaN(TextScale) ? 1 : TextScale));
            if (Accent != "Lime" && Accent != "Glacier" && Accent != "Lavender" && Accent != "Amber") Accent = "Lime";
            if (Theme != "Midnight" && Theme != "Slate" && Theme != "Warm") Theme = "Midnight";
            WidgetInks = Inks.Clean(WidgetInks); WidgetColours = Inks.Mode(WidgetColours);
            CornerRadius = Clamp(CornerRadius, 0, 24, 20);
            ClaudeCardSeconds = ClaudeCardSeconds == 0 ? ClaudeIntervals.CardDefault : ClaudeIntervals.Card(ClaudeCardSeconds);
            ClaudePlanSeconds = ClaudePlanSeconds == 0 ? ClaudeIntervals.PlanDefault : ClaudeIntervals.Plan(ClaudePlanSeconds);
            ClaudeCollapsed = (ClaudeCollapsed ?? new string[0]).Select(ClaudeUsage.Key).Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (ClaudePeriod != "FiveHour" && ClaudePeriod != "Today" && ClaudePeriod != "Week") ClaudePeriod = "Today";
            ClaudeFacts = ClaudeFactKinds.Clean(ClaudeFacts);
            var pages = WidgetScreen.WidgetLayout.Pages(this).Take(WidgetScreen.WidgetLayout.MaxPages).ToList();
            for (int i = 0; i < pages.Count; i++) WidgetScreen.WidgetLayout.NormalizePage(pages[i], this, i);
            WidgetScreen.WidgetLayout.SetPages(this, pages);
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

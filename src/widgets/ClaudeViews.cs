using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace ClaudeDashboard
{
    // The lap chart's views (decision 030, package B): the window's tab row picks one of six, in this order. Ctrl+1 to
    // Ctrl+6 choose them. Below 640 wide there are no tabs and the window shows Garage, as before v0.55.0.
    //
    // The view-host contract. The window owns one IClaudeView per tab and:
    //   1. calls Build(context, room) once the view is first shown, and again when the room or the text size changes;
    //      the element it returns is placed in the window's view area and is the view's whole content;
    //   2. calls Refresh(snapshot, now) with the snapshot the Garage card reads (ClaudeView), on the existing 1 s
    //      heartbeat and when a watcher fires. It is only called while Visible is true, and always after Build;
    //   3. sets Visible false when another tab is chosen or the window is minimised, and true when it shows again (a
    //      Refresh follows at once). A view starts no timers of its own; anything that animates stops while not
    //      Visible.
    // Views keep names, states and times only, never prompts, replies, commands or file contents (decision 030).
    //
    // A package adds its view as a public class named after the tab with "View" on the end (LiveView, LimitsView,
    // HistoryView, ReliabilityView, SourcesView) in namespace ClaudeDashboard, with a public constructor that takes
    // no arguments. ClaudeViews.Create finds it by that name, so no other file has to change; until it exists, the
    // tab shows a placeholder that says which version brings it. Garage is the existing ClaudeCard, hosted by the
    // window itself.
    public interface IClaudeView
    {
        // The tab's name: one of ClaudeViews.Names.
        string Name { get; }
        // Builds the view for the room it has (in units, after the text scale) and returns its root element.
        FrameworkElement Build(ClaudeViewContext context, Size room);
        // Fills the view in from the snapshot, at now (UTC). Never called before Build or while not Visible.
        void Refresh(ClaudeView snapshot, DateTime now);
        // Whether the view is on screen. Set by the window.
        bool Visible { get; set; }
    }

    // What the window hands every view when it builds: settings it shows by, and the actions it may ask for.
    public sealed class ClaudeViewContext
    {
        // Settings → Appearance → Text size (1 = 100%) and Settings → General → 24-hour clock.
        public double TextScale = 1;
        public bool Use24Hour;
        // The fixture is showing (--render-claude-sample, the checks): the view says Sample wherever it shows numbers.
        public bool Sample;
        // Brings a session's terminal or editor forward (as clicking a car does). Returns null when it worked, or what
        // went wrong, for the view to show. Never answers a permission prompt (decision 027).
        public Func<string, string> Focus = id => null;
        // Switches the window to another tab by name (a "See Limits" link, say).
        public Action<string> Show = name => { };
        // Runs one of Settings → Claude's actions by name, through the same path as its button there, confirmation
        // sheet included: Connect, Disconnect, Reconnect, UpdateConnection (these change Claude Code's settings.json,
        // with its backups), OpenLogs, CheckPlan, SignIn, ShowArchived, HideArchived. Returns null when it started or
        // worked, or what went wrong (an unknown name says so).
        public Func<string, string> Act = name => null;
        // A font size scaled with the text size, rounded as the card rounds it.
        public double Scaled(double size) { return Math.Round(size * TextScale, 1); }
    }

    public static class ClaudeViews
    {
        public const string Live = "Live", Garage = "Garage", Limits = "Limits", History = "History", Reliability = "Reliability", Sources = "Sources";
        // Tab order, which is also Ctrl+1 to Ctrl+6.
        public static readonly string[] Names = { Live, Garage, Limits, History, Reliability, Sources };
        // Tabs show from this window width (units); below it the window is the Garage card alone.
        public const double TabsFrom = 640;

        // The version each view lands in, for its placeholder (decision 030's packages C, D and E).
        public static string Coming(string name)
        {
            switch (name)
            {
                case Live: return "v0.56.0";
                case Limits: case History: return "v0.57.0";
                case Reliability: case Sources: return "v0.58.0";
                default: return "";
            }
        }

        public static string Clean(string name) { return Names.FirstOrDefault(n => String.Equals(n, name, StringComparison.OrdinalIgnoreCase)); }

        // The view for a tab other than Garage: its package's class if it's in this build, else a placeholder.
        public static IClaudeView Create(string name)
        {
            name = Clean(name);
            if (name == null || name == Garage) return null;
            var type = typeof(ClaudeViews).Assembly.GetType("ClaudeDashboard." + name + "View", false);
            if (type != null && typeof(IClaudeView).IsAssignableFrom(type) && !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) != null)
            {
                try { return (IClaudeView)Activator.CreateInstance(type); } catch (Exception) { }
            }
            return new PlaceholderView(name);
        }
    }

    // "Live is coming in v0.56.0": a calm, honest stand-in until a view's package merges.
    public sealed class PlaceholderView : IClaudeView
    {
        private readonly string name;
        public PlaceholderView(string name) { this.name = name; }
        public string Name { get { return name; } }
        public bool Visible { get; set; }
        public FrameworkElement Build(ClaudeViewContext context, Size room)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 420 };
            var title = CardParts.Text(name, context.Scaled(20), "Text"); title.HorizontalAlignment = HorizontalAlignment.Center; title.FontWeight = FontWeights.SemiBold;
            string version = ClaudeViews.Coming(name);
            var words = CardParts.Text(version.Length > 0 ? "Coming in " + version + "." : "Not in this build.", context.Scaled(14), "Muted");
            words.HorizontalAlignment = HorizontalAlignment.Center; words.TextAlignment = TextAlignment.Center; words.TextWrapping = TextWrapping.Wrap; words.Margin = new Thickness(0, 8, 0, 0);
            stack.Children.Add(title); stack.Children.Add(words);
            System.Windows.Automation.AutomationProperties.SetName(stack, name + ", coming in " + version);
            return stack;
        }
        public void Refresh(ClaudeView snapshot, DateTime now) { }
    }
}

using System;
using System.Windows;
using System.Windows.Media;

namespace ClaudeDashboard
{
    // Claude Dashboard's palettes (phase 4 of the transformation plan): Midnight, Slate and Warm from the widget app,
    // and Claude, a light one in the spirit of the Claude app (warm paper, soft black, terracotta). Every window of the
    // app takes the same keys, so the pit wall, the header and the Settings window change together, and Settings
    // previews a palette by applying it and Cancel by applying the saved one again. Amber stays the warning colour in
    // every palette (darker on paper, so it reads as text), Turn is the Glacier edge for a session waiting for your
    // reply, and OnAccent and OnWarning are the words on an accent or Amber fill.
    public static class Palettes
    {
        public static readonly string[] Themes = { "Midnight", "Slate", "Warm", "Claude" };
        public static readonly string[] Accents = { "Lime", "Glacier", "Lavender", "Amber", "Terracotta" };
        // The brushes every palette sets, in the order of the colour lists below.
        public static readonly string[] Keys = { "Page", "Surface", "Raised", "Input", "Track", "ControlBorder", "Selected", "Muted", "Text", "ClockSurface", "ClockBorder", "ClockMuted", "Warning", "Turn", "OnAccent", "Scrim", "OnWarning" };

        public static string CleanTheme(string theme) { return Array.IndexOf(Themes, theme) >= 0 ? theme : "Midnight"; }
        public static string CleanAccent(string accent) { return Array.IndexOf(Accents, accent) >= 0 ? accent : "Lime"; }
        // Claude is the one light palette.
        public static bool Light(string theme) { return theme == "Claude"; }
        // The accent a palette starts with when it's picked: terracotta on paper, the user's own otherwise.
        public static string DefaultAccent(string theme) { return Light(theme) ? "Terracotta" : "Lime"; }

        public static string[] Colors(string theme)
        {
            switch (CleanTheme(theme))
            {
                case "Slate": return new[] { "#101923", "#1B2938", "#293C50", "#142130", "#40546A", "#4C637C", "#3A536E", "#B6C7DA", "#F0F6FF", "#1E3341", "#476175", "#B6CDD9", "#F5C47D", "#8ED8F8", "#11170E", "#E8101923", "#11170E" };
                case "Warm": return new[] { "#181411", "#27211D", "#3B3028", "#1F1915", "#544638", "#665344", "#564534", "#C8B8A6", "#FAF3E9", "#30291F", "#61533C", "#D2C4A8", "#F5C47D", "#8ED8F8", "#11170E", "#E8181411", "#11170E" };
                // Warm paper, soft black text and quiet borders; Amber and Glacier darkened so they read on paper.
                case "Claude": return new[] { "#F0EEE6", "#FAF9F5", "#EBE7DC", "#FFFFFF", "#D8D2C4", "#D6D0C2", "#E3DCCB", "#65615A", "#1F1E1D", "#F4F1E8", "#D6D0C2", "#65615A", "#9E5A0A", "#27709A", "#FFFFFF", "#E0F0EEE6", "#FFFFFF" };
                default: return new[] { "#0C0E13", "#161A22", "#252B36", "#10141C", "#353D49", "#343C49", "#394252", "#A0AABB", "#F2F4F8", "#1C2420", "#344235", "#A8BAAA", "#F5C47D", "#8ED8F8", "#11170E", "#E80C0E13", "#11170E" };
            }
        }

        // The accent's colour on this palette: the widget app's four (and terracotta) on the dark ones, deeper shades on
        // paper so text and check marks in the accent stay readable.
        public static string AccentColor(string theme, string accent)
        {
            bool light = Light(theme);
            switch (CleanAccent(accent))
            {
                case "Glacier": return light ? "#27709A" : "#8ED8F8";
                case "Lavender": return light ? "#6E55C9" : "#C5AEFF";
                case "Amber": return light ? "#A15C10" : "#F5C47D";
                case "Terracotta": return light ? "#B5562F" : "#E08A68";
                default: return light ? "#4E7D1E" : "#BAF28B";
            }
        }

        // Puts a palette, its accent and the text size into one window's resources.
        public static void Apply(ResourceDictionary resources, string theme, string accent, double textScale)
        {
            var colors = Colors(theme);
            for (int i = 0; i < Keys.Length; i++) resources[Keys[i]] = Brush(colors[i]);
            // On paper the accent fills buttons with white text; on the dark palettes with the dark ink, as before.
            resources["Accent"] = Brush(AccentColor(theme, accent));
            double scale = Double.IsNaN(textScale) || textScale <= 0 ? 1 : Math.Max(0.9, Math.Min(1.4, textScale));
            resources["SmallSize"] = 13.0 * scale;
            resources["BodySize"] = 15.0 * scale;
            resources["TitleSize"] = 20.0 * scale;
            resources["CardRadius"] = new CornerRadius(16);
        }

        public static SolidColorBrush Brush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        // Relative luminance (WCAG), for the checks: paper is lighter than its text, the dark palettes the other way.
        public static double Luminance(string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            Func<byte, double> channel = v => { double x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); };
            return 0.2126 * channel(c.R) + 0.7152 * channel(c.G) + 0.0722 * channel(c.B);
        }
        public static double Contrast(string first, string second)
        {
            double a = Luminance(first), b = Luminance(second);
            return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
        }
    }
}

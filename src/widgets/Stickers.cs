using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WidgetScreen
{
    // What is left of the Sticker Sheet (decision 017) for the Claude card: its label strip. Colours come from resources,
    // so palettes and accents apply live: "InkOfClaude" is the strip's Coral and "InkText" the dark text on it
    // (DashboardController.ApplySettings).
    public static class Stickers
    {
        public const double StripHeight = 28, StickerHeight = 24, StickerBorder = 1.5, Keyline = 3, Lift = 4, MaxTilt = 3;
        public static string InkKey(string id) { return "InkOf" + id; }
        public static readonly FontFamily Segoe = new FontFamily("Segoe UI");
        public static readonly FontFamily Glyphs = new FontFamily("Segoe MDL2 Assets");
    }

    // Letter-spaced capitals. WPF's TextBlock has no letter spacing, so each character is laid out once with
    // FormattedText and drawn at its spaced place; it ends in "…" when it doesn't fit.
    public sealed class SpacedText : FrameworkElement
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(string), typeof(SpacedText), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, Changed));
        public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register("FontSize", typeof(double), typeof(SpacedText), new FrameworkPropertyMetadata(13.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, Changed));
        public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register("Foreground", typeof(Brush), typeof(SpacedText), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, Changed));
        public string Text { get { return (string)GetValue(TextProperty); } set { SetValue(TextProperty, value); } }
        public double FontSize { get { return (double)GetValue(FontSizeProperty); } set { SetValue(FontSizeProperty, value); } }
        public Brush Foreground { get { return (Brush)GetValue(ForegroundProperty); } set { SetValue(ForegroundProperty, value); } }
        private FontWeight weight = FontWeights.Normal; private double spacing;
        public FontWeight FontWeight { get { return weight; } set { weight = value; laid = null; InvalidateMeasure(); } }
        // Extra space after each character, in ems.
        public double Spacing { get { return spacing; } set { spacing = value; laid = null; InvalidateMeasure(); } }

        private FormattedText[] laid; private FormattedText ellipsis; private double height;
        private static void Changed(DependencyObject owner, DependencyPropertyChangedEventArgs e) { ((SpacedText)owner).laid = null; }

        private void Lay()
        {
            if (laid != null) return;
            string text = Text ?? ""; double dip = 1;
            try { dip = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch (Exception) { }
            var face = new Typeface(Stickers.Segoe, FontStyles.Normal, weight, FontStretches.Normal);
            Brush brush = Foreground ?? Brushes.Black;
            laid = new FormattedText[text.Length];
            for (int i = 0; i < text.Length; i++) laid[i] = new FormattedText(text[i].ToString(), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, FontSize, brush, dip);
            ellipsis = new FormattedText("…", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, FontSize, brush, dip);
            height = ellipsis.Height;
        }
        private double Gap { get { return spacing * FontSize; } }
        public double NaturalWidth
        {
            get { Lay(); double width = 0; foreach (var c in laid) width += c.WidthIncludingTrailingWhitespace + Gap; return Math.Ceiling(Math.Max(0, width - Gap)); }
        }
        protected override Size MeasureOverride(Size available)
        {
            Lay();
            return new Size(Math.Min(NaturalWidth, available.Width), height);
        }
        protected override void OnRender(DrawingContext context)
        {
            Lay();
            double room = ActualWidth + 0.5, x = 0; bool fits = NaturalWidth <= room;
            double stop = fits ? room : room - ellipsis.WidthIncludingTrailingWhitespace;
            foreach (var c in laid)
            {
                double advance = c.WidthIncludingTrailingWhitespace;
                if (x + advance > stop) break;
                context.DrawText(c, new Point(x, 0)); x += advance + Gap;
            }
            if (!fits) context.DrawText(ellipsis, new Point(x, 0));
        }
    }

    // The label strip that names a card (replaces the grey uppercase title): a 28-unit pill in the widget's ink with the
    // name in Segoe UI Black 13, letter-spaced 0.09 em, in dark text. Under 200 units wide it folds to a 12-unit ink
    // dot and the short name. The strip draws in a host Border, so XAML can place it and keep its name.
    public sealed class LabelStrip
    {
        public readonly Border Host;
        public readonly string Id;
        private readonly Ellipse dot;
        private readonly SpacedText text;
        public bool IsCompact { get; private set; }
        public SpacedText Words { get { return text; } }

        public LabelStrip(Border host, string id)
        {
            Host = host; Id = id;
            host.Height = Stickers.StripHeight; host.CornerRadius = new CornerRadius(Stickers.StripHeight / 2);
            if (host.ReadLocalValue(FrameworkElement.HorizontalAlignmentProperty) == DependencyProperty.UnsetValue) host.HorizontalAlignment = HorizontalAlignment.Left;
            host.VerticalAlignment = VerticalAlignment.Center; host.SnapsToDevicePixels = true;
            dot = new Ellipse { Width = 12, Height = 12, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            dot.SetResourceReference(Shape.FillProperty, Stickers.InkKey(id));
            text = new SpacedText { FontWeight = FontWeights.Black, Spacing = 0.09, VerticalAlignment = VerticalAlignment.Center };
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(dot); row.Children.Add(text);
            host.Child = row;
            AutomationProperties.SetName(host, Name(id));
            Fit(Double.PositiveInfinity, 1);
        }
        public static LabelStrip Create(string id) { return new LabelStrip(new Border(), id); }
        // A strip narrower than this shows the ink dot beside the name.
        public const double CompactBelow = 200;
        private static string Name(string id) { return id == "Claude" ? "Claude Code" : id; }

        // The strip's text follows the text size (up to 140%) and stays inside its 28-unit pill.
        public static double TextSize(double textScale) { return Math.Round(13 * Math.Max(0.9, Math.Min(1.4, Double.IsNaN(textScale) ? 1 : textScale)) * 2) / 2; }

        public void Fit(double cardWidth, double textScale)
        {
            IsCompact = cardWidth < CompactBelow;
            text.Text = Name(Id).ToUpperInvariant();
            text.FontSize = TextSize(textScale);
            dot.Visibility = IsCompact ? Visibility.Visible : Visibility.Collapsed;
            Host.Padding = IsCompact ? new Thickness(0) : new Thickness(12, 0, 12, 0);
            if (IsCompact) { Host.Background = null; text.SetResourceReference(SpacedText.ForegroundProperty, "Text"); }
            else { Host.SetResourceReference(Border.BackgroundProperty, Stickers.InkKey(Id)); text.SetResourceReference(SpacedText.ForegroundProperty, "InkText"); }
        }
    }
}

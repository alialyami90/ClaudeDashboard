using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WidgetScreen
{
    // Drawn parts shared by the Claude card's designs (docs/design/claude-watcher.md). They draw only when a reading
    // changes; nothing animates. Brushes are set with SetResourceReference so palettes and accents apply live.

    // A thin rounded bar for a percentage.
    public sealed class MeterBar : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register("Value", typeof(double), typeof(MeterBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty TrackProperty = DependencyProperty.Register("Track", typeof(Brush), typeof(MeterBar), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(MeterBar), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public double Value { get { return (double)GetValue(ValueProperty); } set { SetValue(ValueProperty, value); } }
        public Brush Track { get { return (Brush)GetValue(TrackProperty); } set { SetValue(TrackProperty, value); } }
        public Brush Fill { get { return (Brush)GetValue(FillProperty); } set { SetValue(FillProperty, value); } }

        protected override void OnRender(DrawingContext context)
        {
            double width = ActualWidth, height = ActualHeight, radius = height / 2;
            if (width <= 0 || height <= 0) return;
            if (Track != null) context.DrawRoundedRectangle(Track, null, new Rect(0, 0, width, height), radius, radius);
            double part = Math.Max(0, Math.Min(100, Double.IsNaN(Value) ? 0 : Value)) / 100;
            if (part > 0 && Fill != null) context.DrawRoundedRectangle(Fill, null, new Rect(0, 0, Math.Max(height, width * part), height), radius, radius);
        }
    }

    // A chunky meter in ten segments (Claude Code's plan limits): 4-unit gaps, radius 3, a segment lit for each 10%.
    public sealed class SegmentBar : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register("Value", typeof(double), typeof(SegmentBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty TrackProperty = DependencyProperty.Register("Track", typeof(Brush), typeof(SegmentBar), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(SegmentBar), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public double Value { get { return (double)GetValue(ValueProperty); } set { SetValue(ValueProperty, value); } }
        public Brush Track { get { return (Brush)GetValue(TrackProperty); } set { SetValue(TrackProperty, value); } }
        public Brush Fill { get { return (Brush)GetValue(FillProperty); } set { SetValue(FillProperty, value); } }
        public const int Segments = 10;
        // Segments lit for a percentage: any use lights the first, and a full one only at 100%.
        public static int Lit(double percent) { if (Double.IsNaN(percent) || percent <= 0) return 0; return Math.Max(1, Math.Min(Segments, (int)Math.Floor(percent / 10 + 0.0001))); }

        protected override void OnRender(DrawingContext context)
        {
            double gap = 4, width = (ActualWidth - gap * (Segments - 1)) / Segments, height = ActualHeight;
            if (width <= 1 || height <= 0) return;
            int lit = Lit(Value); double radius = Math.Min(3, height / 2);
            for (int i = 0; i < Segments; i++)
            {
                var brush = i < lit ? Fill : Track;
                if (brush != null) context.DrawRoundedRectangle(brush, null, new Rect(i * (width + gap), 0, width, height), radius, radius);
            }
        }
    }

    public abstract class CardParts
    {
        // Large readings: Segoe UI Light with tabular figures and a line only as tall as the digits need.
        public static TextBlock Reading(string text, double size)
        {
            var block = new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.Light, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, LineHeight = Math.Round(size * 1.12) };
            Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
            TextOptions.SetTextFormattingMode(block, TextFormattingMode.Ideal);
            return block;
        }
        public static TextBlock Text(string text, string sizeKey, string brush)
        {
            var block = new TextBlock { Text = text };
            block.SetResourceReference(TextBlock.FontSizeProperty, sizeKey); block.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return block;
        }
        public static TextBlock Text(string text, double size, string brush)
        {
            var block = new TextBlock { Text = text, FontSize = size };
            block.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return block;
        }
        // The card heading style ("SYSTEM STATS"): SmallSize, semibold, muted.
        public static TextBlock Label(string text) { var block = Text(text, "SmallSize", "Muted"); block.FontWeight = FontWeights.SemiBold; return block; }
        public static TextBlock Caption(string text) { return Text(text, 13, "Muted"); }
        public static Border Tile(UIElement child, double radius, Thickness padding)
        {
            var tile = new Border { CornerRadius = new CornerRadius(radius), Padding = padding, Child = child };
            tile.SetResourceReference(Border.BackgroundProperty, "Raised");
            return tile;
        }
        public static T At<T>(T element, int row, int column = 0, int columnSpan = 1) where T : UIElement
        {
            Grid.SetRow(element, row); Grid.SetColumn(element, column); if (columnSpan > 1) Grid.SetColumnSpan(element, columnSpan);
            return element;
        }
        public static Grid Rows(params GridLength[] heights)
        {
            var grid = new Grid(); foreach (var height in heights) grid.RowDefinitions.Add(new RowDefinition { Height = height });
            return grid;
        }
        public static Grid Columns(params GridLength[] widths)
        {
            var grid = new Grid(); foreach (var width in widths) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
            return grid;
        }
        public static readonly GridLength Star = new GridLength(1, GridUnitType.Star), Auto = GridLength.Auto;
    }

    // Parts of a built design that tests and the controller find by name; names are re-registered on each rebuild.
    public sealed class PartNames
    {
        private readonly FrameworkElement owner;
        private readonly List<string> names = new List<string>();
        public PartNames(FrameworkElement owner) { this.owner = owner; }
        public T Add<T>(T element, string name) where T : FrameworkElement
        {
            var scope = NameScope.GetNameScope(owner);
            if (scope == null) return element;
            if (scope.FindName(name) != null) scope.UnregisterName(name);
            scope.RegisterName(name, element); names.Add(name);
            return element;
        }
        public void Clear()
        {
            var scope = NameScope.GetNameScope(owner);
            if (scope != null) foreach (string name in names) if (scope.FindName(name) != null) scope.UnregisterName(name);
            names.Clear();
        }
    }
}

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace WidgetScreen
{
    // The permission notification with buttons (package 4, decision 027). The notification-area balloon can't carry
    // buttons, so this is a small window of our own in the corner of the main screen: Allow once, Deny, or Open (Claude
    // Code shows its own dialog at once and the session's window comes forward). It stays on top, never takes the focus
    // from the game or app in front, and counts down to the moment Claude Code stops waiting and asks in its own window.
    public sealed class ClaudeAskToast : Window
    {
        public const double ToastWidth = 400, Gap = 12;
        public readonly ClaudeAsk Ask;
        private readonly TextBlock countdown;
        private readonly Action<string> answered;
        private bool done;

        // resource finds a brush by its key in the dashboard's palette (Surface, Raised, Text, Muted, ControlBorder,
        // Accent); answered gets ClaudeBridge.Allow, Deny or OpenIt once.
        public ClaudeAskToast(ClaudeAsk ask, string thread, Func<string, object> resource, Action<string> answered)
        {
            Ask = ask; this.answered = answered;
            Title = "Claude needs your permission";
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = false; ShowActivated = false;
            SizeToContent = SizeToContent.Height; Width = ToastWidth;
            Brush surface = Pick(resource, "Surface", "#161A22"), raised = Pick(resource, "Raised", "#252B36"), text = Pick(resource, "Text", "#F2F4F8"),
                muted = Pick(resource, "Muted", "#A0AABB"), border = Pick(resource, "ControlBorder", "#343C49"), accent = Pick(resource, "Accent", "#B5E853");
            var amber = new SolidColorBrush(Color.FromRgb(0xF5, 0xB7, 0x4A)); amber.Freeze();
            Background = surface; BorderBrush = amber; BorderThickness = new Thickness(2);
            AutomationProperties.SetName(this, "Claude needs your permission");

            var body = new StackPanel { Margin = new Thickness(16, 12, 16, 14) };
            var heading = new TextBlock { Text = ("Needs you · " + ask.ProjectName).ToUpperInvariant(), Foreground = amber, FontFamily = new FontFamily("Segoe UI Semibold"), FontSize = 12 };
            body.Children.Add(heading);
            string what = ask.Tool.Length > 0 ? "Allow " + ask.Tool + "?" : "Allow this?";
            body.Children.Add(new TextBlock { Text = what, Foreground = text, FontFamily = new FontFamily("Segoe UI Semibold"), FontSize = 18, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            if (!String.IsNullOrWhiteSpace(thread))
                body.Children.Add(new TextBlock { Text = thread, Foreground = muted, FontFamily = new FontFamily("Segoe UI"), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
            if (ask.Detail.Length > 0)
            {
                var detail = new Border { Background = raised, CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 8, 0, 0),
                    Child = new TextBlock { Text = ask.Detail, Foreground = text, FontFamily = new FontFamily("Segoe UI"), FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxHeight = 56, TextTrimming = TextTrimming.CharacterEllipsis } };
                body.Children.Add(detail);
            }
            countdown = new TextBlock { Foreground = muted, FontFamily = new FontFamily("Segoe UI"), FontSize = 12, Margin = new Thickness(0, 8, 0, 0) };
            body.Children.Add(countdown);
            var buttons = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            for (int i = 0; i < 3; i++) buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttons.Children.Add(MakeButton("Allow once", "Allow " + (ask.Tool.Length > 0 ? ask.Tool : "it") + " this once", accent, Brushes.Black, accent, 0, ClaudeBridge.Allow));
            buttons.Children.Add(MakeButton("Deny", "Deny it; Claude is told you said no", raised, text, border, 1, ClaudeBridge.Deny));
            buttons.Children.Add(MakeButton("Open", "Show Claude Code's own dialog and bring the session forward", raised, text, border, 2, ClaudeBridge.OpenIt));
            body.Children.Add(buttons);
            Content = body;
            Tick(DateTime.UtcNow);
        }

        private Button MakeButton(string label, string tip, Brush background, Brush foreground, Brush edge, int column, string answer)
        {
            var button = new Button { Content = label, ToolTip = tip, Background = background, Foreground = foreground, BorderBrush = edge, BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Segoe UI Semibold"), FontSize = 14, MinHeight = 40, Margin = new Thickness(column == 0 ? 0 : 4, 0, column == 2 ? 0 : 4, 0), Focusable = false };
            AutomationProperties.SetName(button, label);
            Grid.SetColumn(button, column);
            button.Click += (s, e) => Finish(answer);
            return button;
        }

        // Updates the countdown; false once the wait is over (the caller closes the toast).
        public bool Tick(DateTime now)
        {
            int left = Ask.SecondsLeft(now);
            countdown.Text = left > 0 ? "Claude Code asks in its own window in " + left + " s" : "Claude Code is asking in its own window";
            return left > 0;
        }

        private void Finish(string answer)
        {
            if (done) return;
            done = true;
            try { answered(answer); } catch (Exception) { }
        }

        // Places it in the work area's bottom-right corner, slot 0 lowest, the next ones above it.
        public void Place(int slot)
        {
            var area = SystemParameters.WorkArea;
            double height = ActualHeight > 0 ? ActualHeight : 220;
            Left = area.Right - ToastWidth - Gap;
            Top = area.Bottom - Gap - (slot + 1) * (height + Gap) + Gap;
        }

        // Never activated: a click on a button answers without taking the keyboard from the game or app in front.
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var handle = new WindowInteropHelper(this).Handle;
                long style = GetWindowLongPtr(handle, ExStyle).ToInt64();
                SetWindowLongPtr(handle, ExStyle, new IntPtr(style | NoActivate | ToolWindow));
            }
            catch (Exception) { }
        }

        private static Brush Pick(Func<string, object> resource, string key, string fallback)
        {
            Brush brush = null;
            try { brush = resource == null ? null : resource(key) as Brush; } catch (Exception) { brush = null; }
            if (brush != null) return brush;
            var made = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback)); made.Freeze();
            return made;
        }

        private const int ExStyle = -20;
        private const long NoActivate = 0x08000000L, ToolWindow = 0x00000080L;
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    }
}

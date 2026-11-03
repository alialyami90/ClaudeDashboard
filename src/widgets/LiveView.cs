using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ClaudeDashboard
{
    // The Live tab (decision 030, package C): the lap chart. Every session is a lane on one time axis from the last 100
    // minutes to the 5-hour reset, in urgency order; its car sits on the now line with its trail of states behind it.
    // The 5-hour curve and its projection run above the lanes and the Amber wall drops through them at "≈ limit".
    // Below, "What just happened". The selected session's panel sits at the right from 1000 wide and opens as a drawer
    // from 640 to 1000. Lanes are buttons. Names, states and times only; the view never answers a permission prompt.
    public sealed class LiveView : IClaudeView
    {
        public const double PanelWidth = 360, SideFrom = 1000, WideLabel = 240, NarrowLabel = 200, CarWidth = 44;
        // The panel goes beside the chart when the view's room is this wide: a 1000-unit window less the view area's
        // 24-unit padding each side and the window's own edges.
        public const double SideRoom = 920;
        public string Name { get { return ClaudeViews.Live; } }
        public bool Visible { get; set; }

        private ClaudeViewContext context = new ClaudeViewContext();
        private Size room;
        private bool side, quiet, framesAsked;
        private double labelWidth;
        private LapCurve curve;
        private LapTicks ticks;
        private LapKey trailKey;
        private ScrollViewer panelScroll;
        private TextBlock curveLabel, curveSub;
        private StackPanel lanes, feedLines, panelBody;
        private ScrollViewer scroller;
        private Border panel;
        private Grid root;
        private string laneKey = "";
        private string selected;
        private int feedRoom;
        private ClaudeView last;
        private DateTime lastNow;
        private readonly Dictionary<string, Lane> parts = new Dictionary<string, Lane>(StringComparer.Ordinal);
        private sealed class Lane { public Button Button; public TextBlock Title, State, Project; public LapTrail Trail; public PitSprite Car; public Canvas Track; }

        // For the checks: the lanes' sessions in order, and the selected one.
        public List<string> LaneOrder { get { return lanes == null ? new List<string>() : lanes.Children.OfType<Button>().Select(b => b.Tag as string).ToList(); } }
        public string Selected { get { return selected; } }
        public bool PanelOpen { get { return panel != null && panel.Visibility == Visibility.Visible; } }
        public bool SidePanel { get { return side; } }
        public Button LaneButton(string id) { Lane lane; return parts.TryGetValue(id ?? "", out lane) ? lane.Button : null; }
        public PitSprite LaneCar(string id) { Lane lane; return parts.TryGetValue(id ?? "", out lane) ? lane.Car : null; }
        public bool FramesAsked { get { return framesAsked; } }
        public LapTicks Ticks { get { return ticks; } }
        public LapKey TrailKey { get { return trailKey; } }
        public ScrollViewer PanelScroll { get { return panelScroll; } }

        private double S(double size) { return context.Scaled(size); }

        public FrameworkElement Build(ClaudeViewContext viewContext, Size viewRoom)
        {
            context = viewContext ?? new ClaudeViewContext(); room = viewRoom;
            // v0.57.2: Quiet (keep the cars still) is read here only; the window builds the view again when it changes.
            quiet = context.Quiet; framesAsked = false;
            side = room.Width >= SideRoom;
            labelWidth = side ? WideLabel : NarrowLabel;
            parts.Clear(); laneKey = "";
            root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(side ? 16 + PanelWidth : 0) });
            var main = new Grid();
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(main);

            // The 5-hour curve over the lanes, on the lanes' axis.
            var head = new Grid { Height = Math.Max(56, S(64)), Margin = new Thickness(0, 0, 0, 2) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            curveLabel = CardParts.Text("5-hour limit", S(13), "Text"); curveLabel.FontWeight = FontWeights.SemiBold; curveLabel.TextTrimming = TextTrimming.CharacterEllipsis;
            curveSub = CardParts.Text("", S(12), "Muted"); curveSub.TextTrimming = TextTrimming.CharacterEllipsis; curveSub.TextWrapping = TextWrapping.NoWrap;
            words.Children.Add(curveLabel); words.Children.Add(curveSub);
            if (context.Sample) { var sample = CardParts.Text("Sample", S(12), "Muted"); sample.FontWeight = FontWeights.SemiBold; words.Children.Add(sample); }
            head.Children.Add(words);
            curve = new LapCurve { TextScale = context.TextScale };
            Grid.SetColumn(curve, 1); head.Children.Add(curve);
            AutomationProperties.SetName(head, "5-hour limit");
            main.Children.Add(head);
            // v0.57.2: the time under the curve, on the same axis: whole hours, now and the reset.
            var tickRow = new Grid { Height = Math.Ceiling(S(11) * 1.33) + 4, Margin = new Thickness(0, 0, 0, 4) };
            tickRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            tickRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ticks = new LapTicks { TextScale = context.TextScale, Use24 = context.Use24Hour };
            Grid.SetColumn(ticks, 1); tickRow.Children.Add(ticks);
            Grid.SetRow(tickRow, 1); main.Children.Add(tickRow);

            lanes = new StackPanel();
            scroller = new ScrollViewer { Content = lanes, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            Grid.SetRow(scroller, 2); main.Children.Add(scroller);

            // "What just happened": as many lines as a quarter of the room holds, at least two.
            double line = Math.Ceiling(S(13) * 1.33) + 4;
            feedRoom = Math.Max(2, Math.Min(8, (int)Math.Floor((room.Height * 0.25 - line - 8) / line)));
            var feed = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            var feedHead = CardParts.Text("What just happened", S(13), "Text"); feedHead.FontWeight = FontWeights.SemiBold;
            // v0.57.2: the trails' key on the feed's heading line, as much as fits after the heading.
            var feedTop = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(feedHead, Dock.Left); feedTop.Children.Add(feedHead);
            trailKey = new LapKey { TextScale = context.TextScale, Height = Math.Ceiling(S(11) * 1.33) + 4, Margin = new Thickness(24, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(trailKey, LapKey.Spoken);
            feedTop.Children.Add(trailKey);
            feed.Children.Add(feedTop);
            feedLines = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            feed.Children.Add(feedLines);
            AutomationProperties.SetName(feed, "What just happened");
            Grid.SetRow(feed, 3); main.Children.Add(feed);

            // The selected session: beside the chart from 1000 wide, a drawer over it below that.
            panelBody = new StackPanel();
            panelScroll = new ScrollViewer { Content = panelBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            panel = new Border { Padding = new Thickness(16), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Child = panelScroll, VerticalAlignment = VerticalAlignment.Top };
            // v0.57.2: when the panel's words don't all fit, it ends after the last whole one (no line half cut at the
            // scroll edge); the rest scrolls.
            root.SizeChanged += (sender, e) => Snap();
            panelBody.SizeChanged += (sender, e) => Snap();
            panel.SetResourceReference(Border.BackgroundProperty, "Surface"); panel.SetResourceReference(Border.BorderBrushProperty, "ControlBorder");
            AutomationProperties.SetName(panel, "Selected session");
            if (side) { Grid.SetColumn(panel, 1); panel.Margin = new Thickness(16, 0, 0, 0); }
            else
            {
                panel.Width = Math.Min(PanelWidth, Math.Max(240, room.Width * 0.6));
                panel.HorizontalAlignment = HorizontalAlignment.Right;
                panel.Visibility = selected == null ? Visibility.Collapsed : Visibility.Visible;
            }
            root.Children.Add(panel);
            root.PreviewKeyDown += (sender, e) => { if (e.Key == Key.Escape && !side && PanelOpen) { Close(); e.Handled = true; } };
            if (last != null) Refresh(last, lastNow);
            return root;
        }
        // The panel's scroll area ends at the bottom of the last child that fits whole in the room the view leaves it.
        private void Snap()
        {
            if (panelScroll == null || root == null || root.ActualHeight <= 0) return;
            double room = root.ActualHeight - panel.Padding.Top - panel.Padding.Bottom - panel.BorderThickness.Top - panel.BorderThickness.Bottom - panel.Margin.Top - panel.Margin.Bottom;
            if (room <= 0) return;
            double fit = Double.PositiveInfinity;
            if (panelBody.ActualHeight > room + 0.5)
            {
                fit = 0;
                foreach (FrameworkElement child in panelBody.Children)
                {
                    if (child.ActualHeight <= 0 || child.Visibility != Visibility.Visible) continue;
                    double bottom = child.TranslatePoint(new Point(0, child.ActualHeight + child.Margin.Bottom), panelBody).Y;
                    if (bottom <= room + 0.5) fit = Math.Max(fit, bottom);
                }
                if (fit <= 0) fit = room;
                fit = Math.Ceiling(fit);
            }
            if (!(Math.Abs(panelScroll.MaxHeight - fit) < 0.5) && !(Double.IsInfinity(fit) && Double.IsInfinity(panelScroll.MaxHeight))) panelScroll.MaxHeight = fit;
        }

        public void Refresh(ClaudeView snapshot, DateTime now)
        {
            last = snapshot; lastNow = now;
            if (root == null || snapshot == null) return;
            var axis = LapAxis.For(snapshot, now);
            // The curve, and what "≈ limit" says.
            var w = snapshot.Limits == null ? null : snapshot.Limits.FiveHour;
            curve.Axis = axis; curve.Readings = snapshot.PlanReadings; curve.Pace = snapshot.LimitAtPace;
            curve.Percent = w == null ? Double.NaN : w.Percent;
            // v0.57.2: the same test as the Garage's lines: Reached, or the meter itself at 100%.
            bool reached = ClaudeText.FiveHourReached(snapshot, now);
            curve.Reached = reached;
            curve.WallLabel = axis.Wall.HasValue ? "≈ limit " + ClaudeText.Clock(axis.Wall.Value, context.Use24Hour) : "";
            curve.Note = snapshot.LimitAtPace.Known ? "" : snapshot.LimitAtPace.Why;
            curveSub.Text = LimitWords(snapshot, now, context.Use24Hour);
            if (reached) curveSub.SetResourceReference(TextBlock.ForegroundProperty, "Warning"); else curveSub.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            curveSub.ToolTip = AverageTip(snapshot, context.Use24Hour);
            curve.InvalidateVisual();
            ticks.Axis = axis; ticks.Reset = w == null ? null : w.ResetsAt; ticks.InvalidateVisual();

            // The lanes: running sessions, and ended ones whose trail is still on the axis, most urgent first.
            var threads = Lanes(snapshot, axis);
            string key = String.Join(",", threads.Select(t => t.Id));
            if (key != laneKey)
            {
                laneKey = key;
                lanes.Children.Clear(); parts.Clear();
                foreach (var t in threads) { var lane = MakeLane(t); parts[t.Id] = lane; lanes.Children.Add(lane.Button); }
                if (threads.Count == 0) lanes.Children.Add(CardParts.Text("No sessions in the last 100 minutes.", S(13), "Muted"));
            }
            var errors = snapshot.ApiErrors.GroupBy(a => a.Session).ToDictionary(g => g.Key, g => g.Select(a => a.At).ToList());
            foreach (var t in threads) Fill(parts[t.Id], t, snapshot, axis, errors, now);
            trailKey.InvalidateVisual();
            Motion();

            // The feed.
            feedLines.Children.Clear();
            var feedShown = FeedShown(snapshot);
            foreach (var item in feedShown.Take(feedRoom))
            {
                var text = CardParts.Text(FeedLine(item, context.Use24Hour), S(13), item.Kind == ClaudeFeedItem.Needs || item.Kind == ClaudeFeedItem.Silent || item.Kind == ClaudeFeedItem.Stuck ? "Warning" : "Muted");
                if (item.Kind == ClaudeFeedItem.Retrying || item.Kind == ClaudeFeedItem.ApiError) text.Foreground = ClaudeReliability.Rose(root);
                text.TextTrimming = TextTrimming.CharacterEllipsis;
                feedLines.Children.Add(text);
            }
            if (feedShown.Count == 0) feedLines.Children.Add(CardParts.Text("Nothing yet since the app began watching at " + ClaudeText.Clock(snapshot.WatchStart, context.Use24Hour) + ".", S(13), "Muted"));

            // The panel: the selected session, else (beside the chart) the most urgent one.
            if (selected != null && !threads.Any(t => t.Id == selected)) { selected = null; if (!side) panel.Visibility = Visibility.Collapsed; }
            var shown = threads.FirstOrDefault(t => t.Id == selected) ?? (side ? threads.FirstOrDefault() : null);
            FillPanel(shown, snapshot, now);
        }

        // ----- Lanes -----

        public static List<ClaudeSession> Lanes(ClaudeView view, LapAxis axis)
        {
            return view.Threads.Where(t => !ArchivedHidden(view, t) && (t.Running || (view.Trails.ContainsKey(t.Id) && t.LastActive >= axis.From)))
                .OrderBy(t => (int)t.Lane).ThenBy(t => t.LaneSince).ThenBy(t => t.Id, StringComparer.Ordinal).ToList();
        }

        // v0.57.2: Live follows Settings → Claude's archived choice (archived and resolved threads, as the Garage hides
        // them); hide-idle isn't applied here, since an idle lane is still on the track.
        public static bool ArchivedHidden(ClaudeView view, ClaudeSession t) { return ClaudePace.Hidden(t.Pit, view.ShowArchived, false); }

        // v0.57.2, moving cars: a car's frames step by its state, as on the Garage, through the window's one frame
        // clock. The view asks for frames while a car moves and lets go when every car is still; it never starts a
        // timer of its own, and in Quiet nothing moves.
        private void Motion()
        {
            double fps = 0;
            if (!quiet) foreach (var lane in parts.Values) if (lane.Car.Moving) fps = Math.Max(fps, lane.Car.Fps);
            if (fps > 0) { context.Frames(Step, fps); framesAsked = true; }
            else if (framesAsked) { context.Frames(null, 0); framesAsked = false; }
        }
        private void Step(double seconds)
        {
            bool moving = false;
            foreach (var lane in parts.Values) { lane.Car.Step(seconds); if (lane.Car.Moving) moving = true; }
            if (!moving && framesAsked) { context.Frames(null, 0); framesAsked = false; }
        }

        private Lane MakeLane(ClaudeSession t)
        {
            var lane = new Lane();
            double height = Math.Ceiling(S(14) * 1.33) + Math.Ceiling(S(12) * 1.33) * 2 + 12;
            var grid = new Grid { Height = height };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var line = new Border { BorderThickness = new Thickness(0, 1, 0, 0) }; line.SetResourceReference(Border.BorderBrushProperty, "ControlBorder"); Grid.SetColumnSpan(line, 2); grid.Children.Add(line);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 12, 0) };
            lane.Title = CardParts.Text("", S(14), "Text"); lane.Title.FontWeight = FontWeights.SemiBold; lane.Title.TextTrimming = TextTrimming.CharacterEllipsis;
            lane.State = CardParts.Text("", S(12), "Muted"); lane.State.TextTrimming = TextTrimming.CharacterEllipsis;
            lane.Project = CardParts.Text("", S(12), "Muted"); lane.Project.TextTrimming = TextTrimming.CharacterEllipsis;
            words.Children.Add(lane.Title); words.Children.Add(lane.State); words.Children.Add(lane.Project);
            grid.Children.Add(words);
            lane.Track = new Canvas { ClipToBounds = true };
            lane.Trail = new LapTrail { TextScale = context.TextScale, CarWidth = CarWidth };
            lane.Car = new PitSprite { Width = CarWidth, Height = Math.Round(CarWidth * 60 / 140) };
            var track = new Grid(); track.Children.Add(lane.Trail); track.Children.Add(lane.Track);
            lane.Track.Children.Add(lane.Car);
            Grid.SetColumn(track, 1); grid.Children.Add(track);
            lane.Button = new Button { Style = LaneStyle, Content = grid, Tag = t.Id, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            string id = t.Id;
            lane.Button.Click += (sender, e) => { e.Handled = true; Select(id); };
            lane.Track.SizeChanged += (sender, e) => PlaceCar(lane);
            return lane;
        }

        private void Fill(Lane lane, ClaudeSession t, ClaudeView view, LapAxis axis, Dictionary<string, List<DateTime>> errors, DateTime now)
        {
            lane.Title.Text = ClaudeText.ThreadTitle(t, now, context.Use24Hour);
            lane.State.Text = StateWords(t, now, context.Use24Hour);
            lane.State.Foreground = null;
            if (t.Lane == ClaudeLaneState.Retrying) lane.State.Foreground = ClaudeReliability.Rose(root);
            else lane.State.SetResourceReference(TextBlock.ForegroundProperty, t.Lane == ClaudeLaneState.NeedsYou || t.Lane == ClaudeLaneState.Silent || t.Lane == ClaudeLaneState.Stuck ? "Warning" : "Muted");
            lane.Project.Text = t.ProjectName;
            List<ClaudeStateMark> marks; view.Trails.TryGetValue(t.Id, out marks);
            List<DateTime> seen; errors.TryGetValue(t.Id, out seen);
            uint color = PitArt.Accent(t.Project);
            lane.Trail.Axis = axis; lane.Trail.Marks = marks ?? new List<ClaudeStateMark>(); lane.Trail.Errors = seen ?? new List<DateTime>(); lane.Trail.Color = color;
            lane.Trail.InvalidateVisual();
            // The car on the now line, its nose at now; an ended session's car stays parked where its trail stopped.
            var state = t.Running ? t.Pit : PitState.End;
            var look = new PitArt.CarLook { Car = t.Car };
            string carKey; var frames = PitSprite.CarFrames(color, state, look, CarWidth, out carKey);
            lane.Car.Fps = quiet ? 0 : PitSprite.CarFps(state, ClaudePace.Share(t.Rate, view.Scale), false);
            lane.Car.Once = PitSprite.PlaysOnce(state);
            lane.Car.Show(carKey, frames, PitSprite.CarOverflow, PitSprite.StillFrame(state), false);
            if (quiet) lane.Car.Hold();
            lane.Car.Tag = t.Running ? (object)axis.Now : t.LastActive;
            lane.Car.DataContext = axis;
            PlaceCar(lane);
            AutomationProperties.SetName(lane.Button, LaneName(t, now));
            AutomationProperties.SetHelpText(lane.Button, "Enter shows this session's details.");
            bool chosen = t.Id == selected;
            lane.Button.SetResourceReference(Control.BackgroundProperty, chosen ? "Selected" : "Page");
            if (!chosen) lane.Button.Background = Brushes.Transparent;
        }
        private static void PlaceCar(Lane lane)
        {
            var axis = lane.Car.DataContext as LapAxis;
            if (axis == null || !(lane.Car.Tag is DateTime)) return;
            double w = lane.Track.ActualWidth, h = lane.Track.ActualHeight;
            double x = axis.X((DateTime)lane.Car.Tag, w) - lane.Car.Width;
            Canvas.SetLeft(lane.Car, Math.Max(0, x));
            Canvas.SetTop(lane.Car, Math.Max(0, (h - lane.Car.Height) / 2));
        }

        // "Fix the login timeout, needs you 4 minutes, api-server".
        public static string LaneName(ClaudeSession t, DateTime now)
        {
            return ClaudeText.ThreadTitle(t, now, false) + ", " + SpokenState(t, now) + ", " + t.ProjectName;
        }
        private static string SpokenState(ClaudeSession t, DateTime now)
        {
            int minutes = (int)Math.Max(0, (now - t.LaneSince).TotalMinutes);
            string span = minutes < 1 ? "just now" : minutes < 60 ? minutes + (minutes == 1 ? " minute" : " minutes") : (minutes / 60) + (minutes / 60 == 1 ? " hour" : " hours");
            return Lower(t.Lane) + " " + span;
        }
        private static string Lower(ClaudeLaneState state)
        {
            switch (state)
            {
                case ClaudeLaneState.NeedsYou: return "needs you";
                case ClaudeLaneState.YourTurn: return "your turn";
                default: return state.ToString().ToLowerInvariant();
            }
        }
        // The lane's second line: "Needs you · 4 min", "Retrying · HTTP 529 · attempt 2", "Silent · 8 min", "Ended 3:12 pm".
        public static string StateWords(ClaudeSession t, DateTime now, bool use24)
        {
            string span = ClaudeText.Duration(now - t.LaneSince);
            switch (t.Lane)
            {
                case ClaudeLaneState.NeedsYou: return "Needs you · " + span;
                case ClaudeLaneState.Stuck: return "Stuck · " + span;
                case ClaudeLaneState.Retrying: { string d = ClaudeTimeline.ErrorDetail(t.ErrorStatus, t.ErrorAttempt); return "Retrying" + (d.Length > 0 ? " · " + d : ""); }
                case ClaudeLaneState.Silent: return "Silent · " + span;
                case ClaudeLaneState.YourTurn: return "Your turn · " + span;
                case ClaudeLaneState.Compacting: return "Compacting · " + span;
                case ClaudeLaneState.Working: return "Working · " + span;
                case ClaudeLaneState.Ended: return "Ended " + ClaudeText.Clock(t.LastActive, use24);
                default: return "Idle · " + span;
            }
        }

        // ----- The 5-hour line -----

        // "84% · ≈ limit 3:21 pm", "23% · ≈ 33% at reset", or why there's no projection.
        public static string LimitWords(ClaudeView view, DateTime now, bool use24)
        {
            var w = view.Limits == null ? null : view.Limits.FiveHour;
            if (w == null) return "No plan reading";
            string percent = Math.Round(w.Percent) + "%";
            var pace = view.LimitAtPace;
            if (ClaudeText.FiveHourReached(view, now)) return percent + " · Limit reached";
            if (pace == null || !pace.Known) return percent + " · " + (pace == null ? "no projection" : pace.Why);
            if (pace.BeforeReset) return percent + " · ≈ limit " + ClaudeText.Clock(pace.LimitAt.Value, use24);
            return percent + " · ≈ " + Math.Round(Math.Min(999, pace.AtReset)) + "% at reset";
        }
        // The tip: the same at today's average, and how that's worked out (Scribe on #23).
        public static string AverageTip(ClaudeView view, bool use24)
        {
            var a = view.LimitAtAverage;
            string head = "At the last 30 minutes' pace, from the plan readings.";
            if (a == null || !a.Known) return head + "\nAt today's average: " + (a == null || a.Why.Length == 0 ? "not known" : a.Why) + ".";
            if (a.Reached) return head + "\nAt today's average: limit reached.";
            string at = a.BeforeReset ? "≈ limit " + ClaudeText.Clock(a.LimitAt.Value, use24) : "≈ " + Math.Round(Math.Min(999, a.AtReset)) + "% at reset";
            return head + "\nAt today's average: " + at + ". Today's tokens an hour, converted to the 5-hour window's percent at this window's tokens per percent so far.";
        }

        // ----- The feed -----

        // v0.57.2: the feed's lines, less an archived thread's when Settings → Claude hides archived threads (it has
        // no lane either).
        public static List<ClaudeFeedItem> FeedShown(ClaudeView view)
        {
            var hidden = new HashSet<string>(view.Threads.Where(t => ArchivedHidden(view, t)).Select(t => t.Id), StringComparer.Ordinal);
            return view.Feed.Where(item => !hidden.Contains(item.Session ?? "")).ToList();
        }

        // "3:12 pm  Fix the login timeout · needs you".
        public static string FeedLine(ClaudeFeedItem item, bool use24)
        {
            string what;
            switch (item.Kind)
            {
                case ClaudeFeedItem.Needs: what = "needs you"; break;
                case ClaudeFeedItem.Stuck: what = "stuck"; break;
                case ClaudeFeedItem.Retrying: what = "retrying"; break;
                case ClaudeFeedItem.Silent: what = "silent"; break;
                case ClaudeFeedItem.Turn: what = "your turn"; break;
                case ClaudeFeedItem.Compacting: what = "compacting"; break;
                case ClaudeFeedItem.Working: what = "working"; break;
                case ClaudeFeedItem.Ended: what = "ended"; break;
                case ClaudeFeedItem.ToolFailed: what = (item.Detail.Length > 0 ? ClaudeText.ToolName(item.Detail) : "a tool") + " failed"; break;
                case ClaudeFeedItem.ApiError: what = "API error" + (item.Detail.Length > 0 ? " · " + item.Detail : ""); break;
                default: what = "idle"; break;
            }
            return ClaudeText.Clock(item.At, use24) + "   " + (item.Title.Length > 0 ? item.Title : item.Project) + " · " + what;
        }

        // ----- The selected session -----

        private void Select(string id)
        {
            selected = id;
            if (!side) panel.Visibility = Visibility.Visible;
            if (last != null) Refresh(last, lastNow);
        }
        private void Close()
        {
            string was = selected;
            selected = null;
            if (!side) panel.Visibility = Visibility.Collapsed;
            if (last != null) Refresh(last, lastNow);
            var button = LaneButton(was); if (button != null) button.Focus();
        }

        private void FillPanel(ClaudeSession t, ClaudeView view, DateTime now)
        {
            panelBody.Children.Clear();
            if (t == null) { panelBody.Children.Add(CardParts.Text("Choose a lane to see its session.", S(13), "Muted")); return; }
            var top = new DockPanel { LastChildFill = true };
            if (!side)
            {
                var close = new Button { Style = LaneStyle, Content = CardParts.Text("", S(14), "Muted"), Padding = new Thickness(6), VerticalAlignment = VerticalAlignment.Top };
                ((TextBlock)close.Content).FontFamily = new FontFamily("Segoe MDL2 Assets");
                AutomationProperties.SetName(close, "Close the session panel");
                close.Click += (sender, e) => { e.Handled = true; Close(); };
                DockPanel.SetDock(close, Dock.Right); top.Children.Add(close);
            }
            var title = CardParts.Text(ClaudeText.ThreadTitle(t, now, context.Use24Hour), S(17), "Text"); title.FontWeight = FontWeights.SemiBold; title.TextWrapping = TextWrapping.Wrap;
            top.Children.Add(title);
            panelBody.Children.Add(top);
            Add(t.ProjectName + (ClaudeText.Place(t).Length > 0 ? " · " + ClaudeText.Place(t) : ""), "Muted", 12, 2);
            var state = Add(StateWords(t, now, context.Use24Hour), t.Lane == ClaudeLaneState.NeedsYou || t.Lane == ClaudeLaneState.Silent || t.Lane == ClaudeLaneState.Stuck ? "Warning" : "Text", 14, 10);
            if (t.Lane == ClaudeLaneState.Retrying) state.Foreground = ClaudeReliability.Rose(root);
            // What it asks: the notification's words (never a prompt), and Go to it, which only opens the session.
            if (t.Lane == ClaudeLaneState.NeedsYou && t.Message.Length > 0) Add(t.Message, "Text", 13, 4).TextWrapping = TextWrapping.Wrap;
            if (t.Running)
            {
                var go = new Button { Content = t.Lane == ClaudeLaneState.NeedsYou ? "Go to it" : "Open the session", Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 4, 12, 4), IsEnabled = t.CanFocus };
                if (t.Lane == ClaudeLaneState.NeedsYou) go.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
                AutomationProperties.SetHelpText(go, "Brings its window forward. The dashboard never answers for you.");
                string id = t.Id;
                var said = CardParts.Text("", S(12), "Warning");
                go.Click += (sender, e) => { e.Handled = true; string problem = context.Focus(id); said.Text = problem ?? ""; };
                panelBody.Children.Add(go); panelBody.Children.Add(said);
                if (!t.CanFocus) Add("Its window can't be found from here.", "Muted", 12, 2);
            }
            Fact("Last sign of life", t.LastSign > DateTime.MinValue ? ClaudeText.Duration(now - t.LastSign) + (now - t.LastSign >= TimeSpan.FromMinutes(1) ? " ago" : "") : "not heard yet");
            if (t.Running) Fact("Now", t.Tool.Length > 0 ? ClaudeText.ToolName(t.Tool) + " · " + ClaudeText.Duration(now - t.ToolSince) : LastTool(t, view));
            if (t.TasksTotal > 0 || t.Workers > 0) Fact("Tasks and helpers", (t.TasksTotal > 0 ? t.TasksDone + " / " + t.TasksTotal + " tasks" : "no tasks") + " · " + t.Workers + (t.Workers == 1 ? " helper" : " helpers"));
            Context(t.Context);
            var lines = t.LinesFor(ClaudePeriod.Today);
            Fact("Today", ClaudeUsage.Format(t.TokensFor(ClaudePeriod.Today)) + " tokens · " + ClaudeText.Added(lines[0]) + " " + ClaudeText.Removed(lines[1]) + " lines · " + t.AskedToday + (t.AskedToday == 1 ? " ask" : " asks"));
            // Its tool runs from telemetry: runs, typical (median) and longest.
            var head = CardParts.Text("Tool runs", S(13), "Text"); head.FontWeight = FontWeights.SemiBold; head.Margin = new Thickness(0, 14, 0, 4);
            panelBody.Children.Add(head);
            if (!view.ToolTimesKnown) Add("Telemetry is off, so tool times aren't known.", "Muted", 12, 0);
            else if (t.ToolTimes.Count == 0) Add("No tool runs yet.", "Muted", 12, 0);
            else foreach (var tool in t.ToolTimes.Take(8)) Wrap(Add(ClaudeText.ToolName(tool.Tool) + " · " + tool.Runs + (tool.Runs == 1 ? " run" : " runs") + " · typical " + Ms(tool.MedianMs) + " · longest " + Ms(tool.LongestMs) + (tool.Failed > 0 ? " · " + tool.Failed + " failed" : ""), "Muted", 12, 2));
        }
        private static string LastTool(ClaudeSession t, ClaudeView view)
        {
            var run = view.ToolRunsToday.Where(r => r.Session == t.Id && r.Tool.Length > 0).OrderByDescending(r => r.At).FirstOrDefault();
            return run == null ? "no tool running" : ClaudeText.ToolName(run.Tool) + " (last)";
        }
        private static string Ms(double ms)
        {
            if (Double.IsNaN(ms)) return "—";
            return ms < 1000 ? Math.Round(ms) + " ms" : ms < 60000 ? (ms / 1000).ToString(ms < 10000 ? "0.0" : "0", CultureInfo.InvariantCulture) + " s" : Math.Round(ms / 60000) + " min";
        }
        private TextBlock Add(string text, string brush, double size, double top)
        {
            var block = CardParts.Text(text, S(size), brush); block.Margin = new Thickness(0, top, 0, 0); block.TextTrimming = TextTrimming.CharacterEllipsis;
            panelBody.Children.Add(block);
            return block;
        }
        private static TextBlock Wrap(TextBlock block) { block.TextTrimming = TextTrimming.None; block.TextWrapping = TextWrapping.Wrap; return block; }
        private void Fact(string label, string value)
        {
            var name = CardParts.Text(label, S(12), "Muted"); name.Margin = new Thickness(0, 10, 0, 0);
            // v0.57.2: a number is never cut at 140%: the reading wraps.
            var reading = Wrap(CardParts.Text(value, S(14), "Text"));
            panelBody.Children.Add(name); panelBody.Children.Add(reading);
        }
        // Context used, with ticks at 60, 80 and 95%.
        private void Context(double percent)
        {
            var name = CardParts.Text("Context", S(12), "Muted"); name.Margin = new Thickness(0, 10, 0, 0);
            panelBody.Children.Add(name);
            if (Double.IsNaN(percent)) { panelBody.Children.Add(CardParts.Text("not known", S(14), "Muted")); return; }
            var bar = new ContextBar { Percent = percent, Height = 8, Margin = new Thickness(0, 4, 0, 0) };
            panelBody.Children.Add(bar);
            panelBody.Children.Add(CardParts.Text(Math.Round(percent) + "% used" + (percent >= 80 ? ", compaction soon" : ""), S(12), percent >= 80 ? "Warning" : "Muted"));
        }

        // ----- Parts -----

        // v0.58.2: rose (Retrying, API errors) is ClaudeReliability.Rose, deeper on the light palette so it reads at 4.5:1.
        private static Style laneStyle;
        // A flat button: the lane's whole row, with a 2-unit focus outline.
        private static Style LaneStyle
        {
            get
            {
                if (laneStyle != null) return laneStyle;
                var chrome = new FrameworkElementFactory(typeof(Border));
                chrome.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
                chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
                chrome.SetValue(Border.BorderThicknessProperty, new Thickness(2));
                chrome.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
                chrome.Name = "Chrome";
                var content = new FrameworkElementFactory(typeof(ContentPresenter));
                content.SetBinding(FrameworkElement.MarginProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
                chrome.AppendChild(content);
                var template = new ControlTemplate(typeof(Button)) { VisualTree = chrome };
                var focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
                focused.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Text"), "Chrome"));
                template.Triggers.Add(focused);
                var style = new Style(typeof(Button));
                style.Setters.Add(new Setter(Control.TemplateProperty, template));
                style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
                style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
                style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
                style.Seal();
                laneStyle = style;
                return style;
            }
        }
    }

    // The panel's context bar: how full the context window is, with ticks at 60, 80 and 95%.
    public sealed class ContextBar : PitInstrument
    {
        public double Percent = Double.NaN;
        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            dc.DrawRoundedRectangle(B("Track"), null, new Rect(0, 0, w, h), h / 2, h / 2);
            if (!Double.IsNaN(Percent)) dc.DrawRoundedRectangle(Percent >= 80 ? B("Warning") : B("Text"), null, new Rect(0, 0, Math.Max(h, w * Math.Min(100, Percent) / 100), h), h / 2, h / 2);
            var tick = Line(B("Page"), 2, false);
            foreach (double at in new[] { 60.0, 80, 95 }) dc.DrawLine(tick, new Point(Math.Round(w * at / 100), 0), new Point(Math.Round(w * at / 100), h));
        }
    }
}

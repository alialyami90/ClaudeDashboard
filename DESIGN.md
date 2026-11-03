# Claude Dashboard design: the pit wall

The window is a race team's pit wall. Each project is a garage, each Claude Code thread a car, and the plan's limits are the car's gauges: a fuel tank for the 5-hour window, laps for the week, a burn line for tokens a minute. Everything is calm until a car needs the driver; then that car, and only that car, shouts. Since v0.55.0–v0.58.0 ([decision 030](docs/decisions/030-claude-lap-chart.md)) the window has six views from 640 wide, and the home view, Live, puts every session on one lap chart with the 5-hour limit's wall across it. Since v0.49.0–v0.53.0 ([decision 029](docs/decisions/029-claude-live-status-board.md)) the 5 h and Today periods are a live status board (what needs you, and whether each session is alive), and Week is the ledger (what it all cost). The approved mock is [docs/design/claude-watcher-v2.html](docs/design/claude-watcher-v2.html), the built card is described in [docs/design/claude-watcher.md](docs/design/claude-watcher.md), and the technical record is [decision 017](docs/decisions/017-claude-pit-wall.md). The old Sticker Sheet design of the widget app is archived in [docs/archive/widget-screen/DESIGN.md](docs/archive/widget-screen/DESIGN.md).

## Surfaces

- **Palettes** (`Palettes.cs`): Midnight (default), Slate and Warm, and since v0.45.0 the light **Claude** palette in the spirit of the Claude app: warm paper `#FAF9F5`, soft black text `#1F1E1D` and a terracotta accent. Each sets the same keys (Page, Surface, Raised, Input, Track, ControlBorder, Selected, Muted, Text, Warning, Turn, OnAccent…), so the pit wall, the header and the Settings window change together. On paper, Amber and Glacier are darkened (`#8F5108`, `#21668D` since v0.51.0) so they read as text at 4.5:1 or more on Raised and Selected too.
- **Accent:** Lime `#BAF28B`, Glacier `#8ED8F8`, Lavender `#C5AEFF`, Amber or Terracotta (`#E08A68`; the Claude palette's default), each with a deeper shade on paper. It fills gauges, meters, the hour's burn line and the selected period.
- **Warning:** Amber `#F5C47D`, used for Needs you and Stuck, meters from 80% (a thread's context bar included), the fuel lamp, a source that is old or failed on the source-health line, and the taskbar dot. Your turn and Silent are calm. Nothing decorative is Amber.
- **Rose** `#F09A9A` (`#A93B35` on paper, so it reads at 4.5:1): API errors and Retrying, since v0.56.0, as well as lines removed. Nothing decorative is rose, and rose never means Stuck.
- **The idle line** `#5B6474`: a lane's thin line while a session is idle or silent.
- **Project colours:** each garage and its cars take Lime, Glacier or Lavender from a hash of the project's name, so a project keeps its colour everywhere.

## Saying how a thread is

Each state has a still frame and, when motion is on, a stepped 8-frame cycle. A chip names the state in words, so colour is never the only cue.

| State | The car | The chip |
| --- | --- | --- |
| Working | wheels turn at 4–24 fps by tokens a minute, a pixel of body jitter, exhaust and dust | Working |
| Long tool run | revs on its brakes | Tool run |
| Needs you | hazards blink, headlights flash, a beacon on the garage | Needs you, in Amber |
| Your turn (a turn finished) | brake lights glow | Your turn, in Glacier |
| Compacting | up on the jack, front wheel off | Compacting |
| Stuck or API errors | wheelspin in tyre smoke; the tip gives the reason in words | Retrying |
| Idle (Your turn for 30 minutes, or ready) | engine off, dimmed | Idle |
| Ended | lights off, a slate cover drops, once; the car fades, the row's words turn Muted at full opacity | Ended |
| Resolved, Archived | under the cover, dimmed the most, last in the project | Resolved, Archived |

Two plan states sit on top of any car: a fuel lamp that blinks at 90% of the 5-hour limit, and glowing brake discs at the redline. A context ring appears at 60%, 80% and 95% of the context window.

**The one state allowed to shout is Needs you.** The window's own 3-unit edge turns Amber (Glacier, the `Turn` colour, once a thread has waited 10 minutes for your reply), the title bar says "Claude needs you · Claude Dashboard" so the taskbar shows it, and the pit stop strip names the thread with Go to it. Since v0.52.0 the taskbar button also carries an Amber dot with the count (drawn in code: a 16-unit circle, the digits in OnWarning) and flashes once per new Needs you, never in Quiet mode. Ctrl+G opens the session waiting longest (Ctrl+1 until v0.55.0).

## Window sizes

The window is an ordinary one, at least 360 × 300, with a 56-unit header (the app name, the live summary, 5 h / Today / Week, Quiet and Settings; the words fold away as it narrows). From 640 wide a tab row sits under the header with the six views (see The views); below 640 there are no tabs and the window is the Garage card alone, as before v0.55.0. The summary counts Garage's chosen period while Garage is on screen, and the last 5 hours on the other views. When the whole summary won't fit beside them, it first shortens to the two most urgent parts and the thread count, then takes a line of its own (the header becomes 84 units) unless that would cost the card its design or Small its 5-hour meter; then it shortens to the most urgent part with the thread count, then that part alone, and it never breaks mid-word. Under the card is the source-health line (v0.50.0; see below), which never folds away. The card fills the rest, picks its design from the space it has, and switches live while the window is resized. The thresholds are `CardSizing.Claude` at the bottom of `ClaudeCard.cs`, in device-independent pixels, checked at 360 × 300, 640 × 420, 1000 × 600 and 1600 × 900, and at 150% display scaling (`tests/Shell/ShellTests.cs`).

- **Large** (card from 640 × 420): the plan row (tank, week laps, and from 860 wide the burn tile); the pit stop strip while anything waits on you; the ledger of garages and cars, which scrolls; and from 1000 wide a side panel with the 5-hour lanes and four facts.
- **Medium:** two mini plan tiles over the top garages, with up to three cars inline, and a last row "+3 more projects · 1 your turn" when they don't all fit. Its Needs-you line says how long ("Needs you 4 min · Fix login").
- **Small** (under 360 × 280): the car that needs you, or the busiest running one, over the 5-hour meter, whose line speaks in time like the tank ("≈ limit 3:21 am · resets 9:54 am").

The burn tile (v0.53.0, where the rev counter's dial was) is a 200-unit column, wider with bigger text: tokens a minute now, the last hour's line, and the last 7 days (v0.47.0) as seven small bars, today in the accent, with a tick for what last week had used by this time of day. The bars come from the daily summary files, so they're gone while History is off; the tip says what they count, and the sample says it's a sample.

### The plan row speaks in time

The tank and the week say when, not how much: "≈ limit 3:21 am, 37 min before reset" when the window hits its limit before it resets, else "≈ 84% by reset". Every form keeps "≈". When the long form doesn't fit, the line shortens ("≈ limit 3:21 am", "resets 9:54 am") rather than cutting a word; widths are measured as the window draws text. Medium's tiles and Small's meter line use the short forms.

**There is one "≈ limit" for the 5-hour window, and "≈ limit" always means it** (v0.56.1–v0.57.2): the engine's `LimitAtPace`, from the last 30 minutes of plan readings. The header, the tank, Medium's tile, Small's meter, Live's wall and Limits all show the same time, and only when it is known (3 readings or more) and falls before the reset. Once the newest reading or the meter is at 100%, every one of them says "Limit reached" in Amber, never a time that has passed; the tank says it once, on its reset line ("Limit reached · back 3:23 pm"), and leaves its pace line empty. With fewer than 3 readings Live and Limits show no projection and say why ("Only 2 plan readings so far"); the tank, Medium and Small fall back to the window's own pace so far and never say "limit": "≈ 84% by reset", or "≈ over by reset" when that pace runs past 100%. No other place gives a percentage over 100. The week has no plan-reading projection: its "≈ limit Thu 3:00 pm" comes from the week window's own pace so far, with the same words.

### Attention first (5 h and Today)

- **Ranking.** Projects go Needs you › Stuck › Your turn › Working › the rest, most recent first within each.
- **The pit stop strip is a queue** of every running thread that needs you, is stuck, has been your turn for 10 minutes or more, or is working with no hook event or log reply for 5 minutes (Silent). One at a time, "1 of 4" with Next. Needs you and Stuck are Amber and so is their Go to it; Your turn and Silent are calm. Silent names the tool when telemetry knows it ("In Bash for 9 min").
- **A clipped ledger keeps a footer** that never scrolls away: "+7 below · 1 stuck · 2 your turn", or "↑ 3 above" at the bottom, Amber when a hidden row needs you or is stuck. Pressing it scrolls to the first hidden row that waits on you. Rows end whole wherever the ledger is scrolled to.

### Live rows and the Week ledger

In 5 h and Today each thread row reads car, chip, title (with where it runs beside it, "Claude app" or "Remote Control"), then labelled columns: **For** (time in its state), **Last sign** ("just now", "3 min ago" in Muted; "silent 8 min" in Text once a working thread has been quiet 5 minutes), **Now** (the tool it's in, by name only: "Bash · 2 min", "—" when no tool is known, with why in the tip), **Tokens** and **Lines**. A 3-unit context bar sits under every running thread's title, empty with a tip until the status line reports it, Amber from 80%. On a narrow window Now steps aside first, then Lines, then where it runs.

Week keeps the ledger: Tokens, Cache reads, Replies (or ≈ $ API value), Models and Lines, with where it runs and the age beside the title.

### The source-health line

From 640 wide it sits at the right end of the tab row, so a missing source shows on every view; below 640 it sits under the card. It is one control, moved rather than copied. "Sample" is pinned first on fixtures, then Hooks · Logs · Telemetry · Plan. Each source has a level with its own Segoe MDL2 glyph, so colour is never the only sign: fine (CheckMark), waiting (Sync: "Logs counting…", "Logs: none yet", "Telemetry with Widget Screen"), old (Recent: "Plan 12 min old", Amber), off and why (Cancel: "Hooks off: not connected", "Plan off: not signed in") and failed (Warning: "Logs unreadable", "Telemetry off: port 43832 in use", "Hooks with Widget Screen · Reconnect", Amber). The full reason is in each tooltip and screen-reader name. As the window narrows the line steps down, and never folds or cuts a word: every reason in full; short reasons; the sources that are fine keep only their glyph; the ones with trouble keep only their names; then only the Amber ones keep a name. So on Small healthy sources are glyphs only, and Reconnect and the reasons are in the tooltips. The line is 24 units at 100% text and overlaps the lower 16 units of the card's padding, so its own row is 8.

## The views (v0.55.0–v0.58.0)

From 640 wide a real tab list under the header chooses the view; each tab has a spoken name and its key in the tip. The views build from the same snapshot and refresh on the 1 s heartbeat; none starts a timer of its own.

| Key | View | What it shows |
| --- | --- | --- |
| Ctrl+1 | **Live** (home) | The lap chart, the 5-hour curve and wall, "What just happened" and the selected session |
| Ctrl+2 | **Garage** | The pit wall as above, with its own 5 h / Today / Week |
| Ctrl+3 | **Limits** | The 5-hour window from start to reset, the week's laps, the weekly windows and tokens by project |
| Ctrl+4 | **History** | 8 weeks of days, the hour-of-day profile, the last 7 days and this week's projects |
| Ctrl+5 | **Reliability** | Tool failures, API errors in the last hour, asks and denials, tools across all sessions, and stuck turns with their reasons |
| Ctrl+6 | **Sources** | Hooks, Logs, Telemetry, Plan meters and Claude app threads, each with its state, freshness, what it gives and one action |

### Live: the lap chart

- **One time axis** from 100 minutes ago to the 5-hour reset (an hour ahead when the reset isn't known). Under the curve a row of times marks whole hours, "now" and "reset 2:16 pm"; a label that would meet another is left out. After now the background is the Input colour, so past and future read apart without a label. Lanes start where the app began watching, at a dashed, labelled edge; nothing earlier is invented.
- **A lane per session**, running or ended within the axis, in urgency order: Needs you, Stuck, Retrying, Silent, Your turn, Compacting, Working, Idle, then Ended. Archived threads get no lane or feed line while Settings → Claude hides them; hide-idle doesn't apply, since Idle is a lane. Each lane's label is its title, its state and time ("Needs you · 4 min"; Silent in Amber, Retrying in rose with its status and attempt) and its project, 240 units wide beside the panel and 200 below.
- **The trail** behind each car draws its states: Working is solid project colour; Needs you an Amber block; Stuck a hollow Amber outline; Your turn a hollow 1.5-unit Glacier outline; Idle and Silent the 4-unit idle line; Compacting stripes in the project colour; each API error a 2-unit rose mark. A one-line key sits beside "What just happened", as much as fits, and is read out in full. It tells states, not projects: its Working and Compacting swatches are in the Text colour, and the first reads "Working (project colour)".
- **The 5-hour curve** runs above the lanes on the same axis, with the percent at its end and its dashed projection at the last 30 minutes' pace. Where that meets 100% before the reset, a 2-unit Amber wall drops through every lane, labelled once at the top ("≈ limit 3:21 pm"). With no projection the label says why; at the limit it says "Limit reached" in Amber and there is no wall. The tip gives today's average too and says it is worked out from tokens.
- **What just happened**: the newest lines of the feed (state changes, tool failures, API errors, compactions), as many as a quarter of the view holds. Names and times only.
- **The selected session**: what it asks and Go to it (which only brings its window forward), last sign of life, Now (or the last tool, marked "last"), tasks and helpers, context with ticks at 60, 80 and 95%, today's tokens, lines and asks, and its tool runs with typical and longest times. From 1000 wide it is a 360-unit panel beside the chart showing the most urgent session until you choose a lane; from 640 to 1000 it is a drawer that a lane opens and Esc or its close button shuts. Its readings wrap rather than cut a number, and it ends after its last whole line.

### Limits, History, Reliability and Sources

- **Limits**: the window's use curve, the dashed "≈ limit" at the last 30 minutes' pace (Amber with a wall when it hits before the reset), the dotted line at today's average from tokens, and a mark where heavy use began. Beside it, the week as laps by day in whole percents ("<1%" for a little use) with the pace to the reset, the weekly windows (each saying when it isn't reported) and this window's tokens by project.
- **History**: an 8-week calendar of tokens per day, the hour-of-day profile with a tick at the same hour last week, and the last 7 days (tokens, ≈ $, threads, times it needed you, the longest wait, Amber from 15 minutes). While History is off it says so and where to turn it on.
- **Reliability**: the figures for today, the API error strip for the last hour (a rose mark per failed request with its status), tools across all sessions with runs, typical and longest times, failures and asks, and stuck turns with their reasons in plain words, never the reply. With telemetry off it says so and shows the logs' tool count.
- **Sources**: five rows, each with a glyph and state, how fresh it is, what it gives and one button (Connect, Reconnect, Open folder, Turn on, Sign in, Check now, Show archived). A button takes the same path as its Settings → Claude button, confirmations and backups included, and what went wrong shows above the rows. On the sample nothing changes and it says so.
- Limits and History each scroll as one page that Tab reaches; the arrow, Page and Home/End keys scroll it, and a redraw keeps its place.

## The plain list

Settings → Claude → Show a plain list (v0.47.0) swaps the pit wall for text rows: the plan line, then each project with its totals, and under it one row per thread: its state chip, its name and a line of detail, then its tokens and replies. The same palette, chips and colours; no cars, garages or motion. Each row is a button named for screen readers (v0.51.0); a click, or Enter or Space, on a running thread brings its window forward. The chip column grows with the text so "NEEDS YOU" stays whole, and the list shows whole rows only and scrolls to the rest. Numbers read before the logs are counted show "≈". The source-health line stays under it.

## Keyboard and window

- **Tab** moves between thread rows, Medium's cars, garages and Go to it (v0.51.0). A 2-unit outline in the Text colour shows where focus is, only when the keyboard put it there. **Enter** or **Space** brings a running session's window forward or folds a garage, and a focused row scrolls itself into view.
- **Ctrl+1 to Ctrl+6** choose the views in tab order (v0.55.0); below 640 wide, any view but Garage says how wide the window must be.
- **Ctrl+G** opens the first session in the attention queue: the Needs you that has waited longest, then Stuck, Your turn and Silent (v0.52.0; Ctrl+1 until v0.55.0). With nothing waiting it says "Nothing is waiting on you." It never answers anything (decision 027).
- **Live's lanes** are buttons in tab order, named like "Fix the login timeout, needs you 4 minutes, api-server"; Enter opens the session's panel and Esc closes the drawer.
- **The header summary** is an assertive live region: a screen reader reads it when a session starts needing you, and says nothing when the count falls.
- **The taskbar** carries an Amber dot with the count while a session needs you (a 16-unit circle drawn in code, "9+" past nine, its tooltip "2 sessions need you"), which stays in Quiet mode, and flashes once per new Needs you when the window isn't in front, never in Quiet mode.
- **Settings → Window → Keep on top** keeps the window above others. Off by default.

The card has no heading of its own since v0.45.0: the window's header carries the summary and the period switch. Garages stay 48-unit targets; Settings controls are 32 units, a desktop window's size, now that the touchscreen minimums are gone (decision 026). The ledger may scroll; nothing else does.

## Pixel art

- Cars and garages are drawn in code (`PitArt`, `PitGraphics`) from lists of vector steps, never from images.
- Each sprite is rendered at its native pixel size (2 units a pixel under 150 wide, 3 from there), its alpha thresholded at 80, colours snapped to 17-unit steps, and shown scaled up with nearest-neighbour. Hard pixels, never blurred vectors.
- The car's class follows the thread's main model: a grand tourer for Opus, a sports car for Sonnet, a hot hatch for Haiku.
- Frames are cached by what they show.

## Motion

- One frame clock per card (`PitClock`), at the rate the fastest moving car needs (at most 24 fps; 20 while rolling), and only while something visible moves.
- The views share one more (v0.56.1): a view asks the window for frames while a car moves and lets go when all are still. It runs only while that view is on screen, the window isn't minimised and Quiet isn't keeping the cars still. Live's cars move on it since v0.57.2, as the Garage's do; the trails, curve and wall never move.
- It stops when the window is minimised. Minimised, the app still watches the session files (so Needs you still notifies and chimes) but doesn't scan logs or work out the view on the clock. In Quiet mode the cars keep still unless Settings → Claude says they may move, and the state swaps stay.
- Clicking a garage (or Enter or Space on it) rolls its cars in (tail lights toward you) or out (headlights toward you), about 1.2 s, instant in Quiet mode.
- No other animation: no spinners, no transitions, no hover effects.

## Rules

- **Every word follows the text size** (100–140%, v0.51.0): chips, details, captions, gauge labels and the plain list included. Layouts grow to hold the words rather than cut them.
- **Contrast:** Muted, Text, Amber and Glacier reach 4.5:1 on Page, Surface and Raised in every palette. Rows step back by colour, never by fading their words.
- **Fonts and icons:** Segoe UI (Light for big numerals, Semibold for values) and Segoe MDL2 Assets glyphs. No images or emoji for icons, no custom fonts.
- **No shadows, blurs or other effects.** Depth is a solid offset copy in the Page colour.
- **Numbers say where they come from.** "≈ $ at API prices" for estimates, "~" for an unpriced model, PARTIAL when telemetry saw fewer calls than the logs, a "PLAN · 2:17 AM" stamp in the header (never "LIVE"), and Sample pinned on the source-health line at every size. Token counts show "≈" while the logs are still being read and "—" with "NO LOGS" and the reason when they can't be, so loading and unavailable look different; no logs yet is "Logs: none yet", calm. Cache reads and replies take the same mark. Every pace is a time with "≈". Sessions that are working are "working" in the header and under a project's name alike.
- **Missing beats wrong.** A reading that isn't there shows "—" and says why, never a zero.

# Claude Dashboard design: the pit wall

The window is a race team's pit wall. Each project is a garage, each Claude Code thread a car, and the plan's limits are the car's gauges: a fuel tank for the 5-hour window, laps for the week, a rev counter for tokens a minute. Everything is calm until a car needs the driver; then that car, and only that car, shouts. The approved mock is [docs/design/claude-watcher-v2.html](docs/design/claude-watcher-v2.html), the built card is described in [docs/design/claude-watcher.md](docs/design/claude-watcher.md), and the technical record is [decision 017](docs/decisions/017-claude-pit-wall.md). The old Sticker Sheet design of the widget app is archived in [docs/archive/widget-screen/DESIGN.md](docs/archive/widget-screen/DESIGN.md).

## Surfaces

- **Palettes** (`Palettes.cs`): Midnight (default), Slate and Warm, and since v0.45.0 the light **Claude** palette in the spirit of the Claude app: warm paper `#FAF9F5`, soft black text `#1F1E1D` and a terracotta accent. Each sets the same keys (Page, Surface, Raised, Input, Track, ControlBorder, Selected, Muted, Text, Warning, Turn, OnAccent…), so the pit wall, the header and the Settings window change together. On paper, Amber and Glacier are darkened (`#9E5A0A`, `#27709A`) so they read as text.
- **Accent:** Lime `#BAF28B`, Glacier `#8ED8F8`, Lavender `#C5AEFF`, Amber or Terracotta (`#E08A68`; the Claude palette's default), each with a deeper shade on paper. It fills gauges, meters, the hour's burn line and the selected period.
- **Warning:** Amber `#F5C47D`, used for Needs you, meters from 80%, and the fuel lamp. Nothing decorative is Amber.
- **Project colours:** each garage and its cars take Lime, Glacier or Lavender from a hash of the project's name, so a project keeps its colour everywhere.

## Saying how a thread is

Each state has a still frame and, when motion is on, a stepped 8-frame cycle. A chip names the state in words, so colour is never the only cue.

| State | The car | The chip |
| --- | --- | --- |
| Working | wheels turn at 4–24 fps by tokens a minute, a pixel of body jitter, exhaust and dust | Working |
| Long tool run | revs on its brakes | Tool run |
| Needs you | hazards blink, headlights flash, a beacon on the garage | Needs you, in Amber |
| Your turn (a turn just finished) | brake lights glow | Your turn, in Glacier |
| Compacting | up on the jack, front wheel off | Compacting |
| Stuck or API errors | wheelspin in tyre smoke; the tip gives the reason in words | Retrying |
| Idle (Your turn for 30 minutes, or ready) | engine off, dimmed | Idle |
| Ended | lights off, a slate cover drops, once; the row at 62% | Ended |
| Resolved, Archived | under the cover, dimmed the most, last in the project | Resolved, Archived |

Two plan states sit on top of any car: a fuel lamp that blinks at 90% of the 5-hour limit, and glowing brake discs at the redline. A context ring appears at 60%, 80% and 95% of the context window.

**The one state allowed to shout is Needs you.** The window's own 3-unit edge turns Amber (Glacier, the `Turn` colour, while a thread waits for your reply), the title bar says "Claude needs you · Claude Dashboard" so the taskbar shows it, and the pit stop strip names the thread with Go to it.

## Window sizes

The window is an ordinary one, at least 360 × 300, with a 56-unit header (the app name, the live summary, 5 h / Today / Week, Quiet and Settings; the words fold away as it narrows). When the whole summary won't fit beside them, it first shortens to the two most urgent parts and the thread count, then takes a line of its own (the header becomes 84 units) unless that would cost the card its design or Small its 5-hour meter; then it shortens to the most urgent part with the thread count, then that part alone, and it never breaks mid-word. Under the card is the source-health line (v0.50.0), 24 units at 100% text: "Sample" on fixtures, then Hooks · Logs · Telemetry · Plan, each a Segoe MDL2 glyph and words that say whether it's fine, still waiting ("Logs counting…"), old ("Plan 12 min old", Amber), off and why ("Hooks off: not connected") or failed ("Telemetry off: port 43832 in use", Amber). It never folds away: on a narrow window the reasons shorten, then the sources that are fine keep only their glyph, and every word stays whole; the full reason is in each tooltip. The card fills the rest, picks its design from the space it has, and switches live while the window is resized. The thresholds are `CardSizing.Claude` at the bottom of `ClaudeCard.cs`, in device-independent pixels, checked at 360 × 300, 640 × 420, 1000 × 600 and 1600 × 900, and at 150% display scaling (`tests/Shell/ShellTests.cs`).

- **Large** (card from 640 × 420): the plan row (tank, week laps, and the rev counter from 860 wide); the pit stop strip while a thread needs you; the ledger of garages and cars, which scrolls; and from 1000 wide a side panel with the 5-hour lanes and four facts.
- **Medium:** two mini plan tiles over the top garages, with up to three cars inline.
- **Small** (under 360 × 280): the car that needs you, or the busiest running one, over the 5-hour meter.

Where the rev counter shows, its tile carries the last 7 days (v0.47.0): seven small bars, today in the accent, and a tick for what last week had used by this time of day. They come from the daily summary files, so they're gone while History is off; the tip says what they count, and the sample says it's a sample.

## The plain list

Settings → Claude → Show a plain list (v0.47.0) swaps the pit wall for text rows: the plan line, then each project with its totals, and under it one row per thread: its state chip, its name and a line of detail, then its tokens and replies. The same palette, chips and colours; no cars, garages or motion. Each row is named for screen readers, and a click on a running thread brings its window forward. Numbers read before the logs are counted show "≈".

The card has no heading of its own since v0.45.0: the window's header carries the summary and the period switch. Garages stay 48-unit tap targets; Settings controls are 32 units, a desktop window's size, now that the touchscreen minimums are gone (decision 026). The ledger may scroll; nothing else does.

## Pixel art

- Cars and garages are drawn in code (`PitArt`, `PitGraphics`) from lists of vector steps, never from images.
- Each sprite is rendered at its native pixel size (2 units a pixel under 150 wide, 3 from there), its alpha thresholded at 80, colours snapped to 17-unit steps, and shown scaled up with nearest-neighbour. Hard pixels, never blurred vectors.
- The car's class follows the thread's main model: a grand tourer for Opus, a sports car for Sonnet, a hot hatch for Haiku.
- Frames are cached by what they show.

## Motion

- One frame clock per card (`PitClock`), at the rate the fastest moving car needs (at most 24 fps; 20 while rolling), and only while something visible moves.
- It stops when the window is minimised. Minimised, the app still watches the session files (so Needs you still notifies and chimes) but doesn't scan logs or work out the view on the clock. In Quiet mode the cars keep still unless Settings → Claude says they may move, and the state swaps stay.
- Tapping a garage rolls its cars in (tail lights toward you) or out (headlights toward you), about 1.2 s, instant in Quiet mode.
- No other animation: no spinners, no transitions, no hover effects.

## Rules

- **Fonts and icons:** Segoe UI (Light for big numerals, Semibold for values) and Segoe MDL2 Assets glyphs. No images or emoji for icons, no custom fonts.
- **No shadows, blurs or other effects.** Depth is a solid offset copy in the Page colour.
- **Numbers say where they come from.** "≈ $ at API prices" for estimates, "~" for an unpriced model, PARTIAL when telemetry saw fewer calls than the logs, a "PLAN · 2:17 AM" stamp in the header (never "LIVE"), and Sample pinned on the source-health line at every size. Token counts show "≈" while the logs are still being read and "—" with "logs unreadable" when they can't be, so loading and unavailable look different. Cache reads and replies take the same mark. Sessions that are working are "working" in the header and under a project's name alike.
- **Missing beats wrong.** A reading that isn't there shows "—" and says why, never a zero.

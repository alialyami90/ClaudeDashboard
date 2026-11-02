# Claude Dashboard design: the pit wall

The window is a race team's pit wall. Each project is a garage, each Claude Code thread a car, and the plan's limits are the car's gauges: a fuel tank for the 5-hour window, laps for the week, a rev counter for tokens a minute. Everything is calm until a car needs the driver; then that car, and only that car, shouts. The approved mock is [docs/design/claude-watcher-v2.html](docs/design/claude-watcher-v2.html), the built card is described in [docs/design/claude-watcher.md](docs/design/claude-watcher.md), and the technical record is [decision 017](docs/decisions/017-claude-pit-wall.md). The old Sticker Sheet design of the widget app is archived in [docs/archive/widget-screen/DESIGN.md](docs/archive/widget-screen/DESIGN.md).

## Surfaces

- **Palettes:** Midnight (default), Slate and Warm, each with its own Page, Surface, Raised, Input, Track, ControlBorder, Selected, Muted and Text colours (`DashboardAppearance.cs`). Phase 4 adds a **Claude** palette in the spirit of the Claude app: warm paper, soft black text and a terracotta accent, with Amber kept for warnings.
- **Accent:** one of Lime `#BAF28B`, Glacier `#8ED8F8` or Lavender `#C5AEFF`. It fills gauges, meters, the hour's burn line and the selected period.
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

**The one state allowed to shout is Needs you.** Today it turns the card's border Amber. From phase 4 the window's own edge turns Amber for Needs you and Glacier for Your turn, and the pit stop strip names the thread with Go to it.

## Window sizes

The card picks its design from the space it has, and switches live while the window is resized. The numbers are today's `CardSizing.Claude` thresholds; phase 4 moves them into `ClaudeCard` and checks them at 100% and 150% display scaling.

- **Large** (from 640 × 420, the window's minimum): heading with the live summary and the 5 h / Today / Week switch; the plan row (tank, week laps, and the rev counter from 860 wide); the pit stop strip while a thread needs you; the ledger of garages and cars, which scrolls; and from 1000 wide a side panel with the 5-hour lanes and four facts.
- **Medium:** two mini plan tiles over the top garages, with up to three cars inline.
- **Small** (under 360 × 280): the car that needs you, or the busiest running one, over the 5-hour meter.

Tap targets keep the sizes they have now (48-unit garages and period buttons), but these are no longer touchscreen requirements. The window may scroll its ledger; nothing else scrolls.

## Pixel art

- Cars and garages are drawn in code (`PitArt`, `PitGraphics`) from lists of vector steps, never from images.
- Each sprite is rendered at its native pixel size (2 units a pixel under 150 wide, 3 from there), its alpha thresholded at 80, colours snapped to 17-unit steps, and shown scaled up with nearest-neighbour. Hard pixels, never blurred vectors.
- The car's class follows the thread's main model: a grand tourer for Opus, a sports car for Sonnet, a hot hatch for Haiku.
- Frames are cached by what they show.

## Motion

- One frame clock per card (`PitClock`), at the rate the fastest moving car needs (at most 24 fps; 20 while rolling), and only while something visible moves.
- It stops when the window is minimised or the card hidden. In Quiet mode the cars keep still unless Settings → Claude says they may move, and the state swaps stay.
- Tapping a garage rolls its cars in (tail lights toward you) or out (headlights toward you), about 1.2 s, instant in Quiet mode.
- No other animation: no spinners, no transitions, no hover effects.

## Rules

- **Fonts and icons:** Segoe UI (Light for big numerals, Semibold for values) and Segoe MDL2 Assets glyphs. No images or emoji for icons, no custom fonts.
- **No shadows, blurs or other effects.** Depth is a solid offset copy in the Page colour.
- **Numbers say where they come from.** "≈ $ at API prices" for estimates, "~" for an unpriced model, PARTIAL when telemetry saw fewer calls than the logs, a "LIVE · AS OF" stamp on Large, and a Sample flag on fixtures.
- **Missing beats wrong.** A reading that isn't there shows "—" and says why, never a zero.

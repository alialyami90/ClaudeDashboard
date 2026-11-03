# 030: The lap chart: six views, with Live as home

Status: decided (Ali, 3 Nov 2026), when Ali asked the team to build the lap chart from "Claude Dashboard app: lap chart build brief". It shipped in steps, v0.54.0 to v0.58.3. Windows only. Ali confirmed the six defaults below on 3 Nov 2026 and approved the tags v0.53.1 to v0.58.3.

## Context

[Decision 029](029-claude-live-status-board.md) made 5 h and Today a live status board, but the window was still a garage of rows. Rows answer "what is each session doing now". They don't answer "how long has it been doing it" or "when do I hit the wall".

The brief puts every session on one time axis. The past is on the left, from the last 100 minutes. The future is on the right, up to the next 5-hour reset. Each car rides the now line and leaves a trail of its states. The plan's 5-hour use curve sits above the lanes, and where its projection meets 100%, an Amber wall drops through every lane. The limit arrives on screen before the sessions reach it.

The prototype is the bottom artboard of "Claude Dashboard app layouts". Its numbers are made up.

## Decision

### Six views behind a real tab list

| Key | View | What it shows | Where the data comes from |
| --- | --- | --- | --- |
| Ctrl+1 | **Live** (home) | The lap chart, the 5-hour curve and wall, "What just happened" and the selected-session panel | Bridge session files, logs, telemetry, plan meters, and the in-memory timeline |
| Ctrl+2 | **Garage** | The pit wall as it was at v0.53.x, with its own 5 h / Today / Week switch | As before |
| Ctrl+3 | **Limits** | The 5-hour window from start to reset: use curve, projections at the last 30 minutes' pace and at today's average, and the onset of heavy use. Also week laps per day with pace, per-model weekly windows, extra usage, spend limit, and tokens in this window by project | `ClaudeLimits`, and the in-memory plan reading ring |
| Ctrl+4 | **History** | 8-week calendar, hour-of-day profile against the same hour last week, the last 7 days, and projects this week | `ClaudeHistory` day files (numbers and project folders, kept a year since v0.59.0; past days built from Claude Code's logs by `ClaudeBackfill`) |
| Ctrl+5 | **Reliability** | Tool failure rate, API errors in the last hour, asks and denials, compactions, edits accepted and rejected; the API error strip; tools across all sessions; and stuck turns today with their reasons | Telemetry tool runs and API errors, the logs' tool entries, and the bridge's error fields |
| Ctrl+6 | **Sources** | Hooks, Logs, Telemetry, Plan meters and Claude app threads: each with its state, freshness, what it gives, and one action | Connector, watcher, telemetry and sign-in states |

- **The header** always carries the live summary, including "≈ limit 3:21 am", or "Limit reached" in Amber once the window is full. It is an assertive live region. It counts Garage's chosen period while Garage is on screen (and below 640 wide), and the last 5 hours on the other views.
- **The source-health line** from decision 029 sits at the right end of the tab row from 640 wide, showing the four source ages, so a missing source can be seen from every view. It is one control, moved there rather than copied, and belongs to no single view. Below 640, where there are no tabs, it stays under the card. "Sample" stays first in both places.
- **Ctrl+G** opens the session that has waited longest, in the attention queue's order. It was Ctrl+1 in v0.52.0 and moves to make room for the views.

### The lap chart (Live)

- **Lanes in urgency order:** Needs you, Stuck, Retrying, Silent, Your turn, Compacting, Working, Idle, then Ended. Nothing that needs you sits below the fold.
- **Which sessions get a lane:** every running session, and each ended one whose trail is still on the axis. Live follows Settings → Claude's archived choice, so while archived threads are hidden they get no lane and no feed line (v0.56.1). It doesn't apply hide-idle, because Idle is its own lane. Hidden projects get neither.
- **Lane encodings:**

  | State | How the lane draws it |
  | --- | --- |
  | Working | Solid project colour (Lime, Glacier or Lavender by project hash) |
  | Needs you | Amber block |
  | Stuck | Hollow, with an Amber outline |
  | Your turn | Hollow, with a 1.5-unit Glacier outline |
  | Idle and Silent | 4-unit grey line, `#5B6474` |
  | Compacting | Stripes in the project colour |
  | API error | 2-unit rose mark, `#F09A9A` (`#A93B35` on the light palettes, for 4.5:1) |

- **Lane labels.** Silent and Retrying say their names in the lane label, Silent in Amber and Retrying in rose.
- **The now line, the future and the wall.** The car rides the now line. After now, the Input colour fills the background behind the lanes, so past and future read differently without a label. The wall is a 2-unit Amber line at the projected limit, labelled once at the top. It shows only while a projection exists and only if the limit falls before the reset.
- **The watch-start edge.** Lanes start when the app began watching, and that edge is drawn and labelled. Nothing earlier is invented.
- **The selected-session panel.** It shows:
  - what the session asks, and Go to it;
  - its last sign of life and its current tool;
  - tasks and helpers;
  - context, with ticks at 60, 80 and 95%;
  - today's tokens, lines and asks;
  - its tool runs.

  From 1000 wide it is a 360-unit panel. From 640 to 1000 it is a drawer that opens when a lane is clicked, and lane labels narrow to 200.
- **Motion.** Working cars keep the 1-pixel jitter and turning wheels, and a Needs-you car blinks its hazards (v0.57.2). Everything else is still, and Quiet with "keep the cars still" stops it all, as on the Garage. The view asks the window's one frame clock for frames (`ClaudeViewContext.Frames`) only while a car moves; it runs only while that view is on screen and the window isn't minimised.

### Under 640 wide (Small)

The window is unchanged: the car that needs you over the 5-hour meter, plus the "≈ limit" time. There are no tabs.

### New engine work (all in memory)

- **State timeline:** each session's state changes, with their times, for the last 5 hours.
- **Last sign of life:** the latest of the bridge's events, the last log line and the last telemetry event. Hook events are counted by the `heard` time from v0.49.0, so the status line's refresh can't make a hung turn look alive. **Silent** means Working with no sign of life for 5 minutes. It is a display state, not a new hook state.
- **Retrying:** an API error with retries still pending.
- **Plan reading ring:** the last 5 hours of plan readings (percent and time). "≈ limit" comes from the last 30 minutes' pace, and from today's average. Today's average is turned from tokens an hour into percent an hour at this window's percent per token, so its tip says it is worked out from tokens. **There is one "≈ limit" in the app: `LimitAtPace`.** The header, Live's wall and Limits use it from the start. The Garage tank, Medium's tile and Small's meter line used v0.53.0's token-based ETA, and moved to it in v0.57.2, so no screen shows two different limit times or shows one without 3 readings. **"≈ limit" always means `LimitAtPace`.** With fewer than 3 readings there is no projection: Live and Limits say why ("Only 2 plan readings so far"), and the tank, Medium and Small fall back to the window's own pace so far without the word "limit", as "≈ N% by reset" or, past 100%, "≈ over by reset". **Limit reached** (v0.56.1): once the newest reading is at 100%, `LimitAtPace.Reached` is set and `LimitAt` is null, and every place that would show "≈ limit" says "Limit reached" in Amber instead, never a time that has passed. "≈ limit" is the one word for it everywhere; v0.53.0's "≈ empty" is gone.
- **Typical tool time:** the median of telemetry's tool run times, per tool, per session and across all sessions. With telemetry off, the table says so and shows only the log counts.
- **Event feed:** the last 50 state changes, tool failures, API errors and compactions. It holds names and times only.

## The six defaults (confirmed by Ali, 3 Nov 2026)

1. **Current tool.** The hook does not store it. Now keeps v0.53.0's tool name from telemetry, in memory, and labels it "last" once that tool has finished. Nothing new is written to the session file. Storing the name on PreToolUse would add one field and a write per tool change; it isn't done, and doing it would take a new decision.
2. **Silent** stays at 5 minutes (`ClaudeAttention.SilentAfter`).
3. **Retrying gets rose** (`#F09A9A`) as a state colour, recorded in DESIGN.md. Rose was already used for lines removed. It now also means API errors and Retrying, and nothing decorative is rose.
4. **Shipping in steps**, one PR and version per package, in version order:
   - v0.54.0: engine (Watcher);
   - v0.55.0: tab shell (Shell);
   - v0.56.0: Live (Watcher);
   - v0.56.1: "Limit reached" from the engine (Watcher), and the views' frame clock and the header's "Limit reached" (Shell);
   - v0.57.0: Limits and History (Builder);
   - v0.57.1: Home and End on the Limits and History pages, and the meter's own percent at the limit (Builder);
   - v0.57.2: one "≈ limit" everywhere (the Garage tank, Medium and Small move to `LimitAtPace`), the moving Live cars, the time row and trail key, and archived lanes (Watcher). Main already had v0.57.0, so this went out as v0.57.2 rather than v0.56.2;
   - v0.58.0: Reliability and Sources (Demolition);
   - v0.58.1: the keyboard focus ring on the Limits and History pages, inset off the heading (Builder);
   - v0.58.2: Live's rose shares `ClaudeReliability.Rose`, so it is `#A93B35` on paper too (Watcher);
   - v0.58.3: these docs (Scribe).
5. **The garage pit wall stays** as its own Garage tab, keeping 5 h / Today / Week. Ali confirmed keeping it (3 Nov 2026).
6. **Keys:** Ctrl+1 to Ctrl+6 switch views in tab order, and "open the session that waited longest" moves from Ctrl+1 to Ctrl+G. The brief says Ctrl+1 to Ctrl+5 because it has no Garage tab.

## Rules this keeps

- **In memory only.** The timeline, the plan ring, tool times and the feed hold names, states and times. They never hold prompts, replies, commands or file contents, and nothing new is written to disk.
- **No new polling.** Everything arrives on the existing watchers, the 1 s heartbeat and the 5-minute plan check. Nothing redraws while hidden or minimised, and the frame clock runs only while a visible car moves.
- **Honest states.** No projection from fewer than 3 readings, and the reason is shown. "≈" goes on every estimate, "Sample" is visible at every size, and the watch-start edge is labelled.
- **Views start no timers.** Each view implements Shell's `IClaudeView` (Build, Refresh with the snapshot and the time, Visible), and `ClaudeViews.Create(name)` finds it by name.
- **Never answers** a permission prompt (decision 027). Go to it, Enter on a lane, and Ctrl+G only bring a session forward.
- **Colours.** Amber is only for Needs you and warnings, and rose is only for API errors and Retrying. Use Segoe UI and MDL2, draw cars in code, and use no shadows.
- **Accessibility.** Lanes are buttons in tab order, and Enter opens the panel. Each lane has a name, such as "Fix the login timeout, needs you 4 minutes, api-server". Every size follows the text setting, and no text is faded by whole-row opacity.
- **Unchanged:** the plain list, Settings, Connect and Disconnect, the sign-in, the toast rules and the export files.
- Nobody runs Impeccable.

## Verified

- **Checks.** CI was green on every PR. Each view has its own checks: `LapChartTests` (Live), `LimitsHistoryTests`, `ReliabilitySourcesTests`, and the shell's tabs, keys, frame clock and header checks in `ShellTests`. Each view renders at 640 × 420, 1000 × 600 and 1600 × 900, at 100% and 140%, and Live at 800 × 500 too. The checks point at throwaway folders and never touch the PC's real Claude Code.
- **Renders on Ali's PC** (`artifacts\review\`, through `--render-view` with the sample), checked by the Lead. Each finding below says where it was fixed:
  - **v0.55.0 (tab shell).** Two collisions from v0.51.0 showed up. At 140% the tank gauge's "100" read "T00", and the Garage side panel's times ran into each other. Both were fixed in v0.57.2.
  - **v0.56.0 (Live).** This render found:
    - no time axis;
    - no key for the trails;
    - the curve's percent at the left end of the dashed top line, where it read as the 100% mark;
    - side-panel numbers cut at 140%;
    - the drawer's last line half cut at 800 × 500;
    - a view tab with no screen-reader name (fixed in v0.56.1);
    - the header's "≈ limit" and the tank's "≈ empty" differing by minutes and in words (fixed in v0.57.2, which made one "≈ limit").

    The rest were fixed in v0.57.2.
  - **v0.57.0 (Limits and History).** Found and fixed before the merge:
    - Tab never reached the page, so the week and the last 7 days were out of a keyboard user's reach. Each page is now a named tab stop.
    - The week's laps mixed precision ("7.4%", "0.0%"). They now show whole percents, and "<1%" for a little use.

    With the meter set to 100%, Garage still said "≈ empty before reset". v0.57.2 fixed that. Home and End going sideways was found later, by Demolition on #24 and the v0.58.0 render. v0.57.1 fixed it.
  - **v0.57.2.** The render harness set the 5-hour meter to 100%. The first render (fe37c7d) found three problems:
    - Live still predicted while the meter read 100% but Reached was false;
    - Garage said "Limit reached" twice;
    - the trail key's Working swatch took a project colour.

    All three were fixed before the merge (48cd355). Live, the tank, Medium and Small now say "Limit reached" once, in Amber, with no wall. The header wasn't rendered at 100%; its check covers it. As on the Garage, Quiet stops Live's cars only with Settings → Claude's "keep the cars still" on. That's by design.
  - **v0.58.0 (Reliability and Sources), first render.** Found and fixed before the merge:
    - Tool failures disagreed with the tools table;
    - the page's focus ring couldn't be seen;
    - off Sources buttons were faint on the Claude palette;
    - the two "Asked you" counts didn't say which was which.

    The second render (v0.58.0b) was clean except for one thing: the focus ring touched the heading's "R". v0.58.1 fixed that with `PageInset`. No turn text appeared, rose read well on the Claude palette, and nothing was cut at 140%.
  - The tab order, with Quiet and Settings before the page, was accepted as it is.
- **Not verified live:**
  - a screen reader reading the lane names and the header's "Limit reached";
  - moving Live cars and the frame clock over a long day of real sessions;
  - Retrying and Silent lanes from real API errors and real hangs;
  - "Limit reached" from a real full window, rather than the harness's 100% meter, and the header at 100%, which only its check covers;
  - Sources' Connect, Reconnect and Sign in clicked on the real PC (the checks drive them through a stand-in for `Act`).

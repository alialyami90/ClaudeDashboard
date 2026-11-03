# Product

<!-- impeccable:product-schema 1 -->

## Platform

desktop

Windows 10 and 11, x64. Android is a roadmap item (decision 026).

## Users

People who run Claude Code on a Windows PC, often several sessions at once, and want to know without switching windows which session needs them, which is working, and how much of their plan is left.

Built by and for Ali Alyami first, and meant to work for anyone with Claude Code on Windows, through the Claude app's Code tab or the command line.

## Product Purpose

Claude Dashboard is one window that watches every Claude Code session on the PC. Each project is a pit garage and each thread a pixel-art race car whose motion says its state: working, needs you, your turn, compacting, stuck, idle or ended. Above them sit the plan's 5-hour and weekly limits, with the pace said as a time ("≈ limit 3:21 am"). In the last 5 hours and today the window is a live status board: what waits on you first, and for each thread how long it has been in its state, its last sign of life, the tool it's in and how full its context is. The week is the ledger: tokens, cache reads, replies or ≈ $, model mix and lines changed per project and per thread ([decision 029](docs/decisions/029-claude-live-status-board.md)). From 640 wide the home view is Live, a lap chart: every session is a lane on one time axis from the last 100 minutes to the 5-hour reset, its car on the now line with a trail of its states, and an Amber wall across every lane where the plan's limit will be reached. Garage, Limits, History, Reliability and Sources are tabs beside it ([decision 030](docs/decisions/030-claude-lap-chart.md)).

Success means the user stops tabbing through terminals to find the session that is waiting on them, never hits a limit by surprise, and trusts every number on the screen.

## Positioning

A native Windows app (WPF, .NET Framework 4.8) with no WebView, browser, cloud service or companion server. It reads what Claude Code and the Claude app already write on the PC, adds a small hook exe and a loopback telemetry receiver that Settings → Claude → Connect sets up (and Disconnect takes out exactly), and asks Anthropic for nothing but the plan's usage meters, through its own sign-in.

## What it reads

- **Claude Code's hooks and status line,** through `ClaudeHook.exe`, which writes one small file per session to the bridge folder (`%LOCALAPPDATA%\ClaudeDashboard\claude`): state, project folder, model, title, the notification's own message, cost and context size.
- **Claude Code's own session list and logs,** read-only: token counts per reply, models, times, tool names and lines changed. It also takes the first line of a thread's first request (about 60 characters) as the thread's name when nothing better exists, and keeps it in memory only.
- **The Claude app's thread records,** read-only: thread title, folder, archived flag and last activity.
- **Claude Code's OpenTelemetry,** sent to 127.0.0.1 only: event names, counts, costs, tool decisions and the name of the tool a call is in (shown as Now, kept in memory only). Prompts, replies and tool details (inputs, commands, arguments) stay off.
- **The plan's usage meters** from `api.anthropic.com`, every 5 minutes by default while signed in with the app's own **Sign in with Claude** (`user:profile` only). Anthropic's [legal page](https://code.claude.com/docs/en/legal-and-compliance) says other apps may not offer Claude.ai login or store its tokens; Ali was told and kept the sign-in as it is (3 Nov 2026).
- **Widget Screen's folder** (`%LOCALAPPDATA%\WidgetScreen`): read once, when the first run offers to bring its Claude settings, sign-in and bridge over. Since v0.48.0 the hook also writes its session files and `limits.json` into `WidgetScreen\claude` when that folder exists, so Widget Screen stays current (decision 028); nothing else there is written.

## What it keeps

- Its settings, its own sign-in and the bridge folder, in `%LOCALAPPDATA%\ClaudeDashboard`.
- A daily summary per day (on by default, kept a year since v0.59.0) in `claude\history\`: tokens, cache reads, replies, threads, the API value, how often a session needed you and the longest wait (counted while the window is open), by hour and by project. Numbers, project folders and the folder's own name only: no prompts, replies, titles, thread names or session ids. Past days the dashboard didn't see are built once from Claude Code's logs (`ClaudeBackfill`, never today, never over a complete day) and marked "from logs", because the logs can't say when a session needed you or how long it waited. Turning it off deletes the files. The History calendar shows the last 8 weeks of them.
- In memory only, for the lap chart (decision 030): each session's state changes over the last 5 hours, the plan readings of the 5-hour window, tool run times and the last 50 lines of "What just happened". Names, states and times only, gone when the app closes.
- Nothing else. Copy summary and Save CSV write only when the user asks, and only what the ledger shows: dates, projects, thread names the user, Claude Code or the Claude app gave (otherwise an id label), models, counts and costs.

## What it never does

- It never stores or sends a prompt, a reply or a file's contents.
- It never reads Claude Code's credentials. Its own sign-in is kept encrypted with DPAPI in `claude-session.dat`, and the tokens go only to Anthropic's hosts.
- It never listens on the network. The telemetry receiver binds to 127.0.0.1 and takes JSON only.
- It never spends credits. The usage endpoint reads the meters; nothing calls a model.
- It never answers a permission dialog or acts in a session (decision 027). Needs you brings the session's own window forward, and the user answers there.
- It never overwrites the user's own Claude Code settings: Connect merges into `settings.json` with a dated backup, leaves the user's `OTEL_*` values alone, and says when managed settings override it. Sources → Keep logs a year changes one value, `cleanupPeriodDays` to 365, only on that press and with the same backups; Undo and Disconnect put back what was there only while the value is still the dashboard's own (`logs-kept.json` in the bridge folder).

## The honest-numbers promise

- Costs come from Claude Code's own telemetry first, then the published price table, and say "≈ $ at API prices". Replies from a model the table doesn't know show "~" with a tip naming the model, never a guess.
- Every reply is counted once, by its request id, however many sources report it.
- A missing reading says why ("Telemetry off: port 43832 in use", "Sign in for plan limits"). Loading, unavailable, disconnected and working always look different: "≈" while the logs are counted, "—" with the reason when they can't be read, "Logs: none yet" before Claude Code's first session.
- The source-health line (beside the tabs from 640 wide, under the card below) names where every number comes from (Hooks · Logs · Telemetry · Plan) and whether each source is fine, waiting, old, off or failed, at every window size. The plan stamp says when ("PLAN · 2:17 AM"), never "LIVE".
- Sample data is always labelled, pinned on the source-health line at every size. `--render-claude-sample` draws a fixture that says it is one.
- Every estimate says "≈", the pace included. There is one "≈ limit" for the 5-hour window, from the last 30 minutes of plan readings: it needs 3 readings, says why when it has fewer, and says "Limit reached" rather than a time that has passed. Today's average is worked out from tokens, and says so.
- The lap chart starts where the app began watching, at a labelled edge. Nothing earlier is filled in.

## Capabilities and Constraints

- **One window on any monitor.** It opens where it was closed, at the size it was left, and behaves like any other Windows app: it takes focus, sits in the taskbar and closes fully. While a session needs you the taskbar button carries an Amber dot with the count and flashes once per new one (never in Quiet mode), and Settings → Window → Keep on top keeps it above other windows. The card picks Small, Medium or Large from the window's size, and the window is at least 360 × 300.
- **Light on resources.** One 1-second heartbeat; file watchers instead of re-reading; the frame clock runs only while a car visibly moves, never while minimised or in Quiet mode (unless the user asks the cars to move in Quiet mode).
- **Local.** Preferences are local XML, written atomically. Nothing about the user's sessions leaves the PC.
- **Not built yet:** the cloud garage (package 4.3), which waits on a look at the Claude app's cloud thread records.
- **Undecided:** the Android companion's scope and timing, and whether 1.0 needs anything beyond package 4 and that companion.

## Brand Commitments

- Name **Claude Dashboard**, author **Ali Alyami**. The exe is `ClaudeDashboard.exe`; the logo is redrawn in phase 1 as a car in front of a garage.
- Claude Dashboard is an independent app built on Claude Code's documented hooks, status line and telemetry. It is not made by Anthropic.
- The visual language is in [DESIGN.md](DESIGN.md): the pit wall, its palettes, Amber for Needs you and Glacier for Your turn, and the pixel-art rules.
- System fonts only: Segoe UI and Segoe MDL2 Assets. No images or emoji as icons, no custom fonts. Cars and garages are drawn in code.
- Voice: short and plain. The card speaks when something is true or wrong, and says why when a reading is missing.

## Evidence on Hand

- The approved pit wall mock: [docs/design/claude-watcher-v2.html](docs/design/claude-watcher-v2.html).
- The watcher's design note and records: [docs/design/claude-watcher.md](docs/design/claude-watcher.md) and decisions 014–020 and 022–029 in `docs/decisions/`.
- The Claude checks in `tests/Claude/` and the labelled `--render-claude-sample` renders.
- No user research or usage numbers exist beyond Ali's own use. Real telemetry for every event type, a real compaction and the sign-in on other people's plans are not all verified; don't claim them.

## Product Principles

1. **See who needs you.** A session waiting on the user is the loudest thing on screen, and one click (or Ctrl+G) brings its window forward. Nothing that waits on the user hides below the fold without a line saying so.
2. **Trust the sources.** Show what Claude Code and the Claude app say, in their order of authority, and say plainly when a source is missing.
3. **Honest numbers.** Never a guess shown as a fact; every estimate says it is one.
4. **Cheap to watch.** It runs all day beside work and games, so idle means idle.
5. **Read, don't reach.** It watches local files and one loopback port, writes only its own hook entries, and touches nothing it can't take back out.

## Accessibility & Inclusion

- State is never colour alone: each car state has its own motion and a still frame, and every chip names its state in words.
- Quiet mode stops the loops and keeps the state swaps. Text size goes from 100% to 140%, and every word on the card follows it.
- The keyboard reaches every thread row, car and garage: Tab moves between them, Enter or Space brings a session forward or folds a garage, and each says its state, ended ones included. Live's lanes are buttons named like "Fix the login timeout, needs you 4 minutes, api-server". Ctrl+1 to Ctrl+6 choose the views, and Ctrl+G opens the session that has waited longest. When a session starts needing you, a screen reader announces the header's summary at once.
- Ended and parked threads step back by colour, not by fading, so their words keep 4.5:1 contrast on every palette.
- The plain list (Settings → Claude) shows the same projects, threads, states and numbers as text rows, each named for screen readers, with nothing moving. Click a row to bring its session forward.
- Windows notifications for Needs you, Stuck and long turns reach the user when the window is behind others; they are on by default and silent in Quiet mode.

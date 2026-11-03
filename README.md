# Claude Dashboard

A Windows desktop app that watches every Claude Code session on your PC. Each project is a pit garage and each thread a pixel-art race car: you can see at a glance which session is working, which one needs you and whose turn it is, how much of your plan's 5-hour and weekly limits is left, and what each project and thread has used.

It is native WPF on .NET Framework 4.8, with no WebView, no packages and no server. It reads what Claude Code and the Claude app already write on your PC, and nothing about your sessions leaves it. Claude Dashboard is an independent app, not made by Anthropic.

> **Formerly Widget Screen.** Claude Dashboard grew out of Widget Screen, a touchscreen dashboard with 19 widgets, and was cut down to the Claude watcher in six releases, v0.42.0 to v0.47.0 ([decision 026](docs/decisions/026-claude-dashboard.md)). Coming from Widget Screen? See [Moving over from Widget Screen](#moving-over-from-widget-screen).

## What it shows

From 640 wide the window has seven views, one tab each ([decision 030](docs/decisions/030-claude-lap-chart.md), [decision 031](docs/decisions/031-insights.md)): **Live** (home), **Garage**, **Limits**, **History**, **Reliability**, **Sources** and **Insights**. Ctrl+1 to Ctrl+7 choose them. Narrower, the window is the Garage alone.

- **Live: the lap chart.** Every session is a lane on one time axis, from 100 minutes ago to the 5-hour reset, most urgent first. Its car rides the now line with a trail of its states behind it, so you can see how long each has been working, waiting or silent. Above the lanes is the plan's 5-hour curve; where its projection hits 100% before the reset, an Amber wall drops through every lane at "≈ limit 3:21 pm". Below, "What just happened" lists the latest changes, and clicking a lane shows that session: what it asks, its last sign of life, the tool it's in, context, today's numbers and its tool runs.
- **Limits.** The 5-hour window from start to reset with its "≈ limit" at the last 30 minutes' pace and at today's average, the week as laps by day, the weekly windows and tokens by project.
- **History.** Eight weeks of days, your hours compared with last week, and the last 7 days. Days from before the dashboard was watching are built from Claude Code's logs and marked "from logs".
- **Reliability.** Tool failures, API errors in the last hour, asks, compactions and stuck turns with their reasons.
- **Sources.** Hooks, logs, telemetry, the plan meters and the Claude app's threads: each one's state, how fresh it is, what it gives, and one button to fix it. Under them, History from the logs says how long Claude Code keeps its logs (30 days by default) and how many past days were built, with **Keep logs a year**, which sets Claude Code's `cleanupPeriodDays` to 365 with a dated backup, and Undo.
- **Insights.** The long view of Claude Code's logs over 7 days, 30 days or everything kept, against the period before: models (share, tokens, ≈ $, thinking), effort levels per model, the most and least used tools with errors and ≈ times (filter by model), MCP servers, skills and plugins, projects with sessions and lines, when you work (hour × weekday), and health (API errors, retries, compactions, permissions, how replies ended).

The Garage view, and the whole window below 640 wide:

- **Garages and cars.** One garage per project, one car per thread from the last 5 hours, today or the week. The car's class follows its main model (grand tourer for Opus, sports car for Sonnet, hot hatch for Haiku), and its motion says its state: wheels turning while it works, hazards when it needs you, brake lights on your turn, up on the jack while compacting, wheelspin when it's stuck or retrying, a cover when it ends. Click a garage (or Tab to it and press Enter or Space) to roll its cars in or out.
- **Your plan.** The 5-hour window as a fuel tank and the week as seven laps, each saying when in plain time: "≈ limit 3:21 am, 37 min before reset", "≈ 84% by reset" or "Limit reached". There is one "≈ limit" in the app: the header, the tank, Live and Limits all show the same time, from the last 30 minutes of plan readings, and only once there are 3 of them. Tokens a minute and the last hour's burn line sit beside them.
- **Pit stop.** A short queue of what waits on you: a session that needs you, one that's stuck, a reply you've owed for 10 minutes, or a working session that has gone silent for 5. One at a time with "1 of 3" and Next, what it wants ("Wants to use Bash · waiting 2 min") and **Go to it**, which brings its window forward. Projects are ranked the same way, and when the list is longer than the window a line under it says what's below ("+7 below · 1 stuck").
- **Live rows (5 h and Today).** For each thread: how long it has been in its state, its last sign of life ("just now", "silent 8 min"), the tool it's in now by name ("Bash · 2 min"), a thin context bar, its tokens and lines.
- **The ledger (Week).** Tokens, cache reads, replies or ≈ $ at API prices, model mix and lines added and removed, per project and per thread.
- **Where the numbers come from.** A line beside the tabs (under the card below 640 wide) names each source, Hooks · Logs · Telemetry · Plan, and says whether it's fine, waiting, old or off and why ("Plan 12 min old", "Hooks off: not connected"). Sample data says so there at every size.
- **Alerts.** A Windows notification when a session needs you, gets stuck or finishes a long turn, and an optional chime. While a session needs you the taskbar button carries an Amber dot with how many, and flashes once for each new one when the window isn't in front. Quiet mode keeps the notifications, chime and flash silent. A click brings the session's own window forward; the dashboard never answers a permission prompt for you ([decision 027](docs/decisions/027-claude-answer-permission-prompts.md)).
- **The last 7 days.** Small bars beside tokens a minute, one a day, with a tick for what last week had used by this time of day.
- **Export.** Settings → Claude → History → **Copy summary** (the last 7 days as a Markdown table) and **Save CSV…** (one row per day, project and thread).
- **Plain list.** Settings → Claude → Show a plain list instead of the pit wall: the same projects, threads, states and numbers as rows of text, named for screen readers, with nothing moving.
- **Keyboard.** Tab reaches every thread, car and garage; Enter or Space brings a session forward or folds a garage; Live's lanes are buttons too. **Ctrl+1** to **Ctrl+7** choose the views, and **Ctrl+G** opens the session that has waited longest. A screen reader hears when a session starts needing you, and every word follows the text size up to 140%.

It's an ordinary window (at least 360 × 300) that remembers where it was and picks a Large, Medium or Small design from its size: the full pit wall, the top garages under two plan tiles, or just the car that needs you over the 5-hour meter. Its edge turns Amber when a session needs you, and Glacier when a reply you owe has waited 10 minutes. Settings has three tabs: Theme (Midnight, Slate, Warm or the light Claude palette, accent, text size, clock, Quiet mode), Claude (connection, sign-in, alerts, the plain list, intervals, History and export, projects), and Window (start with Windows, start minimised, open where I last closed it, keep on top).

## Setting it up

1. **Install.** Run the setup exe from the [Releases page](https://github.com/alialyami90/ClaudeDashboard/releases). It installs for your Windows account only, with no administrator rights, and adds a Start menu shortcut and an optional Startup shortcut. Uninstall from Installed apps; your settings stay unless you ask.
2. **Connect Claude Code** (Settings → Claude). This merges a few entries into Claude Code's `settings.json`, after a dated backup:
   - hooks and a status line that run `ClaudeHook.exe`, which writes one small file per session for the dashboard;
   - Claude Code's OpenTelemetry export, pointed at a receiver on `127.0.0.1:43832` (the next free port up to 43839 if that one is taken). Your own `OTEL_*` settings are never overwritten, and prompts, replies and tool details stay off.

   Sessions you start afterwards show up. Sessions that were already running show from Claude Code's own session list and logs. **Disconnect** takes out exactly what Connect added. If your organisation's managed settings override hooks, the status line or telemetry, Settings → Claude says so.
3. **Sign in with Claude** (Settings → Claude) for the plan meters. The app gets its own sign-in that can read only your profile and your plan's usage, never chats and never credits. It is kept encrypted for your Windows account and checks the limits every 5 minutes by default. Without it, the meters come from Claude Code's status line when a session reports them.

### Moving over from Widget Screen

The first time Claude Dashboard starts with nothing in its own folder and something in `%LOCALAPPDATA%\WidgetScreen`, it asks before the dashboard opens:

- **Bring it over** copies the Claude settings, the theme and text size, the Claude sign-in and Claude Code's bridge folder. If Widget Screen had connected Claude Code, it connects again from the new folder on the same telemetry port, after the usual dated backup of `settings.json`.
- **Start fresh** copies nothing. If Claude Code was connected, it keeps sending to Widget Screen until you connect it in Settings → Claude.
- Closing the window decides nothing, and the question comes back next time.

Widget Screen can stay installed, and both apps stay live ([decision 028](docs/decisions/028-claude-both-apps-watch.md)). If Widget Screen's Update connection takes the hooks, Reconnect in Claude Dashboard's Settings → Claude brings them back; Disconnect in either app removes both apps' hooks.

## Where your data lives

| What | Where |
| --- | --- |
| Settings | `%LOCALAPPDATA%\ClaudeDashboard\settings.xml` |
| The app's Claude sign-in | `claude-session.dat` beside it, encrypted with DPAPI for your Windows account |
| The hook's session files | the `claude\` folder beside it |
| The daily summary | `claude\history\YYYY-MM-DD.json`, one a day, kept a year: tokens, replies, threads, the API value, needs and the longest wait, by hour and project folder. Past days are built once from Claude Code's logs and marked `fromLogs` (no needs or waits). No prompts, replies, titles, thread names or session ids. Settings → Claude → History → Keep a daily summary turns it off and deletes them. |
| What it reads, read-only | Claude Code's `sessions\` and `projects\` logs under `%USERPROFILE%\.claude` (or `CLAUDE_CONFIG_DIR`), and the Claude app's thread records |

It never reads Claude Code's credentials, never stores a prompt or reply, and listens only on 127.0.0.1. The first line of a thread's first request may be used as its name when it has no title, and is kept in memory only: Save CSV uses the thread's id label instead. [PRODUCT.md](PRODUCT.md) has the full list of what it reads and never does.

## Building it

On Windows 10 or 11 x64 with .NET Framework 4.8 (no SDK, no Visual Studio), from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Test     # the app, the hook and all checks
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\package.ps1 -Test   # the setup exe and its checks
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1             # run the build
```

The app lands in `artifacts\app\ClaudeDashboard.exe`. The checks print `PASS` and `FAIL` lines; they use a throwaway Claude Code folder and isolated settings, so your real Claude Code setup, sign-in and settings are never touched.

To see the card with labelled sample data, render it to a PNG:

```powershell
.\artifacts\app\ClaudeDashboard.exe --settings "$env:TEMP\cd-sample\settings.xml" --render sample.png --render-claude-sample
```

Add `--render-size 1000x600` for another window size, `--render-claude-needs` to draw a thread that needs you, or `--render-settings --render-tab Claude` for the Settings window.

CI on GitHub runs the same checks on every push and pull request. Every change goes through a pull request and ships as a tagged release with the setup exe and a portable zip; see [BRANCHES.md](BRANCHES.md) and [CHANGELOG.md](CHANGELOG.md).

## Where things are

| Path | What |
| --- | --- |
| `src/integrations/Claude/` | The engine: the bridge, Claude Code's session list and logs, the Claude app's threads, sign-in and plan meters, the telemetry receiver, file watchers, alerts |
| `src/widgets/ClaudeCard.cs`, `PitArt.cs`, `PitGraphics.cs` | The card's three designs, the pixel-art cars and garages, the gauges |
| `src/app/` | Start-up (`ShellProgram`), the window (`ShellWindow.xaml`), Settings (`SettingsWindow.xaml`), `AppController` and its Claude and migration parts, `Migration`, palettes, settings XML, notifications |
| `hook/` | `ClaudeHook.exe`, which Claude Code's hooks and status line call |
| `installer/` | The setup exe |
| `tests/Claude/`, `tests/Migration/` | The watcher's checks and the move from Widget Screen |
| `docs/decisions/` | Decision records 014–031: why the watcher works the way it does |
| `docs/design/` | The watcher's design note, the approved pit wall mock and the setup note |
| `docs/archive/` | Widget Screen's spec, records and notes, kept read-only as history |

[CLAUDE.md](CLAUDE.md) holds the project's standing rules for anyone, person or agent, working in this repository. [DESIGN.md](DESIGN.md) is the visual language.

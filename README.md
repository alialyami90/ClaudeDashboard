# Claude Dashboard

A Windows desktop app that watches every Claude Code session on your PC. Each project is a pit garage and each thread a pixel-art race car: you can see at a glance which session is working, which one needs you and whose turn it is, how much of your plan's 5-hour and weekly limits is left, and what each project and thread has used.

It is native WPF on .NET Framework 4.8, with no WebView, no packages and no server. It reads what Claude Code and the Claude app already write on your PC, and nothing about your sessions leaves it. Claude Dashboard is an independent app, not made by Anthropic.

> **Being rebuilt (Nov 2026).** Claude Dashboard grew out of Widget Screen, a touchscreen dashboard with 19 widgets. It is being cut down to the Claude watcher in six releases, v0.42.0 to v0.47.0 ([decision 026](docs/decisions/026-claude-dashboard.md)). Until v0.44.0 the other widgets are still in the code, and until v0.45.0 the watcher still sits on the old dashboard canvas. Settings stay in `%LOCALAPPDATA%\WidgetScreen` until v0.46.0 moves them.

## What it shows

- **Garages and cars.** One garage per project, one car per thread from the last 5 hours, today or the week. The car's class follows its main model (grand tourer for Opus, sports car for Sonnet, hot hatch for Haiku), and its motion says its state: wheels turning while it works, hazards when it needs you, brake lights on your turn, up on the jack while compacting, wheelspin when it's stuck or retrying, a cover when it ends. Tap a garage to roll its cars in or out.
- **Your plan.** The 5-hour window as a fuel tank with your pace and where it will end up, the week as seven laps, and tokens a minute on a rev counter.
- **Pit stop.** The thread that has waited longest on you, what it wants ("Wants to use Bash · waiting 2 min") and **Go to it**, which brings its window forward.
- **The ledger.** Tokens, cache reads, replies or ≈ $ at API prices, model mix and lines added and removed, per project and per thread.
- **Alerts.** A Windows notification when a session needs you, gets stuck or finishes a long turn, and an optional chime. Quiet mode keeps both silent.

The window picks a Large, Medium or Small design from its size: the full pit wall, the top garages under two plan tiles, or just the car that needs you over the 5-hour meter.

## Setting it up

1. **Install.** Run the setup exe from the [Releases page](https://github.com/alialyami90/ClaudeDashboard/releases). It installs for your Windows account only, with no administrator rights, and adds a Start menu shortcut and an optional Startup shortcut. Uninstall from Installed apps; your settings stay unless you ask.
2. **Connect Claude Code** (Settings → Claude). This merges a few entries into Claude Code's `settings.json`, after a dated backup:
   - hooks and a status line that run `ClaudeHook.exe`, which writes one small file per session for the dashboard;
   - Claude Code's OpenTelemetry export, pointed at a receiver on `127.0.0.1:43832` (the next free port up to 43839 if that one is taken). Your own `OTEL_*` settings are never overwritten, and prompts, replies and tool details stay off.

   Sessions you start afterwards show up. Sessions that were already running show from Claude Code's own session list and logs. **Disconnect** takes out exactly what Connect added. If your organisation's managed settings override hooks, the status line or telemetry, Settings → Claude says so.
3. **Sign in with Claude** (Settings → Claude) for the plan meters. The app gets its own sign-in that can read only your profile and your plan's usage, never chats and never credits. It is kept encrypted for your Windows account and checks the limits every 5 minutes by default. Without it, the meters come from Claude Code's status line when a session reports them.

## Where your data lives

| What | Where |
| --- | --- |
| Settings | `%LOCALAPPDATA%\WidgetScreen\settings.xml` (moves to `%LOCALAPPDATA%\ClaudeDashboard` in v0.46.0) |
| The app's Claude sign-in | `claude-session.dat` beside it, encrypted with DPAPI for your Windows account |
| The hook's session files | the `claude\` folder beside it |
| What it reads, read-only | Claude Code's `sessions\` and `projects\` logs under `%USERPROFILE%\.claude` (or `CLAUDE_CONFIG_DIR`), and the Claude app's thread records |

It never reads Claude Code's credentials, never stores a prompt or reply, and listens only on 127.0.0.1. The first line of a thread's first request may be used as its name when it has no title, and is kept in memory only. [PRODUCT.md](PRODUCT.md) has the full list of what it reads and never does.

## Building it

On Windows 10 or 11 x64 with .NET Framework 4.8 (no SDK, no Visual Studio), from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Test     # the app, the hook and all checks
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\package.ps1 -Test   # the setup exe and its checks
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1             # run the build
```

The app lands in `artifacts\app\` (`ClaudeDashboard.exe` from v0.42.1, `WidgetScreen.exe` before). The checks print `PASS` and `FAIL` lines; they use a throwaway Claude Code folder and isolated settings, so your real Claude Code setup, sign-in and settings are never touched.

To see the card with labelled sample data, render it to a PNG:

```powershell
.\artifacts\app\WidgetScreen.exe --settings "$env:TEMP\cd-sample\settings.xml" --render sample.png --render-claude-sample
```

Add `--render-claude-needs` to draw a thread that needs you.

CI on GitHub runs the same checks on every push and pull request. Every change goes through a pull request and ships as a tagged release with the setup exe and a portable zip; see [BRANCHES.md](BRANCHES.md) and [CHANGELOG.md](CHANGELOG.md).

## Where things are

| Path | What |
| --- | --- |
| `src/integrations/Claude/` | The engine: the bridge, Claude Code's session list and logs, the Claude app's threads, sign-in and plan meters, the telemetry receiver, file watchers, alerts |
| `src/widgets/ClaudeCard.cs`, `PitArt.cs`, `PitGraphics.cs` | The card's three designs, the pixel-art cars and garages, the gauges |
| `src/app/` | Start-up, the window, settings, the Settings → Claude wiring and notifications |
| `hook/` | `ClaudeHook.exe`, which Claude Code's hooks and status line call |
| `installer/` | The setup exe |
| `tests/Claude/` | The watcher's checks |
| `docs/decisions/` | Decision records 014–026: why the watcher works the way it does |
| `docs/design/` | The watcher's design note, the approved pit wall mock and the setup note |
| `docs/archive/` | Widget Screen's spec, records and notes, kept read-only as history |

[CLAUDE.md](CLAUDE.md) holds the project's standing rules for anyone, person or agent, working in this repository. [DESIGN.md](DESIGN.md) is the visual language.

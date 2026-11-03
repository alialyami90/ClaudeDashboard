# Claude Dashboard

A Windows desktop app whose whole window is the Claude Code watcher: every Claude Code session on the PC as a pixel-art car in its project's garage, the plan's 5-hour and weekly limits, and tokens, cost and lines per project and thread. It grew out of Widget Screen (19 widgets on a 1920 × 720 touchscreen) and was cut down to this one widget in phases 1–6 of [decision 026](docs/decisions/026-claude-dashboard.md), finished in v0.47.0. [PRODUCT.md](PRODUCT.md) says what it is for, [DESIGN.md](DESIGN.md) how it looks, [README.md](README.md) how to use and build it.

**Stack:** native **WPF on .NET Framework 4.8, x64**, C#, built with the Framework's `csc.exe`. No `.csproj`, no NuGet, no WebView, no browser frontend, no server. The setup exe (`installer/`) is built the same way.

**Windows only** (Ali, 2 Nov 2026). The Android app and the phone link left the tree in v0.44.0 and stay in history at the `v0.41.0` tag. A Claude-only phone companion is a roadmap item after v0.47.0, started from that tag.

## The agreed plan (decision 026)

| Phase | Version | Owner |
| --- | --- | --- |
| 1. Rename and rules | v0.42.0 docs, v0.42.1 names | Scribe, Builder |
| 2+3. Cut the other widgets, then the second-screen machinery | v0.44.0 (one merge) | Demolition |
| 4. The dashboard window, Settings window, Claude palette | v0.45.0 | Shell |
| 5. Move users over to `%LOCALAPPDATA%\ClaudeDashboard` | v0.46.0 | Builder |
| 6. Watcher package 4 (the Cockpit; answering permission prompts dropped, decision 027) | v0.47.0 | Watcher |
| Docs catch up with v0.46.0 and v0.47.0 | v0.47.1 | Scribe |
| Header fix | v0.47.2 | Shell |
| Namespace `WidgetScreen` → `ClaudeDashboard` | v0.47.3 | Shell |

## The lap chart (decision 030)

| Package | Version | Owner |
| --- | --- | --- |
| A. Engine: state timeline, plan readings, ≈ limit, tool times, feed (in memory only) | v0.54.0 | Watcher |
| B. Tab shell: six views, `IClaudeView`, Ctrl+1–6 and Ctrl+G | v0.55.0 | Shell |
| C. Live, the lap chart | v0.56.0 | Watcher |
| Limit reached; the views' frame clock and the header | v0.56.1 | Watcher, Shell |
| D. Limits and History | v0.57.0 | Builder |
| Home and End on the pages | v0.57.1 | Builder |
| One ≈ limit everywhere, moving Live cars, archived lanes | v0.57.2 | Watcher |
| E. Reliability and Sources | v0.58.0 | Demolition |
| The pages' focus ring | v0.58.1 | Builder |
| Live's rose on paper | v0.58.2 | Watcher |
| F. Docs (decision 030, DESIGN, PRODUCT, README) | v0.58.3 | Scribe |
| The six defaults confirmed | v0.58.4 | Scribe |
| History from the logs: past days built from Claude Code's logs, a year kept, Keep logs a year | v0.59.0 | Watcher |

Ali confirmed decision 030's six defaults and approved the tags v0.53.1 to v0.58.3 (3 Nov 2026). "≈ limit" always means the engine's `LimitAtPace` (3 plan readings or more, before the reset), and "Limit reached" replaces it once the window is full. The views live in `src/widgets/` (`LiveView`, `LapChart`, `LimitsView`, `HistoryView`, `ReliabilityView`, `SourcesView`), start no timers, and get frames only through `ClaudeViewContext.Frames`.

History from the logs (v0.59.0, Ali chose options A, B and C, 3 Nov 2026): Pro and Max plans have no usage-history API, so `ClaudeBackfill` builds missing or partial past days from Claude Code's `projects\` logs on the background scan, never today, numbers, project folders and their folder names only (no thread names), keeping a day file's own Needs and LongestWait and marking it `fromLogs`. Sources → Keep logs a year sets `cleanupPeriodDays` to 365 in Claude Code's `settings.json` only when pressed, with Connect's backups; Undo and Disconnect take out only the dashboard's own value (`logs-kept.json` in the bridge folder).

Sign in with Claude stays as it is (Ali, 3 Nov 2026), after being told Anthropic's [legal page](https://code.claude.com/docs/en/legal-and-compliance) says other apps may not offer Claude.ai login or store its tokens.

Cloud garage (package 4.3) waits on a one-time look at the Claude app's cloud thread records on Ali's PC (Watcher).

Since v0.47.3 the C# namespace is `ClaudeDashboard` (the setup's is `ClaudeDashboardSetup`), and XAML is embedded as `ClaudeDashboard.<FileName>`. Strings that leave the app keep Widget Screen's name on purpose: the `WIDGETSCREEN_*` variables, the `.widgetscreen-backup` suffix, the `Local\WidgetScreen.Claude.` hook mutex and the sign-in's DPAPI entropy `WidgetScreen.ClaudeSignIn`. Since v0.46.0 settings, the sign-in and the bridge live in `%LOCALAPPDATA%\ClaudeDashboard`, and Claude Code's hooks run `...\ClaudeDashboard\claude\ClaudeHook.exe`. `%LOCALAPPDATA%\WidgetScreen` is only read, by the first run's offer to bring it over (`Migration`, `AppController.Migration.cs`); never write to it, with one exception (decision 028, Ali, 3 Nov 2026): `ClaudeHook.exe` also writes its session files and `limits.json` into `%LOCALAPPDATA%\WidgetScreen\claude` when that folder exists, so Widget Screen keeps working while Claude Dashboard is closed and the reverse. Each member touches only the files the plan gives them; the Lead writes `CHANGELOG.md`, `CLAUDE.md` and `BRANCHES.md` at each merge, and the Scribe reviews every PR.

## The engine

- `src/integrations/Claude/`: the bridge folder written by `hook/ClaudeHook.cs` (Claude Code's hooks and status line); Claude Code's `sessions\` and `projects\` logs under `%USERPROFILE%\.claude` or `CLAUDE_CONFIG_DIR`, read-only; the Claude app's thread records, read-only; the widget's own **Sign in with Claude** (PKCE, `user:profile`, DPAPI `claude-session.dat`, usage meters from `api.anthropic.com` every 5 minutes); OpenTelemetry to a loopback receiver on **127.0.0.1:43832** (next free up to 43839); `ClaudeWatch` file watchers with a 30 s fallback; alerts and `ClaudeToast`; since v0.47.0 the daily summary files (`ClaudeHistory`, `claude\history\`, kept a year since v0.59.0, numbers, project folders and their folder names only (no thread names), past days built from the logs by `ClaudeBackfill`) and Copy summary and Save CSV (`ClaudeExport`, which never writes a thread name taken from its first request).
- The card: `src/widgets/ClaudeCard.cs`, `PitArt.cs`, `PitGraphics.cs`. Small, Medium and Large by the `CardSizing.Claude` members, which live at the bottom of `ClaudeCard.cs` since v0.45.0. The window is at least 360 × 300. From 640 wide a tab row shows six views (Live, Garage, Limits, History, Reliability, Sources; decision 030); below 640 it is the Garage card alone. Settings → Claude → Show a plain list swaps the pit wall for text rows (`BuildPlain`).
- Settings → Claude → Connect merges into Claude Code's `settings.json` with five dated backups and never overwrites the user's `OTEL_*`; Disconnect takes out exactly that. `ClaudeHook.exe` and the `.widgetscreen-backup` suffix keep their names so Disconnect finds its own entries.
- Decisions 014, 015, 016 (projects and threads), 017–020, 022 (review fixes), 023–030 are current. Everything else is in `docs/archive/`, read-only history.

## Build, run, test (Windows, PowerShell, from the root)

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Test     # app, hook and checks
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\package.ps1 -Test   # setup exe and its checks
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

- Checks print `PASS`/`FAIL`; add them to `tests/Claude/*.cs` or `tests/SmokeTests.cs` with `Check(name, action)`. They point `CLAUDE_CONFIG_DIR` at a throwaway folder, `WIDGETSCREEN_CLAUDE_MANAGED` at a missing file, `WIDGETSCREEN_CLAUDE_APP_DATA` at an empty one and `WIDGETSCREEN_CLAUDE_BRIDGE` (Widget Screen's bridge folder, which the hook also writes to) at a missing one, and turn `ClaudeToast.Enabled` off, so the PC's real Claude Code, Claude app and notifications are never touched.
- `--settings <path>` isolates settings and sign-in; `--render <png>` with `--render-size WxH` and `--render-claude-sample` (`--render-claude-needs`) draws the labelled fixture. Never touch the user's real `%LOCALAPPDATA%` settings from automated checks.
- `tests/Claude/` is the tripwire: if a deletion makes one fail, fix the deletion, not the check.

## Non-negotiables

- The dashboard never answers a permission dialog; Needs you only opens the session (decision 027).
- Native WPF only. No WebView, no packages without Ali's agreement.
- No server. The telemetry receiver stays loopback-only (`POST /v1/logs`, `/v1/metrics`, JSON only). Never store or send prompts, replies or file contents; never read Claude Code's credentials; the sign-in tokens go only to Anthropic's hosts.
- One 1 s heartbeat; nothing polls while hidden or minimised; the frame clock runs only while a visible car moves.
- Honest states: loading, unavailable, disconnected and working look different; a missing reading says why; fixtures say they are samples; estimates say "≈".
- Settings are local XML written atomically. Never commit `.dat` files, keys or build output.
- Segoe UI and Segoe MDL2 only; cars and garages drawn in code; no images or emoji as icons; no shadows, blurs or effects. Midnight, Slate, Warm and Claude; Amber for warnings and Needs you.
- Don't invoke Impeccable unless Ali authorizes it again.

## Branches, CI and releases

[BRANCHES.md](BRANCHES.md) has the full process. Repository: `alialyami90/ClaudeDashboard`, default branch `main`.

- Every change goes through a pull request from its own branch; never push to `main`. CI (`.github/workflows/ci.yml`) runs `build.ps1 -Test` and `package.ps1 -Test` on GitHub; a PR merges only on a green run, with **Create a merge commit**.
- Each PR's version gets its own `## vX.Y.Z` section in `CHANGELOG.md`. Semver; MAJOR stays 0 until package 4 and the Android companion. MINOR for a feature or visible change, PATCH for fixes and docs-only merges.
- After a merge: tag `vX.Y.Z` on `main` and push it (the Release workflow publishes the setup exe and portable zip), then `scripts\release.ps1 -UpdateShortcut` on Ali's PC for the local `releases\vX.Y.Z` copy. A cloud thread does both through a Remote Control session.

## Working process

- Read this file, decision 026 and the relevant Claude record before changing code. Keep to the agreed scope and preserve user work.
- Make routine, reversible decisions yourself; ask Ali only when a choice changes the result.
- Report what changed, what was verified (CI run, renders) and what wasn't.
- When Ali settles a decision, record it here and, if technical, in `docs/decisions/`.

# 026: Claude Dashboard, one app for one widget

On 2 Nov 2026 Ali decided to turn Widget Screen into **Claude Dashboard**: a normal Windows desktop app whose whole window is the Claude Code watcher, on any monitor, with every other widget removed. The plan is the "Claude Dashboard transformation plan" doc, which Ali approved and asked a six-member team to carry out. This record keeps its decisions, Ali's answers and the order of the work, so the repository explains itself without the doc.

## Decisions

1. **One app, one widget.** The Claude Code watcher (decisions 014–020 and 022–025) becomes the whole product. The other 17 widgets, the canvas, pages, Edit layout, the second-screen machinery and the phone link go.
2. **Same stack.** WPF on .NET Framework 4.8, x64, built with the Framework's `csc.exe`, no packages, no WebView. The watcher already works on this stack, and a rewrite would throw away about 7,000 lines of tested code (the engine in `src/integrations/Claude`, the card and pixel art, the hook and the Claude checks).
3. **Windows only for now.** The Android app leaves the tree in phase 2 and stays in history at the `v0.41.0` tag, the last commit that has `android/` and the phone link. A Claude-only phone companion is a roadmap item after v0.47.0. It would start from that tag's `android/` and `src/integrations/Remote/`, and its link would carry only the `claude` section, `claudeAlerts`, `claude.focus` and `claude.collapse`.
4. **A normal window.** One resizable window (the plan said minimum 640 × 420; it shipped at 360 × 300, see "As built"), that opens where it was closed, takes focus when clicked, sits in the taskbar and closes fully when closed. No `Viewbox`, no 1920 × 720 design canvas, no secondary-monitor fullscreen, no no-activate window, no tray mode. The card picks Small, Medium or Large from the window's own size.
5. **The engine doesn't change.** The hooks, the bridge, the telemetry receiver on 127.0.0.1:43832–43839, the sign-in, the price table, the file watchers, the alerts and the Windows notification stay exactly as they are. The transformation changes the frame, not the picture.
6. **Docs first.** The standing rules were written for a two-platform touchscreen app. They are rewritten in phase 1, before any code is cut, so every later phase works from rules that are true.

## Ali's answers (2 Nov 2026)

- **Name:** Ali asked for a creative one, was offered Pit Wall, Paddock and Race Control, and kept **Claude Dashboard**. The exe is `ClaudeDashboard.exe`, the install folder `%LOCALAPPDATA%\Programs\Claude Dashboard`, and settings go to `%LOCALAPPDATA%\ClaudeDashboard` (from phase 5).
- **Tray:** no tray mode. The app closes fully like an ordinary app, and `TrayMode` goes in phase 3.
- **Palettes:** keep Midnight, Slate and Warm, and add a Claude palette in phase 4.
- **Watcher package 4:** after the transformation, as phase 6.
- **Old releases:** delete them. This repository has no releases or tags yet, so there is nothing to delete here. The Widget Screen releases live on the old repository, outside this project, and stay unless Ali removes them there.
- **1.0:** waits for package 4 and the Android companion. MAJOR stays 0 through all six phases.

## Keep, cut, change

A file stays if the Claude watcher reads it or the app can't ship without it. Shared design pieces stay only where the Claude card already uses them.

| Area | Fate |
| --- | --- |
| `src/integrations/Claude/` (14 files) | Keep, untouched in the cut phases; namespace rename only |
| `ClaudeCard.cs`, `PitArt.cs`, `PitGraphics.cs` | Keep, then change: the card becomes the window's body |
| `DashboardController.Claude.cs`, `ClaudeToast.cs` | Keep, then change: Settings wiring moves to a Settings window, heartbeat ticks to a small controller |
| `hook/ClaudeHook.cs` | Keep; `ClaudeHook.exe` and the `.widgetscreen-backup` suffix keep their names so Disconnect still finds its own entries |
| `tests/Claude/` | Keep; the checks pass at every phase |
| `installer/`, `scripts/package.ps1` | Keep, rename; the hardware sensors checkbox goes |
| `scripts/build.ps1`, `run.ps1`, `release.ps1`, `make-icon.ps1` | Keep, rename; `get-sensors.ps1`, `build-android.ps1` and `export-design-reference.ps1` go |
| `.github/workflows/ci.yml`, `release.yml` | Keep; the Java, sensor and Android steps go, and releases stop shipping an APK |
| `CardSizing.Claude*`, `Stickers.StripHeight`, `ConfirmSheet`, `PreferIPv4Handler`, `Launcher`, the palettes | Keep and fold in: the Claude lines of `CardSizing` move into `ClaudeCard` |
| `Settings.cs` | Change: the non-Claude fields go (about 80 of 100), window position, size and theme come in |
| `MainWindow.xaml`, `DashboardController.cs` | Rewrite: a header, the Claude body and a Settings window |
| `WidgetLayout.cs`, `LayoutEditor.cs`, `ScreenFit.cs`, pages, editor, cards, stickers partials | Cut |
| `FocusKeeper.cs`, `TrayMode.cs`, `DashboardController.Screen.cs`, `DisplayService.cs` | Cut |
| `src/integrations/Remote/`, `DashboardController.Remote.cs` | Cut now, roadmap later |
| The other 17 widgets and their integrations, partials, Settings tabs and checks | Cut |
| `third-party/LibreHardwareMonitor` | Cut (only System stats used it) |
| `android/` | Cut from the tree, kept at the `v0.41.0` tag |
| `design specfication/`, the widget decisions and design notes | Archived in `docs/archive/` (phase 1) |
| `assets/WidgetScreen.ico` | Replaced by a new logo |

## The order of the work

Six pull requests, one release each. Each leaves CI green, `build.ps1 -Test` passing and the app runnable, so the work can stop after any phase.

| Phase | Version | What it does |
| --- | --- | --- |
| 1. Rename and rules | v0.42.0 (docs), v0.42.1 (names) | New name everywhere the user sees it; new `CLAUDE.md`, `PRODUCT.md`, `DESIGN.md`, `README.md`; `AGENTS.md` deleted; the widget app's spec and records archived. Nothing in `src/` but name strings. |
| 2. Cut the other widgets | merged with phase 3 | The 17 widgets, their integrations, partials, tabs, checks and renders; LibreHardwareMonitor; `android/` and the Android CI steps. |
| 3. Cut the second-screen machinery | v0.44.0 | Canvas, pages, Edit layout, inks, `FocusKeeper`, secondary-monitor fullscreen, the phone link, touch polish, tray mode. Only 127.0.0.1:43832 listens while connected. |
| 4. The dashboard window | v0.45.0 | New `MainWindow.xaml` and Settings window (Theme, Claude, Window), the Claude palette, remembered position and size, the card sized by the window. Namespace becomes `ClaudeDashboard`. |
| 5. Move users over | v0.46.0 | `%LOCALAPPDATA%\ClaudeDashboard`; the first run offers to copy the `Claude*` settings, `claude-session.dat` and the `claude\` bridge folder, then re-runs Connect so the hooks point at the new exe. |
| 6. Watcher package 4 | v0.47.0 | Allow and Deny from the Needs-you notification (a themed toast window of our own), and the rest of the package list. (Allow and Deny were dropped by decision 027; see "As built".) |

Two decisions hold the order together:

- **The namespace stays `WidgetScreen` until phase 4** (moved to its own release after phases 5 and 6, see "As built"). Renaming it in phase 1 would touch about 130 files for nothing the user sees; in phase 4 the few remaining files are rewritten anyway.
- **The settings and bridge folder stays `%LOCALAPPDATA%\WidgetScreen` until phase 5.** Connect wrote `%LOCALAPPDATA%\WidgetScreen\claude\ClaudeHook.exe` into Claude Code's `settings.json`. Moving it earlier would make every Claude Code session fail its hooks with "file not found" until Connect runs again, so phase 5 moves the folder and re-runs Connect in the same step.

## The team

The plan runs as six members in four merge waves, split by files so two members never edit the same ones:

- **Lead:** merge order, versions, `CHANGELOG.md`, `CLAUDE.md`, `BRANCHES.md`, every tag and release.
- **Scribe:** `docs/`, the archive move, `PRODUCT.md`, `DESIGN.md`, `README.md`, `AGENTS.md`; then the reviewer of every other pull request.
- **Builder:** names, scripts, installer, CI, icon, `hook/`, `tests/Installer/`; then the user migration (phase 5).
- **Demolition:** phases 2 and 3 on one branch, the only member who deletes code.
- **Shell:** the new window, Settings window, Claude palette and `ClaudeCard` sizing (phase 4), built as new files beside the old ones until the deletions merge.
- **Watcher:** package 4 (phase 6), owning `src/integrations/Claude/`, `ClaudeToast.cs`, `DashboardController.Claude.cs` and `tests/Claude/`.

The Claude checks (`tests/Claude/`) are the tripwire for the cut phases: if a deletion makes one fail, the deletion is fixed, not the check.

## Rules that stay

- Native WPF, no WebView, no packages.
- One 1-second heartbeat; nothing polls while hidden or minimised.
- Honest states, and fixtures that say they are samples (`--render-claude-sample`).
- `claude-session.dat` encrypted with DPAPI, the tokens sent only to Anthropic's hosts.
- The telemetry receiver on loopback only.
- Every change through a pull request with its own version section in `CHANGELOG.md`; tests run on GitHub; a GitHub Release and a local `releases\vX.Y.Z` copy per merge.

## Rules that go

- Both platforms in one branch.
- The 1920 × 720 design canvas, the `Viewbox`, "secondary monitor only" and "the dashboard never scrolls" (the ledger already scrolls inside the card).
- The touch-first minimums (52 px inputs, 48 px thumbs). The existing styles can stay, but they stop being requirements.
- Inks per widget and the Sticker Sheet (decision 021); the Claude card was always outside it.
- The spec precedence list: `design specfication/` describes the old product and is now history in `docs/archive/widget-screen/`.

## Tradeoffs

- **The other widgets are gone for everyone.** Anyone who used Widget Screen for its mixer, Spotify or Quick Launch keeps v0.41.0 and earlier, which stay in git history (and, for now, on the old repository's Releases page). Nothing here will bring them back.
- **No phone until the roadmap item.** Between phase 2 and the Android companion, the phone app has nothing to talk to. The `v0.41.0` tag keeps both sides of the old link for the restart.
- **The card's sizes were designed in 1920 × 720 units.** In a real window they become device-independent pixels, so the 640 × 420, 860 and 1000 thresholds get a second look at 100% and 150% display scaling in phase 4.
- **A window, not a glance.** Losing the second-screen fullscreen means the dashboard competes with other windows for space. That is the point: it now goes wherever the user wants it, including a second monitor as an ordinary window.
- **Archived records still name the old rules.** They are history, kept read-only so links and reasons survive; `CLAUDE.md` and this record say which rules are current.

## Archive

Moved in phase 1 to `docs/archive/`, unchanged except for links that the move broke:

- `design specfication/` → `docs/archive/widget-screen/design specfication/` (still read-only, still misspelled).
- The old `PRODUCT.md` and `DESIGN.md` (the Sticker Sheet) → `docs/archive/widget-screen/`.
- Decisions 001–013, 016 (hardware sensors), 021 (Sticker Sheet) and 022 (GitHub CI and releases) → `docs/archive/decisions/`. The CI and release process from 022 still applies and is described in `BRANCHES.md` and `CLAUDE.md`.
- Every design note but `claude-watcher.md`, `claude-watcher-v2.html` and `setup.md` → `docs/archive/design/`. `setup.md` stays because the setup exe ships with Claude Dashboard and `scripts/package.ps1` and `scripts/release.ps1` point at it.
- `AGENTS.md` is deleted rather than archived: it is the source snapshot's copy, still inside `implementation-source.zip`, and `CLAUDE.md` is the one rules file.

The Claude records (014, 015, 016 projects and threads, 017–020, 022 review fixes, 023–025, and 027 from phase 6) stay in `docs/decisions/`. Their mentions of the phone, both apps and pages describe what was built at the time.

## As built (v0.42.0–v0.47.0)

Where the releases differ from the plan above, and why. The plan's text is left as Ali approved it.

- **Phases 2 and 3 merged once, as v0.44.0,** as the team plan intended. The intermediate commits on that branch don't each build; only the merge does.
- **v0.44.1 (installer and icon)** wasn't a phase of its own. It removed the hardware-sensors checkbox, `--no-sensors` and the sensor files from setup, so an update deletes an older install's `sensors` folder. It also changed the uninstall wording to say to disconnect Claude Code first. The icon (`assets\ClaudeDashboard.ico`) is a Lime car, head-on in front of a Glacier garage on a Midnight tile, drawn by `scripts\make-icon.ps1` from the app's own `PitArt` and `PitRaster`.
- **The window's minimum is 360 × 300, not 640 × 420.** The plan also says Small and Medium survive as compact modes for a narrow window, and a 640 × 420 minimum would make them unreachable. The card's thresholds are unchanged (Small under 360 × 280, Large from 640 × 420, rev counter from 860, side panel from 1000), now in device-independent pixels. The checks cover them at four sizes and at 150% scaling.
- **The window is new files, not a rewritten `MainWindow.xaml`.** `ShellWindow.xaml`, `SettingsWindow.xaml`, `ShellStyles.xaml`, `AppController.cs` (with `AppController.Claude.cs`), `Palettes.cs` and `ShellProgram.cs` replaced `MainWindow.xaml`, `DashboardController.cs`, `DashboardAppearance.cs`, `CardSizing.cs` and `Stickers.cs`. `CardSizing`'s Claude members live at the bottom of `ClaudeCard.cs`.
- **The card has no heading.** The window's header carries the name, the live summary, the period switch, Quiet and Settings. The window's edge turns Amber for Needs you and Glacier for Your turn, and the title bar says "Claude needs you".
- **Settings dropped the corner-radius choice and the touch sizes.** Theme is palette, accent (adding Terracotta), text size, clock and Quiet. Settings controls are 32 units.
- **Minimised does less.** The app still watches the session files, so Needs you notifies and chimes from the taskbar. It no longer scans logs or works out the view on the clock until the window is restored, and a click on the notification restores it.
- **The namespace rename moved after phases 5 and 6,** to its own release (v0.47.3, after the docs at v0.47.1 and a header fix at v0.47.2), so it doesn't collide with the migration and package 4. That release also has to update `scripts\make-icon.ps1`, which loads `WidgetScreen.PitArt` by name.
- **Phase 5 asks before the dashboard starts (v0.46.0).** `Migration` and `AppController.Migration.cs` show the window's confirm sheet first, never for `--render` or `--settings`. **Bring it over** copies every `Claude*` setting and the shared look, `claude-session.dat` and the `claude\` bridge folder (with the earlier status line and the connection record, so Disconnect still restores the user's own `settings.json`), then reconnects from the new folder on the old telemetry port. **Start fresh** saves default settings. Closing the window decides nothing. Widget Screen's folder is only read, and the sheet says to quit Widget Screen, then uninstall it and keep its settings. If reconnecting fails, the status line says so and points to Settings → Claude → Connect.
- **Phase 6 dropped answering permission prompts (v0.47.0).** Ali chose "No, the notification only opens it" ([decision 027](027-claude-answer-permission-prompts.md)). Package 4 shipped history (one summary file a day in `claude\history\`, kept 8 weeks, and 7-day bars on the rev counter's tile), Copy summary and Save CSV, and the plain list. The cloud garage (4.3) waits on a look at the Claude app's cloud thread records; the phone (4.2) moves to the Android companion.

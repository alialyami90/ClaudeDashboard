# Changelog

Released versions of Claude Dashboard (formerly Widget Screen), newest first. Each version is an annotated `vX.Y.Z` tag on `main`. Work that hasn't been merged yet is in the [open pull requests](https://github.com/alialyami90/ClaudeDashboard/pulls); the process and version rules are in [BRANCHES.md](BRANCHES.md).

## Unreleased

Nothing yet.

## v0.55.0 (unreleased)

Claude Dashboard: the lap chart's tab shell (decision 030, package B).

- **Views as tabs.** From 640 wide, a tab list under the header: Live, Garage, Limits, History, Reliability, Sources. Ctrl+1 to Ctrl+6 choose them. Screen readers get a real tab list. Below 640 wide there are no tabs and the window is the Garage card, as before.
- **Garage is today's pit wall**, with its 5 h / Today / Week switch, which shows in the header only while Garage does. Another view's cars keep still.
- **The other views are placeholders for now**, saying when they come: Live in v0.56.0, Limits and History in v0.57.0, Reliability and Sources in v0.58.0. Live opens first once it's in the build; until then Garage does.
- **The header summary is always live**: the states now and the last 5 hours' threads, whichever view or period is chosen, plus "≈ limit 4:12 pm" when the 5-hour window fills at this pace before it resets. It is still an assertive live region.
- **The source-health line** moves to the tab row's right end from 640 wide. It is the same control, with Sample first. When the row has no room for it, it takes a line of its own under the tabs. Below 640 it stays under the card.
- **Ctrl+G** opens the session that has waited longest (Ctrl+1 until now).
- **For the views:** a view-host contract (`IClaudeView` in `ClaudeViews.cs`) that the next packages build on, with actions that take the same path as their Settings → Claude buttons, confirmations included.
- **`--render-view <name>`** renders one view.

## v0.53.1 (unreleased)

Claude Dashboard: follow-ups to the live status board.

- **Medium fills its height.** At 640 × 420 and 140% it showed one project and "+2 more projects" over a row's room left empty. The "+N more projects" line is now only as tall as its words, so the project rows get the room it leaves; at 140% that's two projects and the line.
- **The week's pace refits** when its room settles, like the tank's.
- **The crowded sample has a long MCP tool name**, and a check sees it end in "…" on the row with the whole name in its tip.
- **Checks:** Medium leaves less than a row empty while projects wait, measured from the body's bottom, with the needs and crowded samples at 100% and 140%, and shows at least two projects with the needs sample at 640 × 420 and 140%.

## v0.53.0 (unreleased)

Claude Dashboard: in 5 h and Today the window is a live status board, and Week is the ledger (observability critique, issue 2).

- **Live thread rows.** In 5 h and Today every thread row has labelled time columns. **For** is how long it has been in its state. **Last sign** is the last hook event or reply: "just now" or "3 min ago" in grey, then "silent 8 min" once a working thread has been quiet for 5 minutes. **Now** is the tool it's in, by name only ("Bash · 2 min"; a name over 40 characters ends in "…" and the tip has it whole), from telemetry's open tool calls; it shows "—" when no tool is known. A thin context bar sits under every title, empty with a tip until the status line reports the context, and Amber from 80%. Beside the title is only where it runs, "Claude app" in every period (Week said "Desktop"); the age that used to sit there unlabelled is the For column. Now steps aside first on a narrow window, then Lines.
- **Cache reads, Models and Replies (or API value) are Week's.** The live periods show Tokens and Lines with the live columns.
- **The plan row speaks in time.** The tank says "≈ empty 3:21 am, 37 min before reset" when the last half hour's pace runs it dry before the reset, else "≈ 84% by reset". Over 100% never reads as a percentage: with no ETA from the last half hour, the window's own pace says when it runs dry, else "≈ empty before reset". When the long form doesn't fit (measured as the window draws text), it shortens to "≈ empty 3:21 am", and the reset line shortens to "resets 9:54 am". The week tile, Medium's two plan tiles and Small's meter line speak the same way ("≈ empty Thu", "≈ 76% by reset").
- **The rev dial is gone.** Its tile keeps tokens a minute, the last hour's sparkline and the 7-day bars in a column of 200 units (wider with bigger text). The tank and the week share the rest, so their words fit at 140% at 1000 × 600.
- **The pit stop strip** names the tool a silent thread is in ("In Bash for 9 min").
- **Checks:** three new checks for the live words, the time wording and the live ledger. Renders `claude-live-{today,week}-{100,140}-{1000x600,1600x900}.png` check that the time words aren't cut.

## v0.52.0 (unreleased)

Claude Dashboard: alerts that respect attention (observability critique, issue 5).

- **Glacier waits 10 minutes.** The window's edge turns Glacier only once a reply you owe has waited 10 minutes, the attention queue's Your turn, instead of the moment any turn finished. Amber for Needs you is unchanged.
- **A dot on the taskbar button.** While a session needs you, the taskbar button carries an Amber circle with how many (9+ past nine), drawn in code, and its tooltip says "2 sessions need you". It goes when nothing needs you, and stays in Quiet mode.
- **One flash per new Needs you.** The taskbar button flashes once when a session newly needs you and the window isn't in front. Never in Quiet mode.
- **Ctrl+1** opens the session that has waited longest, in the attention queue's order (Needs you first, the longest waiting first, then Stuck, Your turn and silent). With nothing waiting it says "Nothing is waiting on you."
- **Settings → Window → Keep on top** keeps the window above other windows. Off by default.
- **No logs yet isn't a failure** (a v0.50.0 bug seen live). On a fresh install, before Claude Code has written a session log or made its projects folder, the source-health line said "Logs unreadable" in Amber and the side panel said LOGS UNREADABLE. It now says "Logs: none yet" with the calm waiting glyph, and the numbers count from zero. Only a read error is "Logs unreadable".
- **The side panel's words fit.** An unreadable log's facts say "TODAY · NO LOGS" and "TOOLS · NO LOGS", where "LOGS UNREADABLE" was cut at 1280 wide. The tip still says why.

## v0.51.1 (3 Nov 2026)

Claude Dashboard and Widget Screen are independent apps (Ali, 3 Nov 2026). Since v0.46.0 they have their own folders, and since v0.48.0 both watch Claude Code, so nothing asks you to close or remove Widget Screen any more.

- **Setup leaves Widget Screen alone.** It no longer warns about Widget Screen's startup shortcut or a running Widget Screen. It still points out another startup shortcut that opens Claude Dashboard itself, so two copies don't open.
- **Reinstall.** When the installed version is the setup's own, setup says "Reinstall Claude Dashboard" instead of offering to replace a version with the same one.
- **The first run's question.** It no longer tells you to quit or uninstall Widget Screen. **Bring it over** connects Claude Code to Claude Dashboard, Widget Screen can stay installed, and both apps stay live ([decision 028](docs/decisions/028-claude-both-apps-watch.md)). If Widget Screen's Update connection takes the hooks, Reconnect in Claude Dashboard brings them back; Disconnect in either app removes both apps' hooks. README says the same.
- **Checks.** A setup check that Widget Screen's shortcut isn't pointed out but a second Claude Dashboard shortcut is, and the migration check covers the new words.

## v0.51.0 (unreleased)

Claude Dashboard: keyboard and low vision (issue 4 of the observability critique).

- **The keyboard reaches every thread.** Thread rows, the plain list's rows, Medium's cars and every garage are buttons now. Tab moves between them (a 2-unit outline shows where you are, only when the keyboard put it there), Enter or Space brings a running session's window forward or folds a garage, and a focused row scrolls itself into view. Before, Go to it was the only thing on the card a keyboard could reach.
- **Every row has a name.** A screen reader reads each thread's title, project and state; ended and idle ones say when they were last active and that there's no window to show. The names follow the thread as it changes.
- **A new Needs you is announced.** The header's summary is an assertive live region: when a session starts needing you, a screen reader reads it out at once (fewer or the same number say nothing).
- **Every word follows the text size.** The chips, detail lines, plan tiles, pit stop reason, captions, the folded-garage bubble, the gauges' end labels, the week's days, the 5-hour lanes' names, the header's plan stamp and Sample flag (the `Caption` style, now `CaptionSize`) and the whole plain list were fixed at 10 to 13 units and ignored the 100–140% setting; now they scale. The plan row, the mini tiles and Medium's rows grow to hold them, and on Small the "+2 working" line steps aside first when the text grows, so the 5-hour meter keeps its place at the smallest window; when it shows, it drops its later parts ("+1 working", then nothing) rather than ending in "…". The plain list's chip column grows with the text, so "NEEDS YOU" stays whole, and the list shows whole rows only and scrolls to the rest.
- **Ended rows dim by colour.** Ended and parked rows were faded to 62% and 45%, which put their words at 2 to 3.7:1. Now their title and tokens turn Muted at full opacity and only the car fades; parked chips no longer fade either.
- **The light Claude palette reads on Raised.** Its Amber is `#8F5108` and its Glacier `#21668D`, at least 4.5:1 on Raised and Selected (they were 4.34 and 4.39:1).
- **"Tap" is "Click"** in the tooltips and names.
- **Checks:** rows, cars and garages are focusable buttons with names; ended rows are Muted at full opacity; the smallest words at 140% are 140% of those at 100%; Amber, Glacier, Muted and Text reach 4.5:1 on Raised, Surface and Page in every palette; the header is an assertive live region; renders at 360 × 300, 640 × 420, 1000 × 600 and 1600 × 900 at 100% and 140% go to `artifacts\renders`.

## v0.50.0 (unreleased)

Claude Dashboard: every number says where it came from (observability critique, issue 3).

- **The source-health line.** Under the card, at every size: Hooks · Logs · Telemetry · Plan, each with a Segoe MDL2 glyph and words. A source is fine, still waiting ("Logs counting…", "Plan reading…"), old ("Plan 12 min old"), off with the reason ("Hooks off: not connected", "Telemetry off: not set up", "Plan off: not signed in") or failed ("Logs unreadable", "Telemetry off: port 43832 in use"). Old and failed are Amber; each level has its own glyph, so colour isn't the only sign. The tooltip and the screen-reader name give the full reason. On a narrow window the reasons shorten, then the fine sources keep only their glyph; the line never folds away and never cuts a word. The line sits over the lower part of the card's bottom padding, so its own row is 8 units at 100% text and Small keeps its 5-hour meter at the smallest window.
- **"Telemetry off: port 43832 in use"** moves from the side panel (only from 1000 units wide) to this line, and so do v0.48.0's Widget Screen states: "Telemetry with Widget Screen" while Widget Screen is open with the port (calm, since it hands the port back within about 10 seconds of closing), and "Hooks with Widget Screen · Reconnect" in Amber when Widget Screen took Claude Code's hooks.
- **Sample is pinned** at the start of the line at every size, Small included, instead of in the header flag that narrow windows dropped.
- **Loading and unavailable look different.** While Claude Code's logs are still being read, token counts, cache reads and replies (or the API value) show "≈" ("≈1.2M") and the side panel's period says COUNTING. When the logs can't be read, all three show "—" with "Can't read Claude Code's logs." in the tip, and the side panel says LOGS UNREADABLE.
- **"PLAN · 2:17 AM"** replaces "LIVE · AS OF 2:17 PM" in the header. Numbers up to 15 minutes old were called live; how old they are is now on the source-health line.
- **One meaning for "working".** The header's summary and the line under each project's name both count working sessions as "2 working", as the chips say it, where the header said "running" for working sessions and the project line said it for open ones.
- **The header's summary at 140% text on the smallest window** shortens further instead of being cut ("1 needs you · 1 your turn · 7…"): to the most urgent part with the thread count ("1 needs you · 7 threads"), then that part alone.

## v0.49.0 (unreleased)

Claude Dashboard: the attention rail (observability critique, issue 1). In 5 h and Today the window says first what's waiting on you, and nothing that is hides off the bottom.

- **Projects are ranked by what waits on you:** Needs you, then Stuck, then Your turn, then Working, then the rest, most recent first within each. Before, only Needs you jumped the queue.
- **The pit stop strip is a short queue.** It holds every running thread that needs you, is stuck, has been your turn for 10 minutes or more, or is working with no hook event or log reply for 5 minutes ("Silent"). It shows one at a time with "1 of 4" and Next. Needs you and Stuck stay Amber; Your turn and Silent are calm, and Go to it is Amber only for the first two. Silent goes by a new `heard` time in the session file that only hook events set, so the status line's refresh (which runs while a turn's clock does) can't make a hung turn look alive; when the car is in a long tool run it reads "In a tool for 8 min" instead.
- **A clipped ledger keeps a footer.** When the Large ledger's rows don't all fit, a line under it that never scrolls away says "+7 below · 1 stuck · 2 your turn" (or "↑ 3 above" at the bottom), Amber when a hidden row needs you or is stuck. What waits above is said after an arrow ("+15 below · 6 your turn · ↑ 1 needs you"), and Your turn is counted as the header counts it (a compacting thread isn't one). Pressing it scrolls to the first hidden row that waits on you, else the next row below. Its room is taken only while rows are hidden, and the rows end whole wherever the ledger is scrolled to.
- **Medium counts what it can't show:** a last row "+3 more projects · 1 your turn", and its Needs-you line says how long ("Needs you 4 min · Fix login").
- **A crowded sample** of 25 threads over eight projects (`--render-claude-crowded`, with one of each kind of attention) for the checks and renders. Four new checks; renders at 360 × 300, 640 × 420, 1000 × 600 and 1600 × 900, calm and with Needs you, go to `artifacts\renders`.

## v0.48.0 (unreleased)

Claude Dashboard and Widget Screen both watch Claude Code, so closing either leaves the other working ([decision 028](docs/decisions/028-claude-both-apps-watch.md)).

- **One bridge feeds both apps.** `ClaudeHook.exe` writes each Claude Code event and status-line reading into Claude Dashboard's bridge folder and, when it exists, into Widget Screen's `%LOCALAPPDATA%\WidgetScreen\claude`, in the files Widget Screen 0.41.0 reads. Hooks run without either app open, so whichever app was closed is current the moment it opens.
- **Telemetry goes to whichever app is open.** Both apps take the telemetry port within about 10 seconds when the other closes. While Widget Screen holds it, the card says "Telemetry goes to Widget Screen while it's open" instead of "Telemetry off", and Settings → Claude no longer offers Update connection for it.
- **Widget Screen taking the hooks back is shown.** If Widget Screen's Settings → Claude → Update connection points Claude Code's hooks at its own bridge, the card says "Live updates go to Widget Screen: reconnect in Settings → Claude", and Settings → Claude names that bridge and offers Reconnect, which keeps both apps current again.
- **Checks:** the hook writes to both folders only while Widget Screen's exists, and never twice to one; Settings sees hooks run by another bridge and gets them back on Reconnect. Widget Screen's folder is a throwaway one in the checks (`WIDGETSCREEN_CLAUDE_BRIDGE`).

## v0.47.3 (unreleased)

Claude Dashboard: the code's namespace is renamed.

- **`WidgetScreen` → `ClaudeDashboard`** in every C# file under `src\`, `hook\` and `tests\`, and `WidgetScreenSetup` → `ClaudeDashboardSetup` in the setup exe. The XAML resources are embedded as `ClaudeDashboard.<FileName>`, the manifest's identity is `ClaudeDashboard.app`, and the sign-in's requests say `ClaudeDashboard/<version>`.
- **`scripts\make-icon.ps1`** looks up `PitArt`, `PitRaster` and `PitState` in the new namespace.
- **Nothing you use changes.** Names other programs or saved files depend on stay as they were: the `WIDGETSCREEN_*` variables the checks set, the `.widgetscreen-backup` suffix Disconnect looks for, the hook's `Local\WidgetScreen.Claude.` mutex (so a hook built before the rename and one built after share the lock) and the sign-in's DPAPI entropy (a new value would sign everyone out).

## v0.47.2 (unreleased)

Claude Dashboard: the header's summary.

- **The summary is never cut.** The header's live summary ("1 needs you · 2 running · 8 threads this week") showed as "8 th…" when the period buttons took its room, even at 1000 × 600. Now it shows whole beside the name and the buttons when it fits there. Under 1000 wide it next tries its short form there: the two most urgent parts and the thread count ("1 needs you · 2 running · 8 threads"). Otherwise it moves to a line of its own under them (the Sample flag keeps the first line), and the card gives up those 28 units, but only where the card keeps its room: at the smallest window Small keeps its 5-hour meter, and the short form stays on the header's line, stopping at a whole word.

## v0.47.1 (3 Nov 2026)

Claude Dashboard: the documents catch up with v0.46.0 and v0.47.0, and the transformation plan is done.

- **README.md** drops the "being rebuilt" banner. It gains a "Moving over from Widget Screen" section, the new settings folder, the daily summary files in "Where your data lives", the 7-day bars, export and the plain list.
- **PRODUCT.md** gains a "What it keeps" section (settings, sign-in, bridge and the daily summary; exports only on request, never a thread name taken from a request). It also says the app never answers a permission dialog (decision 027), the plain list is in Accessibility, and the cloud garage isn't built yet.
- **DESIGN.md** describes the 7-day bars and the plain list.
- **Decision 026**'s "As built" covers phases 5 and 6, and the namespace rename moves to v0.47.3.
- **docs/design/setup.md**: "Also delete my settings…" deletes `%LOCALAPPDATA%\ClaudeDashboard` and never Widget Screen's folder.
- **CLAUDE.md**:
  - the plan is finished;
  - the table lists v0.47.1 (docs), v0.47.2 (Shell's header fix) and v0.47.3 (the namespace rename);
  - the folder rule now names `%LOCALAPPDATA%\ClaudeDashboard`, with Widget Screen's folder only read by the migration;
  - the engine includes history and export;
  - the Claude palette is no longer "from phase 4".
- No app changes.

## v0.47.0 (2 Nov 2026)

Claude Dashboard, phase 6 of decision 026: Watcher package 4, the Cockpit.

- **History.** Settings → Claude → History → Keep a daily summary is on by default. It writes one small file a day to the bridge folder's `history\` and keeps them for 8 weeks. Each file holds tokens, cache reads, replies, threads, the API value, how often a session needed you and the longest wait (counted while the window is open, not minimised), by hour and by project. There are no prompts, replies, file contents or session ids. A day already written never shrinks as its replies age out of the week's scan. Turning the switch off deletes the files.
- **The last 7 days on the plan row.** Where the rev counter shows, its tile gets small bars, one a day with today in the accent, and a tick for what last week had used by this time of day. They are worked out once a minute from the history files and today's replies. The tip says what they count, and the sample says it's a sample.
- **Export.** Settings → Claude → History → Copy summary puts the last 7 days on the clipboard as a Markdown table, with the busiest projects. Save CSV… writes one row per day, project and thread: date, project, thread (a typed, generated or Claude app name, else the id label; never the request's first line), session, models, replies, tokens, cache reads and the API value, left blank where the price isn't known. Fields a spreadsheet would run as a formula are defused.
- **Plain mode.** Settings → Claude → Show a plain list instead of the pit wall. The same projects, threads, states and numbers appear as rows of text, each named for screen readers. Click a row to bring its session forward. Nothing animates. Numbers read before the logs are counted show "≈".
- **No answering permission prompts** (decision 027, Ali's choice). The dashboard never answers a permission dialog. Needs you only brings the session's window forward.
- **Not in this version.** The cloud garage (4.3) waits on a look at the Claude app's cloud thread records. The phone (4.2) moves to the Android companion.
- **Checks.** Nine new checks in `tests/Claude/ClaudeCockpitTests.cs` cover history, the 7-day bars, Copy summary, the CSV and its thread names, and plain mode in a real window with the sample. They also render the bars and the plain list at 1000 × 600 and 640 × 420 into `artifacts\renders`.

## v0.46.0 (2 Nov 2026)

Claude Dashboard, phase 5 of decision 026: its own settings folder.

- **New folder.** Settings, the Claude sign-in and Claude Code's bridge live in `%LOCALAPPDATA%\ClaudeDashboard`. Setup's "Also delete my settings…" option deletes that folder and never touches Widget Screen's.
- **Moving over from Widget Screen.** On the first run, if `%LOCALAPPDATA%\WidgetScreen` has settings, a sign-in or a bridge folder, the window asks to bring them over.
  - **Bring it over** copies the Claude settings, the theme and text size, `claude-session.dat` and the `claude\` folder.
  - If Claude Code was connected, it's connected again from the new folder on the same telemetry port. Every hook and the status line then run the new `ClaudeHook.exe`, and `settings.json` gets its usual dated backup first.
  - **Start fresh** copies nothing. Closing the window decides nothing, so the question comes back next time.
  - Widget Screen's folder is only read. The sheet says to quit Widget Screen afterwards, then uninstall it from Installed apps and keep its settings when asked. If reconnecting fails, the status line says why and points to Settings → Claude → Connect.
- **Checks.** Six new migration checks, using a v0.41 `settings.xml` and a `settings.json` connected to the old folder.

## v0.45.1 (2 Nov 2026)

Claude Dashboard: the documents catch up with v0.44.0 to v0.45.0.

- **Decision 026** gains an "As built" section: the 360 × 300 window minimum, the new window files, the header and window edge, the Settings changes, what the app does while minimised, v0.44.1's installer and icon, and the namespace rename moving to v0.47.1.
- **DESIGN.md, README.md and PRODUCT.md** describe the window as it is: the Claude palette and Terracotta accent, the header, the edge, the window sizes and the Settings tabs.
- **docs/design/setup.md** says what v0.44.1 changed in setup.
- **CLAUDE.md**: the namespace rename is its own PR at v0.47.1, the `CardSizing.Claude` members live in `ClaudeCard.cs`, the window is at least 360 × 300, phase 6 no longer answers permission prompts, and a new rule says the dashboard never answers a permission dialog (decision 027, which arrives with package 4).
- No app changes.

## v0.45.0 (unreleased)

Claude Dashboard, phase 4 of decision 026: the dashboard window.

- **The window is the dashboard.** A resizable window (360 × 300 at the smallest) with a header: the app name, the live summary, 5 h / Today / Week, Quiet and Settings. The words fold away as the window gets narrower. The card fills the rest and picks Small, Medium or Large from its size; it has no heading of its own any more. The window's frame turns Amber while a session needs you and Glacier while one waits for your reply, and the title says "Claude needs you".
- **Its place.** The window opens where it was closed, at its last size and maximised if it was, unless that place is off every screen now. Settings → Window can start it with Windows (a Startup shortcut), start it minimised and stop it remembering its place. Minimised, it still watches the sessions and notifies, but doesn't read logs or move the cars.
- **Settings is its own window.** Three tabs: Theme (palette, accent, text size, clock, Quiet), Claude (as before) and Window. Changes show on the dashboard as you make them; Cancel or closing the window puts the saved ones back.
- **The Claude palette.** A light palette beside Midnight, Slate and Warm: warm paper, soft black text and terracotta, with Amber darkened so warnings read on paper. Picking it brings the terracotta accent along. Every palette's text, Amber and accent pass contrast checks.
- **Under the hood.** One window and one controller (`AppController`); the widget app's window, controller, card sizing table and sticker parts are gone. Renders take `--render-size`, `--render-scale` and `--render-settings --render-tab`.

## v0.44.1 (2 Nov 2026)

Claude Dashboard: setup without the sensors, and a new icon.

- **New icon.** A Lime race car, head-on with its headlights on, in front of a Glacier pit garage on a Midnight tile (`assets\ClaudeDashboard.ico`). `scripts\make-icon.ps1` draws it from the app's own pixel art (`PitArt`, `PitRaster`), so it matches the dashboard; build first, then run it with `-STA`.
- **Setup without hardware sensors.** The "Install hardware sensors" checkbox, `--no-sensors` and the sensor files are gone with System stats. Updating removes the `sensors` folder an older install added. Uninstalling still removes an old "Widget Screen full sensors" task, which needs administrator rights if one exists.
- **Uninstall wording.** "Also delete my settings, Claude sign-in and Claude Code connection files", with a note to disconnect Claude Code first so its hooks don't point at a deleted file.
- **Scripts.** `run.ps1` loses `-Windowed`; the build, release script and workflows lose their sensor steps.

## v0.44.0 (2 Nov 2026)

Claude Dashboard, phases 2 and 3 of decision 026: everything but the Claude Code watcher goes. v0.43.0 is skipped on purpose; both phases ship together in this release.

- **One widget.** The 17 other widgets are gone: Focus, Now Playing, Quick Launch, the sound mixer, Spotify, System stats, Weather, the Soundboard, Storage, World clocks, Prayer times, Notes, Clipboard history, Macros, Calendar, Games and Smart home, with their Settings tabs, hardware sensors and checks.
- **No phone.** The Android app and the phone link are gone; nothing listens on the network any more. The only listener left is Claude Code's telemetry receiver on 127.0.0.1:43832.
- **An ordinary window.** The app opens as a resizable window with the Claude card filling it under the header, and closes like any other app. There is no secondary-screen fullscreen, Edit layout, pages, widget inks, tray mode or focus keeping, and Settings has two tabs: Theme and Claude.
- **Claude data untouched.** Claude Code's connection, the sign-in, the project ticks, folds, period and every other Claude choice carry over. Old `settings.xml` files load; the other widgets' fields are dropped when settings are next saved, and a Claude card an old layout had hidden now shows.

## v0.42.1 (2 Nov 2026)

Claude Dashboard, phase 1 of decision 026: the names.

- **New name.** The exe, setup, uninstaller, Start menu and Startup shortcuts, Installed apps entry and window title say Claude Dashboard (`ClaudeDashboard.exe`, `ClaudeDashboard-Setup-vX.Y.Z.exe`, installed in `%LOCALAPPDATA%\Programs\Claude Dashboard`). The start-up error box and the Settings → Claude texts about sign-in, telemetry and the status line use the new name too.
- **Settings stay put.** Settings and Claude Code's hooks still live in `%LOCALAPPDATA%\WidgetScreen` until phase 5, so nothing needs reconnecting.
- **The old app.** Setup warns when Widget Screen is running or another startup shortcut opens it, because for now both share the same settings and Claude Code connection. The uninstall option to delete settings says the folder is shared.
- **Releases.** The GitHub Release ships the setup exe and the portable zip, with no APK; v0.41.0 is the last release with the Android app. CI builds and checks the Windows app and setup only. `scripts\release.ps1 -UpdateShortcut` makes a "Claude Dashboard" desktop shortcut and removes the old "Widget Screen" one when it opened a release from this folder.
- **Builds.** Builds work before the first tag (version 0.0.0), and without the hardware-sensor files folder. Every XAML file under `src\` is embedded, ready for the new window. CI keeps window renders with its test builds.

## v0.42.0 (2 Nov 2026)

Claude Dashboard, phase 1 of decision 026: the documents.

- **New rules.** CLAUDE.md, PRODUCT.md, DESIGN.md and README.md describe Claude Dashboard: a Windows desktop app whose whole window is the Claude Code watcher. Android is a roadmap item.
- **Decision 026** records the transformation plan, Ali's answers and the six phases.
- **Archive.** The Widget Screen spec, the non-Claude decision records and design notes move to `docs/archive/`, unchanged except for links the move broke. AGENTS.md is removed.
- No app changes.

## v0.41.0 (2 Nov 2026)

Claude Code watcher: instant and everywhere (package 3 of the Watcher action plan; Windows only, the phone follows later; decision 025).

- **The card reacts at once.** Windows tells the card when a session, a log or a Claude app thread changes, so states show as they happen. The old 2-second read is now a 30-second fallback.
- **Windows notifications** when a session needs you, gets stuck, or finishes a turn of 10 minutes or more. Click one to bring the session forward. Settings → Claude → Notify me in Windows when a session needs me (on by default; silent in Quiet mode).
- **Helpers on the car.** Small pit-crew figures for each helper (subagent) running and a "3/5" task sticker. A lead whose helpers are still working counts as working.
- **Resolved without the Claude app.** A thread you closed in Claude Code (exit, logout or `/clear`) shows as resolved, unless it picks up again. A thread's name comes from Claude Code's SessionStart when it sends one.
- **Worktrees and managed settings.** A `--worktree` session sits in its original folder's garage. Settings → Claude says when an organisation's managed Claude Code settings override the hooks, status line or telemetry. `CLAUDE_CONFIG_DIR` set for your Windows user is honoured.
- **Lighter and fresher.** Project groups are worked out once per update, and thread tips always show the current numbers.
- The phone link already carries the alerts (`claudeAlerts`, `claudeAlertSeq`, `claudeQuiet`) and each thread's `tasksDone`, `tasksTotal` and `endReason`. The phone app shows them in the Android pass.
- No new polling, ports or outbound calls; the watchers replace most of the 2-second reads.

## v0.40.0 (2 Nov 2026)

Claude Code watcher: honest numbers (package 2 of the Watcher action plan; Windows only, the phone follows later; decision 024).

- **Claude Code's own cost.** The API value uses Claude Code's own cost where its telemetry sends one, and list prices otherwise. Replies from a model with no known price share what's left of the session's total from Claude Code's status line; with no total, the value shows "~" with a tip naming the model, instead of counting $0. The tip also compares Claude Code's count for the running sessions with the widget's.
- **The status line refreshes every 30 s** and keeps Claude Code's cost, session and API time, lines, context size, output style, agent and worktree. A refresh you set yourself is kept. Settings → Claude offers Update connection once.
- **Side panel facts you choose.** Settings → Claude → Side panel facts picks four of: tokens, cache hits, tool calls, lines, slowest tool, tool failures, API errors in the last hour, helpers' share, edits accepted and rejected, commits and pull requests, and active time (Claude's and yours). They follow the card's period.
- **Revving means slow.** A car revs when a tool call runs past twice its usual time for that tool (3 to 60 s), not after a fixed 15 s. A request after a call ends it even if its result never came.
- **Retries show.** A car hit by an API error says "Retrying after an API error · HTTP 529 · attempt 3" in its tip.
- **When the 5-hour limit fills.** The tank says "full in 1 h 40 at this pace" when the last half hour's pace would fill it before it resets.
- **Plan chips** under the plan row: narrower weekly windows beyond the two the week tile names, extra usage, and a Claude apps gateway's spend limit, warning from 80%. They step aside while a session needs you.
- **Helpers apart.** A thread's tip splits its own tokens from its helpers' (subagents').
- **Faster first scan.** Big logs are read from the newest end in 1 MB chunks, at most 64 MB a scan, stopping at the week's edge, so the card fills in at once and catches up over the next scans.
- The phone link already carries the new numbers (`factsBy`, `factKinds`, `costSource`, `unpriced`, `limitEta`, `claudeCodeCost`, `estimateCost`, `spend`, and `helperTokens`, `costUsd`, `mode`, `revving`, `errorStatus`, `errorAttempt` per thread). The phone card doesn't show them yet.
- No new timers, polling or outbound calls; the usage check stays the only one, every 5 minutes.

## v0.39.0 (2 Nov 2026)

Claude Code watcher: trust the sources (package 1 of the Watcher action plan; Windows and the phone; decision 023).

- **No live session disappears.** A session leaves the card only when it ends or its process exits, whatever the process is called. `/clear` keeps it. Sessions that only Claude Code's own list or its logs know show too, straight after Connect.
- **Stuck:** a turn that ended on an API error shows a spinning car with the reason ("Rate limited, resets 14:05", "Overloaded, try again", "Sign in again", "Reply too long"…) until the next prompt.
- **Usage-limit waits:** Claude Code carrying on by itself shows as working, waiting for Enter as Needs you, and giving up as Stuck.
- **Context ring** on each car from 60% of the context window, warning from 80%.
- **Plans with no usage meters** say so instead of asking you to sign in again. A sign-in ends only when it can't be renewed. Cowork and apps weekly limits and extra usage are read.
- **Telemetry port:** Connect picks a free port (43832–43839), and the card says when the port is taken.
- **More hooks:** failed tools, denied permissions, helpers, tasks, model switches and folder changes. Settings → Claude offers Update connection once.
- Cursor, JetBrains, Zed and the Agent SDKs are named as where a thread runs.
- **The phone shows the same.** The phone link adds `reason`, `reasonText`, `context`, `workers`, `tasks` and `failed` per thread, `warning`, and `extraUsage` in the limits.
- No new timers or polling; the usage check stays the only outbound call, every 5 minutes.

## v0.38.0 (1 Nov 2026)

Sticker Sheet, phase 5: the Soundboard (Ali's approved redesign; Windows and the phone; decision 021). Ali skipped phase 4 and the rest of phase 5, so the redesign ends here.

- **Sampler pads:** sounds are pads edged in the Soundboard's colour. Medium pads are numbered; large pads add a play mark and where the sound comes from.
- **A tapped pad lights up** for a moment (not in Quiet mode).
- **The voice channel you're in** shows as a sticker in the heading, with "Not in voice", "Unavailable" (Discord can't be reached) and "Not set up".
- Discord not set up, Discord not running and no sounds yet each say why in a dashed box.
- **The phone shows the same.** The phone link adds `failed` to the Discord section.
- No new polling, timers that repeat, network requests or effects.

## v0.37.0 (1 Nov 2026)

Sticker Sheet, phase 3: the readings (Ali's approved redesign; Windows and the phone; decision 021).

- **System stats:**
  - A sticker in the heading names the busiest reading ("GPU 92%"), filled from 80%.
  - CPU and GPU dials wear their temperature as a badge.
  - Large tiles show the lowest and highest of the recent readings.
  - The CPU and GPU show short model names ("Ryzen 7 7700", "RTX 4070").
- **Weather:**
  - The reading sits on a sky band tinted by the weather and time of day, with the condition on a round sticker.
  - Feels, Humidity and Rain chips; large cards add UV, Wind and High / Low.
  - Large cards (and tall medium ones) show the next 12 hours as a temperature curve over rain bars, and a sunrise–sunset arc.
  - Still one request to Open-Meteo, now also asking for humidity, rain chance, UV, sunrise and sunset.
  - Offline keeps the last reading dimmed; no city, or no reading at all, says why in a dashed box.
- **Storage:** each drive is a jar filled by its used space. A drive low on space fills with Amber caution tape.
- **World clocks:**
  - Each city gets an analog clock face, yellow from 7 AM to 7 PM and lavender otherwise, labelled Day or Evening.
  - Large cards add a 24-hour band with 9 to 5 shaded and a line at the time now.
- **Prayer:**
  - The day's sun path: an arc from Fajr to Isha with each prayer placed by its time and the sun at now.
  - Large cards add Hijri, Method and Alert chips.
- Not set up (no city, no time zones, no drives) shows the reason in a dashed box.
- **The phone shows the same.** The phone link adds `cpuName` to System stats and `alert` to Prayer.
- No new timers, polling or effects, and no extra network requests.

## v0.36.0 (1 Nov 2026)

Sticker Sheet, phase 2: the default five (Ali's approved redesign; Windows and the phone; decision 021).

- **Focus:**
  - Large cards show a kitchen-timer dial, with the time left as a wedge in the card's ink, minute ticks, and the end time in the middle.
  - Today's sessions show as tomato dots, with a Today minutes chip and a Next break chip.
  - The status sticker reads FOCUSING, PAUSED, DONE or "2 TODAY".
- **Now Playing:**
  - The cover is a sleeve with a record peeking out while something plays, or an empty dashed sleeve when nothing does.
  - The card is tinted with the cover's colour.
  - New album line, and Length and Left chips when there's room.
- **Quick Launch:** buttons are keycaps that drop when pressed, with an LED dot for buttons that are on. Big buttons show a hotkey's keys.
- **Sound mixer:**
  - Large cards show channel strips: the master first with an LED ladder, then one strip per app. Apps opens the full list.
  - An ON AIR badge shows while the microphone is live, and MUTED while it's muted.
  - The level meter is LED segments and also shows on medium cards.
  - The phone has the strips and the badge but no level meter: the phone link doesn't carry output levels.
- **Spotify:**
  - Tabs look like record-crate dividers.
  - Albums and playlists show their song count, owner or year.
  - On Large cards the side panel is tinted with the cover's colour.
- **The phone shows the same.** The phone link adds `album` and `tint` to Now Playing and `caption` to Spotify items.
- No new timers, polling, network requests or effects.

## v0.35.3 (1 Nov 2026)

Claude watcher review fixes (Ali's review, 1 Nov 2026; Windows and the phone; decision 022):

- **Your turn stays visible.** An open thread whose latest reply the Claude app marked "completed" shows as Your turn. It only counts as resolved, and is hidden, after 30 idle minutes. Working, needs you and compacting always show. Ended "completed" threads now really do count as resolved: before, a later write to their log kept every one of them showing.
- **Tools and lines come from Claude Code's logs**, so they're right for every session. "Asked" still comes from telemetry, and says PARTIAL when telemetry saw fewer calls.
- **Text fits.**
  - Medium no longer cuts off the heading, the project lines or the replies.
  - At 140% text, Large keeps thread titles readable by dropping the where-and-age text, then Cache reads.
- **The ledger shows whole rows**, and rolling cars keep their covers and pass under the chips.
- **The Claude card wears the label strip** like the other cards, and Large says how fresh the plan numbers are ("LIVE · AS OF 2:40 PM").
- **Lighter and safer:**
  - the cars' frame timer stops when the window is minimised;
  - the dashboard no longer waits for the first log read;
  - the telemetry receiver only accepts JSON;
  - a gone process's window is forgotten;
  - Connect keeps dated backups of Claude Code's settings.json.

## v0.35.2 (1 Nov 2026)

Releases publish again, and every change goes through a pull request.

- **Fixed: GitHub Releases weren't published.** The release workflow's last step stopped on gh's "release not found" message, which Windows PowerShell treats as an error, so v0.35.1 built and passed its checks but got no release. The step now checks gh's exit codes itself.
- **CI:** the workflows use checkout, setup-java and upload-artifact v5, which GitHub runs on Node 24, so the deprecation warnings are gone.
- **Every change goes through a pull request** (Ali's request): CLAUDE.md and BRANCHES.md now say to open a draft PR for every branch, keep it current with `main`, and merge on GitHub with a merge commit once CI is green, then tag. The open PRs replace the branch table in BRANCHES.md.

## v0.35.1 (1 Nov 2026)

GitHub, CI and a release for every merge (decision 022).

- **The project is on GitHub** at RLcafe/OpenSidecar, with `main`, every tag and the open branches.
- **CI on GitHub Actions** (`.github/workflows/ci.yml`): every push and pull request builds and tests the Windows app, the setup exe and the Android app on a Windows runner; Markdown-only and docs-only changes skip it. CLAUDE.md and BRANCHES.md now say to push branches and tags and to wait for a green run before merging.
- **Every merge gets a GitHub Release** (`.github/workflows/release.yml`): pushing a `vX.Y.Z` tag builds and tests it, then publishes the setup exe, a portable zip and the APK with the version's changelog notes. Merges that only touch docs, tests, scripts or CI now get a PATCH version too.
- **Fixed:** when a mute or volume change hit a missing audio device, the "Reconnecting…" note could be raised off the window's thread and throw. It now always goes back to the window (Windows only; the phone sends these through the PC).
- **CI:** the three live audio checks say SKIP on a machine with no audio output, such as GitHub's runner, instead of failing. Any other audio error still fails them.

## v0.35.0 (1 Nov 2026)

Sticker Sheet, phase 1: foundations (Ali's approved redesign; Windows and the phone; decision 021).

- **Every card but Claude Code wears an ink,** one of eight colours: the four accents plus Coral, Mint, Rose and Lemon. Each widget has its own by default, for example Focus Coral, Now Playing Rose, the mixer Lime and Spotify Mint. Amber stays for warnings.
- **Label strips replace the grey card titles.** Each is a bold pill in the card's ink with dark text. Narrow cards show an ink dot and a short name.
- **Status stickers replace the right-hand captions:** Now Playing's source, the mixer, System stats, Storage, Prayer, Notes, Clipboard, Calendar, Smart home, World clocks and Weather's condition.
  - A fact is outlined in the ink, and a prayer starting now fills it.
  - Warnings are Amber with caution stripes and a glyph.
  - UNAVAILABLE is a dashed outline, OFFLINE is a stamp, and loading is a plain outline.
- **Choose a card's ink in Edit layout:** "Ink for …" at the top of the Widgets list, with seven swatches. It shows at once; Save layout keeps it and Cancel drops it.
- **Settings → Theme → Widget colours:** Each its own, or Accent only (every card in the accent, like before).
- **The phone shows the same inks and stickers.** Inks changed on the phone are saved on the PC with its layout. The phone's Theme settings change Widget colours on the PC. The phone link sends the inks with the layout it already sends: no new port, endpoint or polling.
- Card bodies are unchanged in this phase. There are no new timers, effects or animation.
- Docs: `DESIGN.md`, `docs/design/sticker-sheet.md` and decision 021. Renders: `tests\sticker-renders.ps1`.

## v0.34.1 (1 Nov 2026)

Resolved Claude threads (Ali's choice; Windows and the phone; decision 020):

- **Threads the Claude app marks "completed" now show as Resolved.** The car is under its cover, dimmed, last in its project, with a "Resolved" chip. "Review ready" and "blocked" threads don't count.
- **Live state wins.** If a resolved thread starts working again, needs you or is compacting, it shows that state right away and isn't hidden.
- The setting is now **Hide resolved and archived threads** (still on by default). Hidden threads still count in their project's totals ("+N hidden").

## v0.34.0 (1 Nov 2026)

Archived and idle Claude threads (Ali's request; Windows and the phone; decision 020):

- **Archived threads stand apart.** A thread you archived in the Claude app now shows as **Archived**: its car is under its cover, dimmed more than an ended one, and it sits last in its project. Before, archived threads were left out completely.
- **Settings → Claude → Hide archived threads** (on by default) and **Hide idle threads** (off by default). They apply to the PC's card, the phone and the heading's thread count. Hidden threads still count in their project's tokens, replies and lines, and the project says "+2 hidden".
- The PC has no "resolved" flag; the Claude app's archive is the only one. Ended threads stay Ended.

## v0.33.2 (1 Nov 2026)

Claude ledger and timeline polish (Ali's requests; Windows and the phone):

- **The Models column names the model** ("Opus 4.8", "Fable 5.1") in its colour, instead of a bar that was solid green for every thread. A thread that mixed models shows its top two with shares ("Opus 70% · Sonnet 30%"); hovering on the PC lists them all.
- **Columns fit at larger text sizes.** Models and Lines step aside when the text is bigger, so thread titles keep their room, and Replies stays readable.
- **The 5-hour timeline shows one lane per project**, with taller bars and no lanes for single threads.

## v0.33.1 (1 Nov 2026)

- **Clearer dust behind working cars** (Ali's request; Windows and the phone). Light puffs kick up behind each tyre at ground level and drift back and away: two per tyre, four when the thread burns hard. Before, the dust was faint specks hidden under the car.

## v0.33.0 (1 Nov 2026)

Claude watcher fixes from the live app (Ali's report; Windows and the phone; decision 019):

- **The cars move again.** Quiet mode was holding them still. Now they keep driving, revving and rolling into their garages in Quiet mode too. Settings → Claude → **Keep the cars still in Quiet mode** brings back the old way. They still stop whenever the card is hidden or the window is minimised.
- **"Waiting for me" stands apart from idle.**
  - **Needs you:** amber hazards, an amber roof beacon and an Amber chip.
  - **Your turn:** Claude has finished; brake lights on and a blue chip.
  - **Idle:** your turn for over 30 minutes, or nothing asked yet; engine off, dimmed, grey chip.
  - The heading counts "your turn".
- **Plan meters match the Claude app.** The plan's newer limits list is read (for example "Fable only 2%"). When the plan has no 5-hour window, the card says so instead of showing an empty meter.
- **Tokens no longer count cache reads**, which made them hundreds of times too big (207M became 2.1M for one thread). Cache reads have their own column. The API value is hidden by default, since your plan doesn't pay those prices; replies show instead. Settings → Claude can bring the API value back.
- **Every project shows.**
  - **Periods are rolling:** the last 5 hours, today, the last 7 days. "Week" used to follow the plan's week, which had just reset.
  - **Quiet garages:** a ticked project with no thread in the period still has a garage, dimmed, with when it was last used.
- **The 5-hour chart** has a lane for every project used in that time, and under a busy project, its busiest threads.
- **Working cars kick up dust** behind their tyres, more when they burn more, over a ground line that scrolls by.

## v0.32.1 (1 Nov 2026)

Claude thread names (Ali's report; Windows and the phone; decision 018):

- **Threads have real names again.** The Claude app calls every thread started through Remote Control "Remote Control session (from a project)", and the card showed that. Placeholders like that (and "New session", "Untitled") no longer count as a name.
- **The name, in order:** a name you gave the thread, a title the Claude app or Claude Code made for it, then the first line of the thread's first request (up to 60 characters). A short label from its id ("Thread 1b2187") is the last resort.
- **Remote Control is where a thread runs, not its name.** It shows beside the name ("Remote Control · 6 min", "Remote Control · ended 1 h ago").
- **A better title takes over** as soon as one appears. The phone shows the same names.

## v0.32.0 (1 Nov 2026)

Claude Watcher v2, the pit wall (Ali's approved design; Windows and the phone; decision 017):

- **Projects are pit garages and threads are race cars,** drawn in code as pixel art (no images). A car's class follows the model the thread used most: a grand tourer for Opus, a sports car for Sonnet, a hot hatch for Haiku. Its colour is the project's. No faces: a car shows what its thread is doing.
  - **Working:** its wheels turn as fast as it burns tokens, with exhaust puffs when heavy.
  - **Long tool run:** it revs on its brakes.
  - **Needs you:** hazards blink and the headlights flash.
  - **Compacting:** it's up on the jack.
  - **Rate-limited or an API error:** wheelspin in tyre smoke.
  - **Done:** the brake lights glow, then fade.
  - **Ended:** a cover drops over it.
  - Near the 5-hour limit a fuel lamp blinks; at the redline the brake discs glow.
  - Garages show a light under the shutter when busy, an amber beacon when a car inside needs you, and their car count when folded.
- **Tap a garage to roll its cars in or out.** Going in, each car backs out of its row, turns and drives into the bay with its tail lights toward you. Coming out, it drives toward you with its headlights on, then into its row. It takes about a second; Quiet mode skips the motion.
- **Large is a pit wall:**
  - the 5-hour tank gauge with your pace ("on pace for 92%", Amber when you'd run out);
  - the week as seven day-laps;
  - a rev counter in tokens a minute with the last hour's burn;
  - a pit stop strip for the thread waiting on you, with what it wants and **Go to it**;
  - a ledger of projects and threads with tokens, ≈ $, model mix and lines added and removed;
  - on wide cards, a 5-hour timeline per project and today's tokens, cache hit rate, tool calls and lines.
- **Medium** shows the two plan meters with pace over your top projects with their cars. **Small** shows the car that needs you over the 5-hour meter.
- **Motion costs nothing when unseen:** the cars only move while the card is on screen, the window isn't minimised and Quiet mode is off.
- **Settings → Claude offers Update connection once:** it adds a PreCompact hook so compacting shows. Tool calls and lines changed come from Claude Code's telemetry, in sessions started after connecting.
- Not yet tried: the phone card on a device, and live tool, lines and compaction readings from a real session.

## v0.31.0 (1 Nov 2026)

System stats upgrade with hardware sensors (user request; Windows, with the phone showing the same):

- **More readings:** CPU temperature, GPU temperature, video memory (VRAM) and the frame rate of the game in front, beside CPU, GPU, RAM, network and uptime. GPU temperature and VRAM come from Windows itself and need nothing extra. Every reading this PC can't give says why (for example "Needs admin: full-sensors mode" or "Needs the PawnIO driver") instead of showing a number.
- **Graph styles for each reading:** Auto, Line, Bars, Sparkline, Ring or Gauge, in the new Settings → Stats tab, with a checkbox per reading. Temperatures follow Settings → Weather's units and show °C or °F. The designs fit all nine readings, and a small card counts the ones it can't show.
- **LibreHardwareMonitor 0.9.6** for CPU temperatures: pinned by SHA-256, checked before it loads, with its MPL-2.0 licence and a third-party notice. Setup has a new **Install hardware sensors (LibreHardwareMonitor)** checkbox (on by default) that says what the extra packages are; untick it and the app runs without them. The PawnIO driver it needs for CPU temperatures isn't included: Settings says whether it's installed and links to pawnio.eu.
- **Full-sensors mode** (Settings → Stats): one Windows prompt registers a task that starts Widget Screen with administrator rights, for CPU temperatures and frame counting. Apps started from Quick Launch, macros and Games still start without administrator rights. Turning it off, or uninstalling, removes the task.
- **Frame rates** from RivaTuner Statistics Server when it runs (any game), or by counting DirectX frames where Windows allows it (administrators and Performance Log Users).

See `docs/decisions/016-hardware-sensors.md` and `docs/design/system-sensors.md`.

## v0.30.0 (1 Nov 2026)

Projects with their threads on the Claude Code card, and refresh times you choose (Ali's request; Windows and the phone; decision 016).

- **Each project is a big critter, and its threads are baby critters under it.** That covers every Claude Code thread of the chosen period (5 h, Today or Week), running or ended, with the titles the Claude app's sidebar shows. A thread sits under the folder it started in, with worktrees under their project. Ended threads sleep, dimmed. A project's face follows its most urgent running thread, so Needs you still turns it Amber. Under each project: how many threads and its tokens and "≈ $". Tap a running baby to bring its window forward.
- **Tap a project to fold its threads in.** The babies get eaten (they shrink into its mouth), and it shows a count bubble and full cheeks. Tap again and they pop back out. Quiet mode skips the motion. The choice is kept and shared with the phone, and a folded project still lights up when a thread inside needs you.
- **Large:** the plan meters and period switch in a strip, then a column per project with its babies, scrolling inside the column when there are many. **Medium:** a row of projects with up to three babies each, over the plan meters. Small is unchanged.
- **Settings → Claude → Refresh:** "Update the card every … seconds" (default 2, minimum 1) and "Check plan limits every … seconds" (default 300, minimum 10, with a note under 60 that Anthropic may slow down very frequent checks). If Anthropic answers 429 or 5xx, the check waits twice as long each time (up to 15 minutes), and the card shows "Plan limits as of hh:mm" until it gets through again. Quiet mode no longer stretches these. The phone follows the card's interval, at most every 2 seconds.
- Settings → Claude → Projects shows how many threads each project had this week.

## v0.29.0 (1 Nov 2026)

The Claude Code widget now works with the Claude app's Code tab (Ali's report: "it's not working"; Windows and the phone; decision 015). The fix was merged just before the v0.28.0 tag, so that tag already contains it; this is where it's described.

- **Every Claude Code session shows**, whether it runs in the Claude app's Code tab, a terminal or an editor, including ones started before you connected. Before, idle Code tab sessions never showed. The card now also reads Claude Code's own list of running sessions. Needs you also catches permission dialogs (a new permission hook) and questions.
- **Plan meters that actually fill: Settings → Claude → Sign in with Claude.** The Code tab never sends the plan's limits, and keeps Claude Code's sign-in where the widget can't use it, so the meters stayed empty. Widget Screen now has its own Claude sign-in in your browser. It can only read your profile and plan usage (no chats, no credits), is kept encrypted on this PC and renews itself. It checks your 5-hour and weekly limits every 5 minutes, whether or not Claude Code is open. When a check fails, the card keeps the numbers and says "Plan limits as of 10:42"; when the sign-in ends, it says so. Opus and Sonnet weekly limits show too when your plan has them. Your phone gets only the percentages.
- **Live usage through Claude Code's own telemetry.** Connect (or **Update connection**, for a connection from v0.23–v0.27) turns on Claude Code's OpenTelemetry and sends it only to Widget Screen on this PC (127.0.0.1, port 43832, not reachable from the network). It never includes prompts, replies or tool details. Telemetry settings you already have are left alone. It reaches sessions started after you update.
- **Usage per project and per session, with cost.** Tokens and what they'd cost at API prices ("≈ $"; your plan doesn't pay these), from Claude Code's logs plus telemetry, each reply counted once. A session that moves into a worktree stays under the folder it started in.
- **Choose which projects show:** Settings → Claude → Projects has a tick per project (newest first, with when it was last used and this week's tokens and cost) and "Show new projects automatically". Unticked projects leave the card's sessions and usage on the PC and the phone. The plan meters always show your whole plan.
- **Medium shows the plan and the projects together** (project rows beside the critters, plan meters below). Small shows the 5-hour percentage when there's room. The phone's card matches.
- Verified on this PC, read-only: the hooks fire from Code tab sessions, and the new reader finds the three running desktop sessions with their states and this week's usage per project. Not yet tried: your own sign-in (it needs your browser and account), telemetry from a session restarted after Update connection, and the phone card on a device.

## v0.28.0 (1 Nov 2026)

### Added

- **Smart home widget** (Windows and the phone): turn Home Assistant lights, switches and plugs on and off, dim lights and set thermostats from the dashboard. Settings → Home connects with Home Assistant's address and a long-lived access token (kept encrypted for your Windows account; home-network addresses only) and picks up to 24 devices. Small rows that scroll, Medium tiles, Large tiles with dimmers. The phone controls the same devices through the PC and never gets the token. Hidden by default. See `docs/design/smart-home.md`.

## v0.27.0 (1 Nov 2026)

A Games widget (user request; Windows and the phone):

- **Your installed games, one tap to play.** Games from Steam, the Epic Games Launcher and the Xbox app, recently played first, six a page like Quick Launch. A tap starts the game through its store. Turn it on in Edit layout; it starts hidden.
- **Playtime only where it's real.** Steam games show how long you've played and when you last played. Epic and the Xbox app keep that online, so their games show nothing rather than a guess.
- **Read from the PC, no accounts.** The stores' own files are read with their apps closed and no internet. Choose which stores to list in Settings → Widgets → Games.
- **The phone** shows the same games and starts them on the PC.
- Not yet tried: starting games from the card (no check starts a game), Xbox games (none on the development PC; checked with sample files), and the phone on a device.

## v0.26.0 (1 Nov 2026)

A Calendar and agenda widget (user request; Windows and the phone, the same designs on both):

- **What's next, with a countdown.** The next event's day, time, title and place, and "in 25 m" (or "now · 45 m left"). Turn it on in Edit layout; it starts hidden.
- **From your calendar feeds, no sign-in.** Add up to eight read-only calendar addresses in the new Settings → Calendar tab: Google Calendar's "Secret address in iCal format", an Outlook calendar published as ICS, or any other .ics address. The widget only reads them. Signing in with a Google or Microsoft account instead would need an app registered with them, which this app doesn't have.
- **The addresses stay private.** Anyone with one could read that calendar, so they're kept encrypted for your Windows account, shown shortened in Settings, and never sent to your phone.
- **Repeating events, time zones and changes** are understood: weekly, monthly ("last Friday"), yearly and daily repeats, skipped and moved occurrences, cancelled events, all-day events, and each feed's own time zones and daylight saving.
- **Updated every 15 minutes** while the widget is shown (30 in Quiet mode), and when you tap the card. If a feed can't be read, it keeps its last copy and the heading says so.
- **Small, medium and large designs**: the next event; the next event with the ones after it; and an agenda for two weeks grouped by day, with a coloured dot per calendar.
- Settings tabs are a little narrower so all twelve stay on one row.
- Not yet tried with real Google and Outlook feeds (the checks use feeds in their formats), or on a phone.

## v0.25.0 (1 Nov 2026)

A Macros widget (user request; Windows and the phone):

- **Stream Deck–style buttons that run several steps in a row.** Each macro is a chain of up to 12 steps: a keyboard shortcut, opening an app, file, folder or website, a dashboard or media control (mute, microphone, Focus, Quiet mode, volume, play/pause…), a wait, or a script (.ps1, .bat, .cmd or .vbs). Six buttons a page, like Quick Launch, growing with the card. Turn it on in Edit layout; it starts hidden.
- **A new Macros tab in Settings, after Quick Launch**, to make and edit them: name, icon and colour, then the steps in words, each one movable and changeable, with a step editor that says what's wrong before a step is added.
- **Nothing consequential runs by surprise.** A macro with a script, a step that a Quick Launch button would confirm (arguments, Delete, Alt+F4), or "Ask before running" turned on asks first, listing every step. One macro runs at a time, its button shows it's running, and a step that fails stops the rest and says which step it was.
- **On the phone**, the same buttons run the macros on the PC, asking first with the same list when needed. Macros are made on the PC.
- Not yet tried: real hotkey, launch and script steps (the checks only use waits and Quiet mode), and the phone card on a device.

## v0.24.0 (1 Nov 2026)

A Clipboard history widget (user request; Windows, with the phone as a remote):

- **The last texts you copied, one tap from pasting again.** A tap puts the text back on the clipboard and pastes it into the app you're using; the dashboard never takes the keyboard away from that app. Turn it on in Edit layout; it starts hidden.
- **Private copies stay private.** Text that password managers mark as private (the same marks Windows' own clipboard history respects) is never kept. The clipboard is only watched while the widget is on one of your pages, and Windows tells the app about each copy, so nothing polls.
- **Pause and Clear** on the card (Clear asks first). Settings → Widgets → Clipboard history sets how many texts to keep (5–50, default 10).
- **Memory only by default.** The history is gone when the app closes, unless you turn on "Keep the history when Widget Screen closes", which saves it encrypted for your Windows account.
- **Small, medium and large designs**: one-line rows; rows with when each was copied; and two-line previews with their length.
- The phone shows short previews of the PC's history and pastes on the PC. It can't keep a history of the phone's own copies, because Android doesn't let apps watch the clipboard in the background. Not yet tried on a phone, and a live copy and paste weren't tried by the checks (they would touch your real clipboard).

## v0.23.0 (1 Nov 2026)

A Claude Code widget (user request; Windows and the phone, the same designs on both):

- **Your Claude Code sessions as critters.** Each session on this PC is a little drawn creature, the same shape and colour for the same project every time: dozing when it's ready, eyes darting while it works, a happy face when it's done. Turn it on in Edit layout's Widgets list; it starts hidden.
- **It lights up when Claude needs you.** A session waiting for a permission or an answer turns Amber, throws its arms up and hops; the card gets an Amber border and the heading names the project ("api-server needs you"). If the card is on another page, a "Claude needs you" pill appears in the header and takes you there. An optional chime (Settings → Claude, off by default, silent in Quiet mode); the phone taps once.
- **Your plan's limits.** The 5-hour and weekly meters (Amber from 80%, "Limit reached · back 3:40 pm" at 100%), from Claude Code's status line and a read-only check of your plan's usage every 5 minutes with Claude Code's own sign-in. It uses no credits, and the sign-in is never stored, changed or sent anywhere else; the phone gets only the percentages. Settings → Claude can turn the check off.
- **Usage per project and per session**, counted from Claude Code's logs on this PC, for the 5-hour window, today or the week.
- **Tap a session to bring its terminal or editor to the front** (from the phone too).
- **Small, medium and large designs**: one big critter with its project and state; a lineup of critters over the two meters; and a row per session (title, project, model, how long, tokens) beside a panel with the meters, a period switch and usage by project.
- **Settings → Claude → Connect Claude Code** adds Widget Screen's hooks and status line to Claude Code's `settings.json`, after saving a backup next to it (`settings.json.widgetscreen-backup`). Your own hooks stay, and a status line you already had keeps showing. Disconnect takes out exactly what Connect added. The bridge, `ClaudeHook.exe`, comes with the app and the setup exe.
- Not yet tried with real Claude Code hooks firing, bringing a real terminal forward, the live usage check, or on a phone (labelled sample data shows the designs).

## v0.22.0 (1 Nov 2026)

A Notes and to-do widget (user request; Windows and the phone, the same designs on both):

- **A scratchpad and a to-do list on the dashboard.** Type notes, add to-dos with Add (or Enter), tick them off, delete one, or clear all the done ones (it asks first). The heading counts what's left ("3 TO DO"). Turn it on in Edit layout; it starts hidden.
- **Kept on the PC** in `notes.xml` next to your settings, saved a second after you stop typing and at once for list changes. Typing in the notes takes the keyboard like any text box on the dashboard.
- **Edit from your phone too.** The phone shows the same notes and list and changes them on the PC; what you type on the phone stays on screen until the PC has it.
- **Small, medium and large designs**: tabs for Notes and To-do on smaller cards, and both side by side on a large one.
- Not yet tried on a phone or emulator.

## v0.21.0 (1 Nov 2026)

A Prayer times widget (user request; Windows and the phone, the same designs on both):

- **Today's five prayers and a countdown to the next.** The next prayer shows its time and "in 1 h 12 m"; past prayers are dimmed and the next one is in your accent colour. For 20 minutes after a prayer begins, the card's heading says so ("ASR NOW"). Turn it on in Edit layout; it starts hidden.
- **Worked out on the PC, with no internet.** The times come from the sun's position with the standard astronomical method. Choose the calculation method (Umm al-Qura, Muslim World League, ISNA, Egypt, Karachi, Gulf, Kuwait, Qatar, Singapore, Turkey, Tehran or Jafari; the default follows your region) and the Asr school (Standard or Hanafi). Far-north summers use the angle-based rule, like published timetables. Checked against published timetables for Riyadh and London, within two minutes.
- **A new Prayer tab in Settings, after Weather.** Search for a city (the same city search as Weather), or leave it to follow the weather city.
- **An optional alert on the PC at each prayer time**, with an audio file you choose (such as an adhan recording) or a short built-in chime. Quiet mode keeps it silent, and a Stop button shows on the card and on your phone while it plays. It is off until you turn it on.
- **Small, medium and large designs**: the next prayer alone; the next prayer beside today's five times; and the next prayer with its progress, sunrise, the Hijri date and the method, over the times as rows or (on a wide card) six tiles.
- The phone shows the PC's times and counts down by itself; the alert plays on the PC only. Not yet tried on a phone, and the alert hasn't been heard at a real prayer time yet.

## v0.20.0 (1 Nov 2026)

A World clocks widget (user request; Windows and the phone, the same designs on both):

- **Two or three time zones side by side.** Each shows a city name, its local time, its day ("Today", "Tomorrow", "Yesterday") and how many hours it is from the PC ("+6 h", "−5:30 h"). Turn it on in Edit layout's Widgets list; it starts hidden, so your layout doesn't change.
- **Choose the cities in Settings → Widgets → World clocks**: up to three of Windows' time zones, each with a city name you can change (it starts as the zone's first city). With none chosen, a tap on the card opens that section.
- **No internet needed.** Times come from Windows' own time zone rules, so daylight saving switches by itself. The card follows the 12- or 24-hour clock setting.
- **Small, medium and large designs**: rows with the time at the right (cities that don't fit are counted in the heading); the cities side by side with a large time; and a tile per city with an even larger time and the date.
- The phone shows the PC's cities and keeps time itself between readings. Not yet tried on a phone or emulator.

## v0.19.0 (1 Nov 2026)

A Storage widget (user request; Windows and the phone, the same designs on both):

- **Free space on the PC's drives.** Each fixed drive shows how much is free, with a bar or ring of used space. Network, removable and optical drives aren't listed. Turn it on in Edit layout's Widgets list; it starts hidden, so your layout doesn't change.
- **A warning when a drive runs low.** A drive with less free space than the threshold in Settings → Widgets → Storage (1–50%, default 10%) turns to the warning colour, and the card's heading names it ("C: LOW", or "2 DRIVES LOW").
- **Small, medium and large designs**: a list of letters and free space; rows with each drive's name and "used of total" (dropping that line to fit four drives on the usual card); and a tile per drive with a ring and the free percentage. Drives that don't fit are counted in a last line ("+3 more drives").
- Drives are read once a minute while the card is shown (every two minutes in Quiet mode), off the dashboard's thread, and the phone shares the same reading.
- Not yet tried on a phone or emulator.

## v0.18.0 (31 Oct 2026)

Small, medium and large designs for every widget (user request; Windows and the phone, the same sizes on both). Small is each card's folded layout and Medium is the card you know; Large is new:

- **Focus** draws the session as a ring around the countdown, with the status and today's sessions inside it. The default full-height Focus column opens in this design.
- **Now Playing** centres a big cover over the song, with the seek bar and buttons underneath.
- **Sound mixer**: Master, Compact and Full are its small, medium and large designs, and a Full mixer 760 wide or more lists the apps in two columns.
- **Spotify**, 760 wide or more, moves the song into a side panel with a cover as wide as the panel over the title, artist and Like.
- **Quick Launch and Soundboard** buttons become tiles on big cards: the icon, the name and what the button does ("Website · www.google.com", "App", "Hotkey · Ctrl+Shift+M"), or where a sound comes from.
- Not yet tried on a phone or emulator, with a connected Discord, or with a real Spotify account (a labelled test fixture shows the Spotify panel).

## v0.17.0 (31 Oct 2026)

Small, medium and large designs, starting with System stats and Weather (user request; Windows and the phone, the same sizes on both):

- **Each card has three designs and switches between them as you resize it** in Edit layout. Your layouts open in the Medium design, which is the size they already had.
- **System stats.** Small is a clean list: each reading's name and a large number, with a thin bar under CPU, GPU and RAM. Medium shows CPU, GPU and RAM as rings with the percentage inside, and the network rates and uptime in a strip below. Large gives each reading its own tile with a graph of the last minute and a half; network graphs download and upload together, and uptime moves to the heading.
- **Weather.** Small is a glance: temperature and condition icon, city, today's high and low. Medium shows a large icon beside the temperature, the condition and city, and Feels, Wind and High / Low as three small tiles (plus the next three hours on a tall card). Large is a forecast with the next five hours in a strip.
- A reading at 90% or more still turns to the warning colour. Nothing new is polled: the graphs use the readings the card already gets.
- Not yet tried on a phone or emulator; the phone's designs build and their sizes are checked like the PC's.

## v0.16.0 (31 Oct 2026)

Six buttons a page (user request; Windows and the phone, the same rules on both):

- **Quick Launch and the Soundboard always show six buttons**, whatever size the card is. Small cards no longer drop to one or two buttons with lots of pages, and big cards no longer fill up with many small buttons: the six buttons grow with the card instead.
- The six sit 3 × 2, 2 × 3, in one row or in one column, whichever suits the card's shape while keeping every button at full touch size. With fewer than six buttons or sounds, they fill the card.
- On the smallest Quick Launch cards the buttons show just their icon; the name is in the tooltip. Wider cards put the name beside the icon, taller ones under it.
- The smallest sizes went up to fit six buttons: Quick Launch 288 × 204 and the Soundboard 300 × 240, also on small screens. A saved card smaller than that grows to the new minimum.

## v0.15.0 (31 Oct 2026)

Much more resizable widgets (user request, designed with the Impeccable skill; Windows and the phone, the same rules on both):

- **Edge grips.** Edit layout adds a grip in the middle of each side, next to the four corner grips. Drag one to change only the width or only the height. They show on sides long enough to keep clear of the corners.
- **Much smaller cards.** Every widget can now be made a lot smaller: Quick Launch down to 200 × 152 (one button with paging), Weather and the Soundboard to about 240 × 152, Focus to 280 × 192, Now Playing to 312 × 200, the mixer to 320 × 224, Spotify to 360 × 360. Your saved layouts and the default ones don't change.
- **Cards rearrange instead of cutting off.** Below a card's comfortable size it folds its secondary rows away and keeps its main reading and buttons at full touch size: Focus folds the presets into one length button beside Start that steps through them; Now Playing becomes the song with the cover and the buttons; the mixer moves the microphone beside the master slider and, on a short card, keeps the app list one tap away behind an **Apps** button; System stats drop the graphs and bars on narrow cards so the numbers stay big; Weather drops the details line; Spotify gets a smaller now-playing strip; the Connect Spotify and Connect Discord cards keep their button.
- Big cards look the same as before.

## v0.14.0 (31 Oct 2026)

Easier widget organizing (user request; Windows and the phone, the same rules on both):

- **Widgets snap to each other.** With Snap on, a card you move or resize lines up with the other widgets' edges, sits one gap beside them, or centres on them when it comes within a few units, and snaps to the screen's edges. Anywhere else it follows the 8-unit grid as before. Resizing never goes under a card's minimum size.
- **Guide lines** show which edges or centres a card lines up with while you hold it, and disappear when you let go. With Snap off, cards move freely as before.
- **Arrange** in Edit layout lines up the shown widgets neatly in their current order, column by column, filling the screen without gaps or overlaps. Hidden widgets keep their places. It is one undo step, and nothing is saved until Save layout. Reset layout still goes back to the default widgets.
- On the phone, Edit layout gets the same snapping, guides and Arrange button, and saves to the PC as before.

## v0.13.0 (31 Oct 2026)

Keep the game focused (user request; Windows only, the phone is a separate device and never touches the PC's focus):

- **Tapping the dashboard no longer takes the focus** from the game or app on your main screen. Buttons, sliders and dropdowns work as before; Windows just leaves the keyboard where it was.
- **Text boxes still take the keyboard** (Spotify search, Settings fields), so you can type; click back into the game and the dashboard stops taking the focus again.
- The window coming back from the tray when your phone disconnects, opening at sign-in, and a monitor being unplugged or replugged no longer grab the focus either.
- **Settings → Display → Keep the focus on the game or app I'm using**, on by default. With it on, F11 and Escape reach the dashboard only after tapping a text box; turning it off brings back the old behavior.

## v0.12.0 (31 Oct 2026)

A setup exe (user request; Windows only, the APK is already Android's installer):

- **A setup exe**, `WidgetScreen-Setup-vX.Y.Z.exe`, in every release folder. It installs Widget Screen for the signed-in user in `%LOCALAPPDATA%\Programs\Widget Screen` without administrator rights, adds a Start menu shortcut, starts the app when you sign in (a checkbox, on by default), and adds an entry to Settings → Apps → Installed apps.
- **Updating**: running a newer setup replaces the installed copy in place, closing it first, and keeps the startup choice. Settings, sign-ins and paired phones stay in `%LOCALAPPDATA%\WidgetScreen` as before.
- **Uninstalling** from Installed apps removes the app, both shortcuts and the entry. Settings, sign-ins and paired phones are kept unless you tick the box to delete them.
- Silent switches for scripting: `/S`, `--no-startup`, `--launch`, `/uninstall`, `--remove-settings`.

## v0.11.0 (22 Oct 2026)

Phone-native polish (audit branch 10; phone only, see the design note for what the PC side already has):

- **One calm offline card** at the top of the upright phone list while the PC is away ("Can't reach DESKTOP · trying again in 15 s", with Try now and Settings → PC), instead of a sticky status line.
- **A firewall hint right after pairing**: when the first reading after pairing fails, the phone says to allow Widget Screen on private networks in Windows Firewall, where the problem usually is.
- **Edit layout on a phone**: a readout of the card's size and place sits at the top of the card while a finger drags it, and the Widgets panel gains Move and Size nudge arrows (8 units a tap with Snap on, 1 without) for the fine moves a finger can't do.
- **Keep the screen on: Always, While charging or Never** (Settings → Display). New installs get While charging, which suits a phone on a stand; existing settings keep what they had.
- **Haptics**: a light tap on every button.
- **Android 13+ Back** goes through the system's back callback (predictive back), so Back keeps closing panels and sheets on new Android versions.

## 0.10.0 (22 Oct 2026)

Merges `feature/quick-launch-polish`, audit branch 9 (`docs/design/quick-launch-polish.md`).

- **A colour per button**: Settings → Quick Launch → Icon color picks one of the four accents for a button's glyph (or the theme's accent), so Spotify, Browser and Files aren't six identical green glyphs. The phone shows the same colours.
- **Control buttons show their state**: Mute, Microphone, Focus and Quiet mode buttons take the selected look while the thing they toggle is on, on both apps.
- **Drag to reorder** buttons in Settings → Quick Launch, with a mouse or a finger; the up and down buttons stay.
- **A hotkey names its target**: a hotkey button's tooltip (and the phone's confirmation) says which app it would go to right now, for example "Win+Shift+S → chrome".

## 0.9.0 (22 Oct 2026)

Merges `feature/readable-small-screens`, audit branch 8 (`docs/design/any-screen-size.md`).

- **Text size goes up to 140%** (Settings → Theme on the PC, Settings → Theme on the phone), for small screens and screens you sit far from. Above 115% some cards show less at once.
- **Small high-DPI screens keep readable text**: on Windows, a secondary screen smaller than 1920 × 720 on a system with display scaling above 100% now scales the dashboard up with that scaling and lets the widget canvas shrink to 60% instead of 80%. Screens at least 1920 × 720 are not affected, so the owner's panel looks exactly as before.

## 0.8.0 (22 Oct 2026)

Merges `feature/weather`, audit branch 7 (`docs/design/weather.md`).

- **Units**: Settings → Weather chooses Celsius and km/h or Fahrenheit and mph (the region's default until you pick); the phone follows the PC.
- **Today's high and low** in the details line ("↑ 25° ↓ 14°") and **the next hours** (now, +3 h, +6 h) on a tall enough card, on both apps.
- **A condition glyph** (sun, partly cloudy, cloud, fog, rain, snow, storm) beside the condition, drawn as a line path on the PC and in `Glyphs.java` on the phone.
- **The whole card is the refresh tap**; the Refresh button is gone. With no city, the card says where to choose one (and opens Settings → Weather on the PC).

## 0.7.0 (22 Oct 2026)

Merges `feature/now-playing`, audit branch 6.

- **The Now Playing position moves every second** while a song plays, on the PC and the phone, instead of jumping every three seconds; readings still correct it.
- **Any media app is named** in the card's corner (Firefox, VLC, Media Player, Tidal and so on, or a tidied app id), not only Spotify, Chrome and Edge. The phone receives the name too.
- **The playing app's volume is one tap away**: a volume button on the card opens a small panel with that app's mixer slider (master volume when the app has no mixer row), on both apps.
- Titles stay on one line (the full title is in the tooltip or content description), so the artist no longer gets pushed around by long names.

## 0.6.1 (22 Oct 2026)

Merges `feature/confirm-sheet`, audit branch 5 (`docs/design/confirm-sheet.md`).

- **One themed confirm sheet.** Quick Launch buttons that ask first now ask in a sheet over the dashboard (52 px Cancel and Run buttons, Escape or a tap outside cancels) instead of a Windows message box; the phone shows the same sheet instead of the system dialog.
- **First-launch tips** in the header ("Swipe sideways to switch pages · Settings → Phone pairs your phone"; the phone's own line on the phone) with a Got it button that puts them away for good. They show while there is one page and room beside the clock.
- **Settings → Phone comes before Discord**, since it's used far more often.
- **Tray mode with the phone is now off by default** (Settings → Phone). The dashboard vanishing the moment the phone app opened surprised more than it helped when both screens were in use; turn it on if you only ever use the phone.

## 0.6.0 (22 Oct 2026)

Merges `feature/focus-finish`, audit branch 4 (`docs/design/focus-finish.md`).

- **A finished focus session is hard to miss**: the whole card takes the accent tint, the time reads 00:00 with "Tap to clear", a chime plays (Settings → Theme, on by default, silent in Quiet mode), and the phone shows and sounds the same. A tap on the card clears it; otherwise it clears itself after a minute.
- **A fourth preset of your own length**: the "…" button opens a 5 to 180 minute picker inside the card; the length starts a session and stays as the fourth preset on both apps.
- **Sessions today** under the progress bar ("3 sessions today · 1 h 40 m"), counted on the PC and shown on the phone.
- A paused session dims the time so the pause reads from across the room.

## 0.5.0 (22 Oct 2026)

Merges `feature/mixer-events`, audit branch 3 (decision 011).

- **The sound mixer no longer does its Windows audio work on the dashboard's thread.** Outputs and app sessions are read on a dedicated audio thread, output names are cached, and Windows' own device notifications trigger a read the moment an output appears, leaves or becomes the default, so a new headset shows up at once instead of at the next 2-second beat.
- Apps in the mixer are listed playing first, idle next and System sounds last, on both apps.
- The live output levels sit under the master slider, where the sound is, and the row disappears when levels are off or paused instead of showing a dotted line; silence draws nothing.
- A phone and the dashboard share each audio, system and Now Playing reading for a second, so a connected phone no longer doubles the PC's work; the layout version the phone checks is only recomputed after a settings save, an edit or a screen change.
- Network adapters are enumerated every 30 s (and when Windows reports a change) rather than on every System stats tick.
- The phone no longer refreshes every card after each slider nudge, so mixer rows stop flickering mid-drag.

## 0.4.2 (22 Oct 2026)

Merges `fix/phone-link`, audit branch 2.

- **Find PCs on this Wi-Fi** now sends the discovery packet to each network interface's own broadcast address (with 255.255.255.255 as a fallback), which routers and many phones accept where the limited broadcast was dropped.
- The phone backs off while the PC is away (5 s, 15 s, then every minute) instead of knocking every 2 s with the screen on; a tap or a settings change still asks straight away.
- The phone remembers the PC's 12/24-hour clock choice, so the clock no longer flips to 12-hour while the PC is off.
- While the phone is editing the layout it no longer asks the PC for the layout section it would ignore.
- Phone link hardening: requests from web pages (an `Origin` or fetch header, or a host name that isn't this PC's address) are refused, so a page in your browser can't reach the link through DNS rebinding; at most 32 connections are served at once; the phone only accepts home-network addresses (private, link-local, `.local`), so a typo can't send its token to the internet.
- Settings → Phone lists each paired phone with when it paired and was last seen, and a **Forget** button per phone; **Forget all phones** stays.

## 0.4.1 (22 Oct 2026)

Merges `fix/save-settings`, the first branch from the app audit of 22 Oct 2026.

- **Fixed Settings → Save changes deleting every layout page except the first and turning phone control off.** The panel now saves on top of a copy of the current settings, so pages, the current page, Quiet mode and the Phone settings survive a save. A check covers it.
- The dashboard no longer closes when something unexpected goes wrong on the UI thread: the error is logged to `error.log` and shown on the status line, and the dashboard carries on (renders still fail loudly, and a storm of errors still exits).
- The phone link refuses a command whose number is missing or malformed instead of acting on it.
- "Opened ms-settings:sound" became "Opened Windows sound settings."
- Weather's "updated" time follows the 12- or 24-hour clock choice on both apps.

## 0.4.0 (21 Oct 2026)

Merges `feature/android-app`, then `feature/any-screen`, which was stacked on it.

From `feature/android-app` (decisions 007, 008 and 009, `docs/design/android-app.md`):

- **Android app**: a native Android version in `android/`, built by `scripts\build-android.ps1` into an installable `WidgetScreen.apk` (no Gradle, no libraries, no WebView), with the same widgets, palettes and accents. Release folders include the APK when the Android SDK is installed.
- **The phone is a remote for the PC**: it shows the PC's dashboard and every card controls the PC over home Wi-Fi: Focus, Now Playing, Quick Launch, the sound mixer with app outputs and the microphone, System stats, Weather, Spotify through the PC's connection and the Discord soundboard. The phone's own Settings are Theme, Display, Widgets and PC (find the PC and pair with its code). Sideways screens and upright tablets get the dashboard, upright phones get the cards in a list, and Settings → Display can keep the app sideways.
- **Settings → Phone** in the Windows app turns on the phone link for the local network (TCP 43830, UDP discovery on 43831), paired with a six-digit code. Paired phones are stored as token hashes in DPAPI-encrypted `remote-devices.dat`.
- **Tray mode**: a Hide to tray button in the header, and the window moves to the tray by itself while a paired phone shows the dashboard (on by default in Settings → Phone). Windows only.
- Fixed the phone showing no data right after pairing until the app restarted.

From `feature/any-screen` (decision 010, `docs/design/any-screen-size.md`):

- **Any screen size**: the dashboard keeps its 1920 × 720 design scale and lays itself out on a canvas shaped like the screen, so wider or taller second screens get more room, small screens keep readable text (the canvas may shrink to 80%), and narrow ones stack the header. The 1920 × 720 screen looks exactly as before. Layouts are saved per screen shape; a new shape stretches the closest saved layout, or arranges the page's widgets for it. Upright tablets get the dashboard; upright phones keep the list.
- **Layout pages**: up to eight named pages, each with its own widgets and layouts. Switch with the arrows in the header or a sideways swipe; add, rename, reorder and delete pages in Edit layout. The page shown is shared with the phone.
- **The phone mirrors the PC**: the phone shows the PC's layout and page, stretched to its screen, lists the PC's cards in order when upright, and saves its own Edit layout changes to the PC. The phone link gained a layout section and page commands.
- **Edit layout on Windows** uses the phone's list: a check mark per widget and the pages, instead of the widget dropdown.
- **Cards size their content**: Quick Launch fits as many buttons as its card holds (and grows them when they all fit), Focus, the Now Playing cover and the Weather temperature grow with their cards, and Spotify shows two or three columns on wide cards.
- `--render-size WxH` and `tests\screen-size-renders.ps1` render the dashboard for other screens.

## 0.3.0 (20 Oct 2026)

Merges `feature/discord-soundboard`.

- **Discord soundboard**: a new Soundboard widget plays your servers' soundboard sounds as you, in the voice channel you're in, through the Discord app on this PC. Connect it in the new Settings → Discord tab with your own Discord app's Client ID and Client Secret; Discord asks you once to authorize it. The widget starts hidden (turn it on in Edit layout), lists the current server's sounds first, then Discord's built-in sounds, and reacts to joining or leaving voice without polling. App details and sign-in are stored encrypted in `discord-session.dat` (decision 006). The live Discord sign-in and playback haven't been verified yet.

## 0.2.3 (11 Oct 2026)

Merges `feature/app-identity`.

- **App identity**: the exe has a logo (Focus in Lime beside Now Playing, Quick Launch and the mixer on a Midnight tile), shown in Explorer, on the window and in the taskbar. Its properties show the app name "Widget Screen", the author "Ali Alyami" and the version, which the build now takes from the newest tag.
- **Release copies**: `scripts\release.ps1` builds a tagged version from a clean checkout of its tag, runs every check and puts a self-contained copy in `releases\vX.Y.Z\` (git-ignored). The desktop shortcut opens the release copy instead of the `artifacts\app` build. See "Releases" in `BRANCHES.md`.

## 0.2.2 (10 Oct 2026)

Merges `feature/search-play`.

- **Spotify search plays songs**: tapping a search result now plays the song, like tapping a song in a playlist or album, instead of opening it in Spotify. Spotify's own autoplay decides what follows. If no Spotify device is active, the widget opens Spotify and retries. Connections without the playback permission still open the link (decision 003, updated).

## 0.2.1 (10 Oct 2026)

Merges `feature/ui-polish`, a polish, distill and clarify pass over the dashboard and Settings (`docs/design/ui-polish.md`).

- **Quieter dashboard**: labels that never change are gone (LIVE, EVERY 2 SEC, "Playing on this PC", the footer F11 hint and the tagline). The status line clears itself after about ten seconds.
- **Clearer states**: Quiet mode is a toggle that lights up when on, Focus shows when the session ends, and the microphone button states whether it's on or muted.
- **Spotify, not connected**: one "Connect Spotify" empty state with a Set up Spotify button replaces the empty tabs, list and Like strip. The card's header button is now "Account".
- **Polish**: a bigger Focus time in tall cards, the mixer's output name aligned left, a shared selected look for Spotify tabs, and a 13 px caption style for the smallest text.
- **Settings**: plainer headings and shorter copy, Weather in one column with the results list hidden until there are results, and Spotify setup as three numbered steps.
- **Removed** the "Arrange dashboard for Spotify + Now Playing" button. Reset layout does the same.

## 0.2.0 (10 Oct 2026)

Merges `feature/mixer-output-devices` and `feature/header-clock-stats`.

- **Sound mixer outputs**: every app's volume slider shows, whatever output it plays on. Each app has an output button to see and move it to another device, and tapping Master volume picks the default output (decision 005, `docs/design/mixer-output-devices.md`).
- **Header clock**: the time and date always show at the top left of the header, including in Edit layout. The Clock widget and the header brand are gone. Old layouts drop the Clock, and Focus grows into its space when it sat directly below it (`docs/design/header-clock-and-stats.md`).
- **Default layout**: Focus fills the left column, followed by Now Playing, Quick Launch, Sound Mixer and Spotify. Custom layouts are kept.
- **System stats scale with their widget**, using one to three columns that fill the card.
- Branch and version tracking: `BRANCHES.md`, this changelog, and worktrees under the git-ignored `.worktrees\` folder.

## 0.1.0 (9 Oct 2026)

First working version, built on the as-built WPF baseline from `design specfication/`.

- **Touchscreen polish** (`touchscreen-polish.md`): the Settings panel is 1440 × 672 with a footer Cancel, inputs and buttons are at least 52 px tall, slider thumbs are 48 × 48 and capture touch directly, the scrollbar area is 40 px, small text is 15, Focus presets show the selected one, and press-and-hold and flicks are off.
- **Spotify library widget**: tabs are Albums (recently played), Playlists and Search with cover thumbnails, plus a bottom strip with the cover, song, artist and Like. The Playing tab and playback controls are gone (decision 004).
- **Play in collection**: tapping a song inside an album or playlist plays it there, so playback continues through the collection (decision 003).
- **Fixed** Spotify sign-in timing out on networks with unrouted IPv6. Web requests now prefer IPv4 when a host has both.
- **Fixed** a crash on startup when Spotify was already connected.

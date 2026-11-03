# 028: Claude Dashboard and Widget Screen both watch Claude Code

Status: decided (Ali, 3 Nov 2026): "make it so if i close one the other works fine. and vice versa". Windows only. v0.48.0.

## Context

Since v0.46.0 the two apps keep their own settings: Claude Dashboard in `%LOCALAPPDATA%\ClaudeDashboard`, Widget Screen v0.41.0 in `%LOCALAPPDATA%\WidgetScreen`. What they can't both have is Claude Code's connection, because Claude Code has one `settings.json`:

- **Hooks and the status line.** Both apps count any command containing `ClaudeHook.exe` as their own, so whichever connects last removes the other's. Each app's `ClaudeHook.exe` writes only into the folder it sits in, so only the app holding the hooks got live updates.
- **Telemetry.** There is one `OTEL_EXPORTER_OTLP_ENDPOINT`. Both apps listen on the port it names (43832 by default), and only one process can.

The read-only sources (Claude Code's `sessions\` and `projects\` logs, the Claude app's thread records) and each app's own Sign in with Claude were never shared, so they already worked for both.

Widget Screen stays at v0.41.0: its code isn't changed.

## Decision

- **Claude Dashboard's bridge feeds both folders.** `ClaudeHook.exe hook` and `statusline` write each event and reading into their own folder and, when it exists, into `%LOCALAPPDATA%\WidgetScreen\claude` too. The files are the ones v0.41.0 reads: the bridge code is the same apart from the namespace, and the per-session lock keeps Widget Screen's name (v0.47.3). Hooks run without either app open, so the app that was closed is current again the moment it opens. This is the one place Claude Dashboard writes to `%LOCALAPPDATA%\WidgetScreen`. Its settings, sign-in and everything else there stay read-only (the first run's move only reads them).
- **Telemetry goes to whichever app is open.** Both apps already ask for the port again every 10 seconds while they are connected and don't hold it (Claude Dashboard's heartbeat, and the same code at `DashboardController.Claude.cs:165` in v0.41.0). Closing either app therefore hands the port to the other within about 10 seconds. While Widget Screen holds it, Claude Dashboard's card says "Telemetry goes to Widget Screen while it's open", and Settings → Claude says it comes back when Widget Screen closes. It doesn't offer Update connection for this, because moving telemetry to another port would only take it from Widget Screen.
- **Claude Dashboard notices when Widget Screen takes the hooks back.** Widget Screen's Settings → Claude offers "Update connection" whenever the hooks aren't its own, and pressing it points them at Widget Screen's `ClaudeHook.exe`, which writes only to Widget Screen's folder. Claude Dashboard then shows "Live updates go to Widget Screen: reconnect in Settings → Claude", and Settings → Claude names the other bridge and offers **Reconnect**. Reconnecting puts Claude Dashboard's bridge back, which keeps both apps current. Claude Dashboard never takes the hooks back on its own.

## What each app misses, and when

- **Both open:** both get every hook event and the status line. The one that opened first holds telemetry; the other's per-request numbers come from Claude Code's logs (decision 024) until it gets the port.
- **One closed:** the open one gets everything.
- **After Widget Screen's Update connection:** Claude Dashboard still shows every session from Claude Code's own session list and logs, without the hooks' instant states, until you press Reconnect.
- **Disconnect in either app** removes every `ClaudeHook.exe` entry, so both apps' hooks go. That's unchanged.

## Verified (3 Nov 2026, on Ali's PC)

Isolated (a throwaway Claude Code folder, both apps on `--settings` folders of their own, test port 43850), Claude Dashboard from this branch and the installed Widget Screen 0.41.0:

- One hook run put the session in both bridge folders, and both apps showed it ("both-apps-demo · Make both apps watch", Working).
- Claude Dashboard held the port with both open. Closing it handed the port to Widget Screen, which answered a telemetry POST. A turn that ended while Claude Dashboard was closed showed as Your turn in Widget Screen, and in Claude Dashboard when it reopened, with "Telemetry goes to Widget Screen while it's open".
- Closing Widget Screen handed the port back to Claude Dashboard. A turn that started while Widget Screen was closed showed as Working when it reopened.

The checks cover the hook writing to both folders (only while Widget Screen's exists, never twice to one folder), and Settings seeing hooks elsewhere and getting them back. Widget Screen's folder is a throwaway one in the checks (`WIDGETSCREEN_CLAUDE_BRIDGE`).

Not verified: Widget Screen's own Update connection pressed on the real PC (the connector check stands in for it).

# 027: The dashboard may answer a permission prompt the user clicks

Status: proposed, waiting on Ali's choice (2 Nov 2026). Package 4 of the Watcher action plan, phase 6 of the Claude Dashboard transformation. Windows only.

## Context

Until now the watcher only showed what Claude Code did. The action plan's package 4 adds the one thing that acts on a session: Allow or Deny a permission dialog from the dashboard, without switching to the terminal or the Claude app. The Windows notification from package 3 is a notification-area balloon, which can't carry buttons.

Claude Code's `PermissionRequest` hook runs when Claude Code is about to ask for permission. An ordinary (not async) command hook can print `hookSpecificOutput.decision` with `behavior` `allow` or `deny`. If it prints nothing, the dialog shows as usual. Claude Code waits for the hook up to its `timeout`. The dialog appears where the hook would have answered, so while the hook waits, the dialog waits too.

## Decision

1. **An ask hook beside the recording hook.** Connect adds one more `PermissionRequest` entry, `ClaudeHook.exe ask`, as an ordinary hook with a 135-second timeout. The existing async entry still records Needs you. Older connections offer Update connection.
2. **It waits only when it can help.** The hook answers only when the dashboard is running and listening. The dashboard writes `claude\asks\listener.json` with its process id, its start time and the wait. The hook also skips the wait when Quiet mode is on or the session's own window is in front. In any of those cases it ends at once and prints nothing.
3. **Through the bridge folder, not a pipe.** The hook writes `asks\<id>.ask.json`: the session, tool, project folder, a one-line description and the wait's end. It then polls for `asks\<id>.answer.json` every 150 ms. The folder already belongs to the user, the dashboard already watches it, and the checks can drive both sides with a stand-in clock. Both files are deleted when the hook ends.
4. **Allow once, never always.** Allow prints `{"behavior":"allow"}` with no `updatedPermissions` and no `updatedInput`, so no rule is saved and the mode doesn't change. Deny adds a message telling Claude the user said no. Claude Code's own deny rules still apply. Open prints nothing and brings the session's window forward.
5. **Opt-in.** Settings → Claude → Answer permission prompts from Claude Dashboard is off by default. When it's on, Claude Code waits up to 10–120 s (default 30) for the click.
6. **Our own toast window.** It is a small topmost window that never takes the keyboard (`WS_EX_NOACTIVATE`). It shows the project, the thread, the tool and the one-line description, with a countdown to Claude Code's own dialog. Up to four show at once.

## Tradeoffs

- While a toast waits, the terminal's dialog is held back for up to the wait. Opt-in, the in-front check and the Open button keep that cost to the people who want the feature.
- The description of what's being allowed (a command, a path, an address) is written to a file in the user's own folder for the length of the wait. Telemetry never carries tool details, but an informed Allow needs them. The file never leaves the PC and is deleted when the hook ends.
- Any process running as the user could write an answer file. That process could equally edit Claude Code's settings, so this adds no new reach.
- The phone (action plan 4.2) can't answer. The phone link goes in phase 3, so phone answers move to the Android companion on the roadmap.

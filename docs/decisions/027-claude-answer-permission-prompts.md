# 027: The dashboard doesn't answer permission prompts

Status: decided (Ali, 2 Nov 2026): **no**. Package 4.1 of the Watcher action plan is dropped. Windows only.

## Context

The Watcher action plan's package 4 proposed one feature that acts on a session: Allow or Deny a Claude Code permission dialog from the dashboard, without switching to the terminal or the Claude app.

Claude Code's `PermissionRequest` hook could do it. An ordinary (not async) command hook may print `hookSpecificOutput.decision` with `behavior` `allow` or `deny`, and Claude Code waits for the hook up to its timeout before showing its own dialog. The design that was built and tested on the Watcher branch had these parts:

- an extra `ClaudeHook.exe ask` hook;
- a listener file and ask and answer files in the bridge folder;
- a topmost toast window that never takes the keyboard, with Allow once, Deny and Open;
- an opt-in setting with a 10–120 s wait.

## Decision

Ali chose "No, the notification only opens it".

- The dashboard never answers a permission dialog, and Connect adds no hook that can.
- `ClaudeHook.exe` keeps only its `hook` and `statusline` modes. Every Claude Code hook it runs stays async or returns at once, so it never holds a dialog back.
- A Needs-you notification or car brings the session's own window forward. The user answers there.
- The ask-mode code (`ClaudeAsks`, `ClaudePermissions`, `ClaudeAskToast` and the bridge's ask section) was removed before merging and stays in the history of the Watcher branch (PR #3).

## Why

- Holding Claude Code's own dialog back while a toast waits costs time on every prompt.
- Writing the command or path being approved to a file, even briefly, is more than the dashboard needs.
- Watching stays read-only. The dashboard shows what Claude Code does and takes you to it, but doesn't act for you.

## Still in package 4

The cloud garage (4.3), history (4.4), export (4.5) and plain mode (4.6). The phone (4.2) moves to the Android companion on the roadmap.

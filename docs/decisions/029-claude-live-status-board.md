# 029: 5 h and Today are a live status board, Week is the ledger

Status: decided (Ali, 3 Nov 2026), when he asked the team to act on "Claude Dashboard: observability critique". Built in v0.49.0 to v0.53.0. Windows only.

## Context

The critique scored the window 24/40 for observability. Its verdict was that the Needs-you moment is excellent, but everywhere else the window is a spending ledger with a status chip. It answered "what did this cost" well and "what is each session doing right now, is it alive, and what am I not seeing" poorly. It raised five priority issues:

1. Sessions that need you could drop out of view: Stuck and Your turn didn't lift a project, the Large ledger's scrollbar was hidden, Medium had no "+N more", and the pit stop strip named only one thread.
2. The ledger showed spending, not activity: the time in state had no label, and the last sign of life, the current tool and the context fill were only in tooltips. Five of the seven columns were accounting.
3. Missing and old readings didn't always say why. Unreadable logs showed "—", the same as logs still being counted. "Telemetry off" appeared only in the 1000-wide side panel. Sample was among the first things to fold away. "LIVE" was used for plan numbers up to 15 minutes old.
4. Keyboard and low vision: only Go to it could be reached by keyboard, nothing was announced, most text ignored the text size, and faded ended rows fell to 2 to 3.7:1 contrast.
5. Alerts were miscalibrated. The Glacier edge was on whenever any turn had finished, so with 6+ sessions it was almost always on. The Needs-you signal was only a 3-unit edge and the title bar, both easy to miss beside a full-screen game.

The fixes all point at one idea, which the critique called "the shape to aim for": in 5 h and Today the window is a live status board, and in Week it is the ledger.

## Decision

**The live periods (5 h and Today) answer "what needs me, and is everything alive".**

- **The attention rail (v0.49.0).** Projects rank Needs you › Stuck › Your turn › Working › the rest. The pit stop strip is a short queue: Needs you, Stuck, Your turn waiting 10 minutes or more, and Silent (working, but no hook event or log reply for 5 minutes). It shows "1 of N" with Next. Silent goes by the session file's `heard` time, which only hook events set, so the status line's refresh can't make a hung turn look alive. A clipped Large ledger keeps a footer that never scrolls away ("+7 below · 1 stuck · 2 your turn"), and Medium says "+3 more projects".
- **Live thread rows (v0.53.0).** Every thread row has labelled columns:
  - **For:** the time in its state.
  - **Last sign:** "just now", "3 min ago", or "silent 8 min" once a working thread has been quiet for 5 minutes, by the same rule as the queue.
  - **Now:** the tool it's in, by name only ("Bash · 2 min").
  - **A thin context bar** under the title, always there and empty with a tip until the status line reports.
  - **Tokens and Lines.**

  Beside the title is only where the thread runs ("Claude app" in every period).
- **The plan row speaks in time (v0.53.0).** "≈ empty 3:21 am, 37 min before reset", or "≈ 84% by reset" when the window lasts. Over 100% is never a percentage: it says when the window runs dry, or "≈ empty before reset". The week, Medium's tiles and Small's meter line speak the same way.
- **The rev dial is gone (v0.53.0).** Its tile keeps tokens a minute, the last hour's line and the 7-day bars, as the burn tile.

**Week is the ledger.** Tokens, Cache reads, Replies (or ≈ $ API value), Models and Lines, per project and thread. Cache reads, Models and Replies show only there.

**Every number says where it came from, at every size (v0.50.0).**

- The source-health line under the card shows Sample (on fixtures), then Hooks · Logs · Telemetry · Plan, each with a Segoe MDL2 glyph and words.
- Each source is fine, waiting, old, off or failed, and old or failed is Amber. Each level has its own glyph, so the state never depends on colour alone.
- The line never folds away and never cuts a word. As the window narrows it steps down:
  1. every reason in full;
  2. short reasons;
  3. the sources that are fine keep only their glyph;
  4. the sources with trouble keep only their names;
  5. only the Amber sources keep their name.

  So on Small, and on any narrow window, healthy sources show as glyphs only. Their reasons, and actions such as Reconnect ("Hooks with Widget Screen · Reconnect"), are in the tooltips and the screen-reader names.
- Logs still being read show "≈". Logs that can't be read show "—" with "NO LOGS" and the reason. No logs yet, on a fresh install, is calm: "Logs: none yet". The header's stamp is "PLAN · 2:17 AM", never "LIVE".
- "Working" has one meaning everywhere.

**Everyone can reach it (v0.51.0).**

- Rows, cars and garages are buttons. Tab moves between them, with a 2-unit outline only when the keyboard put focus there, and Enter or Space acts.
- The header summary is an assertive live region, so a new Needs you is read out.
- Every word follows the text size from 100% to 140%.
- Ended rows dim by colour (Muted at full opacity); only the car fades.
- The light Claude palette's Amber and Glacier reach 4.5:1 on Raised.

**Alerts respect attention (v0.52.0).**

- The Glacier edge waits until a reply you owe has waited 10 minutes.
- The taskbar button carries an Amber dot with the count, drawn in code. It flashes once per new Needs you, never while the window is in front and never in Quiet mode.
- Ctrl+1 opens the first session in the attention queue.
- Settings → Window → Keep on top is optional and off by default.

## Rules this keeps

- **Never acts in a session.** Ctrl+1 and the queue only bring a session forward (decision 027).
- **No tool details.** Now is telemetry's `tool_name` alone: at most 80 characters kept, 40 shown with "…", and the whole name in the tip. It lives in memory only and is never written to history or export. A check asserts that a thread keeps no input, command or argument fields.
- **No new polling.** The live columns, the queue and the source-health line update on the existing 1 s heartbeat. The connection details the line needs are read with the existing 10 s connection check.
- **Amber only for Needs you and warnings.** That covers Stuck, meters from 80% (the context bar included), old or failed sources, and the taskbar dot. Your turn and Silent are calm.
- **Nobody ran Impeccable.** The critique's "/impeccable …" commands were suggestions. Each PR was checked for them, and none was run.

## Polish list: what the wave left (for Ali)

The critique's points that v0.48.0 to v0.53.0 didn't take on, roughly by how much they'd help. None is started. Each is a choice for Ali.

1. **Pin, sort or filter projects** (critique heuristics 3 and 7). Hiding a project exists, but only in Settings → Claude (decision 020). There is still no one-click way to pin a project to the top or quiet a noisy one.
2. **An in-app key** (heuristic 10) for the car poses, the context ring, the hatched projection and "WEEK · LAP 4 OF 7". It could go in a tooltip on the header or a "?" in Settings, drawn in code.
3. **The plain list's wording** (heuristic 4). The plain list says "Since 3:02 am" where the live rows say "For 6 min", and it has no Last sign or Now. The Large and plain views should say the same thing the same way.
4. **The tank dial** (heuristic 8). The tank's gauge repeats the big percentage beside it. The rev dial went in v0.53.0; the tank's could go the same way, keeping the bar and the time words.
5. **Reading from across the room.** The 15-unit header summary is hard to read from 1 to 2 m, beside a game. A larger summary at 1000+ wide, or a far-view text size, would help.
6. **Minor observations from the critique:**
   - Medium's 30-unit cars read as coloured dots.
   - The share bar under each garage name has no label.
   - "+1 hidden" adds tokens you can't see to the project's total.
   - Check the renders to see whether the 7-day bars still read as part of the hour's line now that both sit in the burn tile.
   - **From the v0.53.1 renders (Lead):** at 1000 × 600 the crowded tank fits only the short "≈ empty 10:19 am" and drops "37 min before reset", which is the more useful half. A shorter form that keeps the gap ("≈ empty 10:19 am, 37 min early") may fit and say more. Crowded Medium at 140% cuts the stuck reason to "Stuck · Overloaded, try a…". A short form ("Stuck · Overloaded") with the full reason in the tip would keep it whole.
7. **The critique's first question:** with 6+ sessions, should the home be the attention queue, with garages as the drill-down? The queue now exists in the strip, so this is a layout choice rather than a data one.
8. **Not verified live:** Ctrl+1 on the real PC; a screen reader reading the live region aloud; Now with real telemetry for MCP tools and long runs.
9. **Done in v0.53.1:** Watcher's follow-up (a check for long tool names, Medium's row count at 140%, the week pace refitting when its tile settles).
10. **Reported only:** PRODUCT.md's `<!-- impeccable:product-schema 1 -->` platform value `desktop` isn't one Impeccable recognises. It matters only if Ali authorises Impeccable again.

## Verified

- CI was green on every PR. For each version, renders were made at 360 × 300, 640 × 420, 1000 × 600 and 1600 × 900, and at 140% text wherever the change touched words. The Lead checked them on Ali's PC (`artifacts\review`).
- v0.52.0 was tried live on Ali's PC: the dot with its count, one flash when the window wasn't in front, no flash in front, Keep on top, and the 10-minute Glacier rule.
- Not verified live: Ctrl+1, which couldn't be driven from a script (the checks cover it); a screen reader reading the live region aloud; and Now with real telemetry for every tool type.

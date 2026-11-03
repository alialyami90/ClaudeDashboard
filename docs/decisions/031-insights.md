# 031: Insights, a detailed view of Claude Code's logs

Status: decided (Ali, 3 Nov 2026). Ali asked: "show me a detailed view of the logs. Where can i have different charts or comparisons or most used or least used models efforts or tool or etc. Give me more." It shipped as v0.60.0 (the engine, Watcher), v0.60.1 (engine fixes after a render on a copy of Ali's logs, Watcher) and v0.61.0 (the Insights tab, Builder). Windows only.

## Context

History ([decision 030](030-claude-lap-chart.md), v0.59.0) keeps a year of day files, but only totals: tokens, replies, threads, ≈ $, hours and projects. Claude Code's session logs hold much more about each reply. A read-only inventory of Ali's 52 logs (304 MB, 3 Nov 2026) found:

- **Assistant lines** (11,813). Each carries:
  - `message.model`;
  - usage, with `output_tokens_details.thinking_tokens` and the 1-hour and 5-minute cache-write split;
  - `stop_reason`, and `effort` or `perTurnEffort` (high, xhigh, medium);
  - `thinkingDurationMs`;
  - `attributionSkill`, `attributionPlugin`, `attributionMcpServer` and `attributionMcpTool`;
  - `isApiErrorMessage`, and `tool_use` blocks with their names.
- **User lines**: each tool result's `tool_use_id` and `is_error`, and `permissionDecision` with its decision and source.
- **System lines**: `compact_boundary`, `api_error` (with `retryAttempt`) and `stop_hook_summary`.
- **Timestamps** on every line.
- **What isn't in the logs**: a duration for a tool call. The only measure is the gap between the call's line and its result's line.
- **Pitfalls**:
  - `api-request-blob` lines repeat content blocks;
  - model ids carry suffixes such as `[1m]`;
  - a streamed reply repeats its usage on several lines.

Pro and Max plans have no usage-history API, so these logs are the only detailed record on the PC.

## Decision

### Day files gain a v2 detail (v0.60.0, v0.60.1)

A day file carries `"v": 2` and a `detail` section (`ClaudeDayDetail`), with `"rev": 2` inside it since v0.60.1. It is tallied from the logs by `ClaudeDetailTally`:

- **Models**: per model id, the replies, input, output, cache read, cache write, thinking tokens and ≈ $ at API prices (unpriced replies counted apart).
- **Effort**: per model id, the replies at each effort level.
- **Thinking**: the replies that thought, total and longest thinking time, and a histogram of thinking times.
- **Tools**: per tool name:
  - calls and errors;
  - the calls timed and their ≈ time, with a histogram;
  - calls, errors and ≈ time per model.
- **MCP servers, skills and plugins**: calls or replies per name.
- **Permissions**: allowed by a rule (source `config`), allowed by a hook (`hook`), asked and allowed (`user_temporary` and the like), and denied. v0.60.0's "allowed without asking" was always 0 in Ali's logs and was dropped in v0.60.1.
- **Health**:
  - API errors: Claude Code's `api_error` lines, plus the replies it marked as an API error;
  - retries: the `api_error` lines that say a retry follows;
  - compactions;
  - stop reasons, as `tool_use`, `end_turn` and everything else as `other`.
- **Sessions**: each session that started that day, with its length in minutes from its first to its last line (past midnight too), and the sessions per project folder. A session counts once, on the day it started (v0.60.1).
- **Lines** added and removed, overall and per project folder.

**How it counts.**
- Each reply counts once, by its request id (its message id when there is none). Each tool call and result counts once, by its id, a helper's copy included.
- `api-request-blob` lines are skipped.
- Model ids are normalised (`[1m]` and the like taken off). Claude Code's own `<synthetic>` replies count only as API errors.
- A tool's ≈ time is the gap from its call's line to its result's line, kept only between 0 and 6 hours.
- An ≈ median comes from the histogram and sits inside its bucket by rank, on a log scale (v0.60.1). Before that it was pinned to the bucket's upper edge, so thinking showed ≈ 10 s against a real 3.8 s.

**Who writes it.**
- `ClaudeBackfill` tallies the detail from the same pieces of log it already reads:
  - a past day with no file gets a v2 file;
  - a v1 day, or a day whose detail is from an older revision, gets the detail added. Its counts, Needs, LongestWait and "from logs" mark stay;
  - a partial day is rebuilt as in v0.59.0, keeping its Needs and LongestWait;
  - the backfill never writes today.
- After its pass, the backfill follows the logs written in the last two days, within its step budget.
- The live writer (`ClaudeHistory`) takes today's and yesterday's detail from the backfill, without waiting on it, and otherwise keeps the detail a file already has.
- A day whose logs Claude Code has already cleaned up stays v1, and the view says so.

### Privacy (the rule unchanged, the names extended)

Day files hold numbers, plus only these names:
- model ids and effort levels;
- tool names;
- MCP server, skill and plugin names;
- project folders and their folder names.

They never hold:
- prompts, replies or thinking text;
- tool inputs or outputs, or file paths from tools;
- branch names;
- titles or a first request;
- session or request ids.

In the code:
- names are capped at 80 characters;
- stop reasons are bucketed into the three above;
- only a tool block's `name` and `id`, and a result's `tool_use_id` and `is_error`, are read;
- `gitBranch` is never read.

The engine's check plants secret text, a branch name, a file path, a command and the ids in every field that can carry text, and fails if any reaches a file.

### ClaudeInsights: the three ranges

- **`ClaudeInsights`** covers one range of local days ending today: 7 days, 30 days, or All (back to the oldest day file). Each range holds:
  - totals and a per-day series;
  - rankings of models, tools, projects, MCP servers, skills and plugins, biggest first with ties by name, with `Most` and `Least` (least used among those used at all);
  - an hour × weekday heat grid of tokens;
  - the previous equal range for comparison (none for All).
- **Fields that say what's missing**:
  - `Missing` says why a range is empty;
  - `TodayPending` and `Note` say that today's detail is still being read, or how many days carry the detail ("cover N of M days");
  - `Sessions`, `PriorSessions` and `PriorActiveDays`.
- **`ClaudeInsightsSet`** holds Week, Month and All. It is worked out from the day files off the UI thread, after each background scan and whenever the backfill writes days. It is published on the `ClaudeView` snapshot as `Insights`, null with History off. The view only picks one range, with no callbacks and no timers. `ClaudeInsightsSet.SampleSet(now)` is the labelled sample.

### The Insights tab (v0.61.0), Ctrl+7

A seventh tab, after Sources, so Ctrl+1 to Ctrl+6 keep their views. Below 640 wide it doesn't exist, as for the others.

- **The range row**: 7 days, 30 days or All, and "compared with the previous 7/30 days". Each total shows ↑ or ↓ with the change in %.
  - A comparison shows only when the period before has at least 3 active days and 50 replies. Otherwise it says "too little before".
  - All says there is nothing to compare with.
- **Six sections on one scrolling page.**
  1. **Models**: a share bar by tokens; a table of replies, tokens, ≈ $, thinking tokens, and share of tokens and of replies; tokens per day stacked by model.
     - Model ids read as names ("Opus 5.5", "Fable 5.1"), and an unknown id stays as it is.
  2. **Effort and thinking**: replies by effort level for each model (one split bar a model), the share of replies with thinking, and the ≈ median and longest thinking time.
  3. **Tools**: the 10 most used and the 5 least used, with calls, errors, error % and ≈ median time.
     - A filter by model shows that model's own calls, errors and ≈ times.
     - MCP servers, skills and plugins are listed underneath.
     - An MCP tool reads "server · tool", and a connector's bare id reads "Connector 1a59c9".
  4. **Projects**: ranked by tokens, with replies, ≈ $, sessions, and lines + and −; the most and least active.
  5. **When**: an hour × weekday heat grid of tokens, and sessions per day.
  6. **Health**: API errors, retries and compactions against the period before; how permissions were decided (by rule, by a hook, asked you, denied); and how replies ended (tool use, end of turn, other).
- **Honest states.**
  - Every estimate says ≈: $, tool times, medians.
  - A missing figure says why: no ranges yet, days kept before v0.60.0, today still being read, untimed tools ("— in ≈ median: tool times need both the call and its result in the log"), no effort levels.
  - Days built from the logs are counted in the footer.
  - The sample says it is one.
- **Look.**
  - Drawn in code with the palette, in Segoe UI, with no effects.
  - The series use the accent, Glacier and Muted, never Amber. Amber marks only warnings: a tool with at least 3 errors and 20% or more of its calls failing, and API errors more than twice the period before.
  - One page that Tab reaches, with the shared focus ring and Home and End, like Limits and History.
- **Shell's files.** Builder made the tab-list edits, flagged in the PR and agreed by the Lead:
  - `ClaudeViews.Insights` and the 7th name;
  - Ctrl+7 and NumPad 7 in `AppController.ViewKey`;
  - `ViewTab6` in `ShellWindow.xaml`;
  - the tab count in `ShellTests`.

## Settled (Lead, 3 Nov 2026)

- The engine computes all three ranges, each with its previous period, off the UI thread after each background scan. It publishes them as `ClaudeInsightsSet Insights` { Week, Month, All }. The view only picks one, with no callbacks or timers.
- Builder makes the Ctrl+7 edits in Shell's files and flags them in the PR.

## Verified

- CI (`build.ps1 -Test`, `package.ps1 -Test`) was green on #38, #40 and #39 before each merge.
- **The engine's checks** (`InsightsEngineTests`) cover:
  - every v2 field from fixture logs, counted once with a helper's copy;
  - request blobs skipped, model ids normalised, the error join, ≈ tool time and the ≈ median inside its bucket;
  - sessions once on their first day;
  - permission sources and API errors versus retries;
  - no text, path, branch or id in a file;
  - v1 to v2, and rev 1 to rev 2, keeping counts, Needs and LongestWait;
  - following today's new lines once;
  - the three ranges, ties, least used and the empty states.
- **The view's checks** (`InsightsViewTests`) cover:
  - the model from the engine's ranges;
  - the comparison words;
  - the range row and the model filter, by clicks;
  - the missing states, and no Sample label on live numbers;
  - the page as a tab stop with the ring;
  - layouts inside the width at 1600 × 900, 1100 × 700 and 700 × 500, at 100% and 140%.
- **The first render.** The Lead rendered the engine and the tab on a copy of Ali's logs. That render found the problems v0.60.1 fixes: the medians, sessions counted per day, permission sources, API errors, and today before its detail.
- **The render after v0.60.1.** It ran on #39 at ae30323, which contains #38 and #40, on a fresh copy of Ali's logs, on a first launch and again after reopening. It found these right:
  - the ≈ medians: thinking ≈ 4.1 s against 3.86 s in the logs, and Bash ≈ 1.6 s against 1.51 s;
  - 52 sessions, 34 of them in Widget screen, the same on both launches;
  - permissions: by rule 6,153, by a hook 0, asked 616, denied 25;
  - 4 API errors;
  - "too little before";
  - the model and tool names;
  - exact tool calls and errors.
- **Left open, for Builder's v0.61.1.** On a first launch the Replies tile counts today before today's detail arrives. Also, `error.log` ignores `--settings`.

## Not decided here

- Custom date ranges (a start and an end). The spec allowed them; v0.61.0 ships 7 days, 30 days and All.
- Whether a prior range with fewer detailed days should damp the model and tool comparisons. Today the footer says how many days in the current range carry the detail.

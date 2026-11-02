# Archive: Widget Screen

Claude Dashboard grew out of **Widget Screen**, a dashboard of 19 widgets for a 1920 × 720 secondary touchscreen with an Android phone remote. On 2 Nov 2026 it became a single-widget app for watching Claude Code ([decision 026](../decisions/026-claude-dashboard.md)). These files describe the old product. They are kept so the reasons behind today's code survive, and they are **read-only**: don't edit them to describe new work, and don't follow them as current rules. [CLAUDE.md](../../CLAUDE.md) and the records in [docs/decisions/](../decisions/) are what's current.

| Folder | What's in it |
| --- | --- |
| [widget-screen/design specfication/](widget-screen/design%20specfication/) | The as-built spec of the widget app (9 Oct 2026) with its reference XAML, 1920 × 720 renders, default settings and the hashed source snapshot. The misspelled folder name is kept on purpose. |
| [widget-screen/](widget-screen/) | The old `PRODUCT.md` and `DESIGN.md` (the Sticker Sheet). |
| [decisions/](decisions/) | Records 001–013, 016 (hardware sensors), 021 (Sticker Sheet inks) and 022 (GitHub CI and releases). Numbers are unchanged, so references such as "decision 010" still find them. |
| [design/](design/) | The widget app's design notes: layouts, touch, the phone app and every widget but Claude's. |

Some records still apply in part. The setup exe of decision 012 ships with Claude Dashboard ([docs/design/setup.md](../design/setup.md)), and the CI and release process of decision 022 continues in [BRANCHES.md](../../BRANCHES.md). The Android app and the phone link leave the tree in v0.44.0; the `v0.41.0` tag keeps them, and the Claude-only phone companion on the roadmap starts from there.

Only links that the move broke were changed. Links inside `design specfication/` that pointed outside the repository were already broken and are left as they were.

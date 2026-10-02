# Shaper harmony programme — rules for every task agent (2026-09-07)

These rules extend `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0158\PROGRAMME_RULES.md`, which you read first and obey in full, with these differences:

1. **The analysis you are executing** is `D:\Claude@GDrive\Shaper — Why it is unusable and how to make it whole.md` with four appendices in `D:\Claude@GDrive\Shaper analysis appendices\`. Read the main document and the appendix your task names. Every `file:line` in the appendices was measured on 2026-09-07 at HEAD 4466f3c0; re-verify before editing, the tree may have moved.
2. **Pyre files.** `Runtime/Pyre/Forms/**` may be edited ONLY to add or change attributes (`[ZUILabel]`, `[ZUIGroup]`, `[ZUIShowIf]`, `[Tooltip]`, `[Range]`) and field ORDER. Never rename a serialized field, never change a default, never touch render code. Name every Pyre file you touched in your handover.
3. **Do not commit.** The PM commits each wave after compiling. Do not run `git add`, `git commit` or `git stash`. Do not touch `CHANGELOG.md`; instead put the one-line CHANGELOG entry you would have written at the top of your handover text under the heading `CHANGELOG:`.
4. **Code-only unless your card says otherwise.** No Coplay, no Unity CLI, no `unity mcp`, no compile. Self-review by reading every call site you changed (grep for every deleted symbol across `Assets/Packages/Laubrary`, including `Editor/Shaper/Audits/`, which is behind a define but must still compile when it is on).
5. **Deleting a file means deleting its `.meta` too.** Never leave an orphan `.meta`.
6. **No new concepts, no new controls, no new menu items.** This programme removes, hides, renames and re-defaults. If you believe something new is needed, say so in the handover instead of building it.
7. **Serialized data is sacred.** A field that exists on an authored asset is retired (kept in the class, hidden, no longer read) rather than deleted, unless your card explicitly says the field is never authored. Enum values are append-only.
8. **Handover** via `POST http://127.0.0.1:8778/api/task/handover` as in PROGRAMME_RULES, three buckets. For code-only tasks bucket one is "self-reviewed, not compiled" — say so plainly.

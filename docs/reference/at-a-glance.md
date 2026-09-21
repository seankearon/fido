---
icon: lucide/table
description: Every capability in one table.
---

# At a glance

| Capability | Summary |
| --- | --- |
| Input | Branch name (required); the solution box **filters** the detected solution chips |
| Worktree folders | Listed under the branch box: the worktree folders the branch **already has on disk**, with **copy** and **open in File Explorer / Finder** buttons per row. Found the way a worktree would be created (worktree root, else beside each scanned clone); a folder counts even when no repo has the branch, and nothing shows when there is no folder |
| Discovery | Debounced scan of the search roots for working trees **currently on the branch**; results inline as cards, worktrees before main clones |
| Multiple locations | Every checkout shown, labelled **worktree** / **main clone** — you choose which to act on |
| Open pull request | Every scan that finds the branch asks **GitHub** (via `gh`) whether it has a PR open; one that does gets a row above the cards — `PR #42 · <title>` with an **Open pull request ↗** link. Asked again on each check (a scan, or arming a delete), never cached; runs behind the results and never holds them up; "couldn't ask" is reported as such, not as "no PR" |
| Open gate | Open & delete actions unlock only when discovery **finds** the branch |
| Open target | Rider / Visual Studio: the chosen `.sln` / `.slnx` / `.slnf` chip or the folder; every other tool: the folder |
| Delete worktree | Inline two-step confirm; removes the worktree + **local** branch, with an **opt-in to also delete the remote branch** (unticked by default, disabled while an open PR — via `gh`, re-checked as the confirm arms — blocks it, linking to the PR); retries transient failures; long-path aware with a Recycle-Bin-bypassing force-delete for **`filename too long`** |
| Delete reporting | Each target reported separately — **already gone counts as done**, not as failure; anything genuinely left behind gets an inline **Retry** strip that re-runs just that step |
| Tools | Rider / WebStorm / VS Code / Visual Studio / Zed / Custom — hero default + Ctrl+1…9, or by CLI id |
| Folder targets | **Console** (`term`) opens **your** terminal, **File Explorer** (`files`) the OS file manager — Windows / macOS / Linux |
| In-repo config | `.fido/cfg.yaml` on the branch: **prefer main clone** and **commands** — an ordered list of command lines, offered as a run menu on the **Console** button *and* on the **Console tab**. Created (or opened) from the **OPEN** strip, seeded at its defaults and never overwritten |
| Console tab | Fido's own terminal beside the **Flight log**, at the selected location: a real shell over a real pseudo-terminal — colour, prompts, **Ctrl+C**, and a live prompt still there when a script fails. Its **Run** menu leads with **shell here**, then the branch's commands; a run never closes Fido. **Ctrl+Click** follows an `http`/`https` link in the output, and the flight log names what opened |
| Editor discovery | Explicit path → PATH → standard installs (per kind) |
| CLI | `fido <branch> [tool]` — auto-opens only for an explicitly named tool with exactly one location |
| Window title | Once a branch resolves, the title reads `<repo> · <branch>` — no "Fido" in front, following the selected card; switchable off in Settings |
| Config | `%APPDATA%\Fido\config.json` (migrates the legacy folder), plus the repo's own `.fido/cfg.yaml` on the branch |

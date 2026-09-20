---
icon: lucide/settings
description: Every setting, its default, and where settings live.
---

# Settings

## In the app

Reach these via ⚙ → **All settings…**.

- **Search roots** — directories to scan for working trees (one per line).
- **Editors** — the tools Fido can open into. Each row has a name, an optional **slug** (the
  command-line token that selects it, e.g. `rider`), a **kind** (Rider, WebStorm, VS Code, Visual Studio,
  Zed, **Console**, **File Explorer**, or Custom), and an optional path (blank = auto-detect for known kinds;
  required for Custom). For **Console** the path is the **terminal program** and for **File Explorer** the
  **file manager** (blank = the OS default; a full path or a bare command name like `wt` / `pwsh` both work),
  so you can point Fido at the terminal you prefer. Tick the
  **●** radio to set the default (the hero button) — or use the ⚙ gear popover on the main screen,
  which offers the same choice plus **No default (equal weight)**. The rest are reached by
  **Ctrl+1 … Ctrl+9** or by their slug on the command line. **Add** appends a new row; **✕** removes one.
- **Worktree root** — leave blank for the sibling `<repo>.worktrees` convention. Setting one also collapses
  the **worktree folders line** under the branch box to a single row, answered from the branch name alone
  with no scan needed (see **[The branch's worktree folders](../guide/discovery.md#the-branchs-worktree-folders)**).
- **Theme** — **System**, **Light**, or **Dark**.
- **Window title** — **Show the repo and branch once discovery resolves them** *(default on)*. On, a
  resolved branch renames the window to `<repo> · <branch>` (see **The window title** above); untick it
  and the title stays `Fido`.
- **Before running** — **Fast-forward the target onto `origin` first** *(default on)*. On, picking a
  command from the Console run menu updates the target before the console opens (see **Up to date before
  it runs** above); untick it to run against the tree exactly as it stands.
- **Close after opening** — when Fido quits after a successful launch: **Command line** *(default —
  only when started with a branch on the command line)*, **Always** (after every launch, including
  the on-screen buttons), or **Never** (turns auto-close off).
- **Close delay** — seconds Fido counts down before it auto-closes (default **10**; **0** closes
  immediately). The flight log shows a single line that ticks down in place (`Closing in 10…`, then
  `9…`, `8…`), and a **Keep open** bar appears at the bottom with the live countdown. Clicking
  **Keep open** — or simply starting another scan or open — cancels the close, so it's never a point
  of no return.

## Defaults

- **Search roots:** `%USERPROFILE%\source\repos`, `%USERPROFILE%\src`,
  `%USERPROFILE%\RiderProjects`, `%USERPROFILE%\Projects`.
- **Default branch names:** `main`, `master` (never offered for deletion).
- **Search depth:** 4.
- **Close after opening:** command-line launches only, with a **10-second** close delay.
- **Window title:** shows `<repo> · <branch>` once discovery resolves.
- **Before running:** fast-forwards the target onto `origin` when you pick a run command.

## In-repo config (committed to the branch)

A repo can also configure Fido for itself, in **`.fido/cfg.yaml`** on the branch — *prefer main clone*,
*run files* and *aspire start*. It's read from the branch every time a scan lands and needs no user
setting to enable; see **[In-repo config](../guide/in-repo-config.md#in-repo-config-fidocfgyaml)** above for the file's shape.

## Where settings live

JSON at **`%APPDATA%\Fido\config.json`**. If that doesn't exist, Fido reads a legacy
`%APPDATA%\atlantic-opener\config.json` (from before the rename) so existing settings survive;
the next save writes to the new location.

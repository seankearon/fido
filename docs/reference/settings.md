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
  which offers the same choice plus **No default (equal weight)**. The rest are reached by their
  keyboard shortcut — **Ctrl+1 … Ctrl+9** unless you assign others (see [Keyboard shortcuts](#keyboard-shortcuts)
  below) — or by their slug on the command line. **Add** appends a new row; **✕** removes one, and its
  shortcut with it.
- **Worktree root** — leave blank for the sibling `<repo>.worktrees` convention. Setting one also collapses
  the **worktree folders line** under the branch box to a single row, answered from the branch name alone
  with no scan needed (see **[The branch's worktree folders](../guide/discovery.md#the-branchs-worktree-folders)**).
- **Theme** — **System**, **Light**, or **Dark**, plus **Colour the Console tab to match**
  *(default off)*. Off, [the Console tab](../guide/console.md#its-colours) keeps the terminal emulator's
  own sixteen ANSI colours, on a plain ground the right way up for the theme; on, the ground and all
  sixteen come from Fido's palette instead. **Either way the console follows the theme**, and a change
  repaints the shell that is already running. This is the **saved default**; the header's
  [sun/moon toggle](../guide/interface.md#the-theme-toggle) flips the screen for the current run without
  touching it, and saving here takes back over.
- **Window title** — **Show the repo and branch once discovery resolves them** *(default on)*. On, a
  resolved branch renames the window to `<repo> · <branch>` (see
  **[The window title](../guide/interface.md#the-window-title)**); untick it
  and the title stays `Fido`.
- **Before running** — two switches for what a **Console run menu** pick does:
    - **Fast-forward the target onto `origin` first** *(default on)*. On, picking a command from either
      run menu updates the target before the console opens (see
      **[Up to date before it runs](../guide/in-repo-config.md#up-to-date-before-it-runs)**); untick it to
      run against the tree exactly as it stands. The Console tab's **shell here** never triggers it.
    - **Run it in Fido's Console tab** *(default off)*. Off, a pick from the **Console button's** run menu
      goes to the terminal you have configured. On, it runs in
      **[Fido's own Console tab](../guide/console.md)** instead — a real shell over a real pseudo-terminal,
      beside the flight log. It governs that button's menu only: the Console **button itself** always opens
      your terminal, and the Console **tab's** own Run menu always runs in the pane.
- **Command line** — **Switch to a window that already has the branch** *(default on)*. On, `fido <branch>`
  brings the Fido window already open on that branch to the front instead of opening a second, and runs
  anything else the command line asked for — a tool, `-s`, `--folder` — there (see
  **[A window that already has the branch](../guide/command-line.md#a-window-that-already-has-the-branch)**).
  Untick it and every launch opens its own window; `--new-window` does the same for a single launch. Takes
  effect from the next launch.
- **Close after opening** — when Fido quits after a successful launch: **Command line** *(default —
  only when started with a branch on the command line)*, **Always** (after every launch, including
  the on-screen buttons), or **Never** (turns auto-close off).
- **Close delay** — seconds Fido counts down before it auto-closes (default **10**; **0** closes
  immediately). The flight log shows a single line that ticks down in place (`Closing in 10…`, then
  `9…`, `8…`), and a **Keep open** bar appears at the bottom with the live countdown. Clicking
  **Keep open** — or simply starting another scan or open — cancels the close, so it's never a point
  of no return.

## Keyboard shortcuts

Reach these via ⚙ → **Keyboard shortcuts…**, or press ++ctrl+k++, ++ctrl+s++. Every action on the main
screen is listed with its keys — a single press, or a two-press **chord** such as `Ctrl+K, Ctrl+S`. Click
one and press the keys to change it; **↺** restores a default, **✕** removes a shortcut, **Reset all**
restores them all. How recording, chords and clashes work is in
**[Your own shortcuts](../guide/interface.md#your-own-shortcuts)**.

In the config, a **tool's** shortcut lives on the tool, and **everything else** under `Shortcuts`, keyed by
the action's name. Only what differs from the defaults is written; an empty string means *no shortcut*
rather than *the default*; and a chord is two presses with a comma between them:

```json
"Editors": [
  { "Name": "Rider", "Slug": "rider", "Kind": "Rider", "Shortcut": "Ctrl+K, R" },
  { "Name": "WebStorm", "Slug": "ws", "Kind": "WebStorm" }
],
"Shortcuts": {
  "ToggleTheme": "Ctrl+K, T",
  "CopyPath": "Ctrl+Shift+C",
  "Rescan": ""
}
```

A tool with no `Shortcut` keeps its number — the first nine answer to **Ctrl+1 … Ctrl+9** by their place in
the list. The names under `Shortcuts`, matched in any case:

| Name | Action | Default |
|---|---|---|
| `OpenDefault` | Open in the default tool | — |
| `Rescan` | Scan for the branch again | `F5` |
| `FocusBranch` | Go to the branch box | — |
| `FocusSolution` | Go to the solution filter | — |
| `NextLocation` / `PreviousLocation` | Select the next / previous location card | — |
| `CopyPath` | Copy the selected path | — |
| `EditRepoConfig` | Create or edit `.fido/cfg.yaml` | — |
| `OpenPullRequest` | Open the branch's pull request | — |
| `DeleteWorktree` | Delete the worktree (raises the confirm strip) | — |
| `ShowFlightLog` / `ShowConsole` | Show the Flight log / Console tab | — |
| `CopyFlightLog` / `SaveFlightLog` | Copy / save the flight log | — |
| `ToggleTheme` | Flip light / dark for this run | `Ctrl+K, Ctrl+T` |
| `Settings` | Settings… | `Ctrl+,` |
| `KeyboardShortcuts` | Keyboard shortcuts… | `Ctrl+K, Ctrl+S` |

Keys are written as on the dialog — modifiers `Ctrl`, `Shift`, `Alt`, `Meta` (also `Cmd` / `Win`), then the
key by the character on it (`Ctrl+,`, `Ctrl+[`) or its name (`F5`, `Enter`, `PageUp`); VS Code's
space-separated `ctrl+k ctrl+t` reads too. A hand edit that clashes is settled the way the dialog would
have it: a shortcut you chose beats a default, and otherwise the first in the dialog's order keeps the keys.

## Defaults

- **Search roots:** `%USERPROFILE%\source\repos`, `%USERPROFILE%\src`,
  `%USERPROFILE%\RiderProjects`, `%USERPROFILE%\Projects`.
- **Default branch names:** `main`, `master` (never offered for deletion).
- **Search depth:** 4.
- **Command line:** `fido <branch>` switches to a window already open on the branch.
- **Keyboard shortcuts:** **Ctrl+1 … Ctrl+9** open with the tools by position; **F5** rescans; **Ctrl+,**
  opens Settings; **Ctrl+K, Ctrl+S** the Keyboard shortcuts; **Ctrl+K, Ctrl+T** flips light / dark.
- **Close after opening:** command-line launches only, with a **10-second** close delay.
- **Window title:** shows `<repo> · <branch>` once discovery resolves.
- **Console tab colours:** the terminal's own scheme; Fido's palette is opt-in.
- **Before running:** fast-forwards the target onto `origin` when you pick a run command; a pick from
  the Console button's run menu goes to **your** terminal, not Fido's Console tab.

## In-repo config (committed to the branch)

A repo can also configure Fido for itself, in **`.fido/cfg.yaml`** on the branch — *prefer main clone*
and *commands*. It's read from the branch every time a scan lands and needs no user
setting to enable; see **[In-repo config](../guide/in-repo-config.md#in-repo-config-fidocfgyaml)** for the file's shape.

## Where settings live

JSON at **`%APPDATA%\Fido\config.json`**. If that doesn't exist, Fido reads a legacy
`%APPDATA%\atlantic-opener\config.json` (from before the rename) so existing settings survive;
the next save writes to the new location.

Each open window also leaves an empty marker file in **`%LOCALAPPDATA%\Fido\instances`**
(`~/.local/share/Fido/instances` on Linux, `~/Library/Application Support/Fido/instances` on macOS), which
is how `fido <branch>` finds [a window that already has the branch](../guide/command-line.md#a-window-that-already-has-the-branch).
A window removes its marker when it closes, and the next launch clears away any left by a Fido that didn't.

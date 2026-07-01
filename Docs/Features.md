# Fido — Features

**Fido** is a small desktop utility that turns a **branch name** into an open Rider
window. You tell it the branch (and optionally a solution name); it works out
*where that branch lives on disk* — an existing checkout, a linked worktree, or a
fresh one — and launches **JetBrains Rider** there.

The name is a nod to the Apollo **Flight Dynamics Officer (FIDO)**, whose job was to
track the spacecraft and compute its trajectory. Fido does the same for your code:
a branch name in, its exact path on disk out.

- **Platform:** Windows (primary). macOS support is included but experimental.
- **Stack:** .NET 10, Avalonia 12.

---

## Overview

Given a branch and a solution, Fido:

1. Finds the git repository (or repositories) on your machine that contain the solution.
2. Works out where the branch should be opened — reusing an existing checkout when one
   exists, or letting you switch the main tree / create a worktree when it doesn't.
3. Opens the resolved `.sln`/`.slnx`/`.slnf` (or the repo folder) in your chosen editor — Rider by
   default, or WebStorm / VS Code / Visual Studio / Zed / a custom editor.

Everything is keyboard-friendly, and a live log narrates each step.

---

## Two ways to use it

### 1. Branch + solution name

Enter both a **branch** and a **solution name** (a name like `MyApp`, *not* a path).
Fido searches your configured roots for that solution, resolves the branch, and opens it.
This is the full flow, including the ability to **create** a checkout if the branch
isn't on disk yet.

### 2. Branch only (leave the solution blank)

Enter just a **branch**. Fido first scans your roots for a working tree that is **currently
on that branch**, then lists the solution files in that folder (plus an "open the folder"
option) for you to pick. When that folder is a **linked worktree**, the same dialog also offers to
**delete it** — see [Deleting a worktree](#deleting-a-worktree).

If the branch isn't checked out anywhere, Fido falls back to your **new-branch repos** — the
repositories you tick in Settings. It keeps only those whose refs actually contain the branch
(a local branch **or** an `origin` remote-tracking branch) and offers the same **decision
dialog** as solution mode: **checkout in the main tree** or **create a linked worktree**. If
the branch exists in none of your configured repos, Fido says so and does nothing.

---

## Feature reference

### Finding the repository

- Matches **`.sln`**, **`.slnx`**, and **`.slnf`** (Visual Studio solution filter) files. A full
  solution wins over a same-named filter, so a repo's primary target stays the full solution.
- Walks each configured **search root** to a limited depth, skipping noise directories
  (`.git`, `node_modules`, `bin`, `obj`, `.vs`, `.idea`, `packages`, `.svn`, `.hg`, and
  hidden folders).
- Collapses matches by their **canonical main working tree**, so copies of a solution found
  inside worktrees fold back into a single repository entry.

### Cross-clone safety (no duplicate worktrees)

Git enforces "one worktree per branch" only **within a single clone**. If you have two
clones of the same upstream (e.g. `D:\shine\apps` and `D:\main\apps`), each can independently
check out the same branch — leaving you with duplicate copies on disk.

Fido prevents this. Before offering to create anything, it scans **every** candidate clone for
a working tree already on the branch (a clone's own main tree counts):

- **Already checked out somewhere →** it reuses that checkout. No duplicate is created.
- **Checked out in several places →** it asks which one to open.
- **Not checked out anywhere →** only then does it offer to place the branch.

### Placing a new branch

When the branch isn't checked out anywhere, Fido shows a **decision dialog** describing the
main tree's current branch, whether the branch exists on `origin`, the resolved start point,
and any outstanding changes. You choose:

- **Checkout in main** — switch the main working tree to the branch (creating it if needed).
  The dialog warns when the main tree has uncommitted changes that a switch would carry along.
- **Create a worktree** *(default — it's non-destructive)* — add a linked worktree and leave
  the main tree untouched.

**Start-point resolution** when creating a branch: prefer `origin/<branch>` (set up to track),
else the repo's default branch, else the current `HEAD`.

**Worktree location**: a configurable worktree root, otherwise a sibling
`<repo>.worktrees/<sanitized-branch>` folder. Existing paths get a numeric suffix so nothing
is clobbered.

### Choosers

When a choice is needed, Fido shows a keyboard-navigable list with rich, two-line rows:

- **Pick a clone** (more than one repo matches): each row shows the path plus
  *current branch · origin · worktree count* — making it obvious when two rows are clones of
  the same upstream.
- **Pick a checkout / folder** (branch is in more than one place): each row shows the path and
  the **short HEAD commit**. On GitHub remotes the commit is a **clickable link** to the commit
  page (plain text for other remotes) — handy for spotting when two checkouts have diverged.
- **Pick what to open** (branch-only mode): the folder's solutions, plus an "open this folder"
  entry. When the folder is a **linked worktree**, a **Delete worktree & branch** button appears too
  (see [Deleting a worktree](#deleting-a-worktree)).

### Deleting a worktree

In branch-only mode, once Fido has located a **linked worktree** on the branch, the **"Open from branch
folder"** chooser adds a **Delete worktree & branch** button alongside the open choices — reachable even when
there's nothing to open (a folder-only editor, or a worktree with no solution file). It's a shortcut for
tidying up a branch you're finished with, in one step:

- A **confirmation dialog** offers a **checkbox for each present target** — the **worktree**, its **local
  branch**, and the **branch on `origin`** — each **ticked by default**. Untick any to keep it, so you can (say)
  drop just the remote branch, or remove the worktree while keeping its branches. Because a branch that stays
  checked out can't be deleted, **keeping the worktree disables deleting its local branch**. The dialog adds
  explicit **data-loss warnings** when the worktree has **uncommitted changes**, or when the branch carries
  **commits that exist nowhere else** — unpushed and unmerged work that a force-delete would orphan. Nothing
  happens unless you click **Delete** (disabled when nothing is ticked); **Cancel**, **Enter**, and **Esc** all
  back out, and the destructive button sits outside the keyboard tab order so it can't be fired by a stray keypress.
- On confirmation Fido carries out **exactly the ticked targets**: it **removes the linked worktree**,
  **deletes the local branch**, and — when it exists — **deletes the branch on `origin`**. The git steps run
  from the clone's **main working tree**, so the worktree is dropped cleanly; a dirty worktree is
  force-removed after the warning.
- If the remote delete fails (say you're offline), the completed **local** cleanup stays done and the
  failure is reported in the flight log rather than rolled back.

The button is offered **only for a linked worktree on a non-default branch** — a clone's **main working
tree** can't be worktree-removed, and the default branches (`main`/`master`) are deliberately never offered
for deletion. In those cases the button is hidden and a normal open proceeds.

### What gets opened: solution or folder

- **Solution mode:** a radio toggle chooses **Solution** (the `.sln`/`.slnx`/`.slnf`) or **Folder**
  (the repo root). If solution mode can't find the file, it falls back to opening the folder.
- **Branch-only mode:** the chooser lists each solution found in the folder plus an
  "open the folder" option.
- **Folder-only targets:** some targets only understand a project folder — **WebStorm**, and the
  **Console** / **File Explorer** targets that open the folder itself. When one of those is chosen, Fido
  always hands over the folder — it ignores the solution toggle and skips the "which solution?" chooser.

### Editors / IDEs

Fido can open the resolved target into any of several editors — plus the **Console** and **File Explorer**
targets below. The list is configured in Settings, and one entry is the **default**:

- The **default** target is launched by the **Open** button and **Enter**.
- Every other one gets a **secondary button** on the main window and a numbered keyboard
  shortcut, **Ctrl+1 … Ctrl+9** (Ctrl+N opens with the Nth entry in the list).

Built-in editor kinds — **Rider**, **WebStorm**, **VS Code**, **Visual Studio**, **Zed** — auto-detect
when their path is left blank; a **Custom** editor opens whatever executable/app-bundle path you give it.
**WebStorm** is **folder-only**: it's always handed the repo folder rather than a `.sln`/`.slnx`/`.slnf`.
Optional extra command-line arguments can be supplied per editor (passed before the target path).

Each entry also carries a **slug** — a short command-line token (built-in defaults: `rider`, `ws`,
`vsc`, `vs`, `zed`, `term`, `files`) — so a specific one can be picked when launching Fido from the command
line (see **Command-line launch**). The slug is editable per entry in Settings; leave it blank to make that
entry un-selectable from the CLI.

**Auto-detection** for each known kind looks, in order, at an explicit path, then your **`PATH`**,
then common install locations:

- **Rider** — `%LOCALAPPDATA%\Programs\Rider`, JetBrains **Toolbox** apps (newest) and shim,
  `Program Files\JetBrains\JetBrains Rider *`; macOS `/Applications`, `~/Applications`, Toolbox bundles/shim.
- **WebStorm** *(folder-only)* — `%LOCALAPPDATA%\Programs\WebStorm`, JetBrains **Toolbox** apps (newest)
  and shim, `Program Files\JetBrains\WebStorm *`; macOS `/Applications`, `~/Applications`, Toolbox bundles/shim.
- **VS Code** — `code` on `PATH`; `%LOCALAPPDATA%\Programs\Microsoft VS Code\bin\code.cmd` or under
  `Program Files`; macOS `Visual Studio Code.app`.
- **Visual Studio** *(Windows)* — `devenv` on `PATH`; `Program Files\Microsoft Visual Studio\<year>\<edition>\Common7\IDE\devenv.exe`.
- **Zed** — `zed` on `PATH`; macOS `Zed.app`; Windows `%LOCALAPPDATA%\Programs\Zed\Zed.exe`.

The editor is launched **detached** (Fido doesn't wait on it). If the chosen editor can't be found,
Fido says so and points you to its path setting.

### Console & file explorer

Beyond editors, Fido can open the resolved **folder** directly — handy when you just want a shell on the
branch or to browse its files. Two built-in targets, present out of the box and working on **Windows,
macOS, and Linux**:

- **Console** *(folder-only, slug `term`)* — opens a terminal **at the folder**. Auto-detection picks the
  OS default: **Windows** — Windows Terminal (`wt`), else PowerShell (`pwsh`/`powershell`), else `cmd`;
  **macOS** — the **Terminal** app (via `open -a`); **Linux** — the first of `x-terminal-emulator`,
  `gnome-terminal`, `konsole`, `xfce4-terminal`, `kitty`, `alacritty`, `tilix`, `xterm` on `PATH`.
  **The terminal is configurable:** set the Console row's **path** to a specific terminal program — a full
  path *or* just a command name like `wt`, `pwsh`, or `gnome-terminal` (resolved on `PATH`, including Windows
  Terminal's Store alias) — and add arguments if needed. Most terminals open in the folder because Fido sets
  it as their working directory; Windows Terminal is pointed at it explicitly with `-d`.
- **File Explorer** *(folder-only, slug `files`)* — reveals the folder in the OS file manager: **Windows**
  `explorer.exe`, **macOS** Finder (via `open`), **Linux** `xdg-open` (honouring your default file manager),
  else `nautilus` / `dolphin` / `thunar` / `nemo` / `pcmanfm`. The file manager is configurable via the
  row's **path** too.

Both behave like any other target — a secondary button, a **Ctrl+N** shortcut, and a CLI slug — so
`fido feature/new-ui term` opens a terminal on that branch and `fido feature/new-ui files` opens its folder.
They always hand over the **folder**, ignoring the Solution/Folder toggle and the "which solution?" chooser.

### Mission-control console

The in-app log narrates each launch like a flight-control "go around the horn" poll:

```
🚀 Going around the horn…
[✓] Branch resolved: feature/new-ui
[✓] Worktree located: D:\dev\src\worktrees\feature-new-ui
[✓] Solution found: Shine.sln
[✓] Rider located: C:\…\rider64.exe

Fido? GO!
The Eagle has landed...
Closing in 7…
```

The `Closing in N…` line ticks down in place (one line, not a line per second). The countdown also
shows in a **Keep open** bar at the bottom of the window — click it to call off the close and keep
Fido up.

When a typed branch isn't checked out anywhere and Fido searches the repos configured for new branches,
it narrates the hunt the same way — `Searching for local branch in <repo>`, then `Searching for remote
branch in <repo>` when it queries origin — ticking through the repo names in place on a single line.

Failures call it straight — `[✗] …` lines and a **No-go** status; a cancelled dialog reads
**Aborted**.

### Keyboard & shortcuts

- The **branch** field is focused on launch; both fields and the Open button have access-key
  mnemonics.
- **Enter** triggers **Open in &lt;default editor&gt;**; the inputs disable while a launch is in progress.
- **Ctrl+1 … Ctrl+9** open in the corresponding configured editor (the same editors shown as
  secondary buttons), so you can pick a non-default editor without leaving the keyboard.
- **Decision dialog:** `M` / `1` = checkout in main, `W` / `2` = create worktree,
  `Enter` = worktree (the default), `Esc` = cancel.
- **Choosers:** `↑` / `↓` move the highlighted row, `Enter` (or the **Open** button, or a
  double-click) opens it, `Esc` cancels. The shortcuts work whatever holds focus, and a hint
  line along the bottom edge spells them out.
- **Settings dialog:** `Enter` saves, `Esc` cancels.
- **`Alt+Space`** opens the window's native **system menu** (Move, Size, Minimize, Maximize, Close)
  on any window — the same menu reached from the title-bar icon or a title-bar right-click.

Every dialog follows the same convention — `Enter` triggers the primary action and `Esc` dismisses —
so the keyboard behaves consistently across the app.

### Command-line launch

Launch arguments pre-populate the form, and **supplying a branch runs the open flow
automatically** — exactly as if you'd typed it and clicked **Open in Rider**, so any
chooser/decision dialogs still appear when a choice is genuinely needed:

| Argument | Effect |
| --- | --- |
| `<name>` (bare, first) or `--branch` / `-b` `<name>` | Set the branch — **and auto-run the open** |
| `<slug>` (bare, second) or `--editor` / `-e` `<slug>` | Open with the target whose **slug** matches (e.g. `rider`, `vsc`, `vs`, `zed`, or `term` / `files` for a terminal / file manager) instead of the default |
| `--solution` / `-s` `<name>` | Set the solution name |
| `--folder` | Start in Folder open-mode |

For example, `fido feature/new-ui -s MyApp` opens that branch's `MyApp` solution and,
by default, closes Fido a few seconds after Rider is launched (see **Close after opening** and
**Close delay** below).

To pick a non-default target, give its slug as the **second bare argument** — `fido feature/new-ui zed`
opens in Zed, `fido feature/new-ui term` opens a terminal on the branch, `fido feature/new-ui files` opens
its folder — or pass it explicitly with `--editor` / `-e`: `fido -b feature/new-ui -s MyApp -e vs`.
An unrecognised slug stops with a **No-go** that names it (and lists the known slugs) rather than
silently falling back to the default.

---

## Configuration

### Settings (in the app's **Settings** panel)

- **Search roots** — directories to scan for solutions / working trees (one per line).
- **Editors** — the targets Fido can open into. Each row has a name, an optional **slug** (the
  command-line token that selects it, e.g. `rider`), a **kind** (Rider, WebStorm, VS Code, Visual Studio,
  Zed, **Console**, **File Explorer**, or Custom), and an optional path (blank = auto-detect for known kinds;
  required for Custom). For **Console** the path is the **terminal program** and for **File Explorer** the
  **file manager** (blank = the OS default; a full path or a bare command name like `wt` / `pwsh` both work),
  so you can point Fido at the terminal you prefer. Tick the
  **●** radio to set the default (the Open button / Enter); the rest are reached by **Ctrl+1 … Ctrl+9**
  or by their slug on the command line. **Add** appends a new row; **✕** removes one.
- **Worktree root** — leave blank for the sibling `<repo>.worktrees` convention.
- **New-branch repos** — the repositories Fido may place a branch into in **branch-only mode**
  when the branch isn't checked out anywhere. Click **Detect** to scan your search roots for git
  repositories, then tick the ones to use.
- **Close after opening** — when Fido quits after a successful launch: **Command line** *(default —
  only when started with a branch on the command line)*, **Always** (after every launch, including
  the Open button), or **Never** (turns auto-close off).
- **Close delay** — seconds Fido counts down before it auto-closes (default **10**; **0** closes
  immediately). The flight log shows a single line that ticks down in place (`Closing in 10…`, then
  `9…`, `8…`), and a **Keep open** bar appears at the bottom with the live countdown. Clicking
  **Keep open** — or simply starting another open — cancels the close, so it's never a point of no return.

### Defaults

- **Search roots:** `%USERPROFILE%\source\repos`, `%USERPROFILE%\src`,
  `%USERPROFILE%\RiderProjects`, `%USERPROFILE%\Projects`.
- **Default branch names:** `main`, `master`.
- **Search depth:** 4.
- **Close after opening:** command-line launches only, with a **10-second** close delay.

### Where settings live

JSON at **`%APPDATA%\Fido\config.json`**. If that doesn't exist, Fido reads a legacy
`%APPDATA%\atlantic-opener\config.json` (from before the rename) so existing settings survive;
the next save writes to the new location.

---

## At a glance

| Capability | Summary |
| --- | --- |
| Input | Branch name (required) + solution name (optional) |
| Branch-only mode | Open from an existing checkout, or place the branch into a configured repo that already has it |
| Cross-clone reuse | Never creates a second worktree for a branch already checked out |
| Placement | Switch main tree **or** create a linked worktree |
| Open target | `.sln` / `.slnx` / `.slnf` solution, or the repo folder |
| Editors | Rider / WebStorm / VS Code / Visual Studio / Zed / Custom — default + Ctrl+1…9, or by CLI slug |
| Folder targets | **Console** (`term`) opens a terminal, **File Explorer** (`files`) the OS file manager — Windows / macOS / Linux |
| Editor discovery | Explicit path → PATH → standard installs (per kind) |
| Commit links | Short HEAD hash, clickable to the GitHub commit |
| Config | `%APPDATA%\Fido\config.json` (migrates the legacy folder) |

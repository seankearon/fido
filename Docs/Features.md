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
3. Opens the resolved `.sln`/`.slnx` (or the repo folder) in Rider.

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
option) for you to pick.

If the branch isn't checked out anywhere, Fido falls back to your **new-branch repos** — the
repositories you tick in Settings. It keeps only those whose refs actually contain the branch
(a local branch **or** an `origin` remote-tracking branch) and offers the same **decision
dialog** as solution mode: **checkout in the main tree** or **create a linked worktree**. If
the branch exists in none of your configured repos, Fido says so and does nothing.

---

## Feature reference

### Finding the repository

- Matches both **`.sln`** and **`.slnx`** solution files.
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
  entry.

### What gets opened: solution or folder

- **Solution mode:** a radio toggle chooses **Solution** (the `.sln`/`.slnx`) or **Folder**
  (the repo root). If solution mode can't find the file, it falls back to opening the folder.
- **Branch-only mode:** the chooser lists each solution found in the folder plus an
  "open the folder" option.

### Locating Rider

Fido finds Rider automatically, in order:

1. An explicit **Rider path** from settings.
2. Your **`PATH`** (`rider64`, `rider`, `rider.cmd` on Windows; `rider` on macOS).
3. Common installs — Windows: `%LOCALAPPDATA%\Programs\Rider`, JetBrains **Toolbox** apps
   (newest) and shim, `Program Files\JetBrains\JetBrains Rider *`. macOS: `/Applications`,
   `~/Applications`, and Toolbox app bundles/shim.

Rider is launched **detached** (Fido doesn't wait on it). If Rider can't be found, Fido says so
and points you to the Rider-path setting.

### Mission-control console

The in-app log narrates each launch like a flight-control "go around the horn" poll:

```
🚀 Going around the horn…
[✓] Branch resolved: feature/new-ui
[✓] Worktree located: D:\dev\src\worktrees\feature-new-ui
[✓] Solution found: Shine.sln
[✓] Rider located: C:\…\rider64.exe

Fido? GO!
Launching Rider…
```

Failures call it straight — `[✗] …` lines and a **No-go** status; a cancelled dialog reads
**Aborted**.

### Keyboard & shortcuts

- The **branch** field is focused on launch; both fields and the Open button have access-key
  mnemonics.
- **Enter** triggers **Open in Rider**; the inputs disable while a launch is in progress.
- **Decision dialog:** `M` / `1` = checkout in main, `W` / `2` = create worktree,
  `Enter` = worktree (the default), `Esc` = cancel.
- **Choosers:** arrow keys to move, `Enter` / Open to select, `Esc` to cancel, double-click to
  pick.

### Command-line launch

Launch arguments pre-populate the form, and **supplying a branch runs the open flow
automatically** — exactly as if you'd typed it and clicked **Open in Rider**, so any
chooser/decision dialogs still appear when a choice is genuinely needed:

| Argument | Effect |
| --- | --- |
| `<name>` (bare) or `--branch` / `-b` `<name>` | Set the branch — **and auto-run the open** |
| `--solution` / `-s` `<name>` | Set the solution name |
| `--folder` | Start in Folder open-mode |

For example, `fido feature/new-ui -s MyApp` opens that branch's `MyApp` solution and,
by default, closes Fido once Rider is launched (see **Close after opening** below).

---

## Configuration

### Settings (in the app's **Settings** panel)

- **Search roots** — directories to scan for solutions / working trees (one per line).
- **Rider path** — leave blank to auto-detect.
- **Worktree root** — leave blank for the sibling `<repo>.worktrees` convention.
- **New-branch repos** — the repositories Fido may place a branch into in **branch-only mode**
  when the branch isn't checked out anywhere. Click **Detect** to scan your search roots for git
  repositories, then tick the ones to use.
- **Close after opening** — when Fido quits after a successful launch: **Command line** *(default —
  only when started with a branch on the command line)*, **Always** (after every launch, including
  the Open button), or **Never**.

### Defaults

- **Search roots:** `%USERPROFILE%\source\repos`, `%USERPROFILE%\src`,
  `%USERPROFILE%\RiderProjects`, `%USERPROFILE%\Projects`.
- **Default branch names:** `main`, `master`.
- **Search depth:** 4.

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
| Open target | `.sln` / `.slnx` solution, or the repo folder |
| Rider discovery | Config → PATH → Toolbox / standard installs |
| Commit links | Short HEAD hash, clickable to the GitHub commit |
| Config | `%APPDATA%\Fido\config.json` (migrates the legacy folder) |

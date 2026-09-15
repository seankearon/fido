# Fido — Features

**Fido** is a small desktop utility that turns a **branch name** into an open Rider
window. You type the branch; it works out *where that branch lives on disk* — every
linked worktree and main clone currently checked out on it — and launches
**JetBrains Rider** (or any other configured tool) there.

The name is a nod to the Apollo **Flight Dynamics Officer (FIDO)**, whose job was to
track the spacecraft and compute its trajectory. Fido does the same for your code:
a branch name in, its exact path on disk out.

- **Platform:** Windows (primary). macOS support is included but experimental.
- **Stack:** .NET 10, Avalonia 12.

---

## Overview

Given a branch, Fido:

1. Scans your configured **search roots** for every working tree **currently on that
   branch** — linked worktrees and main clones alike.
2. Shows each location **inline on the main screen** as a selectable card, clearly
   labelled **worktree** or **main clone**, and lets you choose which to act on.
3. Opens the chosen `.sln`/`.slnx`/`.slnf` (or the folder) in your chosen tool — Rider by
   default, or WebStorm / VS Code / Visual Studio / Zed / a terminal / the file
   explorer / a custom editor.

Everything happens on one screen, everything is keyboard-friendly, and a live log
narrates each step.

---

## The discovery loop

Type a branch and Fido starts looking. There is no separate search button, no pop-up
chooser, and no second window:

- **Typing** debounces into a scan (about 600 ms after you stop); **Enter** fires one
  immediately.
- A status pill tracks the phase: **idle → scanning → found / not found**.
- **Found:** the locations render inline as **target cards** — worktrees listed before
  main clones, the first auto-selected. When the branch is checked out in more than
  one place, a helper strip says so and you pick the card to act on.
- **Checked out nowhere, but a repo has it:** the scanned clones' refs are consulted —
  a local branch, the cached `origin` tracking ref, or (as a last resort) one live
  `ls-remote` sweep, so a branch pushed after your last fetch is still found. Each
  clone that has the branch offers **two placement cards**: a **new worktree** card
  (plus icon, leads and is auto-selected) showing where the worktree *would* be
  created, and a **switch clone** card (arrows icon) that moves the clone's **main
  tree** onto the branch instead — warning right on the card when uncommitted changes
  would ride along. **Opening performs the placement first** (fetching and tracking
  the remote ref when needed) and then launches.
- **Not found:** a warning card says no working tree or clone has the branch —
  double-check the name. Fido never switches an existing checkout; the only thing it
  will create is a worktree you explicitly selected.

The **open and delete actions stay locked until discovery succeeds** — nothing opens
before Fido knows where the branch lives, and the locked block states the reason
(`🔒 Enter a branch name to begin discovery`, `🔒 Scanning…`, `🔒 No location found`).

The **Solution** box is optional: it **filters** which detected solutions appear as
chips (see [What gets opened](#what-gets-opened-solution-or-folder)) — it's no longer a
search input or a mode switch.

---

## Feature reference

### Finding the branch

- Walks each configured **search root** to a limited depth looking for git working
  trees, skipping noise directories (`.git`, `node_modules`, `bin`, `obj`, `.vs`,
  `.idea`, `packages`, `.svn`, `.hg`, and hidden folders).
- Checks each working tree's **current branch** — only trees actually on the typed
  branch count. Both **linked worktrees** and a clone's **main tree** qualify.
- Labels every hit: a **worktree** card (git-branch icon, deletable), a **main
  clone** card (home icon, never deletable), or — when the branch is checked out
  nowhere — a **new worktree** card (plus icon, created on open) and a **switch
  clone** card (arrows icon, switches the main tree on open), neither deletable.
  Meta lines read like `platform · 2 solutions · updated 3d ago`,
  `platform · branch on origin · opening creates this worktree`, or
  `platform · main tree on 'main' · opening switches it here · ⚠ 3 uncommitted
  change(s) ride along`.
- Detects the **solution files** inside each target — **`.sln`**, **`.slnx`**, and
  **`.slnf`** (Visual Studio solution filter) — for the solution chips.

### Cross-clone visibility

Git enforces "one worktree per branch" only **within a single clone**. If you have two
clones of the same upstream (e.g. `D:\shine\apps` and `D:\main\apps`), each can
independently check out the same branch — leaving you with duplicate copies on disk.

Fido makes that visible instead of guessing: **every** location on the branch appears
as its own card, labelled with its kind and owning repo, and **you choose** which one
to open (or delete). A worktree is only ever created from a card you selected — never
behind your back — so Fido can't add a duplicate of its own.

### Choosing the target

The selected card is the target for **every** action — the hero button, the tool grid,
the keyboard accelerators, and the delete row. Selecting a different card:

- updates the **context strip** (the `OPEN` row showing the target path and its kind),
- rebuilds the **solution chips** from that target's detected solutions, and
- cancels any pending delete confirmation.

Exactly one card is always selected; the first (a worktree, when there is one) is
selected automatically when a scan lands — unless the branch's own
[`.fido/cfg.yaml`](#in-repo-config--fidocfgyaml) asks for the **main clone**.

### What gets opened: solution or folder

- The context strip lists a **chip per detected solution** (`.sln`/`.slnx`/`.slnf`)
  in the selected target, plus a **Folder** chip. The first solution is pre-selected;
  pick **Folder** to open the working tree itself.
- **Only Rider and Visual Studio consult the chips** — they open the chosen solution
  (or the folder, when Folder is chosen or no solutions were found).
- **Every other tool opens the folder** — WebStorm, VS Code, Zed, Console, and File
  Explorer have no solution concept and ignore the chips entirely.
- The **Solution** box **filters** the chips by file name (blank = show every solution
  found). When the filter hides them all, only the Folder chip remains.

### Open actions & the default tool

- The **hero button** is the **default tool** — full-width, marked with a `default`
  pill and its `Ctrl+N` accelerator. Every other tool sits in the **3-column grid**
  below it, each with its own accelerator.
- **No default set?** There's no hero — all tools render in the equal-weight grid,
  with a note: *"No default tool set — every option is equal weight. Pick one, set a
  default in ⚙, or pass `--tool` on launch."*
- The **⚙ gear popover** (top-right) sets the default: a radio list of your configured
  tools plus **No default (equal weight)**. The choice persists to config immediately.
  **All settings…** opens the full Settings dialog from the same popover.
- A CLI `--tool <id>` overrides the default **for that run only** — picking a radio in
  the popover (or editing Settings) takes back over.
- All open actions are visible in every phase but **enabled only when discovery has
  found the branch** — the accelerators respect the same gate.

### Deleting a worktree

Once discovery has found the branch, a **Delete worktree & branch** button sits on the
main screen beneath the tools — no dialog to dig through. It's a shortcut for tidying
up a branch you're finished with:

- The button is enabled **only when the selected card is a linked worktree on a
  non-default branch**. Selecting the **main clone** disables it with a note (*"only
  worktrees can be deleted — the main clone stays put."*); the default branches
  (`main`/`master`) are deliberately never deletable, even from a worktree (*"default
  branches can't be deleted — main/master stay put."*).
- **Two-step inline confirm.** The first click swaps the button for an in-place
  confirm strip that spells out exactly what happens — *"Remove the worktree at
  `<path>` and delete local branch `<branch>`? This can't be undone."* — plus explicit
  **data-loss warnings** when the worktree has **uncommitted changes** or the branch
  carries **commits that exist nowhere else** (unpushed and unmerged work a delete
  would orphan). Nothing happens unless you click **Delete**; **Cancel** or **Esc**
  backs out, and the destructive buttons sit outside the keyboard tab order so they
  can't be fired by a stray keypress.
- On confirmation Fido **removes the linked worktree** and **deletes the local
  branch**. When the branch is also on `origin`, the confirm strip offers an **opt-in
  checkbox — _Also delete the remote branch `origin/<branch>`_** — left **unticked by
  default**, so the remote is never touched unless you ask. **An open pull request
  blocks it:** when the **GitHub CLI (`gh`)** reports a PR open for the branch, the
  checkbox is **disabled** and the strip names the PR (`PR #42 · <title>`) with an
  **Open pull request ↗** link — close or merge it on GitHub first. PR detection
  degrades gracefully: if `gh` isn't installed, isn't authenticated, or the remote
  isn't GitHub, the option is simply offered without a PR note. The git steps run from
  the clone's **main working tree**, so the worktree is dropped cleanly; a dirty
  worktree is force-removed after the warning.
- Each git step is **retried on transient failures** so a fleeting hiccup doesn't
  leave a half-tidied branch: a worktree file still held open by an editor or
  antivirus scan (common on Windows), or a git ref/index `.lock` left by a racing git
  process. Fido retries a few times with a short, backing-off wait — each attempt
  narrated in the flight log — while **permanent** refusals still fail fast on the
  first try.
- **The three targets are independent, and nothing-to-do isn't failure.** The worktree,
  the local branch and the branch on `origin` are each attempted and reported on
  separately: one that fails no longer abandons the ones after it, and the flight log's
  closing line says exactly what went and what didn't (*"✓ Removed worktree & branch
  `feature/x` + origin/feature/x."*). A target that had **already gone** — a branch the
  server dropped on merge, a folder cleared by hand, a worktree git no longer knows
  about — is reported as **done**, not as an error: *"✓ Removed worktree & branch
  `feature/x` — origin/feature/x was already gone."* A `⚠` is spent only on something
  you asked for that is genuinely **still there**.
- **Retry what's left.** When something does survive the delete, an inline **Retry**
  strip appears with what's still standing, git's own words for why, and a **Retry** /
  **Dismiss** pair. Retrying re-runs **only the outstanding step** — a worktree and
  local branch that already went are not touched again — and the closing report then
  covers the whole attempt. The strip sits outside the delete row so it outlives the
  card you just deleted; **Dismiss** or **Esc** drops the offer without touching
  anything on disk, and a fresh scan clears it as stale.
- **Long filenames & a force-delete fallback.** Deep worktrees can trip Windows'
  **260-character `MAX_PATH`** limit — a `node_modules` tree or generated output whose
  paths are too long — and a delete then fails with **`filename too long`** /
  **`unable to unlink … Filename too long`**, leaving the worktree stuck. Fido guards
  against this two ways. First, git's worktree commands run with **long-path support**
  (`core.longpaths`) so git's own file operations use the Windows extended-length API
  and can remove those files. Second, if git **still** can't delete the folder (a path
  too long even for that), Fido **offers to delete it straight from disk**: a modal,
  clearly-labelled confirmation for a recursive removal that **bypasses the Recycle
  Bin** and uses an extended-length (`\\?\`) path so it isn't defeated by the same
  limit. Once the folder is gone Fido **prunes** git's dangling worktree registration
  and carries on with the branch deletion. Nothing is force-deleted unless you choose
  it, and backing out leaves everything in place.
- Afterwards the card **drops out of the results** and the next location is selected —
  or the screen falls to **not found** when none remain.

### Editors / IDEs

Fido can open the resolved target into any of several editors — plus the **Console** and **File Explorer**
targets below. The list is configured in Settings, and one entry can be the **default**:

- The **default** tool takes the **hero button**; set it from the **⚙ gear popover**
  or the **●** radio in Settings (or leave it unset for the equal-weight grid).
- Every tool — hero included — has a numbered keyboard shortcut, **Ctrl+1 … Ctrl+9**
  (Ctrl+N opens with the Nth entry in the configured list).

Built-in editor kinds — **Rider**, **WebStorm**, **VS Code**, **Visual Studio**, **Zed** — auto-detect
when their path is left blank; a **Custom** editor opens whatever executable/app-bundle path you give it.
**WebStorm** is **folder-only**: it's always handed the folder rather than a `.sln`/`.slnx`/`.slnf`.
Optional extra command-line arguments can be supplied per editor (passed before the target path).

Each entry also carries a **slug** — a short command-line token (built-in defaults: `rider`, `ws`,
`vsc`, `vs`, `zed`, `term`, `files`) — so a specific one can be picked when launching Fido from the
command line (see **Command-line launch**). The slug is editable per entry in Settings, and the
built-in kinds also answer to **well-known aliases** (`webstorm`, `vscode` / `code`,
`visualstudio` / `devenv`, `console` / `terminal`, `explorer` / `fileexplorer` / `finder`…) — only a
**Custom** editor with a blank slug is un-selectable from the CLI.

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

Both behave like any other tool — a grid button, a **Ctrl+N** shortcut, and a CLI slug — so
`fido feature/new-ui term` opens a terminal on that branch and `fido feature/new-ui files` opens its folder.
They always hand over the **folder**, ignoring the solution chips.

### In-repo config — `.fido/cfg.yaml`

A repository can carry its own Fido settings, committed to the branch in a **`.fido`** folder at the
root. When a scan lands, Fido reads **`.fido/cfg.yaml`** *from the branch it just found* — **before** the
checkout options are offered — so a solution can say how it prefers to be opened and which of its scripts
are worth a button:

```yaml
# .fido/cfg.yaml
prefer main clone: true       # default to opening the clone's own tree, not a worktree
run files:                    # scripts the Console button offers to run
  - build.ps1
  - '*'                       # …plus every script in the repo root
aspire start: true            # …and `aspire start`, for an Aspire app host
```

- **`prefer main clone`** *(true/false)* — **which checkout Fido offers by default** once the scan has
  landed. Normally that's the first result, and worktrees lead; with this set it's the **main clone**
  instead (or, when the branch is checked out nowhere, the **switch the main tree** placement offer).
  It directs the **default choice, not the scan**: every location on the branch is still found and
  listed exactly as before, and each is one click away — this only decides which one the open actions
  start on. If the results hold no main tree at all, the flight log says so and the first card keeps
  the default.
- **`run files`** *(list of script names)* — each name becomes an entry in the **Console button's
  drop-down**, in the order given. A **`*`** entry stands for *every script in the tree root* —
  `.ps1`, `.cmd`, `.bat`, `.sh` — expanded in place and sorted by name; a name listed explicitly keeps
  its position and is never offered twice. A named script is offered whether or not it's in the tree
  today (it may be generated), so a typo shows up as a shell error rather than a missing button.
- **`aspire start`** *(true/false)* — adds **`aspire start`** to the same drop-down, at the end.

**Keys are matched loosely** — case, spaces, dashes and underscores are all ignored, so
`preferMainClone`, `prefer-main-clone` and `Prefer main clone` are the same key. Comments, quotes and
inline lists (`run files: [build.ps1, test.ps1]`) are understood; a setting Fido doesn't recognise is
skipped rather than rejected, and a missing or unreadable file simply means "no in-repo config" — a scan
never fails because of one.

**Where it's read from.** For a location that's **already on the branch** the file is read from that
working tree, so an edit you haven't committed yet counts. For a **placement offer** — the branch isn't
checked out anywhere — it's read straight out of the branch with `git show`, including from
`origin/<branch>` when only the remote has it. Nothing has to be checked out for the config to apply.

**Creating it.** The **OPEN** strip carries a small **document button** beside the copy-path icon:
it creates `.fido/cfg.yaml` in the selected location and opens it in your default tool, so a repo can
be set up without leaving Fido. (The same action sits at the foot of the Console run menu, as
**Edit `.fido/cfg.yaml`…**, for when you're already in there wanting another entry.) Three things it
deliberately does *not* do:

- **It never overwrites.** A repo that already has the file gets it **opened**, untouched — create and
  edit are the same button.
- **It changes nothing by itself.** The file it writes has every setting present at its **default**,
  with the tree's own root scripts named in a comment so the run-file list can be filled in without
  going looking. Until you edit it, the next scan reads it as *no in-repo config*.
- **It doesn't stage or commit.** What lands in the repo's history stays your call, as with every other
  git action in Fido. Edit it, commit it, then press **Enter** to rescan and pick the settings up.

The button is **absent for a placement offer** — there's no working tree on disk to write into until you
open it.

**The Console drop-down.** The run files and `aspire start` appear under a small **caret beside the
Console button** (or beside the hero button, when Console *is* your default tool). Picking one opens the
console at the **selected** location and runs the command there — a `.ps1` through PowerShell, a root
`.sh` as `./name`, anything else through the platform's shell — leaving the window open afterwards so you
can read the output. On Windows a Windows Terminal console hosts the shell in a tab; elsewhere the shell
runs on its own. **Nothing runs on its own:** Fido only ever *offers* these commands, and a placement
offer still creates the worktree (or switches the tree) first, exactly as opening it would.

### Mission-control console

The in-app **flight log** narrates each scan and launch like a flight-control "go around
the horn" poll:

```
🚀 Going around the horn…
Scanning 24 working tree(s) for 'feature/new-ui'…
✓ Found 2 location(s) for 'feature/new-ui'.
✓ Rider located: C:\…\rider64.exe
▸ Opening Shine.sln in Rider
Fido? GO!
The Eagle has landed...
Closing in 7…
```

Each fresh scan resets the log and starts the poll again; the `Scanning N working
tree(s)…` line ticks in place as the count comes in. The `Closing in N…` line counts
down in place too (one line, not a line per second), and the countdown also shows in a
**Keep open** bar at the bottom of the window — click it to call off the close and keep
Fido up.

Lines are colour-coded by kind — accent for the mission beats (`🚀`, `🗑`, `Fido? GO!`),
green `✓` for successes, plain `▸` for actions — and failures call it straight: `⚠`
lines for a branch that isn't checked out anywhere, a tool that can't be located, or a
delete that went wrong.

The panel **grows with the window**: drag Fido's bottom edge down and every spare pixel
goes to the log rather than to a gap above it — the upper section keeps as much room as
its content needs, and the log takes the rest. Shrink the window again and the log falls
back to its compact box while the upper section scrolls.

Two buttons on the **Flight log** rule take the narration with you:

- **Copy** puts the whole log — every line, not just the visible ones — on the clipboard
  as plain text.
- **Save** writes it to a text file you pick, suggesting a dated name like
  `fido-flight-log-20260806-142317.txt`.

Both are disabled until there's something to hand over, and each confirms itself in the
log (`📋 Copied 8 flight-log line(s) to the clipboard.`, `✓ Flight log saved to …`).

### The window title

Once discovery **resolves** the branch, the window title names the work rather than the
app: the selected card's **repo** and the **branch**, with no `Fido` in front —

```text
platform · feature/new-ui
```

so a taskbar (or `Alt`+`Tab`) full of Fido windows tells you which is which at a glance.
The title **follows the selection**: picking a different card in a multi-location result
swaps the repo name with it. Anything less than a resolved branch — nothing typed yet, a
scan in flight, a branch found nowhere, a cleared branch box — reads plain **`Fido`**
again.

Prefer the title to stay put? Untick **Window title · _Show the repo and branch once
discovery resolves them_** in Settings and it reads `Fido` throughout.

### The version badge

The running build's version sits beside the **fido** wordmark in the header, in small muted
type on the wordmark's own baseline —

```text
fido  v0.9.3
```

— so "which Fido is this?" is answered on screen rather than from the installed-programs
list or the exe's properties. It is read from the **running assembly**, so it names what is
actually running.

### Keyboard & shortcuts

- The **branch** field is focused on launch. Typing debounces into a scan; **Enter**
  fires one immediately.
- **Ctrl+Space** in either input opens its **recently-used** suggestions (the **✕** on
  a suggestion forgets it).
- **Ctrl+1 … Ctrl+9** open the selected target with the corresponding configured tool
  (the same tools shown as buttons), gated — like the buttons — on discovery having
  **found** the branch.
- **Esc** backs out of a pending delete confirmation — or, once a delete has run,
  dismisses the **Retry** strip a part-way delete left behind.
- **Settings dialog:** `Enter` saves, `Esc` cancels.
- **`Alt+Space`** opens the window's native **system menu** (Move, Size, Minimize, Maximize, Close)
  on any window — the same menu reached from the title-bar icon or a title-bar right-click.

The destructive delete buttons sit outside the tab order, so `Enter`/`Tab` can never
land on them by accident.

### Command-line launch

Launch arguments pre-populate the form, and **supplying a branch starts discovery
immediately** — exactly as if you'd typed it. Opening, though, stays deliberate:

| Argument | Effect |
| --- | --- |
| `<name>` (bare, first) or `--branch` / `-b` `<name>` | Set the branch — **discovery runs straight away** |
| `<tool>` (bare, second) or `--tool` / `-t` `<id>` (also `--editor` / `-e`) | The named tool becomes **this run's default** (the hero button) — and **auto-opens** when discovery finds **exactly one** location |
| `--solution` / `-s` `<name>` | Pre-fill the **solution filter** |
| `--folder` | Start on the **Folder** chip instead of the first solution |

A tool `<id>` is a configured **slug** (`rider`, `vsc`, `vs`, `zed`, `term`, `files`…)
or a built-in kind alias (`webstorm`, `vscode`, `visualstudio`, `console`,
`explorer`…). `--tool none` shows the equal-weight grid for this run without touching
your saved default.

For example, `fido feature/new-ui rider` scans for the branch and — if it's checked
out in exactly one place — opens it in Rider and, by default, closes Fido a few
seconds later (see **Close after opening** and **Close delay** below). The auto-open
is intentionally narrow:

- **A bare `fido <branch>`** (no tool named) scans and presents the results — it
  **never auto-opens**.
- **Multiple locations** are never auto-opened either — Fido shows the labelled cards
  and lets you choose, rather than guessing between clones.
- **An unrecognised tool id** is reported in the flight log after the scan (listing
  the ids that *are* known) and never auto-opens — Fido won't silently fall back to
  the default.

---

## Configuration

### Settings (in the app's **Settings** dialog — reach it via ⚙ → **All settings…**)

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
- **Worktree root** — leave blank for the sibling `<repo>.worktrees` convention.
- **Theme** — **System**, **Light**, or **Dark**.
- **Window title** — **Show the repo and branch once discovery resolves them** *(default on)*. On, a
  resolved branch renames the window to `<repo> · <branch>` (see **The window title** above); untick it
  and the title stays `Fido`.
- **Close after opening** — when Fido quits after a successful launch: **Command line** *(default —
  only when started with a branch on the command line)*, **Always** (after every launch, including
  the on-screen buttons), or **Never** (turns auto-close off).
- **Close delay** — seconds Fido counts down before it auto-closes (default **10**; **0** closes
  immediately). The flight log shows a single line that ticks down in place (`Closing in 10…`, then
  `9…`, `8…`), and a **Keep open** bar appears at the bottom with the live countdown. Clicking
  **Keep open** — or simply starting another scan or open — cancels the close, so it's never a point
  of no return.

### Defaults

- **Search roots:** `%USERPROFILE%\source\repos`, `%USERPROFILE%\src`,
  `%USERPROFILE%\RiderProjects`, `%USERPROFILE%\Projects`.
- **Default branch names:** `main`, `master` (never offered for deletion).
- **Search depth:** 4.
- **Close after opening:** command-line launches only, with a **10-second** close delay.
- **Window title:** shows `<repo> · <branch>` once discovery resolves.

### In-repo config (committed to the branch)

A repo can also configure Fido for itself, in **`.fido/cfg.yaml`** on the branch — *prefer main clone*,
*run files* and *aspire start*. It's read from the branch every time a scan lands and needs no user
setting to enable; see **[In-repo config](#in-repo-config--fidocfgyaml)** above for the file's shape.

### Where settings live

JSON at **`%APPDATA%\Fido\config.json`**. If that doesn't exist, Fido reads a legacy
`%APPDATA%\atlantic-opener\config.json` (from before the rename) so existing settings survive;
the next save writes to the new location.

---

## At a glance

| Capability | Summary |
| --- | --- |
| Input | Branch name (required); the solution box **filters** the detected solution chips |
| Discovery | Debounced scan of the search roots for working trees **currently on the branch**; results inline as cards, worktrees before main clones |
| Multiple locations | Every checkout shown, labelled **worktree** / **main clone** — you choose which to act on |
| Open gate | Open & delete actions unlock only when discovery **finds** the branch |
| Open target | Rider / Visual Studio: the chosen `.sln` / `.slnx` / `.slnf` chip or the folder; every other tool: the folder |
| Delete worktree | Inline two-step confirm; removes the worktree + **local** branch, with an **opt-in to also delete the remote branch** (unticked by default, disabled while an open PR — via `gh` — blocks it, linking to the PR); retries transient failures; long-path aware with a Recycle-Bin-bypassing force-delete for **`filename too long`** |
| Delete reporting | Each target reported separately — **already gone counts as done**, not as failure; anything genuinely left behind gets an inline **Retry** strip that re-runs just that step |
| Tools | Rider / WebStorm / VS Code / Visual Studio / Zed / Custom — hero default + Ctrl+1…9, or by CLI id |
| Folder targets | **Console** (`term`) opens a terminal, **File Explorer** (`files`) the OS file manager — Windows / macOS / Linux |
| In-repo config | `.fido/cfg.yaml` on the branch: **prefer main clone**, **run files** (`*` = every root script) and **aspire start** — the latter two as a drop-down under the **Console** button. Created (or opened) from the **OPEN** strip, seeded at its defaults and never overwritten |
| Editor discovery | Explicit path → PATH → standard installs (per kind) |
| CLI | `fido <branch> [tool]` — auto-opens only for an explicitly named tool with exactly one location |
| Window title | Once a branch resolves, the title reads `<repo> · <branch>` — no "Fido" in front, following the selected card; switchable off in Settings |
| Config | `%APPDATA%\Fido\config.json` (migrates the legacy folder), plus the repo's own `.fido/cfg.yaml` on the branch |

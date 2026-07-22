# Changelog

All notable changes to Fido are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- **The main screen was redesigned around inline discovery** (per the Claude Design handoff in
  `design/design_handoff_fido_redesign`) — one window, one screen, no auto-popping dialogs:
  - **Discovery is the core loop.** Typing a branch name (debounced, or Enter to fire immediately)
    scans every configured working tree and clone for where that branch is checked out. Results render
    **inline as selectable cards** — each labelled **worktree** or **main clone** (worktrees first, the
    first auto-selected) with its repo, solution count, and last-updated age. The separate "Open from
    branch folder" window and the multi-checkout chooser dialog are gone.
  - **A branch checked out nowhere is offered as a *new worktree* card.** When no working tree has the
    branch, discovery consults the scanned clones' refs — a local branch, the cached `origin` tracking
    ref, or (as a last resort) one live `ls-remote` sweep, so a branch a teammate or cloud session
    pushed after your last fetch is still found. Each clone that has it appears as a clearly-labelled
    **new worktree** card showing where the worktree would be created; **opening it creates the
    worktree first** (fetching and tracking the remote ref when needed) and then launches, exactly
    like the old decision-dialog flow — minus the dialog. Candidates preview the clone's solutions as
    chips and are never deletable.
  - **Open actions stay locked until discovery succeeds.** The hero button and tool grid are always on
    show but enabled only when the branch was **found** — with a one-line reason (idle / scanning /
    not found) in their place until then. Nothing opens before discovery resolves.
  - **The default tool is a hero button, not a hard-coded Rider.** Whatever config or the CLI sets
    drives the full-width amber **Open in &lt;tool&gt;** button; the remaining tools sit in a
    three-column grid with their `Ctrl+1…9` accelerators. Choosing **no default** (new ⚙ popover
    option, or `--tool none`) renders every tool at equal weight.
  - **The Solution/Folder toggle is gone; behaviour is inferred per tool.** Rider and Visual Studio
    open the **chosen solution chip** (`*.sln`/`*.slnx`/`*.slnf` detected per target; the Solution box
    now **filters** the chips); WebStorm, VS Code, Zed, Console, and File Explorer always open the
    folder. A **Folder** chip opens the working tree itself (`--folder` starts the run on it).
  - **Delete moved to the main screen.** "Delete worktree & branch" sits under the open actions,
    enabled only when the selected target is a **linked worktree** on a non-default branch. Clicking it
    swaps the button for an **in-place two-step confirm** that spells out the worktree path and branch
    — plus uncommitted-change and orphaned-commit warnings — with Cancel/Delete (Esc backs out; Enter
    never deletes). The confirmed delete removes the worktree and its **local** branch; the branch on
    `origin` is never touched. The "filename too long" recovery (permanent, Recycle-Bin-bypassing
    folder delete) still offers itself when git can't remove the folder.
  - **The ⚙ gear opens a default-tool popover** (radio list incl. *No default (equal weight)*, with a
    note that a `--tool` launch flag overrides it per run) and an **All settings…** door to the full
    settings dialog.
  - **CLI:** `fido <branch> [tool]`, `--branch/-b`, `--solution/-s` (chip filter), and `--tool/-t`
    (`--editor/-e` still accepted) which also takes kind aliases (`webstorm`, `vscode`, `explorer`, …)
    and `none`. A CLI-supplied branch **prefills and scans**; naming a tool makes it the run's hero and
    **auto-opens only when discovery finds exactly one location** — with several, the choice is always
    presented, never auto-popped. An unknown tool id is reported (with the known ids) and never guessed.
  - **A warm, cream-and-amber look.** The handoff's palette is now the Light theme verbatim — canvas
    `#FBF9F3`, amber `#F4A62A` accents, worktree/main-clone colour coding, red-outline danger styling —
    with the Dark theme re-derived in the same warm hues, anchored on the logo tile. The flight log
    keeps the FIDO aviation voice, now colour-coded per line kind (accent/ok/warn/muted/plain), and the
    window height hugs its content.

### Removed

- **The chooser, decision, and delete-worktree dialogs** — inline discovery, the target cards, and the
  in-place delete confirm replace all three. Placing a not-checked-out branch lives on as the inline
  **new worktree** card (above); the decision dialog's **check-out-in-main-tree** option and the
  Settings section for **New-branch repos** are gone — candidates now come from the clones discovery
  already scans, so there's no separate list to maintain (a configured list is preserved on disk,
  unused).

### Added

- **Delete a worktree, its branch, and the remote branch — from the branch-folder chooser.** When
  branch-only mode locates a **linked worktree** on a branch, the **"Open from branch folder"** dialog now
  offers a **Delete worktree & branch** button beside the open choices (and it's reachable even when there's
  nothing to open — a folder-only editor, or a worktree with no solution file). Clicking it shows a
  confirmation dialog with a **checkbox for each present target** — the worktree, its local branch, and the
  branch on `origin` — **ticked by default**, so you can untick any to keep it (keeping the worktree disables
  deleting its branch, since a checked-out branch can't be removed). The dialog adds **explicit data-loss
  warnings** when the worktree has **uncommitted changes** or the branch carries **commits that exist nowhere
  else** (unpushed and unmerged — `git branch -D` would orphan them). Once confirmed, Fido carries out exactly
  the ticked targets — **removing the linked worktree, deleting the local branch, and deleting the branch on
  `origin`**. The work runs from the clone's main tree (so the worktree is dropped cleanly),
  a dirty worktree is force-removed after the warning, and a failed remote delete leaves the completed local
  cleanup in place and reports it. Each git step is **retried on transient failures** — a worktree file still
  held by an editor or antivirus scan, a racing git ref `.lock`, or a network blip deleting the branch on
  `origin` — a few times with backoff (narrated in the flight log) before it counts; permanent refusals
  (`use --force`, `remote ref does not exist`) still fail fast. Git's worktree commands run with **long-path
  support** (`core.longpaths`) so a deep tree that crosses Windows' 260-character `MAX_PATH` limit (deep
  `node_modules`, generated output) can still be created and removed. If git **still** can't delete the folder,
  Fido **offers to delete it permanently from disk** — a recursive removal that **bypasses the Recycle Bin**
  (using an extended-length path so it isn't stopped by the same limit) — and then prunes git's now-dangling
  worktree registration so the branch can be deleted too. The button is offered **only for a linked worktree on a non-default branch**
  — the clone's main working tree can't be worktree-removed, and `main`/`master` are deliberately never
  offered. Nothing is deleted unless you confirm; Cancel, Enter, and Esc all back out safely, and the
  destructive button is out of the keyboard tab order so it can't be triggered by a stray keypress.

- **Open the folder in a console or the file explorer.** Two new built-in open targets sit alongside the
  editors: **Console** (slug `term`) opens a terminal at the resolved folder, and **File Explorer** (slug
  `files`) reveals it in the OS file manager. Both work on **Windows, macOS, and Linux** — Console finds
  Windows Terminal / PowerShell / `cmd`, macOS **Terminal**, or a Linux terminal emulator
  (`x-terminal-emulator`, `gnome-terminal`, `konsole`, `xterm`, …); File Explorer uses Explorer, Finder
  (`open`), or `xdg-open`. Like every other target they get a **secondary button**, a **Ctrl+1 … Ctrl+9**
  shortcut, and a **command-line slug** — so `fido feature/new-ui term` drops you into a terminal on that
  branch and `fido feature/new-ui files` opens its folder. Both always hand over the **folder** (never a
  `.sln`), and the **terminal program is configurable**: pick the **Console** / **File Explorer** kind for an
  editor row in Settings and set its path (blank = the OS default) to use a specific terminal or file manager.
  Existing configs are migrated forward once on load — Console and File Explorer are **appended** to the
  editor list, preserving your existing order and default.

- **Solution filters (`.slnf`).** Fido now detects Visual Studio **solution filter** files alongside
  `.sln`/`.slnx`, so a filtered subset of a solution shows up in the "which solution?" chooser and can
  be handed straight to the editor (Rider, Visual Studio, etc. open `.slnf` directly). When a filter
  sits beside a same-named full solution, the full `.sln`/`.slnx` still wins as the repository's primary
  target — the filter is offered as an additional choice, never a replacement.

- **Open in WebStorm.** [JetBrains **WebStorm**](https://www.jetbrains.com/webstorm/) is now a built-in
  editor kind (slug `ws`), auto-detected on `PATH`, in `%LOCALAPPDATA%\Programs\WebStorm`, the JetBrains
  **Toolbox** apps/shim, and `Program Files\JetBrains\WebStorm *` (macOS `/Applications`, `~/Applications`,
  Toolbox bundles/shim). Because WebStorm only understands a project folder, it's **folder-only**: Fido
  always hands it the repo folder — ignoring the Solution/Folder toggle and skipping the "which solution?"
  chooser — rather than a `.sln`/`.slnx`. Existing configs are migrated forward once on load: WebStorm is
  **appended** to the editor list (preserving your existing editor order and default), so it appears after
  an upgrade without overwriting your settings.

- **Branch search progress.** When a typed branch isn't checked out anywhere, Fido hunts for it across
  the repos configured for new branches — and now narrates that hunt in the flight log:
  `Searching for local branch in <repo>`, then `Searching for remote branch in <repo>` only when it
  actually reaches out to origin. The repo names tick through in place on a single line (like the close
  countdown) rather than scrolling a line per repo.

- **Pick the editor on the command line.** Each editor now carries a short **slug** (built-in defaults
  `rider`, `vsc`, `vs`, `zed`; editable per editor in Settings). Pass it as the second bare argument —
  `fido feature/new-ui zed` — or explicitly with `--editor` / `-e` (`fido -b feature/new-ui -e vs`) to
  open with that editor instead of the configured default. An unrecognised slug stops with a **No-go**
  that names it and lists the known slugs, rather than silently using the default.

- **Multiple editors / IDEs.** Fido can now open into Rider, **VS Code**, **Visual Studio**,
  **Zed**, or any **custom** editor you point it at. Configure the list in Settings and mark one
  as the **default** — the Open button (and **Enter**) launch into it. Every other editor gets a
  numbered keyboard shortcut (**Ctrl+1 … Ctrl+9**) and a secondary button on the main window, so a
  branch can be opened in whichever editor you want without changing the default. Known editors
  auto-detect (PATH + common install locations) when their path is left blank; a custom editor uses
  the path you give it. An older config's single **Rider path** is migrated onto the Rider editor
  automatically.

- **Close delay** after a successful launch. When Fido is set to close after opening
  (see **Close after opening**), it now counts down before quitting instead of vanishing
  instantly. The flight log shows a single line that ticks down in place (`Closing in 10…` → `9…`
  → `8…`) and a **Keep open** bar appears at the bottom of the window with the live countdown —
  click it to call off the close. Starting another open also cancels it. The delay is configurable in Settings
  (default **10 seconds**; **0** closes immediately), and selecting **Never** turns auto-close
  off entirely.

### Changed

- **MRU suggestions no longer drop down on focus.** The Branch and Solution boxes used to open
  their recently-used list the moment they were focused — so the window started up looking like it
  had a list permanently stuck open. The list now appears only when you start typing or summon it
  with **Ctrl+Space** (when there's history to show). The list also keeps the **10** most recent
  entries per field (was 12).

### Fixed

- **Windows keep the OS system menu, and `Alt+Space` opens it.** Every window now explicitly uses
  the operating system's standard window decorations (`WindowDecorations="Full"`), so the native
  title bar and its **system menu** — Move, Size, Minimize, Maximize, Close — are always present from
  the title-bar icon or a title-bar right-click. The **`Alt+Space`** keyboard shortcut now opens it
  too: Avalonia's Win32 backend swallows that gesture instead of forwarding it to Windows, so Fido
  catches it and drops the menu itself (a no-op on other platforms). Making the decoration setting
  explicit also means a future custom title bar can't silently take the system menu away again. The
  leftover styles for an application-drawn title bar's window-control buttons (never wired up) were
  removed.

- **The chooser dialog is now fully keyboard-driven.** Up/Down arrows move the highlighted
  row, **Enter** opens it, and **Esc** cancels — previously the arrows didn't move the
  selection, so picking a clone / checkout / what-to-open meant reaching for the mouse. A
  shortcut hint runs along the dialog's bottom edge, matching the decision dialog.

- Pressing **Enter** in the Branch (or Solution) box now opens in a single press.
  Previously the first Enter only dismissed the MRU suggestion drop-down, so you
  had to press Enter again to launch. The keystroke now closes the drop-down and
  acts on the entered branch in one go.

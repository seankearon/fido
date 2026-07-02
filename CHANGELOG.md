# Changelog

All notable changes to Fido are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
  (`use --force`, `remote ref does not exist`) still fail fast. The button is offered **only for a linked worktree on a non-default branch**
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

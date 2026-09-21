# Changelog

All notable changes to Fido are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- **A checkout that's behind no longer loses the branch's `.fido/cfg.yaml`.** The in-repo config was read
  only out of the folder in front of you (or off the local branch ref), so a worktree made *before* the
  config landed on the branch had nothing to find — no run menu, no `prefer main clone`, and not a word
  about why. Since a merge or a teammate's push can put the file on a branch long after your worktree was
  made, whether Fido "picked the config up" came down to how fresh your copy happened to be.

  There are now **three copies and a fixed order**, and Fido says which one answered:

  1. **A local edit** — the file in the working tree with changes that aren't committed. What you're
     writing right now still wins outright.
  2. **The copy on `origin`**, whenever it differs from the one this machine has. The flight log says
     `Read from origin/<branch> — the copy here is missing or out of date`, because the tree your commands
     will run in hasn't caught up with the settings offering them.
  3. **The local copy** — the committed file in the tree, or the branch ref's for a branch checked out
     nowhere.

  A copy that **asks for nothing counts as no file at all**, so it never shadows the one below it: the
  starter file the create button writes can't mask the settings the branch really carries. Reads stay on
  **what this machine already has** — the working tree and the tracking refs as last fetched — because a
  scan runs on a keystroke and must never go to the network; fetch and press **Enter** to pick up
  something pushed since. Placing a branch re-reads the tree it just created, which is how a branch this
  clone had **never fetched** turns up with its config in one go rather than needing a second scan.

- **A branch with no in-repo config now says so.** `No .fido/cfg.yaml on 'feature/x' — nothing here, and
  nothing on origin/feature/x as last fetched` — because "looked, found nothing" and "never looked" are
  not the same thing to anyone wondering where their run menu went.

### Added

- **Fido has a console of its own.** The panel at the foot of the main screen now carries two tabs.
  **Flight log** is the narration it always was; **Console** is a real shell at the selected location,
  running inside Fido. Its **Run** menu — at the right of the tab rule, where the log keeps its copy and
  save buttons — leads with a plain **shell here** and then lists whatever the branch's `.fido/cfg.yaml`
  nominated, with **Edit `.fido/cfg.yaml`…** at the foot.

  **A pseudo-terminal, not a pipe**, because the output is the entire point of running something here.
  Everything that checks `isatty` — which is most build tooling — keeps its colour and progress
  rendering; anything that prompts has somewhere to type; **Ctrl+C** reaches the program. And the shell
  stays interactive when the command finishes, so a **script that fails leaves its output on screen with
  a live prompt underneath** rather than taking it away with the exit code.

  Nothing starts until you open the tab — the tab is what asks for a shell, so having it there costs
  nothing. A pick replaces what's running with a fresh shell rather than typing into the live one (the
  folder may have changed with the selection, and a shell mid-command would swallow it), switching to
  the Console tab first if you started from the flight log. Switching tabs doesn't kill it: the pane is
  kept loaded, and its scrollback is the record of what just ran. A run in the tab never triggers
  **close after opening**, however that is set — nothing was handed over, and the output is right here.
  Closing Fido stops the shell rather than orphaning it.

  **The shell is the one you'd expect.** Windows: PowerShell 7 when it's installed, else the in-box
  Windows PowerShell, with `-NoExit` keeping the prompt. macOS and Linux: your login shell from `SHELL`,
  started interactive so your rc file, prompt and aliases are the ones you know. The command conventions
  are shared with the launch path — a `.ps1` to PowerShell, a root `.sh` as `./name` — so a command
  behaves the same whichever console it lands in. One Windows wrinkle is handled here and nowhere else:
  PowerShell won't look in the current directory for something it is asked to run, so a script in the
  tree root is made explicitly relative (`& ./build.ps1`) instead of coming back *"not recognized as the
  name of a cmdlet…"* while you stand in the very folder that holds it.

  **Its colours are the terminal's own**, which is what a console is expected to look like, and
  **Settings → Theme → Colour the Console tab to match** swaps them for Fido's palette — the flight
  log's own greens and ambers, following the theme, with a contrast floor for the 256-colour output no
  palette can speak for. Either way the colours are taken when a shell starts, so one already running
  keeps the ones it came up with.

- **Links in the Console tab open in your browser.** A URL the console prints — a dev server's
  `http://localhost:5173`, the pull-request link `git push` answers with, a CI build's report — is now a
  link you can follow. Hovering one underlines it and turns the pointer into a hand; **Ctrl+Click**
  opens it in your default browser, the gesture every other terminal uses. A plain click still selects
  text, so nothing you could do before has changed meaning.

  Two kinds of link count: text that **reads** as a URL (`http://…`, `https://…`, with any trailing
  `.` or `)` left out of it), and one a program **declares** with an OSC 8 escape — where the text on
  screen says "view the report" and the URL rides along in the escape sequence. Both wrap across lines
  without breaking.

  **Only `http` and `https` go anywhere**, and the flight log names what went: `▸ Opening
  https://… in your browser`. That matters most for the declared kind, which needn't show its target at
  all — the log is where you get to read what your click actually opened. Anything else is refused out
  loud (`⚠ Not opening file:///… — the console only follows http and https links.`), because "open" on
  Windows means handing the string to the shell, and a scheme other than the web's can be a registered
  program rather than a page. A refusal that said nothing would just read as a click that missed.

- **A setting for where a run-menu pick lands.** **Settings → Before running → Run it in Fido's Console
  tab** *(off by default)* decides what the **Console button's** run menu does: hand the command to the
  terminal you have configured, as it always has, or run it in the Console tab. It governs that menu
  only — the Console button itself still opens your terminal, and the Console tab's own Run menu always
  runs in the pane, since driving that pane is what it is for.

- **The branch box now lists the worktree folders that branch already has on disk.** Type a branch and
  the folders holding a worktree for it appear underneath — one row each, with a button to **copy** the
  path and a button to **open it in File Explorer / Finder**. Where to look is the *same rule Fido
  creates worktrees by*, not a second answer to the same question: both go through one `WorktreePath`
  helper, so what is shown and what would be created can't drift apart.

  **A folder counts even when no repo has the branch** — that is the point. A folder sitting at
  `<repo>.worktrees/<branch>` while discovery reports "no working tree or clone has it" is a leftover, or
  a tree since switched to another branch, and it is exactly what you want pointed out when you type the
  name again. There is no git in this check, just a folder. Equally, when the branch has no folder
  anywhere the line **says nothing at all**: it reports what is there, never what could be.

  **How many rows depends on where your worktrees live.** With a **worktree root** configured every
  branch lands under that one root whatever repo it belongs to, so there's at most one row, no repo
  named, answered **as you type** with no scan at all. Without one, a worktree is a sibling of its clone,
  so each clone that has a folder for the branch gets its own row, labelled with the repo. Those clones
  come from the discovery scan and are **branch-independent**, so they're kept after the scan that found
  them and every later branch answers instantly.

  Opening a row whose folder has been deleted since it was drawn falls back to the nearest folder above
  it, with the flight log naming which, rather than failing at a path still on screen. The line only ever
  *reports*: putting a branch on disk is still the **new worktree** card's job.

- **A run command now starts from an up-to-date tree.** Picking a script or `aspire start` from the
  **Console run menu** fast-forwards the target onto `origin` before the console opens, so what runs is
  what the branch actually has. The flight log narrates it (`Pulling origin/feature/x…`) and the console
  follows straight after. It happens at the one point where **every** kind of card has become a folder on
  disk, so a worktree Fido created a second ago takes exactly the same path as one that's been there for
  weeks — which matters, because a new worktree *isn't* automatically current: when the branch already
  exists locally, `git worktree add` checks out that local ref, however far behind it is.

  **It only ever fast-forwards, and it never withholds the console.** A **diverged** branch is refused and
  reported — reconciling one is your call, not Fido's — and the console opens on the tree as it stands. A
  branch that **tracks nothing** (local-only, never pushed) and a **detached HEAD** are skipped with a line
  saying so, not flagged as failures. A **dirty tree** isn't pre-checked: git fast-forwards fine when your
  edits aren't in the way, and refuses with a better message than Fido could write when they are. And an
  **unreachable `origin`** costs you nothing but a log line. Only a *run command* triggers it — opening a
  folder to look at it doesn't move the tree under you — and the new **Settings → Before running** switch
  (**on** by default, and on for configs written before it existed) turns it off.

- **A repo can now configure Fido for itself, in `.fido/cfg.yaml` on the branch.** Commit a **`.fido`**
  folder at the root of your repo and Fido reads its **`cfg.yaml`** from whichever branch a scan just
  found — **before** the checkout options go up — so a solution can say how it prefers to be opened:
  **`prefer main clone`** decides **which checkout Fido offers by default** once it has scanned — the
  clone's own working tree rather than a worktree (or, for a branch checked out nowhere, the *switch the
  main tree* offer); it directs the choice, never the scan, so every location on the branch is still
  found and listed, each one click away. **`commands`** is an ordered list of command lines — whatever
  you'd type in a terminal at that location (`build.ps1`, `aspire start`, `npm run dev`) — and each one
  becomes an entry in a new **drop-down beside the Console button**, taken as written rather than
  resolved as a file name. Picking an entry opens the console at the **selected** location and runs the
  command there — a `.ps1` via PowerShell, a root `.sh` as `./name`, anything else via the
  platform's shell — leaving the window open so you can read the output. On Windows, a console that *is* a
  shell is used exactly as configured, while one that merely **hosts** a shell (Windows Terminal, or a
  third-party emulator) gets the best on the machine: **`pwsh` first**, then Windows PowerShell. A host
  console says nothing about which shell it wants, and the difference is not cosmetic — a tool your
  PowerShell profile puts on `PATH` is "not recognized" under `cmd`, and Windows PowerShell's default
  execution policy refuses `.ps1` files outright. `cmd` is never chosen for you, only honoured when you
  configured it, and a `.ps1` overrides even that. **Nothing ever runs by itself:**
  the menu only offers. The file is read from the working tree when the branch is checked out (so an
  uncommitted edit counts) and straight off the branch — `origin/<branch>` included — when it isn't, so
  it applies to a placement offer too. Keys are forgiving about case, spaces, dashes and underscores;
  comments, quotes and inline lists are understood; and a missing, unreadable or unfamiliar file costs
  only itself — never the scan.

  **Setting a repo up doesn't mean hand-writing YAML:** the **OPEN** strip has a new **document button**
  beside the copy-path icon (and the run menu a matching **Edit `.fido/cfg.yaml`…** row) that creates the
  file in the selected location and opens it in your default tool. What it writes is a **form, not a
  switch**: every setting present at its default, with the tree's own root scripts named in a comment so
  the commands list can be filled in without going looking — so until you edit it, the next scan still
  reads *no in-repo config*. An existing file is only ever **opened, never overwritten**, and Fido does
  **not** stage or commit it: what lands in the repo's history stays your call, as with every other git
  action here. The button is absent for a placement offer — there's no working tree on disk to write
  into until you open it.

- **The window title names the work once discovery resolves it.** With a branch resolved, the title bar —
  and so the taskbar button and `Alt`+`Tab` — reads the selected card's repo and the branch on their own:
  `platform · feature/new-ui`, with no `Fido` in front, so a screenful of Fido windows can finally be told
  apart. The title **follows the selection**, swapping repo names as you pick between the cards of
  a multi-location result, and falls back to plain `Fido` whenever nothing is resolved — before the first
  scan, mid-scan, for a branch found nowhere, or when the branch box is cleared. New **Settings → Window
  title** switch (**on** by default, and on for configs written before it existed) turns the renaming off
  for anyone who wants the title to stay put.

- **A worktree delete that goes part-way now says so honestly — and offers a Retry.** The three things a
  delete removes (the worktree, the local branch, the branch on `origin`) are attempted and reported
  **separately**: a step that fails no longer abandons the ones after it, and the flight log's closing line
  names exactly what went and what didn't. A target that had **already gone** — a branch the server dropped
  on merge, a folder cleared by hand, a worktree git no longer knows about — is reported as **done** rather
  than as an error (*"✓ Removed worktree & branch `feature/x` — origin/feature/x was already gone."*); a `⚠`
  is now spent only on something you asked for that is genuinely **still there**. Previously a
  `git push --delete` that came back with *"remote ref does not exist"* was reported as a failure even
  though the worktree and local branch had been removed cleanly and the branch was, in fact, gone from
  `origin`. Anything that does survive the delete gets an inline **Retry** strip — what's left, git's own
  words for why, and **Retry** / **Dismiss**. Retrying re-runs **only the outstanding step** (a worktree and
  branch that already went are not touched again) and the report that follows covers the whole attempt. The
  strip outlives the card you just deleted; **Esc** or **Dismiss** drops the offer without touching anything
  on disk, and a fresh scan clears it as stale.

- **The flight log grows with the window, and its text can be copied or saved.** Drag Fido taller and
  every spare pixel now goes to the **flight log** instead of to a gap above it — the upper section
  keeps the room its content needs and the log takes the rest; shrink the window and the log falls back
  to its compact box while the upper section scrolls as before. Two new buttons on the **Flight log**
  rule lift the narration out: **copy** puts the whole log (every line, not just the visible ones) on
  the clipboard as plain text, and **save** writes it to a text file you pick, suggesting a dated name
  like `fido-flight-log-20260806-142317.txt`. Both are disabled until there's something to hand over,
  and both confirm themselves in the log.

- **Copy the selected working-tree path to the clipboard.** The OPEN strip now has a small **copy
  button** beside the path, and the ellipsised card and strip paths carry a **tooltip with the full
  path** — so a long worktree path (previously truncated and un-selectable) can be read in full and
  grabbed in one click. The copy is confirmed in the flight log.

- **Optionally delete the remote branch when deleting a worktree.** The inline delete confirm strip
  now offers an opt-in **_Also delete the remote branch `origin/<branch>`_** checkbox — shown only when
  the branch exists on `origin`, and **unticked by default** so the remote is never removed unless you
  ask. **An open pull request blocks it:** when the **GitHub CLI (`gh`)** reports a PR open for the
  branch, the checkbox is disabled and the strip names the PR (`PR #42 · <title>`) with an **Open pull
  request ↗** link to open it in the browser — close or merge it first. PR detection degrades
  gracefully: if `gh` isn't installed, isn't authenticated, or the remote isn't GitHub, the option is
  simply offered without a PR note. The confirmed delete still removes the worktree and the **local**
  branch as before; the flight log notes the origin branch when it was deleted.

- **The header now says which Fido you are running.** The version sits beside the **fido** wordmark in
  small, muted type — `v0.9.3`, on the wordmark's own baseline — so answering *"which build is this?"*
  no longer means going to the installed-programs list or the exe's properties. The number is read from
  the **running assembly** rather than from a file next to it, so it names what is actually running, and
  a source-linked build's `+<commit>` suffix is dropped to keep the badge to a version. An ordinary
  build (the IDE, CI, `dotnet run`) now takes that version from **`ver.txt`** — the same single source a
  release stamps from — instead of carrying the SDK's `1.0.0` placeholder into the header.

### Changed

- **No git command Fido runs can hang the app any more.** Every one is now started with git's own
  prompting disabled (`GIT_TERMINAL_PROMPT=0`, no askpass helper, and `ssh -o BatchMode=yes` unless you've
  set `GIT_SSH_COMMAND` yourself), so a repo git can't authenticate to fails in milliseconds with a message
  in the flight log instead of sitting forever on a prompt that — with the console window hidden and the
  pipes redirected — had nowhere to appear. (A credential helper that shows its **own** window is left
  alone: you can see and answer that one, and disabling helpers would break every private-repo fetch.)
  A **five-minute backstop** catches anything that wedges anyway — set well clear of what a big first fetch
  can legitimately take, since killing a real transfer part-way is worse than waiting for it — and a
  cancelled command is now **killed along with everything it spawned** rather than abandoned: discovery
  re-runs on every keystroke, so a stranded `git` per keystroke was a real prospect.
  This applies to the whole of Fido's git usage, not just the new pre-run fast-forward. Nothing it accepts
  changes — `BatchMode` makes ssh *refuse* where it used to ask, never trust something it wouldn't have.

- **The main screen was redesigned around inline discovery** (per the Claude Design handoff in
  `design/design_handoff_fido_redesign`) — one window, one screen, no auto-popping dialogs:
  - **Discovery is the core loop.** Typing a branch name (debounced, or Enter to fire immediately)
    scans every configured working tree and clone for where that branch is checked out. Results render
    **inline as selectable cards** — each labelled **worktree** or **main clone** (worktrees first, the
    first auto-selected) with its repo, solution count, and last-updated age. The separate "Open from
    branch folder" window and the multi-checkout chooser dialog are gone.
  - **A branch checked out nowhere is offered as inline placement cards.** When no working tree has
    the branch, discovery consults the scanned clones' refs — a local branch, the cached `origin`
    tracking ref, or (as a last resort) one live `ls-remote` sweep, so a branch a teammate or cloud
    session pushed after your last fetch is still found. Each clone that has it offers **two cards**:
    a **new worktree** (leads, auto-selected) showing where the worktree would be created, and a
    **switch clone** that moves the clone's main tree onto the branch — with the old decision dialog's
    dirty-tree warning right on the card when uncommitted changes would ride along. **Opening performs
    the placement first** (fetching and tracking the remote ref when needed) and then launches —
    the decision-dialog flow, minus the dialog. Placement cards preview the clone's solutions as
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

### Fixed

- **An existing worktree outside the search roots is now detected and offered to open.** Discovery
  used to recognise a branch's checkout only where the folder itself sat under a configured search
  root, so a worktree kept in a central directory, nested past the scan depth, or created inside the
  main clone went unseen — and Fido offered to **create** a new worktree instead, which git rejected
  with _"`<branch>` is already used by worktree at …"_. Discovery now asks git itself
  (`git worktree list`) for each scanned clone's worktrees, so any checkout of the branch — wherever
  it lives on disk — is surfaced as an openable **worktree** (or **main clone**) card.

### Removed

- **The chooser, decision, and delete-worktree dialogs** — inline discovery, the target cards, and the
  in-place delete confirm replace all three. Both decision-dialog placement options live on as the
  inline **new worktree** / **switch clone** cards (above). The Settings section for **New-branch
  repos** is gone — placement candidates now come from the clones discovery already scans, so there's
  no separate list to maintain (a configured list is preserved on disk, unused).

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

### Fixed

- **Opening a second tool on a freshly placed branch no longer fails.** Opening a **new worktree**
  placement card created the worktree and launched into it — but the card still read *new worktree*,
  so a second click (open a **Console**, then open **Rider**) tried to `git worktree add` the same
  branch again and failed with *git worktree add failed*, since the branch was now checked out in the
  worktree the first click made. Placing a branch now **converts the card in place** to the real
  checkout it became — a created worktree becomes a deletable **worktree**, a switched clone becomes a
  **main clone** — with its solution chips re-scanned from the tree that now exists. Any further tool
  clicks open that folder directly, no rescan required.

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

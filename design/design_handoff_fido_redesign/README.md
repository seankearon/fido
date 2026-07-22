# Handoff: Fido — main‑screen redesign

## Overview
Fido is an AvaloniaUI desktop launcher. You type a **branch name**, Fido scans the configured repos' working trees and main clones to find where that branch is checked out, and then opens it in an IDE, editor, file explorer, or console — in either a git **worktree** or the **main clone**.

This handoff redesigns Fido's **main window** to fix four UX problems in the current build:

1. **Delete lived in the wrong place.** "Delete worktree & branch" was buried in the *launch* dialog (only reachable after you'd already chosen to open in Rider / File Explorer). It moves to the **main screen**, and is **enabled only when the branch actually exists** (and specifically only when a *worktree* is selected — the main clone can't be deleted).
2. **The open actions were always live and Rider was hard‑promoted.** Now the open actions **stay locked until discovery completes**, and the **default tool is whatever config / the CLI sets** (Rider only by default). No auto‑popping dialog.
3. **The separate "Open from branch folder" window auto‑opened.** Discovery results now render **inline on the main screen** as a selectable list — no second window, no auto‑pop.
4. **Worktree vs. main clone was invisible.** When a branch is checked out in more than one place, each location is now shown, **clearly labelled**, and the user **chooses** which to act on.

Also per request: the **Solution / Folder** segmented toggle is **removed** (behaviour is now inferred per action — see §Interactions), and the **FIDO aviation voice** is kept.

## About the design files
The files in this bundle are **design references authored in HTML** (a streaming "Design Component" prototype). They are **not production code to paste in**. The task is to **re‑create this design and behaviour in the existing AvaloniaUI app** (XAML + C# ViewModels), using the app's established controls, styles, and patterns. Treat the HTML as the source of truth for **look, layout, and behaviour**, and map it onto Avalonia idioms (bindings, `IsEnabled`, `DataTemplate`, `ItemsControl`, commands, etc.).

- `Fido Redesign.dc.html` — the interactive prototype. Open it in a browser (it loads `support.js` from the same folder). Type in the **Branch name** field, or use the **Demo shortcuts** row beneath the window to jump between the states described below. `Ctrl+1…7` trigger the tools; the ⚙ gear sets the default tool.
- `support.js` — runtime required to open the prototype. Not part of the app; ignore for implementation.

## Fidelity
**High‑fidelity.** Final colours, typography, spacing, radii, and interaction states are all specified below and should be reproduced faithfully — subject to matching Avalonia's real window chrome and the app's existing conventions where they diverge from the mock's simulated Windows title bar.

---

## Screen: Fido main window

One window, one screen. Everything happens here now (there is no second dialog). The window's visible content is a single vertical stack:

**Header → Branch/Solution inputs → Discovery results → Open actions → Delete row → Flight log.**

Overall window: **720 px** wide (resizable; content is a single column, so it reflows fine wider — keep the current min width). Warm off‑white canvas. Simulated title bar in the mock is just for context — use Avalonia's actual title bar.

### 1. Header
- **Logo tile**: 52×52, `#1B1712`, radius 13, containing the amber "rising line + arrow" mark (stroke `#F4A62A`). Reuse the app's existing Fido icon.
- **Title** `fido` — 26 px / weight 800, `#211E17`, letter‑spacing −0.5 px.
- **Subtitle** `Flight Dynamics Officer — guiding Rider to a safe landing` — 12.5 px / 400, `#8A8271`. **Keep this copy.**
- **Gear button** (top‑right, 36×36, radius 9, icon `#8A8271`, hover bg `#F1ECE0`). Opens a **settings popover** (see §Interactions → Default tool).

### 2. Inputs (two columns, grid `1.7fr / 1fr`, 16 px gap)
- **Branch name** (left, wider). Label 12 px / 600 `#8A8271`. Input: height 52, radius 10, 1.5 px border `#E2DBCA`, white bg, 15.5 px / 500 text `#211E17`, placeholder `type a branch to discover` (`#B7AF9D`). **Focus:** border `#E3A643` + 3 px ring `rgba(244,166,42,.18)`.
- **Solution · optional** (right). Same input styling. Placeholder `filter solutions`. This is now a **filter** over the discovered solutions (see §Interactions), not a mode switch.

### 3. Discovery results (inline — replaces the old dialog)
Section header row: label `Discovery` (12 px / 600 `#8A8271`), a hairline rule (`#EAE3D3`) filling the middle, and a **status chip** on the right that reflects the phase:
- **idle** → pill `idle`, bg `#F1ECE0`, text `#A79E8B`
- **scanning** → pill with a 12 px spinner + `scanning`, bg `#F3EEE2`, text `#8A8271`
- **found** → pill `✓ N locations`, bg `#FBEFCF`, text `#9A6412`
- **not found** → pill `not found`, bg `#FBECEA`, text `#C0392B`

Body varies by phase:
- **Idle** (empty branch): dashed placeholder card (`#DDD5C2` dashed, radius 11) — *"Enter a branch name above. Fido scans every configured working tree and clone to find where that branch is checked out — nothing opens until it does."*
- **Scanning**: soft card (`#F5F1E8` bg, `#EBE4D4` border) with a 16 px spinner + *"Scanning 24 working tree(s) for '<branch>'…"*.
- **Not found**: warning card (`#FCF2F0` bg, `#F0D6D0` inset border, text `#8A4A40`) — *"No working tree or clone has '<branch>'."* + sub‑line *"Double‑check the name, or fetch the remote first — then try again."*
- **Found**: if there's more than one location, a helper strip (`#FBF3DE` bg, `#8A6A2E` text): *"This branch is checked out in more than one place — choose which to act on:"* — then a **selectable list of target cards**.

**Target card** (one per location; the whole card is the click/keyboard target):
- Card: white, radius 11, 1.5 px inset border `#E5DECE`, padding 14×15. Hover: border `#E3A643` + bg `#FEFCF5`.
- **Selected** state: fill `#FCEFCF` + 1.5 px inset border `#E3A643`, and the trailing radio dot fills (`#E89A1C`). Exactly one card is selected; default = the first (worktrees are listed before the main clone).
- Left **icon tile** 38×38, radius 9:
  - *worktree* → bg `#F1EAD8`, git‑branch glyph stroke `#8A6A2E`
  - *main clone* → bg `#E6EDEF`, home glyph stroke `#4E6A72`
- Middle: **path** (13.5 px / 600 `#211E17`, ellipsised) over **meta** (12 px `#9A9280`, e.g. `platform · 2 solutions · updated 3d ago` for a worktree; `platform · 1 solution · currently on this branch` for the clone).
- Right: a **kind chip** — *worktree* (text `#8A6A2E`, bg `#F0E7D2`) or *main clone* (text `#4E6A72`, bg `#E1EAEC`) — then the **radio indicator** (17 px; selected = amber ring + `#E89A1C` dot, else `#D9D1BE` ring).

### 4. Open actions (locked until discovery succeeds)
Rendered as one block. **When phase ≠ found, the entire block is covered by a translucent lock overlay** (`rgba(251,249,243,.74)`, `not-allowed` cursor) centred with a message that doubles as the reason:
- idle → `🔒 Enter a branch name to begin discovery`
- scanning → `🔒 Scanning… open actions unlock when discovery finishes`
- not found → `🔒 No location found — nothing to open`

*(In Avalonia you don't need the literal overlay — the equivalent is: disable all open commands unless `Phase == Found`, and show the same one‑line reason above the buttons. The overlay is the prototype's way of making the gate obvious; keep the message.)*

When **found**, the block shows, top to bottom:
- **Target/solution context strip** (`#F5F1E8` bg, `#EBE4D4` border, radius 10): top row = `OPEN` label · the selected path in a white chip · the kind (`worktree` / `main clone`). If the selected target contains solution files (`*.sln` / `*.slnx`), a second row appears (divided by a hairline): a caption **`Rider / Visual Studio open`** + a **chip per detected solution** + a **`Folder`** chip. This choice only affects Rider and Visual Studio (see §Interactions); every other tool opens the folder regardless. Selected chip uses the amber tint (`#FBEFCF` / `#E3A643` / `#9A6412`); others white with `#E7DFCB` border. Default = first solution.
- **Hero open button** = the **default tool**. Full width, amber gradient `#F6B23C→#EF9E1C`, radius 10, text `#2C2206`, shadow `0 2px 0 #D5870F, 0 8px 16px -7px rgba(224,133,20,.6)`. Content: `▶` · `Open in <Tool>` (15.5 px / 700) · a small **`default`** pill (`rgba(255,255,255,.55)` bg, `#7A5A10`) · right‑aligned accelerator (e.g. `Ctrl+1`). Hover brightens the gradient; active nudges down 1 px.
- **Tool grid** — the remaining tools, 3 columns, 10 px gap. Each: white, radius 10, 1.5 px inset border `#E5DECE`, name (14 px / 600 `#3A3527`) left, accelerator (11.5 px `#A79E8B`) right. Hover: bg `#FBEFCF` + border `#E3A643` (this is the same pale‑amber highlight the current build uses on hover).
- **If no default tool is set** (config/CLI = none): **no hero button**; all seven tools sit in the equal‑weight grid, with a one‑liner: *"No default tool set — every option is equal weight. Pick one, set a default in ⚙, or pass `--tool` on launch."*

Tools & accelerators (order preserved from current build): **Rider `Ctrl+1`**, WebStorm `Ctrl+2`, VS Code `Ctrl+3`, Visual Studio `Ctrl+4`, Zed `Ctrl+5`, Console `Ctrl+6`, File Explorer `Ctrl+7`.

### 5. Delete row (moved here from the old dialog)
Separated by a top hairline (`#EDE7D8`). Visible only when phase == found. Three states:
- **Deletable** (selected target is a **worktree**): red‑outline button `🗑 Delete worktree & branch` — bg `#FCF1EF`, 1.5 px inset border `#E6B7B0`, text `#BC392C` (13.5 px / 600). Hover bg `#FAE7E3` / border `#D98A80`.
- **Not deletable** (selected target is the **main clone**): the same button rendered **disabled** (grey `#BCB4A2` on `#F4F1EA`) with an inline note: *"only worktrees can be deleted — the main clone stays put."*
- **Confirming** (after a click): the button is replaced **in place** by a confirm strip (`#FCF1EF` bg, `#E6B7B0` inset border) that spells out exactly what happens — *"Remove the worktree at **<path>** and delete local branch **<branch>**? This can't be undone."* — with **Cancel** (white, `#E1D7C6` border) and **Delete** (solid `#C0392B`, hover `#AC2E22`, white text). Two‑step, no separate modal.

### 6. Flight log
Section label `Flight log` + hairline. Console panel: `#F5F1E8` bg, `#EBE4D4` border, radius 10, min‑height 104 / max‑height 150, scrolls, auto‑scrolls to newest line. Monospace 13.5 px / line‑height 1.7. Line kinds carry colour (keep the voice):
- accent `#DE7F17` (600) — e.g. `🚀 Going around the horn…`
- muted `#9A9280` (400) — e.g. `Scanning 24 working tree(s) for branch '…'…`
- ok `#3E7C55` (600) — e.g. `✓ Found 2 location(s) for '…'.`
- warn `#C0392B` (600) — e.g. `⚠ No working tree or clone has '…'.`
- plain `#57513F` (500) — action lines like `▸ Opening Platform.sln in Rider`

---

## Interactions & behaviour

**Discovery (the core loop).**
- Trigger a scan when the branch text changes (debounce ~600 ms) **and** on `Enter` (fire immediately). The prototype uses a ~1.05 s simulated scan; use the real scan.
- Phase machine: `Idle → Scanning → (Found | NotFound)`. Empty branch ⇒ `Idle`.
- On a fresh scan, reset the Flight log to the two "going around the horn / scanning N trees" lines, then append the found/​not‑found result line.
- **Open actions are enabled iff `Phase == Found`.** This is the headline change — nothing opens before discovery resolves.

**Selection.** First target is auto‑selected (worktrees before clone). Selecting a card sets it as the target for every open/delete action and resets the solution selection to that target's first solution.

**Open behaviour (replaces the Solution/Folder toggle).**
- **Rider & Visual Studio** detect solution files (**`*.sln`** and **`*.slnx`**) in the selected target and open the **chosen solution**, or the **folder** if the user picks the `Folder` chip (also the default when no solution files are found).
- **WebStorm, VS Code, Zed, Console, and File Explorer** always open the **folder** — they have no solution concept and ignore the solution selection entirely.
- The **Solution · optional** field filters which detected solutions appear as chips (blank = show every `*.sln`/`*.slnx` found in the target).

**Default tool / hero (config + CLI).**
- The **default tool** decides the hero button. Source of truth: persisted config, overridable per‑run by a CLI flag (`--tool rider|webstorm|vscode|vs|zed|console|explorer`).
- The ⚙ **settings popover** lets the user pick the default (radio list incl. **No default (equal weight)**) and states: *"A launch flag like `--tool rider` overrides this for a single run."*
- `--tool none` (or unset config with none chosen) ⇒ no hero, equal‑weight grid.

**Delete.** Enabled only when the selected target is a worktree in a found branch. Two‑step inline confirm (above). On confirm: remove that worktree + delete the local branch, drop it from the results, re‑select the next target (or fall to `NotFound` if none remain), and log `🗑 Deleting…` then `✓ Removed worktree & branch '<branch>'`.

**Keyboard.** `Ctrl+1…7` invoke the corresponding tool (respecting the found‑gate). `Esc` cancels a pending delete confirm, then closes the settings popover. `Enter` in the branch field forces an immediate scan.

**Command‑line launch.** Support prefill + optional auto‑run: `--branch <name>` prefills and scans; `--solution <name>` prefills the filter; `--tool <id>` sets the run's default. If a tool is specified on launch, it's reasonable to auto‑open **once discovery completes and finds exactly one location** — but **never** auto‑open a disambiguation UI when there are multiple locations (that's exactly the auto‑pop behaviour we're removing); present the choice instead.

## State management (maps to the main ViewModel)
- `BranchName : string` (two‑way, debounced) → triggers discovery.
- `SolutionFilter : string` (two‑way) → filters solution chips.
- `Phase : enum { Idle, Scanning, Found, NotFound }` → drives status chip, results body, the open‑actions lock, and delete visibility.
- `Targets : IReadOnlyList<Target>` where `Target { TargetKind Kind (Worktree|MainClone), string Path, string Repo, IReadOnlyList<string> Solutions /* *.sln + *.slnx */ }`.
- Solution files are discovered per target by globbing `*.sln` and `*.slnx`; the list is only consulted when the chosen tool is Rider or Visual Studio.
- `SelectedTarget : Target?` (default first).
- `SelectedSolution : string?` (null ⇒ "Folder"; default = SelectedTarget.Solutions[0]; only consulted for Rider & Visual Studio).
- `DefaultTool : ToolId?` (from config, CLI‑overridden; null ⇒ equal grid).
- `IsConfirmingDelete : bool`.
- `IsSettingsOpen : bool`.
- `Log : ObservableCollection<LogLine{ Kind, Text }>`.
- Derived: `CanOpen = Phase == Found`; `CanDelete = Phase == Found && SelectedTarget?.Kind == Worktree`; `HeroTool = DefaultTool`; `GridTools = AllTools minus HeroTool`.

## Design tokens
**Colour**
- Canvas `#FBF9F3`; soft panel `#F5F1E8`; panel border `#EBE4D4`; hairline/divider `#EAE3D3`.
- Text: primary `#211E17`; secondary/label `#8A8271`; muted/hint `#A79E8B`; placeholder `#B7AF9D`; log body `#57513F`.
- Borders: default `#E2DBCA`; soft inset `#E5DECE`; dashed `#DDD5C2`.
- Amber primary: gradient `#F6B23C → #EF9E1C` (hover `#F8BA4D → #F2A526`); on‑amber text `#2C2206`; shadow accent `#D5870F` + `rgba(224,133,20,.6)`.
- Amber selected/tint: fill `#FCEFCF`; border/focus `#E3A643`; focus ring `rgba(244,166,42,.18)`; radio dot `#E89A1C`; deep accent (log/hero pill) `#DE7F17` / `#7A5A10` / `#9A6412`.
- Worktree accents: text `#8A6A2E`, chip bg `#F0E7D2`, icon tile `#F1EAD8`.
- Main‑clone accents: text `#4E6A72`, chip bg `#E1EAEC`, icon tile `#E6EDEF`.
- Danger: text `#BC392C`; outline border `#E6B7B0` (hover `#D98A80`); soft bg `#FCF1EF` (hover `#FAE7E3`); solid `#C0392B` (hover `#AC2E22`); confirm text `#7A4038`, emphasis `#9A2C20`.
- Not‑found card: bg `#FCF2F0`, border `#F0D6D0`, text `#8A4A40`, sub `#B08A82`.
- Log kinds: accent `#DE7F17`, ok `#3E7C55`, warn `#C0392B`, muted `#9A9280`, plain `#57513F`.
- Logo tile `#1B1712`; brand mark stroke `#F4A62A`.

**Type** — JetBrains Mono throughout (the current build uses a friendly coding monospace; JetBrains Mono is the closest freely‑licensed match — substitute the app's existing mono if it already ships one). Scale: title 26/800; subtitle 12.5/400; field label 12/600 (.03em); section label 11/700 (.06em, upper); input 15.5/500; target path 13.5/600; meta 12/400; hero label 15.5/700; tool name 14/600; accelerator 11.5/500; chip 11–12.5/600; log 13.5/400–600 (lh 1.7).

**Radius** — window 12; cards/inputs 10–11; buttons 9–10; chips 6–8; pills 999; icon tiles 9–13; radios 50%.

**Spacing** — content padding 26 / 30 / 24; major section gap 22; label→field 8; input→results 12; two‑col grid gap 16; tool grid gap 10; card padding 14×15.

**Shadow** — window `0 24px 60px -20px rgba(35,28,14,.45), 0 6px 18px -8px rgba(35,28,14,.22)`; hero `0 2px 0 #D5870F, 0 8px 16px -7px rgba(224,133,20,.6)`; popover `0 16px 36px -12px rgba(35,28,14,.4)`.

## Assets
- **Font:** JetBrains Mono (or the app's existing monospace). No other font.
- **Icons:** all inline vector, no image files — brand mark (line+arrow), gear, git‑branch (worktree), home (main clone), spinner. Reproduce with the app's icon system or simple `Path` geometry; hex values above.
- **Emoji** used in log/labels: 🚀 🔒 🗑 ⚠ ✓ ▸ ▶ — the FIDO voice depends on them; keep or map to the app's glyphs.

## Files
- `Fido Redesign.dc.html` — the hi‑fi interactive prototype (all states; use the Demo shortcuts row to walk them).
- `support.js` — runtime to open the prototype locally (not for implementation).

*Everything needed to implement is in this README; the HTML is the visual/behavioural reference.*

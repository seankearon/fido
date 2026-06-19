# Handoff: Fido — Mission-Control Console UI

## Overview
Fido is a desktop **git worktree launcher** (Windows, opens branches in JetBrains Rider). You type a branch name; Fido finds its working tree(s) on disk and opens the matching solution/folder in Rider. This package redesigns the existing default-Windows-form UI into a cohesive **mission-control console** that matches the Fido brand (the app is named after the Apollo Flight Dynamics Officer).

Four screens are covered, in **two themes** (dark = primary, light):
1. Main window
2. Main window with Settings expanded
3. Dialog — "Open from branch folder" (choose which solution to open)
4. Dialog — "Branch checked out in multiple places" (choose which worktree)

## About the Design Files
The files in this bundle are **design references created in HTML** — prototypes showing the intended look and layout, **not production code to copy directly**. The task is to **recreate these designs in the Fido app's existing environment** using its established UI stack and patterns. Fido appears to be a native Windows app (WinForms/WinUI/WPF or similar); implement the redesign with that toolkit's native controls and theming — do **not** ship the HTML. If the relevant view layer is being rebuilt, pick the framework that best fits the existing codebase and implement there.

The `.dc.html` files are interactive component files; opening one requires the sibling `support.js` runtime (included). Open `Fido App Redesign.dc.html` in a browser to view all four screens in both themes side-by-side.

## Fidelity
**High-fidelity (hifi).** Final colors, typography, spacing, and component states are specified below with exact values. Recreate pixel-faithfully using the codebase's native controls, then map the tokens to the app's theme system so dark/light switch cleanly.

---

## Design Tokens

### Color — Dark theme (primary)
| Token | Hex | Use |
|---|---|---|
| `bg/window` | `#0c0d10` | window background |
| `bg/titlebar` | `#15161b` | title bar |
| `bg/input` | `#101116` | text fields, segmented control, textarea |
| `bg/log` | `#08090c` | flight-log panel |
| `border/window` | `#26272e` | window border |
| `border/subtle` | `#23242b` / `#2a2b33` | titlebar divider / input border |
| `border/log` | `#1c1d24` | log panel border |
| `text/primary` | `#f2f1ec` | wordmark, primary values |
| `text/body` | `#e9e7e1` | input text |
| `text/secondary` | `#c9cbd2` | log values, titlebar label |
| `text/muted` | `#8b8d96` / `#9a9ca6` | labels-inline, scanning lines |
| `text/label` | `#6e7079` | uppercase section labels |
| `text/dim` | `#4f5159` / `#5b5d66` | "· optional" hints, placeholders |
| `accent/amber` | `#FFB02E` | brand accent — focus, active, primary fill, glyph, links |
| `accent/amber-text-on-dark` | `#FFB02E` | hashes, branch names in copy |
| `accent/amber-tint` | `rgba(255,176,46,0.16)` | active segmented bg, selected card bg |
| `accent/amber-tint-border` | `rgba(255,176,46,0.5)` | selected card border |
| `status/no-go` | `#ff5a52` (dot/bar), `#ff7a73` (text) | NO-GO status + error log line |
| `status/no-go-bg` | `rgba(255,90,82,0.09)` | NO-GO strip background |
| `status/go` | `#36E27E` | GO / HEAD indicator dot |

### Color — Light theme
| Token | Hex | Use |
|---|---|---|
| `bg/window` | `#ffffff` | window background |
| `bg/titlebar` | `#f3f1ea` | title bar (cream) |
| `bg/input` | `#ffffff` | text fields |
| `bg/panel` | `#f1efe9` | segmented control track |
| `bg/log` | `#f6f5f2` | flight-log panel |
| `border/window` | `#e0ded7` | window border |
| `border/subtle` | `#e6e3db` / `#d8d5cc` | dividers / input border |
| `text/primary` | `#1a1916` | wordmark, primary values |
| `text/body` | `#2a2823` | input text, card values |
| `text/muted` | `#8a877f` | labels-inline, subpaths, scanning lines |
| `text/label` | `#94917f` | uppercase section labels |
| `text/dim` | `#b8b5ab` / `#a8a59d` | hints / placeholders |
| `accent/amber` | `#FFB02E` | primary button fill (keeps dark text) |
| `accent/amber-glyph` | `#D98A00` | titlebar logo glyph (deeper for contrast) |
| `accent/amber-text` | `#C77A00` | links, hashes, branch names, prompt `❯` |
| `accent/amber-strong` | `#9a6800` | active segmented text, "Save settings" text |
| `accent/amber-tint` | `rgba(255,176,46,0.16)` | selected card bg |
| `accent/amber-tint-border` | `#E0A53A` | selected card / focus border |
| `accent/amber-focus-ring` | `rgba(255,176,46,0.22)` | input focus glow |
| `status/no-go` | `#d6453d` (bar/dot), `#c2362f` (text) | NO-GO + error log line |
| `status/no-go-bg` | `rgba(214,69,61,0.09)` | NO-GO strip background |
| `status/go` | `#2EA866` | HEAD indicator dot |
| `status/stale` | `#b3b0a8` | "behind" indicator dot |

### Primary button (both themes)
Fill `#FFB02E`, text `#1c1400`, weight 700. Amber works on both backgrounds; text stays near-black.

### Typography
- **UI / data / log / inputs:** `JetBrains Mono` (weights 400, 500, 600, 700, 800). This is the dominant typeface — labels, values, buttons, log.
- **Incidental body copy** (the intro/tagline only): `Inter`. The app itself can use the system UI font here; everything functional is mono.
- Scale:
  - Wordmark (header): 22px / 800 / letter-spacing −0.02em
  - Window title (titlebar): 12.5px / 600
  - Section label (uppercase): 11px / letter-spacing 0.16em / uppercase
  - Input text: 13.5px / 400
  - Button: 14.5px / 700 (primary), 13px / 600 (secondary)
  - Segmented option: 13px
  - Status text: 12.5px
  - Log: 12.5px / line-height 1.85
  - Card title: 14px / 600–700; card subpath: 11.5px

### Spacing / radius / shadow
- Window radius **10px**; input/button radius **7–8px**; cards/log radius **8px**; status dot square radius **2px**.
- Window body padding **24px 26px**; dialog body padding **20px 22px**; dialog footer padding **14px 22px**.
- Field group bottom margin **18–20px**.
- Titlebar height **42px**; window control hit area **42×42px**.
- Window shadow: dark `0 24px 60px rgba(0,0,0,.34)`; light `0 24px 60px rgba(60,55,45,.16)`.
- Input focus ring: `box-shadow: 0 0 0 3px <focus-ring>` + accent border.

---

## Screens / Views

### 1. Main window
**Purpose:** Enter a branch (and optionally a solution), choose Solution/Folder, launch in Rider. Status + log report the result.

**Layout (top → bottom), single column inside the window body:**
1. **Title bar** (42px): left = logo glyph (15px) + `fido` label; right = minimize / maximize / close controls (42×42 each, line icons).
2. **Header row:** app icon (38px rounded-square, see Assets) + wordmark `fido` (22/800) with tagline beneath: *"Flight Dynamics Officer — branch in, trajectory on disk out"* (11.5px muted).
3. **Branch name** field — label above, mono input. Shown in **focused** state: accent border + focus ring, a leading amber `❯` prompt glyph, and a 2px amber text-caret at the end. Value: `claude/optimistic-franklin-1hu89u`.
4. **Solution name · optional** field — placeholder: *"blank → find the branch's folder and list its solutions"*.
5. **Open as** row — inline label + **segmented control** (two options `Solution` | `Folder`); `Solution` active (amber tint bg, amber text), `Folder` inactive (muted).
6. **Primary button** (full width) — amber fill, dark text, leading ▶ play/launch triangle: *"Open in Rider"*.
7. **Settings disclosure** — full-width row, bordered, gear icon + *"Settings"* + chevron (down when collapsed). Click toggles screen 2.
8. **Status strip** — appears after an action. Left amber/red 2px bar + colored square dot + text. Example (NO-GO): **NO-GO** · no checkout of branch '`claude/optimistic-franklin-1hu89u`' found. (branch name rendered in amber.)
9. **Flight log** — section label *"Flight log"* with a thin rule, then a panel. Lines, color-coded:
   - `🚀 Going around the horn…` — amber
   - `Scanning 25 working tree(s) for branch '…'…` — muted (branch in secondary color)
   - `Found 0 folder(s) on '…'.` — muted (number in secondary)
   - `[✗] No working tree on '…' under the search roots.` — error color

### 2. Main window — Settings expanded
Same window; the Settings disclosure is **open** (chevron up, label in amber) and reveals a panel (replacing status/log focus while open):
- **Search roots · one per line** — multiline textarea, height ~88px, value:
  ```
  D:\myapp
  D:\main
  ```
- **Rider path · blank = auto-detect** — input, placeholder *"auto-detect"*.
- **Worktree root · blank = sibling ".worktrees"** — input, placeholder *"sibling .worktrees folder"*.
- **Save settings** — secondary button (amber outline + faint amber tint fill, amber text).

### 3. Dialog — "Open from branch folder"
**Purpose:** When a branch resolves to a folder containing multiple solutions, choose what to open.
- Title bar: logo glyph + title *"Open from branch folder"* + close.
- Prompt: *"Found 4 solution(s). Choose what to open:"* (the count `4` in amber).
- **Selectable list** (8px gap). Each row: solution/folder icon + name (mono) + subpath beneath (muted).
  - `apps.slnx` — *repo root* — **selected** (amber tint bg + amber border, amber icon)
  - `myapp-hitec.slnx` — *hitec\src*
  - `web.slnx` — *project\src\web*
  - `spectrum.slnx` — *spectrum*
  - `Open this folder in Rider` — *D:\myapp\apps-worktrees\claude-nifty-sagan-rbnrpg* — **dashed border** (distinct "fallback action" row), `+` icon.
- Footer (right-aligned): **Cancel** (ghost/outline) + **Open** (amber primary).

### 4. Dialog — "Branch checked out in multiple places"
**Purpose:** When a branch is checked out in more than one worktree, choose which to open.
- Title bar: glyph + title *"Branch checked out in multiple places"* + close.
- Prompt: *"'claude/nifty-sagan-rbnrpg' is checked out in more than one folder. Choose which to open:"* (branch in amber).
- **Selectable list.** Each row: full path (mono) + a meta line beneath = status dot + short commit hash (amber) + state label:
  - `D:\myapp\apps-worktrees\claude-nifty-sagan-rbnrpg` — green dot · `3e84176ec` · **HEAD** — **selected**
  - `D:\main\apps.worktrees\claude-nifty-sagan-rbnrpg` — gray dot · `fe69fc01b` · **behind 2**
- Footer: **Cancel** + **Open** (amber primary).

> NOTE — embellishments to confirm against real data: the **HEAD / behind N** state labels and the green/gray status dots, and the folder/disclosure/gear glyph choices, are design additions. Keep them only if Fido actually has that data; otherwise drop to just the path + hash.

---

## Interactions & Behavior
- **Branch name** is the primary input and gets autofocus; show the amber prompt `❯` + caret only on focus.
- **Open as** segmented control: single-select, toggles Solution vs Folder.
- **Open in Rider**: validates, runs the worktree search, streams lines into the flight log, then sets the status strip to GO (green) or NO-GO (red). On a single unambiguous match → opens Rider directly; on multiple solutions → screen 3; on multiple checkouts → screen 4.
- **Settings disclosure**: expand/collapse with chevron flip; "Save settings" persists and collapses.
- **Dialogs**: list is single-select (selected row = amber tint + border); double-click a row or click **Open** confirms; **Cancel**/close dismisses. Default selection = first row.
- **Theme**: support dark (default) and light via the token tables above — map every literal to a theme variable; never hardcode per-theme.
- Status strip and log are **append/replace on action**, not persistent chrome.

## State Management
- `branchName: string`, `solutionName: string | null`
- `openAs: 'solution' | 'folder'`
- `settingsOpen: boolean`; settings: `searchRoots: string[]`, `riderPath: string | null` (null = auto-detect), `worktreeRoot: string | null` (null = sibling `.worktrees`)
- `status: { kind: 'go' | 'no-go' | 'idle', message: string }`
- `log: LogLine[]` where `LogLine = { text: string, level: 'info' | 'accent' | 'muted' | 'error' }`
- Dialog state: `solutionChoices: {name, subpath, kind}[]` + `selectedIndex`; `checkoutChoices: {path, hash, state}[]` + `selectedIndex`
- `theme: 'dark' | 'light'`
- Data: enumerate worktrees under `searchRoots` for the branch; resolve solutions within a matched folder; detect Rider path.

## Assets
- **App icon** — `assets/fido-icon.svg` (master) + PNGs `assets/png/fido-icon-{1024,512,256,128,64,32,16}.png`. Rounded-square dark tile (`#0c0d10`, radius 27/120) with an amber "plotboard": faint inner frame + axes + a rising ascent curve + an end node. Use as the app/dock/taskbar icon and the in-window header logo.
- **Glyph (mark only)** — `assets/fido-glyph.svg` — single-color (`currentColor`), transparent; the ascent curve + node, no tile. Use for the title-bar logo and any monochrome/small contexts (deeper `#D98A00` on light, `#FFB02E` on dark).
- **Lockup** — `assets/fido-lockup.svg` — icon + `fido` wordmark + tagline.
- Inline UI icons (window controls, gear, folder, `+`, play triangle, chevron) are simple strokes — reimplement with the toolkit's icon set; specs are in the screen descriptions.
- No third-party brand assets are used.

## Files
- `Fido App Redesign.dc.html` — all four screens, both themes (open in a browser to view; needs `support.js`).
- `support.js` — runtime required to render the `.dc.html`.
- `Fido Logo Final.dc.html` — logo presentation (icon variants, scale ladder, dock mock) for reference.
- `assets/` — exported icon/glyph/lockup SVGs and icon PNGs.

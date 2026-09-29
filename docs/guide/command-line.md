---
icon: lucide/terminal
description: Pre-fill the form and auto-open from the command line.
---

# Command line

## Command-line launch

Launch arguments pre-populate the form, and **supplying a branch starts discovery
immediately** — exactly as if you'd typed it. Opening, though, stays deliberate:

| Argument | Effect |
| --- | --- |
| `<name>` (bare, first) or `--branch` / `-b` `<name>` | Set the branch — **discovery runs straight away** |
| `<tool>` (bare, second) or `--tool` / `-t` `<id>` (also `--editor` / `-e`) | The named tool becomes **this run's default** (the hero button) — and **auto-opens** when discovery finds **exactly one** location |
| `--solution` / `-s` `<name>` | Pre-fill the **solution filter** |
| `--folder` | Start on the **Folder** chip instead of the first solution |
| `--new-window` / `-n` | Open a window of its own even when another already has the branch — see [below](#a-window-that-already-has-the-branch) |

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

## A window that already has the branch

When a Fido window is **already open on the branch** you name, `fido <branch>` **switches to that window**
instead of opening a second one. The window comes to the front (restored first, if it was minimised), the
flight log notes that it was called up, and the command returns straight away without a window of its own.
Should that window be counting down to an [auto-close](../reference/settings.md#in-the-app), the countdown is called off —
you've just asked to see it.

A window counts as having the branch when its **branch box** holds exactly that name (git branch names are
case-sensitive, so `Feature/X` isn't `feature/x`). What the window already shows is left as it is — a bare
branch only asks to see it. A command line that asks for **more** than the branch — a tool, `-s`, or
`--folder` — is run **in that window** just as a new one would have run it: `fido feature/new-ui rider` re-scans
the branch there and opens Rider if the branch is in exactly one place.

- **Another branch** — or no window having this one — opens a new window as usual.
- **`--new-window`** (or `-n`) always opens a new window, for that one launch.
- **Settings → Command line → _Switch to a window that already has the branch_** *(default on)* turns the
  switch off altogether: every launch opens its own window, as it did before. It's read as each launch starts,
  so a change applies from the next `fido` you run — no restart needed.
- If anything gets in the way — a window that doesn't answer in time, say — Fido opens a new window, rather
  than leaving you with none.

It works across every Fido window you have open, on Windows, macOS and Linux, and only among your own: each
window listens on a pipe that only your user account can reach.


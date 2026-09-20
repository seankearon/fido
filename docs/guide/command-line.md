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

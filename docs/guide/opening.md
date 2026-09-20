---
icon: lucide/external-link
description: Choosing the target, what gets opened, and the default tool.
---

# Opening a target

## Choosing the target

The selected card is the target for **every** action — the hero button, the tool grid,
the keyboard accelerators, and the delete row. Selecting a different card:

- updates the **context strip** (the `OPEN` row showing the target path and its kind),
- rebuilds the **solution chips** from that target's detected solutions, and
- cancels any pending delete confirmation.

Exactly one card is always selected; the first (a worktree, when there is one) is
selected automatically when a scan lands — unless the branch's own
[`.fido/cfg.yaml`](in-repo-config.md#in-repo-config-fidocfgyaml) asks for the **main clone**.

## What gets opened: solution or folder

- The context strip lists a **chip per detected solution** (`.sln`/`.slnx`/`.slnf`)
  in the selected target, plus a **Folder** chip. The first solution is pre-selected;
  pick **Folder** to open the working tree itself.
- **Only Rider and Visual Studio consult the chips** — they open the chosen solution
  (or the folder, when Folder is chosen or no solutions were found).
- **Every other tool opens the folder** — WebStorm, VS Code, Zed, Console, and File
  Explorer have no solution concept and ignore the chips entirely.
- The **Solution** box **filters** the chips by file name (blank = show every solution
  found). When the filter hides them all, only the Folder chip remains.

## Open actions & the default tool

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


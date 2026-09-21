---
icon: lucide/image
description: A visual tour of Fido, in whichever theme you are reading in.
---

# Screenshots

A visual tour of **Fido**. Each shot follows the theme you are reading in — switch the
palette in the header and the screenshots switch with you. See
[The main screen](guide/interface.md) and [The Console tab](guide/console.md) for the full
behaviour reference.

---

## Home screen

Mission control — one window, one screen. Type a **branch name** and Fido scans your
search roots for every working tree currently on it; the results render **inline** as selectable
cards. The **open actions** below stay locked until discovery finds the branch, then the big amber
button launches your **default tool** (the rest sit in the grid, ++ctrl+1++ … ++ctrl+9++). The
panel at the foot carries two tabs: the **Flight log**, reporting each step as Fido "goes around
the horn", and the **Console**.

![Fido home screen](assets/screenshots/home-screen-light.png#only-light)
![Fido home screen](assets/screenshots/home-screen-dark.png#only-dark)

---

## Discovery results

When a branch is checked out in more than one place, every location gets its own card — labelled
**worktree** or **main clone**, worktrees first — and you choose which to act on. The context strip
shows the selected target with a **chip per detected solution** (plus **Folder**); Rider and Visual
Studio open the chosen chip, every other tool opens the folder. The **Solution** box filters the
chips.

![Inline discovery results with target cards](assets/screenshots/open-dialog-light.png#only-light)
![Inline discovery results with target cards](assets/screenshots/open-dialog-dark.png#only-dark)

---

## Placing a branch that isn't checked out anywhere

When no working tree has the branch but a scanned clone's refs do (locally, on `origin`, or found by
a live `ls-remote` for a branch you never fetched), each such clone offers **two placement cards**:
a **new worktree** (leads, auto-selected) showing where it would be created, and a **switch clone**
that moves the clone's main tree onto the branch — warning inline when uncommitted changes would
ride along. Opening performs the placement first, then launches.

![New-worktree and switch-clone placement cards](assets/screenshots/placement-offer-light.png#only-light)
![New-worktree and switch-clone placement cards](assets/screenshots/placement-offer-dark.png#only-dark)

---

## Delete a worktree

When the selected card is a **linked worktree** on a non-default branch, the **Delete worktree &
branch** button beneath the tools is live (selecting the main clone — or a default branch —
disables it with a note saying why).

![Delete row beneath the open actions](assets/screenshots/open-dialog-delete-light.png#only-light)
![Delete row beneath the open actions](assets/screenshots/open-dialog-delete-dark.png#only-dark)

Clicking it swaps the button for an **inline confirm strip** that spells out exactly what happens —
remove the worktree and delete the **local** branch (the branch on `origin` is never touched) —
with warnings when the worktree has uncommitted changes or commits that exist only on that branch.
**Cancel** or ++esc++ backs out; only the explicit **Delete** click confirms.

![Inline delete confirm strip](assets/screenshots/delete-worktree-dialog-light.png#only-light)
![Inline delete confirm strip](assets/screenshots/delete-worktree-dialog-dark.png#only-dark)

---

## The Console tab

The second tab under the open actions is **Fido's own terminal**, at the selected location — a real
shell over a real pseudo-terminal, so colour, prompts and ++ctrl+c++ all work and a failed script
leaves its output on screen with a live prompt underneath. It is not the **Console tool**, which
opens *your* terminal at the folder; see [The Console tab](guide/console.md) for the difference.

![The Console tab running the branch's build script](assets/screenshots/console-tab-light.png#only-light)
![The Console tab running the branch's build script](assets/screenshots/console-tab-dark.png#only-dark)

The **Run** menu at the right of the tab rule leads with a plain **shell here**, then lists what
the branch's `.fido/cfg.yaml` nominated — its run files and `aspire start` — with
**Edit `.fido/cfg.yaml`…** at the foot. Every pick runs in the pane below it.

![The Console tab's Run menu](assets/screenshots/console-run-menu-light.png#only-light)
![The Console tab's Run menu](assets/screenshots/console-run-menu-dark.png#only-dark)

The console keeps the terminal's own colours by default. Tick **Settings → Theme → Colour the
Console tab to match** and it takes Fido's palette instead — the flight log's own greens and ambers,
following the theme.

![The Console tab in Fido's own palette](assets/screenshots/console-palette-light.png#only-light)
![The Console tab in Fido's own palette](assets/screenshots/console-palette-dark.png#only-dark)

---

## Settings

The ⚙ gear popover picks the **default tool** (or **No default** for the equal-weight grid);
**All settings…** opens the full dialog to configure **search roots**, your **editors** (each with
a CLI slug, with the default marked **●**), the **worktree root**, **theme**, what the **window
title** reads, what happens **before running** a command — fast-forward the target, and whether a
run-menu pick lands in [Fido's Console tab](guide/console.md) or your own terminal — and the
**close-after-opening** behaviour and delay.

![Fido settings dialog](assets/screenshots/settings-dialog-light.png#only-light)
![Fido settings dialog](assets/screenshots/settings-dialog-dark.png#only-dark)

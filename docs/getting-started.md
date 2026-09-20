---
icon: lucide/rocket
description: What Fido does, and the discovery loop it runs on.
---

# Getting started

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
chips (see [What gets opened](guide/opening.md#what-gets-opened-solution-or-folder)) — it's no longer a
search input or a mode switch.


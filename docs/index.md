---
template: home.html
title: Fido — a launch manager for your IDE
description: Give Fido a branch name and it finds every worktree and clone on it, then opens your pick in Rider, WebStorm, VS Code, Visual Studio or Zed.
hide:
  - navigation
  - toc
  - path
  - footer
---

## What it does { .fido-section-title }

<div class="grid cards" markdown>

-   :material-magnify:{ .lg .middle } **Finds the branch, everywhere it lives**

    ---

    Git enforces one worktree per branch only *within* a clone. Fido scans every
    search root and shows each location as its own card — worktree or main clone,
    labelled and ordered — so duplicates are visible instead of guessed at.

    [:octicons-arrow-right-24: Discovery](guide/discovery.md)

-   :material-open-in-new:{ .lg .middle } **Opens it where you want**

    ---

    A default tool on the big button, every other tool one ++ctrl+1++ … ++ctrl+9++
    away. Solution chips for `.sln`, `.slnx` and `.slnf`, or hand over the folder —
    plus a terminal and your file explorer.

    [:octicons-arrow-right-24: Opening a target](guide/opening.md)

-   :material-file-cog:{ .lg .middle } **Configured by the repo itself**

    ---

    Commit a `.fido/cfg.yaml` and Fido reads it *off the branch* — your uncommitted
    edit first, then `origin`, then this machine. A worktree made before the config
    landed still gets it.

    [:octicons-arrow-right-24: In-repo config](guide/in-repo-config.md)

-   :material-delete-outline:{ .lg .middle } **Cleans up after you**

    ---

    Finished with a branch? Delete its worktree and local branch — and optionally the
    remote, unless an open PR says otherwise — from the same screen, behind an inline
    confirm that spells out exactly what goes.

    [:octicons-arrow-right-24: Deleting a worktree](guide/worktrees.md)

-   :material-console:{ .lg .middle } **Starts from the command line**

    ---

    `fido feature/new-ui rider` resolves the branch and auto-opens — but only when
    it is checked out in exactly one place. Multiple locations are presented, never
    guessed between.

    [:octicons-arrow-right-24: Command line](guide/command-line.md)

-   :material-console-line:{ .lg .middle } **Runs the branch's scripts here**

    ---

    A **Console** tab beside the flight log: a real shell at the selected location, over a
    real pseudo-terminal. Pick a script from the branch's own config and watch it run —
    colour, ++ctrl+c++, and the output still there when it fails.

    [:octicons-arrow-right-24: The Console tab](guide/console.md)

-   :material-folder-search:{ .lg .middle } **Points out the leftovers**

    ---

    The worktree folders a branch already has on disk are listed under the branch
    box — even when no repo has the branch. Last month's leftover is named the
    moment you type.

    [:octicons-arrow-right-24: Discovery](guide/discovery.md#the-branchs-worktree-folders)

</div>

## Why "Fido"? { .fido-section-title }

A nod to the Apollo **Flight Dynamics Officer** — call sign FIDO — whose job was to
track the spacecraft and compute its trajectory. Fido does the same for your code: a
branch name in, its exact path on disk out. The flight log narrates each step as it
goes around the horn, and when your editor comes up, *the Eagle has landed*.

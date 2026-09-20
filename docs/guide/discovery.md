---
icon: lucide/search
description: How Fido finds every working tree on a branch.
---

# Discovery

## Finding the branch

- Walks each configured **search root** to a limited depth looking for git working
  trees, skipping noise directories (`.git`, `node_modules`, `bin`, `obj`, `.vs`,
  `.idea`, `packages`, `.svn`, `.hg`, and hidden folders).
- Checks each working tree's **current branch** — only trees actually on the typed
  branch count. Both **linked worktrees** and a clone's **main tree** qualify.
- Labels every hit: a **worktree** card (git-branch icon, deletable), a **main
  clone** card (home icon, never deletable), or — when the branch is checked out
  nowhere — a **new worktree** card (plus icon, created on open) and a **switch
  clone** card (arrows icon, switches the main tree on open), neither deletable.
  Meta lines read like `platform · 2 solutions · updated 3d ago`,
  `platform · branch on origin · opening creates this worktree`, or
  `platform · main tree on 'main' · opening switches it here · ⚠ 3 uncommitted
  change(s) ride along`.
- Detects the **solution files** inside each target — **`.sln`**, **`.slnx`**, and
  **`.slnf`** (Visual Studio solution filter) — for the solution chips.

## The branch's worktree folders

Under the branch box, Fido lists the worktree folders the typed branch **already has on disk** — one
row each, with a button to **copy** the path and one to **open** it in your **File Explorer / Finder**:

```text
Branch name
┌────────────────────────────────────────┐
│ xyz                                      │
└────────────────────────────────────────┘
 📁 fido  D:\main\fido.worktrees\xyz            ⌨  ↗
```

Where to look is the **same rule Fido creates worktrees by**, not a second guess at it: the branch's
slashes (and anything else a filesystem rejects) become dashes, and the folder sits under your
**worktree root** if you have one, else beside the clone in `<clone>.worktrees`. Fido then shows only
the folders that are **actually there**, so the line reports what exists rather than what could, and
**says nothing at all** when the branch has no folder anywhere.

**A folder counts even when no repo has the branch.** That is the point: `D:\main\fido.worktrees\xyz`
on disk while discovery reports *"no working tree or clone has 'xyz'"* is a leftover, or a tree since
switched to another branch — exactly what you want pointed out when you type the name again. There is
no git in this check, just a folder.

**How many rows you get depends on where your worktrees live:**

- With a **worktree root** configured (Settings → *Worktree root*), every branch lands under that one
  root whatever repo it belongs to — so there is at most a **single row**, no repo named, and it
  answers **as you type**, with no scan needed at all.
- With **no root**, a worktree is a sibling of its clone, so each clone can have its own folder for the
  branch — and each one that does gets a row, labelled with the repo, ordered by name. Those clones come
  from the **discovery scan**, so the rows appear once the first scan lands; after that they're kept
  (they don't depend on the branch) and **every later branch answers instantly**.

The **open** button opens the row's folder. If it has been deleted since the row was drawn, the nearest
folder above it opens instead and the flight log says which, rather than failing at a path still on
screen.

The line is purely informational: it never creates anything. Putting a branch **on disk** is still the
**new worktree** card's job, reached by opening it.

## Cross-clone visibility

Git enforces "one worktree per branch" only **within a single clone**. If you have two
clones of the same upstream (e.g. `D:\shine\apps` and `D:\main\apps`), each can
independently check out the same branch — leaving you with duplicate copies on disk.

Fido makes that visible instead of guessing: **every** location on the branch appears
as its own card, labelled with its kind and owning repo, and **you choose** which one
to open (or delete). A worktree is only ever created from a card you selected — never
behind your back — so Fido can't add a duplicate of its own.

---
icon: lucide/trash-2
description: Removing a worktree, its local branch, and optionally its remote.
---

# Deleting a worktree

## Deleting a worktree

Once discovery has found the branch, a **Delete worktree & branch** button sits on the
main screen beneath the tools — no dialog to dig through. It's a shortcut for tidying
up a branch you're finished with:

- The button is enabled **only when the selected card is a linked worktree on a
  non-default branch**. Selecting the **main clone** disables it with a note (*"only
  worktrees can be deleted — the main clone stays put."*); the default branches
  (`main`/`master`) are deliberately never deletable, even from a worktree (*"default
  branches can't be deleted — main/master stay put."*).
- **Two-step inline confirm.** The first click swaps the button for an in-place
  confirm strip that spells out exactly what happens — *"Remove the worktree at
  `<path>` and delete local branch `<branch>`? This can't be undone."* — plus explicit
  **data-loss warnings** when the worktree has **uncommitted changes** or the branch
  carries **commits that exist nowhere else** (unpushed and unmerged work a delete
  would orphan). Nothing happens unless you click **Delete**; **Cancel** or **Esc**
  backs out, and the destructive buttons sit outside the keyboard tab order so they
  can't be fired by a stray keypress.
- On confirmation Fido **removes the linked worktree** and **deletes the local
  branch**. When the branch is also on `origin`, the confirm strip offers an **opt-in
  checkbox — _Also delete the remote branch `origin/<branch>`_** — left **unticked by
  default**, so the remote is never touched unless you ask. **An open pull request
  blocks it:** when the **GitHub CLI (`gh`)** reports a PR open for the branch, the
  checkbox is **disabled** and the strip names the PR (`PR #42 · <title>`) with an
  **Open pull request ↗** link — close or merge it on GitHub first. **Arming the confirm
  asks GitHub again**, rather than trusting what the scan found: a PR merged since you
  typed the branch stops blocking the delete, and one opened since starts blocking it —
  and either way the [PR row](discovery.md#the-branchs-pull-request) above the cards is
  brought up to date with the same answer. PR detection degrades gracefully: if `gh`
  isn't installed, isn't authenticated, or the remote isn't GitHub, the option is simply
  offered without a PR note (and the flight log says the check couldn't be made, rather
  than claiming there's no PR). The git steps run from the clone's **main working tree**,
  so the worktree is dropped cleanly; a dirty worktree is force-removed after the warning.
- Each git step is **retried on transient failures** so a fleeting hiccup doesn't
  leave a half-tidied branch: a worktree file still held open by an editor or
  antivirus scan (common on Windows), or a git ref/index `.lock` left by a racing git
  process. Fido retries a few times with a short, backing-off wait — each attempt
  narrated in the flight log — while **permanent** refusals still fail fast on the
  first try.
- **The three targets are independent, and nothing-to-do isn't failure.** The worktree,
  the local branch and the branch on `origin` are each attempted and reported on
  separately: one that fails no longer abandons the ones after it, and the flight log's
  closing line says exactly what went and what didn't (*"✓ Removed worktree & branch
  `feature/x` + origin/feature/x."*). A target that had **already gone** — a branch the
  server dropped on merge, a folder cleared by hand, a worktree git no longer knows
  about — is reported as **done**, not as an error: *"✓ Removed worktree & branch
  `feature/x` — origin/feature/x was already gone."* A `⚠` is spent only on something
  you asked for that is genuinely **still there**.
- **Retry what's left.** When something does survive the delete, an inline **Retry**
  strip appears with what's still standing, git's own words for why, and a **Retry** /
  **Dismiss** pair. Retrying re-runs **only the outstanding step** — a worktree and
  local branch that already went are not touched again — and the closing report then
  covers the whole attempt. The strip sits outside the delete row so it outlives the
  card you just deleted; **Dismiss** or **Esc** drops the offer without touching
  anything on disk, and a fresh scan clears it as stale.
- **Long filenames & a force-delete fallback.** Deep worktrees can trip Windows'
  **260-character `MAX_PATH`** limit — a `node_modules` tree or generated output whose
  paths are too long — and a delete then fails with **`filename too long`** /
  **`unable to unlink … Filename too long`**, leaving the worktree stuck. Fido guards
  against this two ways. First, git's worktree commands run with **long-path support**
  (`core.longpaths`) so git's own file operations use the Windows extended-length API
  and can remove those files. Second, if git **still** can't delete the folder (a path
  too long even for that), Fido **offers to delete it straight from disk**: a modal,
  clearly-labelled confirmation for a recursive removal that **bypasses the Recycle
  Bin** and uses an extended-length (`\\?\`) path so it isn't defeated by the same
  limit. Once the folder is gone Fido **prunes** git's dangling worktree registration
  and carries on with the branch deletion. Nothing is force-deleted unless you choose
  it, and backing out leaves everything in place.
- Afterwards the card **drops out of the results** and the next location is selected —
  or the screen falls to **not found** when none remain.


---
icon: lucide/file-cog
description: Configure Fido per repository with .fido/cfg.yaml.
---

# In-repo config

## In-repo config — `.fido/cfg.yaml`

A repository can carry its own Fido settings, committed to the branch in a **`.fido`** folder at the
root. When a scan lands, Fido reads **`.fido/cfg.yaml`** *from the branch it just found* — **before** the
checkout options are offered — so a solution can say how it prefers to be opened and which of its scripts
are worth a button:

```yaml
# .fido/cfg.yaml
prefer main clone: true       # default to opening the clone's own tree, not a worktree
run files:                    # scripts the Console run menus offer
  - build.ps1
  - '*'                       # …plus every script in the repo root
aspire start: true            # …and `aspire start`, for an Aspire app host
```

- **`prefer main clone`** *(true/false)* — **which checkout Fido offers by default** once the scan has
  landed. Normally that's the first result, and worktrees lead; with this set it's the **main clone**
  instead (or, when the branch is checked out nowhere, the **switch the main tree** placement offer).
  It directs the **default choice, not the scan**: every location on the branch is still found and
  listed exactly as before, and each is one click away — this only decides which one the open actions
  start on. If the results hold no main tree at all, the flight log says so and the first card keeps
  the default.
- **`run files`** *(list of script names)* — each name becomes an entry in **both run menus** — the
  **Console button's** drop-down and the **[Console tab's](console.md)** — in the order given. A **`*`**
  entry stands for *every script in the tree root* — `.ps1`, `.cmd`, `.bat`, `.sh` — expanded in place
  and sorted by name; a name listed explicitly keeps its position and is never offered twice. A named
  script is offered whether or not it's in the tree today (it may be generated), so a typo shows up as
  a shell error rather than a missing button.
- **`aspire start`** *(true/false)* — adds **`aspire start`** to the same menus, at the end.

**Keys are matched loosely** — case, spaces, dashes and underscores are all ignored, so
`preferMainClone`, `prefer-main-clone` and `Prefer main clone` are the same key. Comments, quotes and
inline lists (`run files: [build.ps1, test.ps1]`) are understood; a setting Fido doesn't recognise is
skipped rather than rejected, and a missing or unreadable file simply means "no in-repo config" — a scan
never fails because of one.

**Where it's read from, and in what order.** A branch can be carrying the file in more than one place at
once, and the copies needn't agree, so Fido takes them in a fixed order and says which one answered:

1. **A local edit** — the file in the working tree with changes that aren't committed. The one you're
   writing right now wins outright; an edit in flight has always counted.
2. **The copy on `origin`**, whenever it differs from the one this machine has. This is the case a
   **checkout that's behind** creates: the config — or a newer one — landed on the branch after your
   worktree was made, so there's nothing in that folder to find. The flight log says
   `Read from origin/<branch> — the copy here is missing or out of date`, because the tree your commands
   will run in hasn't caught up with the settings offering them.
3. **The local copy** — the committed file in the tree, or the one on the local branch ref for a branch
   that's checked out nowhere.

A copy that **asks for nothing is treated as no file at all**, so it never shadows the one below it: a
starter file you created and haven't edited yet can't mask the settings the branch really carries. A `*`
in `run files` is expanded against **whichever tree the settings came from** — `origin/<branch>`'s root
for a config read off `origin` — so settings from one commit are never paired with a file listing from
another. A script the folder hasn't got yet is no obstacle: a run fast-forwards the tree first.

Everything is read from **what this machine already has**: the working tree, and the tracking refs *as
last fetched*. A scan runs on a keystroke, so it never goes to the network — a config pushed since your
last `git fetch` isn't visible yet, and fetching then pressing **Enter** picks it up. When the branch
carries nothing anywhere the log **says so** (`No .fido/cfg.yaml on 'feature/x' — nothing here, and
nothing on origin/feature/x as last fetched`) rather than leaving you wondering whether it looked.

**Nothing has to be checked out** for the config to apply: a placement offer reads the branch's refs, and
placing the branch reads the tree that placement just created — which is how a branch this clone had
never fetched, config and all, arrives without a second scan.

**Creating it.** The **OPEN** strip carries a small **document button** beside the copy-path icon:
it creates `.fido/cfg.yaml` in the selected location and opens it in your default tool, so a repo can
be set up without leaving Fido. (The same action sits at the foot of the Console tab's Run menu, as
**Edit `.fido/cfg.yaml`…**, for when you're already in there wanting another entry.) Three things it
deliberately does *not* do:

- **It never overwrites.** A repo that already has the file gets it **opened**, untouched — create and
  edit are the same button.
- **It changes nothing by itself.** The file it writes has every setting present at its **default**,
  with the tree's own root scripts named in a comment so the run-file list can be filled in without
  going looking. Until you edit it, the next scan reads it as *no in-repo config*.
- **It doesn't stage or commit.** What lands in the repo's history stays your call, as with every other
  git action in Fido. Edit it, commit it, then press **Enter** to rescan and pick the settings up.

The button is **absent for a placement offer** — there's no working tree on disk to write into until you
open it.

**Two menus offer them.** The run files and `aspire start` appear in both, and a pick does the same
thing in each — runs the command at the **selected** location, a `.ps1` through PowerShell, a root `.sh`
as `./name`, anything else through the platform's shell. What differs is *where*:

- **Beside the Console button** — a small **caret** next to it (or next to the hero button, when Console
  *is* your default tool). The command goes to the terminal you have configured, and the window stays
  open afterwards so you can read the output. On Windows a Windows Terminal console hosts the shell in a
  tab; elsewhere the shell runs on its own. Tick **Settings → Before running → Run it in Fido's Console
  tab** and this menu's picks land in the tab instead.
- **The [Console tab's](console.md) own Run menu**, at the right of the tab rule. It leads with a plain
  **shell here** and always runs in the pane below it — that's what the tab is for.

**Nothing runs on its own:** Fido only ever *offers* these commands, and a placement
offer still creates the worktree (or switches the tree) first, exactly as opening it would.

**Which shell runs it (Windows).** This is about the console *you* have configured; Fido's own
[Console tab](console.md) hosts the shell itself and picks `pwsh` first there too. A console that *is* a shell — `cmd`, `powershell`, `pwsh` — is used
exactly as you configured it; you picked it on purpose. Windows Terminal and third-party emulators are
different: they only **host** a shell and say nothing about which, so Fido picks the best on the machine —
**`pwsh` first**, then Windows PowerShell. That matters more than it sounds: PATH entries and aliases your
PowerShell profile sets up are the difference between `aspire start` running and coming back *'aspire' is
not recognized*, and Windows PowerShell ships with an execution policy that refuses to run `.ps1` files at
all. `cmd` is never chosen for you — only honoured when it's what you configured — and even then a `.ps1`
goes to a PowerShell, since `cmd` can't run one.

## Up to date before it runs

Picking a command from the Console run menu **fast-forwards the target first**, so a script or
`aspire start` runs against what `origin` has rather than whatever was last checked out. The flight log
narrates it (`Pulling origin/feature/x…`, then `✓ 'feature/x' is up to date with origin/feature/x`) and
the console opens straight after.

This runs at the one point where **every** kind of card has become a folder on disk, so a worktree Fido
created a second ago goes through exactly the same step as one that has sat there for weeks — and it has
to, because a new worktree is **not** automatically current: when the branch already exists locally,
`git worktree add` checks out that local ref, stale and all. Only a branch fetched fresh from `origin` is
current by construction, and the card can't tell you which you have.

**It only ever fast-forwards, and it never blocks the launch.** Four things it deliberately won't do:

- **Merge or rebase a diverged branch.** `--ff-only` refuses, the refusal is logged, and the console opens
  on the tree exactly as it stands. Reconciling a divergence is your call.
- **Touch a branch that tracks nothing.** A local-only branch that was never pushed — and a detached HEAD —
  is skipped with a line saying so, not reported as a failure.
- **Second-guess git about a dirty tree.** Fido doesn't pre-check for local changes: git fast-forwards
  happily when your edits aren't in the way, and refuses with a better message than Fido could write when
  they are.
- **Strand you when `origin` is unreachable.** Offline, or behind an expired credential, the pull fails
  fast (git is run with its prompts disabled, so it can never sit waiting for input you can't see), the log
  says so, and the console still opens.

Only a **run command** triggers it: opening a folder to look at it doesn't move the tree under you or cost
you a round trip, and the Console tab's **shell here** skips it too — opening a shell to look around isn't
running the thing. Turn it off entirely with **Settings → Before running**.


---
icon: lucide/layout
description: The mission-control screen, window title, version badge and keyboard shortcuts.
---

# The console

## Mission-control console

The in-app **flight log** narrates each scan and launch like a flight-control "go around
the horn" poll:

```
🚀 Going around the horn…
Scanning 24 working tree(s) for 'feature/new-ui'…
✓ Found 2 location(s) for 'feature/new-ui'.
✓ Rider located: C:\…\rider64.exe
▸ Opening Shine.sln in Rider
Fido? GO!
The Eagle has landed...
Closing in 7…
```

Each fresh scan resets the log and starts the poll again; the `Scanning N working
tree(s)…` line ticks in place as the count comes in. The `Closing in N…` line counts
down in place too (one line, not a line per second), and the countdown also shows in a
**Keep open** bar at the bottom of the window — click it to call off the close and keep
Fido up.

Lines are colour-coded by kind — accent for the mission beats (`🚀`, `🗑`, `Fido? GO!`),
green `✓` for successes, plain `▸` for actions — and failures call it straight: `⚠`
lines for a branch that isn't checked out anywhere, a tool that can't be located, or a
delete that went wrong.

The panel **grows with the window**: drag Fido's bottom edge down and every spare pixel
goes to the log rather than to a gap above it — the upper section keeps as much room as
its content needs, and the log takes the rest. Shrink the window again and the log falls
back to its compact box while the upper section scrolls.

Two buttons on the **Flight log** rule take the narration with you:

- **Copy** puts the whole log — every line, not just the visible ones — on the clipboard
  as plain text.
- **Save** writes it to a text file you pick, suggesting a dated name like
  `fido-flight-log-20260806-142317.txt`.

Both are disabled until there's something to hand over, and each confirms itself in the
log (`📋 Copied 8 flight-log line(s) to the clipboard.`, `✓ Flight log saved to …`).

## The window title

Once discovery **resolves** the branch, the window title names the work rather than the
app: the selected card's **repo** and the **branch**, with no `Fido` in front —

```text
platform · feature/new-ui
```

so a taskbar (or `Alt`+`Tab`) full of Fido windows tells you which is which at a glance.
The title **follows the selection**: picking a different card in a multi-location result
swaps the repo name with it. Anything less than a resolved branch — nothing typed yet, a
scan in flight, a branch found nowhere, a cleared branch box — reads plain **`Fido`**
again.

Prefer the title to stay put? Untick **Window title · _Show the repo and branch once
discovery resolves them_** in Settings and it reads `Fido` throughout.

## The version badge

The running build's version sits beside the **fido** wordmark in the header, in small muted
type on the wordmark's own baseline —

```text
fido  v0.9.3
```

— so "which Fido is this?" is answered on screen rather than from the installed-programs
list or the exe's properties. It is read from the **running assembly**, so it names what is
actually running.

## Keyboard & shortcuts

- The **branch** field is focused on launch. Typing debounces into a scan; **Enter**
  fires one immediately.
- **Ctrl+Space** in either input opens its **recently-used** suggestions (the **✕** on
  a suggestion forgets it).
- **Ctrl+1 … Ctrl+9** open the selected target with the corresponding configured tool
  (the same tools shown as buttons), gated — like the buttons — on discovery having
  **found** the branch.
- **Esc** backs out of a pending delete confirmation — or, once a delete has run,
  dismisses the **Retry** strip a part-way delete left behind.
- **Settings dialog:** `Enter` saves, `Esc` cancels.
- **`Alt+Space`** opens the window's native **system menu** (Move, Size, Minimize, Maximize, Close)
  on any window — the same menu reached from the title-bar icon or a title-bar right-click.

The destructive delete buttons sit outside the tab order, so `Enter`/`Tab` can never
land on them by accident.


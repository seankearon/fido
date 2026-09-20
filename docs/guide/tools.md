---
icon: lucide/app-window
description: Rider, WebStorm, VS Code, Visual Studio, Zed, terminals and the file explorer.
---

# Editors and tools

## Editors / IDEs

Fido can open the resolved target into any of several editors — plus the **Console** and **File Explorer**
targets below. The list is configured in Settings, and one entry can be the **default**:

- The **default** tool takes the **hero button**; set it from the **⚙ gear popover**
  or the **●** radio in Settings (or leave it unset for the equal-weight grid).
- Every tool — hero included — has a numbered keyboard shortcut, **Ctrl+1 … Ctrl+9**
  (Ctrl+N opens with the Nth entry in the configured list).

Built-in editor kinds — **Rider**, **WebStorm**, **VS Code**, **Visual Studio**, **Zed** — auto-detect
when their path is left blank; a **Custom** editor opens whatever executable/app-bundle path you give it.
**WebStorm** is **folder-only**: it's always handed the folder rather than a `.sln`/`.slnx`/`.slnf`.
Optional extra command-line arguments can be supplied per editor (passed before the target path).

Each entry also carries a **slug** — a short command-line token (built-in defaults: `rider`, `ws`,
`vsc`, `vs`, `zed`, `term`, `files`) — so a specific one can be picked when launching Fido from the
command line (see **Command-line launch**). The slug is editable per entry in Settings, and the
built-in kinds also answer to **well-known aliases** (`webstorm`, `vscode` / `code`,
`visualstudio` / `devenv`, `console` / `terminal`, `explorer` / `fileexplorer` / `finder`…) — only a
**Custom** editor with a blank slug is un-selectable from the CLI.

**Auto-detection** for each known kind looks, in order, at an explicit path, then your **`PATH`**,
then common install locations:

- **Rider** — `%LOCALAPPDATA%\Programs\Rider`, JetBrains **Toolbox** apps (newest) and shim,
  `Program Files\JetBrains\JetBrains Rider *`; macOS `/Applications`, `~/Applications`, Toolbox bundles/shim.
- **WebStorm** *(folder-only)* — `%LOCALAPPDATA%\Programs\WebStorm`, JetBrains **Toolbox** apps (newest)
  and shim, `Program Files\JetBrains\WebStorm *`; macOS `/Applications`, `~/Applications`, Toolbox bundles/shim.
- **VS Code** — `code` on `PATH`; `%LOCALAPPDATA%\Programs\Microsoft VS Code\bin\code.cmd` or under
  `Program Files`; macOS `Visual Studio Code.app`.
- **Visual Studio** *(Windows)* — `devenv` on `PATH`; `Program Files\Microsoft Visual Studio\<year>\<edition>\Common7\IDE\devenv.exe`.
- **Zed** — `zed` on `PATH`; macOS `Zed.app`; Windows `%LOCALAPPDATA%\Programs\Zed\Zed.exe`.

The editor is launched **detached** (Fido doesn't wait on it). If the chosen editor can't be found,
Fido says so and points you to its path setting.

## Console & file explorer

Beyond editors, Fido can open the resolved **folder** directly — handy when you just want a shell on the
branch or to browse its files. Two built-in targets, present out of the box and working on **Windows,
macOS, and Linux**:

- **Console** *(folder-only, slug `term`)* — opens a terminal **at the folder**. Auto-detection picks the
  OS default: **Windows** — Windows Terminal (`wt`), else PowerShell (`pwsh`/`powershell`), else `cmd`;
  **macOS** — the **Terminal** app (via `open -a`); **Linux** — the first of `x-terminal-emulator`,
  `gnome-terminal`, `konsole`, `xfce4-terminal`, `kitty`, `alacritty`, `tilix`, `xterm` on `PATH`.
  **The terminal is configurable:** set the Console row's **path** to a specific terminal program — a full
  path *or* just a command name like `wt`, `pwsh`, or `gnome-terminal` (resolved on `PATH`, including Windows
  Terminal's Store alias) — and add arguments if needed. Most terminals open in the folder because Fido sets
  it as their working directory; Windows Terminal is pointed at it explicitly with `-d`.
- **File Explorer** *(folder-only, slug `files`)* — reveals the folder in the OS file manager: **Windows**
  `explorer.exe`, **macOS** Finder (via `open`), **Linux** `xdg-open` (honouring your default file manager),
  else `nautilus` / `dolphin` / `thunar` / `nemo` / `pcmanfm`. The file manager is configurable via the
  row's **path** too.

Both behave like any other tool — a grid button, a **Ctrl+N** shortcut, and a CLI slug — so
`fido feature/new-ui term` opens a terminal on that branch and `fido feature/new-ui files` opens its folder.
They always hand over the **folder**, ignoring the solution chips.


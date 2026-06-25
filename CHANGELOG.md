# Changelog

All notable changes to Fido are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Pick the editor on the command line.** Each editor now carries a short **slug** (built-in defaults
  `rider`, `vsc`, `vs`, `zed`; editable per editor in Settings). Pass it as the second bare argument —
  `fido feature/new-ui zed` — or explicitly with `--editor` / `-e` (`fido -b feature/new-ui -e vs`) to
  open with that editor instead of the configured default. An unrecognised slug stops with a **No-go**
  that names it and lists the known slugs, rather than silently using the default.

- **Multiple editors / IDEs.** Fido can now open into Rider, **VS Code**, **Visual Studio**,
  **Zed**, or any **custom** editor you point it at. Configure the list in Settings and mark one
  as the **default** — the Open button (and **Enter**) launch into it. Every other editor gets a
  numbered keyboard shortcut (**Ctrl+1 … Ctrl+9**) and a secondary button on the main window, so a
  branch can be opened in whichever editor you want without changing the default. Known editors
  auto-detect (PATH + common install locations) when their path is left blank; a custom editor uses
  the path you give it. An older config's single **Rider path** is migrated onto the Rider editor
  automatically.

- **Close delay** after a successful launch. When Fido is set to close after opening
  (see **Close after opening**), it now counts down before quitting instead of vanishing
  instantly. The flight log shows a single line that ticks down in place (`Closing in 10…` → `9…`
  → `8…`) and a **Keep open** bar appears at the bottom of the window with the live countdown —
  click it to call off the close. Starting another open also cancels it. The delay is configurable in Settings
  (default **10 seconds**; **0** closes immediately), and selecting **Never** turns auto-close
  off entirely.

### Fixed

- **The chooser dialog is now fully keyboard-driven.** Up/Down arrows move the highlighted
  row, **Enter** opens it, and **Esc** cancels — previously the arrows didn't move the
  selection, so picking a clone / checkout / what-to-open meant reaching for the mouse. A
  shortcut hint runs along the dialog's bottom edge, matching the decision dialog.

- Pressing **Enter** in the Branch (or Solution) box now opens in a single press.
  Previously the first Enter only dismissed the MRU suggestion drop-down, so you
  had to press Enter again to launch. The keystroke now closes the drop-down and
  acts on the entered branch in one go.

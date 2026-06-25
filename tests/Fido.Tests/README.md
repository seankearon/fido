# Fido.Tests

Automated tests for Fido. They **drive the real Avalonia UI headlessly** and exercise the real
resolution logic against **real, throwaway git repositories** — the only hand-written fakes are the
two things CI can't run for real: Rider launch and the modal dialogs.

## Running

On the .NET 10 SDK the legacy `dotnet test` (VSTest) path is unsupported. TUnit runs on
Microsoft.Testing.Platform, so execute the test app directly:

```sh
dotnet run -c Release --project tests/Fido.Tests
```

Filter to a subset with Microsoft.Testing.Platform's tree filter:

```sh
dotnet run -c Release --project tests/Fido.Tests -- --treenode-filter "/*/*/TwoClonesTests/*"
```

Captured UI screenshots are written to `bin/<config>/net10.0/screenshots/` (or `FIDO_SCREENSHOT_DIR`
if set) and an HTML report to `bin/<config>/net10.0/TestResults/`. CI uploads both as artifacts.

## Layout

| Folder | What it covers |
|--------|----------------|
| `E2E/` | The headline scenarios through the real `MainWindow`: two clones of one repo, two repos, multiple worktrees, editor-not-installed, editor selection, CLI prefill, validation, button click |
| `Dialogs/` | The real `ChooserDialog` / `DecisionDialog` / `SettingsDialog` windows, driven by list/keyboard/buttons |
| `Services/` | `OpenerService` / `GitService` / `Mru` / `ConfigService` against a real temp git world (no UI) |
| `Locator/` | The real `EditorLauncher.Locate` probing, against a fake editor executable |
| `Infrastructure/` | `TestRepoWorld` (real git fixtures), `FakeEditorLauncher`, `FakeDialogService`, headless helpers, screenshots |

`TestRepoWorld` builds bare origins, clones and `git worktree`s with the real git CLI in a hermetic
temp folder (the machine's git config is ignored; a deterministic identity is supplied).

## Headless harness gotchas (load-bearing)

`TestAppBuilder` / `Ui` in `TestAppBuilder.cs` encode three non-obvious requirements:

1. **`.UseSkia()` must be called explicitly** before `.UseHeadless(UseHeadlessDrawing = false)`.
   Without it the headless session deadlocks on the first dispatch (no frames, no error).
2. **Dispatch through the generic `HeadlessUnitTestSession.Dispatch<T>`**, not the non-generic
   `Dispatch(Func<Task>)` — the latter runs the body fire-and-forget and returns before the first
   `await` resumes, silently skipping every assertion after any real async work.
3. **Dispose the session once** via a TUnit `[After(TestSession)]` hook (`TestHooks.cs`); its UI
   thread is a foreground thread, so without it the process never exits.

UI-touching test classes are `[NotInParallel]` (single Avalonia UI thread + the shared static
`Program.StartupArgs`). Pure service/locator/MRU tests run in parallel.

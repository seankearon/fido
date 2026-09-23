---
icon: lucide/hammer
description: Prerequisites, running from source, publishing and the release pipeline.
---

# Building Fido

Fido is a **.NET 10 / Avalonia 12** desktop app. Windows is the primary target;
macOS is a bonus. The solution is `Fido.slnx`; the project is
`src/Fido.csproj` (assembly / executable name: `Fido`).

## Prerequisites

- **.NET 10 SDK** (10.0.301 or newer).
- **For a Native AOT publish only** — the platform C/C++ toolchain (for the native linker):
  - **Windows:** Visual Studio with the **“Desktop development with C++”** workload, or the
    standalone **Build Tools for Visual Studio** with that workload. See
    <https://aka.ms/nativeaot-prerequisites>.
  - **macOS:** Xcode Command Line Tools (`xcode-select --install`).
- *Optional:* the **JetBrains Mono** font for the intended look (the UI falls back to
  Cascadia Code / Consolas if it's absent).
- **For a release only** — the [**Avalonia Parcel**](https://avaloniaui.net/parcel) CLI
  (`parcel`, which packages and signs the installers) and the **GitHub CLI** (`gh`,
  authenticated), plus the code-signing configuration described under
  [Release](#release-build-sign-package-publish).

## Develop & run

From the project folder `src/`:

```sh
dotnet build      # compile (Debug)
dotnet run        # build and launch
```

…or from the repo root: `dotnet run --project src`.

CLI prefill (optional — pre-populates the form, does not auto-launch):

```sh
dotnet run -- --branch feature/my-thing --solution MyApp     # also: -b, -s, --folder
```

## Publish — Native AOT (default)

`PublishAot` is enabled in the project, so publishing produces a **self-contained,
single native executable** — no .NET runtime required on the target machine, with a
faster cold start. Requires the C++ toolchain listed above.

```sh
# from src/
dotnet publish -r win-x64 -c Release
```

Output: `src/bin/Release/net10.0/win-x64/publish/Fido.exe`

Other runtimes (publish on a machine of that OS): `-r osx-arm64`, `-r osx-x64`, `-r linux-x64`.

The app is AOT-safe because the UI uses **compiled bindings** (`x:DataType` on every view)
and the settings file uses **source-generated `System.Text.Json`** — no runtime reflection.

## Publish — framework-dependent (no C++ toolchain needed)

For a portable build that relies on an installed .NET runtime:

```sh
dotnet publish -c Release -p:PublishAot=false
```

## Release — build, sign, package, publish

The release is driven by **[`release.ps1`](https://github.com/seankearon/fido/blob/main/release.ps1)**, and the work itself is a
program rather than a script: **`build/Fido.Build.fsproj`**, an F# console app that runs a
sequence of named stages, times each one and prints a summary. It is in the solution, so
it compiles with everything else and breaks loudly rather than at 2 a.m.

```powershell
.\release.ps1                       # release the next patch version, after confirming
.\release.ps1 -DryRun               # installers only — nothing tagged, released or pushed
.\release.ps1 -Version 1.0.0        # pin the version instead of using ver.txt
.\release.ps1 -Version 1.0.0 -Force # …and skip the confirmation prompt
```

Run it on **Windows**: it publishes `win-x64` with Native AOT and lets Parcel cross-build
the macOS heads from there.

`release.ps1` is a front end. It checks the things that are cheap now and expensive later
— the SDK, the Parcel CLI, that `gh` is installed *and authenticated*, and that all six
signing keys are present — then prints what is about to happen and asks before doing it.
The `gh` check earns its place: the build tags and pushes before it creates the release,
so an unauthenticated `gh` would strand a pushed tag with no release against it. If a run
does fail after tagging, the script prints the commands to clear the tag.

Windows PowerShell 5.1 refuses unsigned scripts by default — use PowerShell 7 (`pwsh`), or
run it as `powershell -ExecutionPolicy Bypass -File .\release.ps1`.

The build project can also be run directly, which is what `release.ps1` does:

```sh
dotnet run --project build                      # full local build — nothing leaves the machine
dotnet run --project build -- version:1.0.0     # pin the version instead of using ver.txt
dotnet run --project build -- release           # …and tag, publish to GitHub, bump ver.txt
```

### The stages

| Stage | What it does |
|---|---|
| **Verify** | Refuses to start on a dirty tree or off `main`; loads the signing configuration and checks every key is present. |
| **Update** | `git pull --ff-only`, then confirms `main` is not behind `origin`. |
| **Clean** | Wipes `_build/`. |
| **Test** | Builds and runs the TUnit suite; a non-zero exit code fails the build. |
| **Restore** | Restores `src/Fido.csproj` for `win-x64` **with `PublishAot=true`**, so the ILCompiler package is in the assets file. |
| **Version** | Writes a generated root `Directory.Build.props` carrying the version and product metadata. |
| **Publish Windows** | Native AOT publish to `_build/out/win-x64`, then checks the output really is native. |
| **Package** | Hands `src/Fido.parcel` to Parcel, which builds, **signs** and packages the NSIS installer and the `.dmg` into `_build/drop`. |
| **Revert Generated Files** | Removes the generated `Directory.Build.props`, on success *and* on failure. |
| **Tag Repo**, **GitHub Release**, **Update Version File** | `release` only — tag `vX.Y.Z`, `gh release create` with the installers attached and generated notes, then commit the new `ver.txt`. |

Without `release` (or with `release.ps1 -DryRun`) nothing leaves the machine: no tag, no
push, no version bump. You get the installers in `_build/drop` and nothing else.

Two ordering details are load-bearing and commented in the code. **Test runs before
Restore**, because `Fido.Tests` references `src/Fido.csproj` and building it re-restores
that project without a runtime — which would throw away the AOT restore and leave the
publish failing with `NETSDK1047`. And the publish uses `--no-restore` deliberately, so a
lost restore fails loudly instead of quietly shipping a directory of managed assemblies in
place of one native binary.

### Versioning

`ver.txt` holds the last version built; the next build increments its patch number.
`version:X.Y.Z` overrides it, and a `release` run writes the version it used back to the
file. Fido has never been released, so **the first release should pin `version:1.0.0`** —
after that the file carries it.

`ver.txt` is also what an **ordinary** build stamps: `src/Fido.csproj` reads it whenever
nothing else set `Version`, so a build from the IDE, from CI or from `dotnet run` carries
a real number — and shows it in the app's header — rather than the SDK's `1.0.0` default.
The generated `Directory.Build.props` is imported before the project body, so the release
build's own version still wins.

### Code signing

Parcel signs the app exe, the NSIS uninstaller and the installer with **Azure Trusted
Signing**. Nothing identifying the signing account is in this repo: the build reads
`%USERPROFILE%\.config\appbuild.env` — a private `KEY=value` file with `#` comments, shared
with the Klippy build and never checked in — and refuses to start unless it holds all six
keys:

| Key | What it is |
|---|---|
| `CodeSigning__TenantId`, `CodeSigning__ClientId`, `CodeSigning__ClientSecret` | The Entra app registration holding the *Trusted Signing Certificate Profile Signer* role. |
| `CodeSigning__Endpoint`, `CodeSigning__AccountName`, `CodeSigning__CertificateProfileName` | The Trusted Signing resource. |

A value already exported in the shell wins over the file, which is how CI or a one-off
override supplies it. The checked-in `src/Fido.parcel` knows nothing about signing: the
build writes a copy under `_build/` with the signing block filled in and packs from that,
so the repo stays clean and a hand-run `parcel pack` on the original still gives an
unsigned build. Signing matters because an unsigned NSIS installer wrapping a large native
binary is exactly the shape Defender's `Wacatac.B!ml` heuristic flags.

### macOS

The `.dmg` is cross-built from Windows. ILC cannot compile native code across operating
systems, so `src/Fido.csproj` turns Native AOT **off** for the `osx-*` runtimes unless the
build is actually running on a Mac, falling back to a trimmed, self-contained publish —
larger and JIT-started, but it runs. Run the build on a Mac and those come out native.

The bundle is **ad-hoc signed** (`MacOsSettings.SigningCredentialsType`), so a first launch
needs right-click → Open. Moving to a Developer ID certificate and notarization is a
separate, Mac-only piece of work — see `macos-packaging-handoff.md`, which also records a
Parcel icon-conversion problem worth re-checking.

## Documentation

The site you are reading is built with **[Zensical](https://zensical.org/)** from the
`docs/` folder, configured by `zensical.toml` at the repo root:

```powershell
uv tool install zensical      # or: pip install zensical
zensical serve                # preview on http://localhost:8000
zensical build --strict       # what the release and the PR check run
```

**`release.ps1` builds and publishes it.** The *Verify Docs* stage builds the site with
`--strict` before anything is signed or tagged, so a broken link stops the release, and
*Publish Docs* force-pushes the result to the `gh-pages` branch that Pages serves.
[`.github/workflows/docs.yml`](https://github.com/seankearon/fido/blob/main/.github/workflows/docs.yml)
only validates the build on pull requests; it does not publish. So the published site always
describes the **released** version, not `main`.

Set `ZENSICAL` to the executable's full path if you keep it in a virtual environment, or pass
`-NoDocs` to release without touching the documentation.

### The screenshot gallery

Every screenshot under `docs/assets/screenshots/` is **generated, not taken by hand**.
`GalleryScreenshotTests` drives the real window headlessly against a throwaway demo world under
`%TEMP%\fido-demo` — two clones, a worktree, a branch checked out nowhere, and a committed
`.fido/cfg.yaml` with a build script in it — and writes the dark/light pair for every state, plus
the README hero.

```powershell
$env:FIDO_GALLERY = '1'
$env:FIDO_SCREENSHOT_DIR = "$PWD\docs\assets\screenshots"
dotnet run --project tests/Fido.Tests -- --treenode-filter "/*/*/GalleryScreenshotTests/*"
```

**Run it alone**, as above, rather than as part of a full suite run. No other test's frames should
land in the gallery folder — and the **Console tab** only paints in the *first* window a headless
process shows, so sharing a process with the rest of the suite yields an empty console pane. The
console shots are a real shell running the demo repo's own build script, which is why the output in
them is real.

What the machine has shows up in the result: the UI wants **JetBrains Mono** (falling back to
Cascadia Code, then Consolas, then whatever monospace is installed), the console runs the platform's
own script — a `.ps1` on Windows, a `.sh` elsewhere — and the paths on the cards are the real temp
folder, so a gallery generated on Windows shows Windows paths.

## Notes

- Settings persist to `%APPDATA%\Fido\config.json` (a legacy `atlantic-opener` folder is
  read once and migrated forward).
- Theme — **System** (default) / Light / Dark — via the cog in the app header. The sun/moon
  button beside the cog flips light ↔ dark for the running app only; it saves nothing.
- A native AOT `win-x64` build has been verified to compile cleanly; producing the final
  `.exe` just needs the C++ "Desktop development" workload installed (see Prerequisites).

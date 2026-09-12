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
  [Release](#release--build-sign-package-publish).

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

The release is driven by **[`release.ps1`](release.ps1)**, and the work itself is a
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

### Code signing

Parcel signs the app exe, the NSIS uninstaller and the installer with **Azure Trusted
Signing**. Nothing identifying the signing account is in this repo: the build reads
`%USERPROFILE%\.config\shine.env` — a private `KEY=value` file with `#` comments, shared
by every Shine build and never checked in — and refuses to start unless it holds all six
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

## Notes

- Settings persist to `%APPDATA%\Fido\config.json` (a legacy `atlantic-opener` folder is
  read once and migrated forward).
- Theme — **System** (default) / Light / Dark — via the cog in the app header.
- A native AOT `win-x64` build has been verified to compile cleanly; producing the final
  `.exe` just needs the C++ "Desktop development" workload installed (see Prerequisites).

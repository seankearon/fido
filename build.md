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

## Notes

- Settings persist to `%APPDATA%\Fido\config.json` (a legacy `atlantic-opener` folder is
  read once and migrated forward).
- Theme — **System** (default) / Light / Dark — via the cog in the app header.
- A native AOT `win-x64` build has been verified to compile cleanly; producing the final
  `.exe` just needs the C++ "Desktop development" workload installed (see Prerequisites).

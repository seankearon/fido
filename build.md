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

## Packaging the macOS app (signed + notarized)

The macOS `.dmg` is built with [**Avalonia Parcel**](https://docs.avaloniaui.net/tools/parcel/)
from `src/Fido.parcel`. Two important constraints:

- **Native AOT can't cross-compile**, so the Mac build must run *on a Mac* — we use the
  `macos-latest` GitHub Actions runner (`.github/workflows/release-macos.yml`).
- For the app to open without Gatekeeper warnings it must be **signed with a Developer ID
  Application certificate and notarized by Apple**. The signature carries the team name
  (*Shine Forms*), which is what macOS shows users as the verified developer.

### How it runs

Push a tag (`git tag v1.2.3 && git push --tags`) or trigger the **Release (macOS)**
workflow manually. It installs Parcel, decodes the certificate, runs
`parcel pack src/Fido.parcel -r osx-arm64 -p dmg`, and attaches the `.dmg` to the release.
(Only `osx-arm64` is built today; add `-r osx-x64` to the workflow to also cover Intel Macs.)

### Required GitHub secrets

| Secret | What it is | How to get it |
| --- | --- | --- |
| `PARCEL_LICENSE_KEY` | Avalonia Plus licence for the Parcel **CLI** (separate from Apple). | From your Avalonia account portal. |
| `APPLE_DEVELOPER_ID_P12_BASE64` | Your *Developer ID Application* cert + private key, as a base64-encoded `.p12`. **Must be a "Developer ID Application" cert** — see below. | See below. |
| `APPLE_DEVELOPER_ID_P12_PASSWORD` | The password you set when exporting the `.p12`. | You choose it at export time. |
| `APPLE_NOTARY_APPLE_ID` | The Apple ID email of the Shine Forms developer account. | Your account login. |
| `APPLE_NOTARY_PASSWORD` | An **app-specific password** (not your Apple password). | Create at <https://account.apple.com> → Sign-In & Security → App-Specific Passwords. |
| `APPLE_TEAM_ID` | The 10-character Team ID. | Apple Developer site → Membership details. |

The `.parcel` file references all of these by env-var name only — **no credentials are committed**.

### Creating the Developer ID certificate (one-time, on a Mac)

> ⚠️ It **must** be a **Developer ID Application** certificate. An *Apple Development*,
> *Apple Distribution*, *Mac Developer*, or *Developer ID Installer* cert will sign fine but
> Apple's notary rejects every binary with *"not signed with a valid Developer ID certificate."*
> Creating a Developer ID Application cert requires a **paid Apple Developer Program membership**
> (a free Apple ID can only make *Apple Development* certs). No membership? Use AdHoc (see below).

1. In **Keychain Access** → Certificate Assistant → *Request a Certificate from a Certificate
   Authority* — save the `.certSigningRequest` to disk.
2. At <https://developer.apple.com/account> → Certificates → **+** → choose
   **Developer ID Application**, upload the CSR, download the resulting `.cer`.
3. Double-click the `.cer` to import it; in Keychain Access find it, **expand it to include the
   private key**, right-click → *Export* → save as `developer_id.p12` and set a password.
   (You don't need to also select the intermediate — the workflow bundles Apple's full
   certificate chain into the `.p12` automatically before signing.)
4. Base64-encode it for the GitHub secret:
   - macOS/Linux: `base64 -i developer_id.p12 | pbcopy`
   - Windows (PowerShell): `[Convert]::ToBase64String([IO.File]::ReadAllBytes("developer_id.p12")) | Set-Clipboard`

   Paste that into the `APPLE_DEVELOPER_ID_P12_BASE64` secret.

### Notarization rejected? Check the certificate

If notarization fails with *"The binary is not signed with a valid Developer ID certificate"*,
the signing identity in the secret is the problem (the rest of the pipeline is fine). The
workflow now bundles Apple's intermediate + root into the `.p12`, so a missing chain is handled
automatically — which leaves the **wrong cert type** as the cause. The workflow's *"Decode and
re-chain Developer ID certificate"* step prints the leaf subject and fails fast if it isn't a
Developer ID Application cert. To check the secret yourself on a Mac (Keychain exports need
Homebrew's openssl, as the system `openssl` is LibreSSL and lacks `-legacy`):

```sh
OSSL="$(brew --prefix openssl@3)/bin/openssl"
# The CN must read "Developer ID Application: <Name> (<TEAMID>)":
"$OSSL" pkcs12 -legacy -in developer_id.p12 -passin pass:YOUR_PASSWORD -clcerts -nokeys \
  | "$OSSL" x509 -noout -subject -issuer -dates
```

If the subject says anything else (*Apple Development*, *Apple Distribution*, *Developer ID
Installer*, …), regenerate it as a **Developer ID Application** cert per the steps above and
update both `APPLE_DEVELOPER_ID_P12_BASE64` and `APPLE_DEVELOPER_ID_P12_PASSWORD`.

> No Apple Developer membership available? Set `MacOsSettings.SigningCredentialsType` back to
> `"AdHoc"` in `src/Fido.parcel`. The app still builds, but users must right-click → **Open**
> (or run `xattr -dr com.apple.quarantine /Applications/Fido.app`) the first time.

### Building it locally on a Mac instead

```sh
dotnet tool install --global AvaloniaUI.Parcel
export PARCEL_LICENSE_KEY=...            FIDO_VERSION=1.2.3
export APPLE_DEVELOPER_ID_P12=~/developer_id.p12   APPLE_DEVELOPER_ID_P12_PASSWORD=...
export APPLE_NOTARY_APPLE_ID=...         APPLE_NOTARY_PASSWORD=...   APPLE_TEAM_ID=...
parcel pack src/Fido.parcel -r osx-arm64 -p dmg -o artifacts/macos
```

## Notes

- Settings persist to `%APPDATA%\Fido\config.json` (a legacy `atlantic-opener` folder is
  read once and migrated forward).
- Theme — **System** (default) / Light / Dark — via the cog in the app header.
- A native AOT `win-x64` build has been verified to compile cleanly; producing the final
  `.exe` just needs the C++ "Desktop development" workload installed (see Prerequisites).

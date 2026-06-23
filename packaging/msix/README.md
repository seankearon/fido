# MSIX packaging

Fido ships as an [MSIX](https://learn.microsoft.com/windows/msix/) installer, built
and published by the **Release** workflow (`.github/workflows/release.yml`).

## Cutting a release

1. Push the workflow to the default branch (it only appears under **Actions** once it's
   on `main`).
2. **Actions → Release → Run workflow**, then enter the SemVer version (e.g. `1.2.0`).
   Tick *prerelease* for a beta.

The workflow:

1. Publishes the app as a self-contained **Native AOT** `win-x64` executable.
2. Stages the payload plus the logos (copied from `assets/png`) and this folder's
   [`AppxManifest.xml`](AppxManifest.xml), substituting the version and publisher.
3. Packs it with `makeappx` and signs it with `signtool`.
4. Creates a GitHub release tagged `v<version>` (`Fido v<version>`) with the
   `.msix` — and, when self-signed, the public `.cer` — attached.

## Signing

MSIX packages must be signed to install. The workflow supports two modes:

| Mode | When | Notes |
| --- | --- | --- |
| **Real certificate** | Repo secrets `MSIX_CERTIFICATE_BASE64`, `MSIX_CERTIFICATE_PASSWORD`, and `MSIX_PUBLISHER` are set | `MSIX_CERTIFICATE_BASE64` is a base64-encoded `.pfx`; `MSIX_PUBLISHER` must equal the certificate's subject (e.g. `CN=Your Name, O=Your Org`). |
| **Self-signed** (default) | No certificate secrets | The workflow mints a self-signed cert (subject `CN=Fido`, or `MSIX_PUBLISHER` if set) and attaches the public `.cer` to the release. |

The package `Publisher` in the manifest must match the signing certificate's subject
exactly — the workflow keeps them in sync.

## Installing a self-signed build

The `.cer` must be trusted once, per machine, before the `.msix` will install:

```powershell
# From an elevated PowerShell prompt, in the folder with the downloaded files:
Import-Certificate -FilePath .\Fido-<version>.cer -CertStoreLocation Cert:\LocalMachine\Root
Add-AppxPackage -Path .\Fido-<version>-win-x64.msix
```

Trusting the certificate is only needed for self-signed builds; an MSIX signed with a
certificate that chains to a trusted CA installs by double-click.
